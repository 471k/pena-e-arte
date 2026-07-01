using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Saved.Commands;

public record SavePortfolioImageCommand(Guid UserId, Guid ImageId) : IRequest;

public class SavePortfolioImageHandler(IAppDbContext db)
    : IRequestHandler<SavePortfolioImageCommand>
{
    public async Task Handle(SavePortfolioImageCommand cmd, CancellationToken ct)
    {
        // Approved: cross-tenant public image lookup before saving.
        bool imageExists = await db.PortfolioImages
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == cmd.ImageId, ct);

        if (!imageExists)
            throw new NotFoundException(nameof(PortfolioImage), cmd.ImageId);

        bool alreadySaved = await db.SavedPortfolioImages
            .AnyAsync(s => s.UserId == cmd.UserId && s.PortfolioImageId == cmd.ImageId, ct);

        if (alreadySaved) return; // idempotent — already saved, no error

        db.SavedPortfolioImages.Add(new SavedPortfolioImage
        {
            UserId           = cmd.UserId,
            PortfolioImageId = cmd.ImageId,
        });
        await db.SaveChangesAsync(ct);
    }
}
