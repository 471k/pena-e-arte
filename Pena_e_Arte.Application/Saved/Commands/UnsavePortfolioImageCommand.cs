using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Saved.Commands;

public record UnsavePortfolioImageCommand(Guid UserId, Guid ImageId) : IRequest;

public class UnsavePortfolioImageHandler(IAppDbContext db)
    : IRequestHandler<UnsavePortfolioImageCommand>
{
    public async Task Handle(UnsavePortfolioImageCommand cmd, CancellationToken ct)
    {
        SavedPortfolioImage? saved = await db.SavedPortfolioImages
            .FirstOrDefaultAsync(
                s => s.UserId == cmd.UserId && s.PortfolioImageId == cmd.ImageId, ct);

        if (saved is null) return; // idempotent
        db.SavedPortfolioImages.Remove(saved);
        await db.SaveChangesAsync(ct);
    }
}
