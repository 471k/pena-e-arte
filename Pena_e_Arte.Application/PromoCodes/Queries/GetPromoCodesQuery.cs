using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.PromoCodes.Commands;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.PromoCodes.Queries;

public record GetPromoCodesQuery : IRequest<List<PromoCodeResponse>>;

public class GetPromoCodesHandler(IAppDbContext db)
    : IRequestHandler<GetPromoCodesQuery, List<PromoCodeResponse>>
{
    public async Task<List<PromoCodeResponse>> Handle(GetPromoCodesQuery query, CancellationToken ct) =>
        await db.PromoCodes
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.CreatedAt)
            .Select(p => CreatePromoCodeHandler.Map(p))
            .ToListAsync(ct);
}
