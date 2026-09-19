using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

/// <summary>
/// Pays an appointment's deposit with one of the client's own saved cards. Reuses
/// CreateDepositPaymentHandler's own ownership resolution and hold-resolution logic (identical
/// rules either way — the only difference is what happens once a hold is Pending: a new-card
/// checkout hands the order id straight to POK's hosted widget; this instead sets up a 3DS
/// challenge against the already-tokenized card and hands the client-facing SDK everything it
/// needs to call payByCardToken).
/// </summary>
public record PayDepositWithSavedCardCommand(PayWithSavedCardRequest Request) : IRequest<PayWithSavedCardSetupResponse>;

public class PayDepositWithSavedCardHandler(
    IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser,
    IPaymentProvider paymentProvider, IPokCardTokenService cardTokens)
    : IRequestHandler<PayDepositWithSavedCardCommand, PayWithSavedCardSetupResponse>
{
    public async Task<PayWithSavedCardSetupResponse> Handle(PayDepositWithSavedCardCommand command, CancellationToken ct)
    {
        PayWithSavedCardRequest req = command.Request;

        Appointment appointment = await CreateDepositPaymentHandler.ResolveOwnAppointmentAsync(
            db, currentUser, req.AppointmentId, ct);

        Client client = await db.FindClientForUserAsync(currentUser, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        // 404-not-403 ownership pattern — a saved method belonging to another client, or at a
        // different studio, is reported the same as one that doesn't exist.
        SavedPaymentMethod method = await db.SavedPaymentMethods
            .FirstOrDefaultAsync(s => s.Id == req.SavedPaymentMethodId && s.ClientId == client.Id, ct)
            ?? throw new NotFoundException(nameof(SavedPaymentMethod), req.SavedPaymentMethodId);

        (Guid paymentId, string clientToken, PaymentStatus status) =
            await CreateDepositPaymentHandler.ResolveOrCreateHoldAsync(db, tenant, paymentProvider, appointment, ct);

        if (status != PaymentStatus.Pending)
        {
            // Already settled (healed from a webhook that never arrived, or a second tab
            // finished payment first) — nothing left to set up. The frontend shows the same
            // "already authorised/paid" panel it already shows for a new-card checkout in this
            // state, driven off this same Status field.
            return new PayWithSavedCardSetupResponse(paymentId, status.ToString(), null, null, null, null);
        }

        PokPayerAuthSetup setup = await cardTokens.SetupTokenizedThreeDsAsync(
            tenant.StudioId, clientToken, method.ProviderCardTokenId, ct);

        PayWithSavedCardDeviceDataCollection? deviceDataCollection = setup.DeviceDataCollection is { } d
            ? new PayWithSavedCardDeviceDataCollection(d.Url, d.AccessToken)
            : null;

        return new PayWithSavedCardSetupResponse(
            paymentId, status.ToString(), clientToken, method.ProviderCardTokenId,
            setup.PayerAuthSetupReferenceId, deviceDataCollection);
    }
}
