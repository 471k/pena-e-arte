using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.PromoCodes.Commands;

public record UpdatePromoCodeCommand(Guid Id, UpdatePromoCodeRequest Request) : IRequest<PromoCodeResponse>;

public class UpdatePromoCodeHandler(IAppDbContext db)
    : IRequestHandler<UpdatePromoCodeCommand, PromoCodeResponse>
{
    public async Task<PromoCodeResponse> Handle(UpdatePromoCodeCommand command, CancellationToken ct)
    {
        UpdatePromoCodeRequest req = command.Request;

        PromoCode? promoCode = await db.PromoCodes
            .FirstOrDefaultAsync(p => p.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(PromoCode), command.Id);

        promoCode.Code = req.Code.Trim().ToUpperInvariant();
        promoCode.AmountFixed = req.AmountFixed;
        promoCode.AmountPercent = req.AmountPercent;
        promoCode.IsActive = req.IsActive;
        promoCode.ExpiresAt = req.ExpiresAt;
        promoCode.MaxRedemptions = req.MaxRedemptions;
        promoCode.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return CreatePromoCodeHandler.Map(promoCode);
    }
}
