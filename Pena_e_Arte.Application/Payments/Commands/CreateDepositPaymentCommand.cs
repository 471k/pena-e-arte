using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

/// <summary>
/// Client-facing card deposit: creates (or resumes) the provider order for the caller's own
/// appointment. The amount always comes from the appointment's DepositAmount — never from the
/// request — so a client cannot choose what to pay. Since C-4 (2026-10-22, "charge at booking")
/// the order charges the card as soon as the client pays; there is no hold for the studio to
/// capture later.
/// </summary>
public record CreateDepositPaymentCommand(Guid AppointmentId) : IRequest<PaymentIntentResponse>;

public class CreateDepositPaymentHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IPaymentProvider paymentProvider,
    IJobScheduler jobs,
    IReferenceAmountSnapshotter referenceAmounts)
    : IRequestHandler<CreateDepositPaymentCommand, PaymentIntentResponse>
{
    // Matches the Postman/REST example (expiresAfterMinutes: 1440) — a full day for the client to
    // complete the card step before the hold self-expires. PaymentReconciliationJob's 3-day stale
    // sweep is the backstop if this and POK's own expiry both somehow miss.
    internal const int HoldDurationMinutes = 1440;

    public async Task<PaymentIntentResponse> Handle(CreateDepositPaymentCommand command, CancellationToken ct)
    {
        Appointment appointment = await ResolveOwnAppointmentAsync(db, currentUser, command.AppointmentId, ct);
        (Guid paymentId, string clientToken, PaymentStatus status) =
            await ResolveOrCreateHoldAsync(db, tenant, paymentProvider, jobs, referenceAmounts, appointment, ct);
        Payment payment = await db.Payments.AsNoTracking().FirstAsync(p => p.Id == paymentId, ct);
        return new PaymentIntentResponse(paymentId, clientToken, status.ToString(), payment.Amount, payment.Currency);
    }

    /// <summary>
    /// Resolves the caller's own appointment by id — clients may only act on their own
    /// (ownership resolved, and healed, through Client.UserId/email); staff roles may act on
    /// any appointment in their studio. Shared by both CreateDepositPaymentCommand and
    /// PayDepositWithSavedCardCommand (identical ownership rule for either payment method).
    /// </summary>
    internal static async Task<Appointment> ResolveOwnAppointmentAsync(
        IAppDbContext db, ICurrentUser currentUser, Guid appointmentId, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), appointmentId);

        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appointment.ClientId)
                throw new NotFoundException(nameof(Appointment), appointmentId);
        }

        return appointment;
    }

    /// <summary>
    /// Resolves (or mints) the provider hold for an appointment's deposit — shared by the
    /// new-card flow (this handler) and the saved-card flow (PayDepositWithSavedCardCommand,
    /// which additionally sets up a 3DS challenge against whatever order this returns before
    /// the client can actually charge it). Returns the payment id, the provider's client-facing
    /// token (POK order id), and the resulting status: Pending means a fresh/resumed hold that
    /// still needs the client to complete payment; Captured/Paid means the deposit is already
    /// settled and the caller should report that directly, with no further POK interaction.
    /// A freshly minted order freezes the lek reference (Albanian EUR studios, spec D3); a resumed
    /// order keeps the reference frozen when it was created.
    /// </summary>
    internal static async Task<(Guid PaymentId, string ClientToken, PaymentStatus Status)> ResolveOrCreateHoldAsync(
        IAppDbContext db, ICurrentTenant tenant, IPaymentProvider paymentProvider, IJobScheduler jobs,
        IReferenceAmountSnapshotter referenceAmounts, Appointment appointment, CancellationToken ct)
    {
        if (appointment.DepositAmount <= 0)
            throw new BusinessRuleViolationException("This appointment does not require a deposit.");

        // Single payment row per appointment (unique index) — failed attempts and
        // cash declarations are converted in place, never duplicated.
        Payment? existing = await db.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == appointment.Id, ct);

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            // Never take a deposit for a cancelled appointment. A card payment still in flight is
            // settled by PaymentReconciliationJob: released, or refunded in full if it was charged.
            if (existing is { Method: ClientPaymentMethod.Card, Status: PaymentStatus.Pending, ProviderReferenceId: not null })
                jobs.TriggerPaymentReconciliationNow();
            throw new BusinessRuleViolationException("This appointment was cancelled, so no deposit is due.");
        }

        // An unauthorized card hold may be resumable — but never trust the stored token
        // blindly: reconcile with the provider first. This also heals local state when
        // webhooks were missed (e.g. the client authorized but we never heard).
        if (existing is
            {
                Method: ClientPaymentMethod.Card, Status: PaymentStatus.Pending,
                ClientToken: not null, ProviderReferenceId: not null
            })
        {
            PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
                existing.StudioId, existing.ProviderReferenceId, ct);

            // Legacy rows (owner payment links before HoldExpiresAt was set on them) have no
            // HoldExpiresAt: POK applied its 24 h default from the last mint, which is UpdatedAt (the
            // link's retry branch sets it; a new row's UpdatedAt defaults to its creation time). Not
            // named holdExpiresAt: that local is declared later in the enclosing scope (CS0136).
            DateTime orderExpiresAt = existing.HoldExpiresAt ?? existing.UpdatedAt.AddMinutes(HoldDurationMinutes);

            switch (status)
            {
                case PaymentProviderStatus.Pending when orderExpiresAt > DateTime.UtcNow.AddMinutes(10):
                    // Still awaiting the client and comfortably inside POK's order window — resume,
                    // unless card payments were switched off since the order was created (C-13).
                    // The healing branches below still run: that money was already taken.
                    CardCurrencyGuard.EnsureCardPaymentsEnabled(paymentProvider);
                    return (existing.Id, existing.ClientToken, PaymentStatus.Pending);

                case PaymentProviderStatus.Pending:
                    // Unpaid but at (or within 10 minutes of) its POK expiry. POK's public order read,
                    // which the checkout widget loads, returns 410 "Order is expired" after expiresAt,
                    // so release it and fall through to mint a fresh hold.
                    await paymentProvider.CancelAsync(existing.StudioId, existing.ProviderReferenceId, ct);
                    break;

                case PaymentProviderStatus.Authorized:
                    // Authorized but the webhook never arrived — heal and report
                    existing.Status = PaymentStatus.Captured;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return (existing.Id, existing.ClientToken, PaymentStatus.Captured);

                case PaymentProviderStatus.Captured:
                    // Captured but the webhook never arrived — heal and report
                    existing.Status = PaymentStatus.Paid;
                    existing.PaidAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;
                    appointment.DepositStatus = DepositStatus.Paid;
                    appointment.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    jobs.EnqueueDepositPaidNotification(existing.Id);
                    return (existing.Id, existing.ClientToken, PaymentStatus.Paid);

                    // Canceled / Failed / null (gone) at the provider — fall through and mint a fresh hold
            }
        }

        bool convertible = existing is { Method: ClientPaymentMethod.Cash, Status: PaymentStatus.CashPending }
                        or { Method: ClientPaymentMethod.Card, Status: PaymentStatus.Pending }
                        or { Status: PaymentStatus.Failed };
        if (existing is not null && !convertible)
            throw new BusinessRuleViolationException("A payment for this appointment is already in progress.");

        var studio = await db.Studios
            .Where(s => s.Id == tenant.StudioId)
            .Select(s => new { s.CountryCode, s.Currency })
            .SingleAsync(ct);
        string currency = studio.Currency;
        CardCurrencyGuard.EnsureSupported(paymentProvider, currency);

        Guid paymentId = existing?.Id ?? Guid.NewGuid();

        (string providerReferenceId, string clientToken) = await paymentProvider.CreatePaymentHoldAsync(
            new PaymentHoldRequest(
                StudioId: tenant.StudioId,
                PaymentId: paymentId,
                Amount: appointment.DepositAmount,
                Currency: currency,
                HoldDurationMinutes: HoldDurationMinutes,
                CaptureImmediately: true),
            ct);
        DateTime holdExpiresAt = DateTime.UtcNow.AddMinutes(HoldDurationMinutes);

        Payment payment;
        if (existing is null)
        {
            payment = new Payment
            {
                Id = paymentId,
                StudioId = tenant.StudioId,
                AppointmentId = appointment.Id,
                ClientId = appointment.ClientId,
                Amount = appointment.DepositAmount,
                Status = PaymentStatus.Pending,
                Method = ClientPaymentMethod.Card,
                Provider = "pok",
                Currency = currency,
                ProviderReferenceId = providerReferenceId,
                ClientToken = clientToken,
                HoldExpiresAt = holdExpiresAt,
            };
            db.Payments.Add(payment);
        }
        else
        {
            // Convert in place: cash declaration switched to card, or a failed attempt retried
            payment = existing;
            existing.Method = ClientPaymentMethod.Card;
            existing.Status = PaymentStatus.Pending;
            existing.Provider = "pok";
            existing.Currency = currency;
            existing.ProviderReferenceId = providerReferenceId;
            existing.ClientToken = clientToken;
            existing.HoldExpiresAt = holdExpiresAt;
            existing.CashNote = null;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        // The order just minted is what the client pays: freeze the lek reference they saw now (D3).
        await referenceAmounts.FreezeAsync(payment, studio.CountryCode, currency, payment.Amount, ct);

        await db.SaveChangesAsync(ct);
        return (paymentId, clientToken, PaymentStatus.Pending);
    }
}
