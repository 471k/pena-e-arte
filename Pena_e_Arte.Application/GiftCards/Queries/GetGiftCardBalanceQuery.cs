using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.GiftCards.Queries;

/// <summary>
/// Public, anonymous balance lookup by code alone (no studio slug in the route). GiftCard.Code is
/// unique only per (StudioId, Code) — not globally — so this searches across every studio. With a
/// 12-char base32 code space, a real cross-studio collision is astronomically unlikely; accepted
/// as a public gift-card code is effectively a global identifier in practice. Response
/// deliberately excludes PurchaserEmail/RecipientEmail — see architecture.md AllowAnonymous
/// Exceptions table for the enumeration-risk rate-limit reasoning.
/// </summary>
public record GetGiftCardBalanceQuery(string Code) : IRequest<GiftCardBalanceResponse>;

public class GetGiftCardBalanceHandler(IAppDbContext db)
    : IRequestHandler<GetGiftCardBalanceQuery, GiftCardBalanceResponse>
{
    public async Task<GiftCardBalanceResponse> Handle(GetGiftCardBalanceQuery query, CancellationToken ct)
    {
        GiftCard giftCard = await db.GiftCards
            .IgnoreQueryFilters()
            .Where(g => g.Code == query.Code && g.DeletedAt == null)
            .OrderBy(g => g.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(GiftCard), query.Code);

        return new GiftCardBalanceResponse(giftCard.RemainingBalance, giftCard.Status.ToString());
    }
}
