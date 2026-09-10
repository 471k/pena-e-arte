using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.PromoCodes.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.PromoCodes.Queries;

public record GetPromoCodeQuery(Guid Id) : IRequest<PromoCodeResponse>;

public class GetPromoCodeHandler(IAppDbContext db)
    : IRequestHandler<GetPromoCodeQuery, PromoCodeResponse>
{
    public async Task<PromoCodeResponse> Handle(GetPromoCodeQuery query, CancellationToken ct)
    {
        PromoCode? promoCode = await db.PromoCodes
            .FirstOrDefaultAsync(p => p.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(PromoCode), query.Id);

        return CreatePromoCodeHandler.Map(promoCode);
    }
}
