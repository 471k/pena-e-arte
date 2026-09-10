using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.GiftCards.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.GiftCards.Queries;

public record GetGiftCardsQuery : IRequest<List<GiftCardResponse>>;

public class GetGiftCardsHandler(IAppDbContext db) : IRequestHandler<GetGiftCardsQuery, List<GiftCardResponse>>
{
    public async Task<List<GiftCardResponse>> Handle(GetGiftCardsQuery query, CancellationToken ct) =>
        await db.GiftCards
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => RedeemGiftCardHandler.Map(g))
            .ToListAsync(ct);
}
