using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.PromoCodes.Commands;

public record DeletePromoCodeCommand(Guid Id) : IRequest;

public class DeletePromoCodeHandler(IAppDbContext db)
    : IRequestHandler<DeletePromoCodeCommand>
{
    public async Task Handle(DeletePromoCodeCommand command, CancellationToken ct)
    {
        PromoCode? promoCode = await db.PromoCodes
            .FirstOrDefaultAsync(p => p.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(PromoCode), command.Id);

        promoCode.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
