using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.PromoCodes.Commands;

public record CreatePromoCodeCommand(CreatePromoCodeRequest Request) : IRequest<PromoCodeResponse>;

public class CreatePromoCodeHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreatePromoCodeCommand, PromoCodeResponse>
{
    public async Task<PromoCodeResponse> Handle(CreatePromoCodeCommand command, CancellationToken ct)
    {
        CreatePromoCodeRequest req = command.Request;

        PromoCode promoCode = new()
        {
            StudioId = tenant.StudioId,
            Code = req.Code.Trim().ToUpperInvariant(),
            AmountFixed = req.AmountFixed,
            AmountPercent = req.AmountPercent,
            IsActive = req.IsActive,
            ExpiresAt = req.ExpiresAt,
            MaxRedemptions = req.MaxRedemptions,
        };

        db.PromoCodes.Add(promoCode);
        await db.SaveChangesAsync(ct);

        return Map(promoCode);
    }

    internal static PromoCodeResponse Map(PromoCode p) => new(
        p.Id, p.StudioId, p.Code,
        p.AmountFixed, p.AmountPercent,
        p.IsActive, p.ExpiresAt, p.MaxRedemptions, p.RedemptionCount,
        p.CreatedAt, p.UpdatedAt);
}
