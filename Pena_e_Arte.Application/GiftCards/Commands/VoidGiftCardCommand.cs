using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.GiftCards.Commands;

/// <summary>Not in the original P1 backlog spec's own command list, but implied by its
/// frontend section's "void action" — added since that bullet requires a backend counterpart.</summary>
public record VoidGiftCardCommand(Guid Id) : IRequest<GiftCardResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.GiftCardVoided;
    public string AuditTargetType => AuditTargetTypes.GiftCard;
    public Guid AuditTargetId => Id;
}

public class VoidGiftCardHandler(IAppDbContext db)
    : IRequestHandler<VoidGiftCardCommand, GiftCardResponse>
{
    public async Task<GiftCardResponse> Handle(VoidGiftCardCommand command, CancellationToken ct)
    {
        GiftCard giftCard = await db.GiftCards
            .FirstOrDefaultAsync(g => g.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(GiftCard), command.Id);

        if (giftCard.Status == GiftCardStatus.Voided)
            return RedeemGiftCardHandler.Map(giftCard);

        giftCard.Status = GiftCardStatus.Voided;
        giftCard.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return RedeemGiftCardHandler.Map(giftCard);
    }
}
