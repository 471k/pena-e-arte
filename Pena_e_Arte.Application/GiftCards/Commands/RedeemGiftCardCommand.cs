using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.GiftCards.Commands;

/// <summary>
/// Applies a redeemed amount as a deduction against an appointment's outstanding deposit — the
/// same "discount the deposit before the client pays the rest by card" shape Group 4's PromoCode
/// redemption uses (not present on this branch; Group 3 ships independently — see architecture.md
/// Decisions Log for the exact interaction order once both land). Redemption need not cover the
/// full deposit in one shot; call again (or combine with a card payment for the remainder) to
/// cover the rest.
///
/// Deliberately NOT IAuditableCommand: that interface's AuditTargetId is read off the command
/// object itself, before the handler runs — but the GiftCard's real Guid is only known after
/// resolving it by Code inside the handler. The audit entry is written manually below instead,
/// once the real id is known (same "manual, not pipeline-driven" precedent as
/// AuditActions.AdminAccountBootstrapped).
/// </summary>
public record RedeemGiftCardCommand(RedeemGiftCardRequest Request) : IRequest<GiftCardResponse>;

public class RedeemGiftCardHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser)
    : IRequestHandler<RedeemGiftCardCommand, GiftCardResponse>
{
    public async Task<GiftCardResponse> Handle(RedeemGiftCardCommand command, CancellationToken ct)
    {
        RedeemGiftCardRequest req = command.Request;

        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == req.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), req.AppointmentId);

        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appointment.ClientId)
                throw new NotFoundException(nameof(Appointment), req.AppointmentId);
        }

        GiftCard giftCard = await db.GiftCards
            .FirstOrDefaultAsync(g => g.StudioId == tenant.StudioId && g.Code == req.Code, ct)
            ?? throw new NotFoundException(nameof(GiftCard), req.Code);

        if (giftCard.Status != GiftCardStatus.Active)
            throw new BusinessRuleViolationException($"This gift card is {giftCard.Status} and cannot be redeemed.");

        if (req.Amount <= 0 || req.Amount > giftCard.RemainingBalance)
            throw new BusinessRuleViolationException("Redemption amount exceeds the gift card's remaining balance.");

        if (appointment.DepositAmount <= 0)
            throw new BusinessRuleViolationException("This appointment has no outstanding deposit to redeem against.");

        decimal applied = Math.Min(req.Amount, appointment.DepositAmount);

        giftCard.RemainingBalance -= applied;
        giftCard.UpdatedAt = DateTime.UtcNow;
        if (giftCard.RemainingBalance <= 0)
            giftCard.Status = GiftCardStatus.Redeemed;

        appointment.DepositAmount -= applied;
        appointment.UpdatedAt = DateTime.UtcNow;
        if (appointment.DepositAmount <= 0)
        {
            appointment.DepositAmount = 0;
            appointment.DepositStatus = DepositStatus.Paid;
        }

        db.AuditLogEntries.Add(AuditLogEntry.Create(
            actorUserId: currentUser.UserId,
            actorRole: currentUser.Role,
            action: AuditActions.GiftCardRedeemed,
            targetType: AuditTargetTypes.GiftCard,
            targetId: giftCard.Id,
            studioId: tenant.StudioId,
            metadata: AuditMetadataBuilder.Build(command)));

        await db.SaveChangesAsync(ct);

        return Map(giftCard);
    }

    internal static GiftCardResponse Map(GiftCard g) => new(
        g.Id, g.StudioId, g.Code, g.InitialBalance, g.RemainingBalance,
        g.PurchaserEmail, g.RecipientEmail, g.Status.ToString(), g.CreatedAt);
}
