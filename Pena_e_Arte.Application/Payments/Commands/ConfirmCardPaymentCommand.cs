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
/// Client-facing: re-fetches this payment's real status from the provider and heals local state.
/// Exists for the standalone /pay/:paymentId link page (CreatePaymentIntentCommand's flow), which
/// — unlike CreateDepositPaymentCommand's appointment-scoped resume branch — has no AppointmentId
/// in scope to key off of. The POK checkout widget's own onSuccess callback is UX only, never a
/// source of truth (ADR-0001: a client-side callback is exactly the kind of untrusted signal the
/// webhook-is-a-trigger-not-a-fact rule already covers) — this command is what the frontend calls
/// right after that callback fires, before it reports success back to the user.
/// </summary>
public record ConfirmCardPaymentCommand(Guid PaymentId) : IRequest<PaymentResponse>;

public class ConfirmCardPaymentHandler(
    IAppDbContext db, ICurrentUser currentUser, IPaymentProvider paymentProvider, IJobScheduler jobs)
    : IRequestHandler<ConfirmCardPaymentCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(ConfirmCardPaymentCommand command, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .Include(p => p.Client)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct)
            ?? throw new NotFoundException(nameof(Payment), command.PaymentId);

        if (currentUser.Role == "client")
        {
            Client? client = await db.FindClientForUserAsync(currentUser, ct);
            if (client is null || client.Id != payment.ClientId)
                throw new UnauthorizedAccessException("You can only access your own payment details.");
        }

        if (payment.Method != ClientPaymentMethod.Card || payment.ProviderReferenceId is null)
            return payment.ToResponse();

        if (payment.Status is not (PaymentStatus.Pending or PaymentStatus.Captured))
            return payment.ToResponse(); // already resolved (Paid/Refunded/Failed) — nothing to reconcile

        PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
            payment.StudioId, payment.ProviderReferenceId, ct);

        Appointment? appt = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == payment.AppointmentId, ct);

        // Authorised or charged on an appointment cancelled meanwhile: never heal it here, neither to
        // held nor to Paid. PaymentReconciliationJob releases the hold or refunds the charge in full;
        // run it now rather than overnight. A late-cancel forfeit is the exception: the studio keeps it.
        bool owedBack = appt is { Status: AppointmentStatus.Cancelled }
            && appt.DepositStatus != DepositStatus.Forfeited;

        switch (status)
        {
            case PaymentProviderStatus.Authorized when payment.Status == PaymentStatus.Pending && owedBack:
            case PaymentProviderStatus.Captured when payment.Status != PaymentStatus.Paid && owedBack:
                jobs.TriggerPaymentReconciliationNow();
                break;

            case PaymentProviderStatus.Authorized when payment.Status == PaymentStatus.Pending:
                payment.Status = PaymentStatus.Captured;
                payment.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                break;

            case PaymentProviderStatus.Captured when payment.Status != PaymentStatus.Paid:
                payment.Status = PaymentStatus.Paid;
                payment.PaidAt = DateTime.UtcNow;
                payment.UpdatedAt = DateTime.UtcNow;

                if (appt is not null)
                {
                    // A forfeit stays Forfeited: the same rule as CaptureDepositCommand and
                    // PaymentReconciliationJob.MarkPaid.
                    if (appt.DepositStatus != DepositStatus.Forfeited)
                        appt.DepositStatus = DepositStatus.Paid;
                    appt.UpdatedAt = DateTime.UtcNow;
                }

                await db.SaveChangesAsync(ct);
                // The client's "deposit received" email. Card deposits are charged when the client
                // pays (C-4), so this is usually the moment the money arrives. Not for a forfeit on
                // a cancelled appointment: the job sends nothing in that case either.
                if (appt is { Status: not AppointmentStatus.Cancelled })
                    jobs.EnqueueDepositPaidNotification(payment.Id);
                break;

                // Pending/Canceled/Failed/null — nothing resolved yet at the provider; leave local
                // state as-is rather than guess.
        }

        return payment.ToResponse();
    }
}
