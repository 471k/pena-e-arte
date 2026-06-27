using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record UpdateArtistPortfolioCommand(Guid Id, UpdateArtistPortfolioRequest Request) : IRequest<ArtistResponse>;

public class UpdateArtistPortfolioHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdateArtistPortfolioCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(UpdateArtistPortfolioCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        if (currentUser.Role == "artist" && artist.UserId != currentUser.UserId)
            throw new ForbiddenException();

        // Sync PortfolioImage rows: preserve existing (to keep their reviews), add new, remove stale.
        List<PortfolioImage> existing = await db.PortfolioImages
            .Where(p => p.ArtistId == command.Id)
            .ToListAsync(ct);

        HashSet<string> existingUrls = existing.Select(p => p.ImageUrl).ToHashSet();
        HashSet<string> newUrls      = command.Request.ImageUrls.ToHashSet();

        // Delete removed images — cascade removes their reviews.
        List<PortfolioImage> toRemove = existing.Where(p => !newUrls.Contains(p.ImageUrl)).ToList();
        db.PortfolioImages.RemoveRange(toRemove);

        // Add genuinely new images.
        foreach (string url in command.Request.ImageUrls)
        {
            if (!existingUrls.Contains(url))
            {
                db.PortfolioImages.Add(new PortfolioImage
                {
                    ArtistId = artist.Id,
                    StudioId = artist.StudioId,
                    ImageUrl = url,
                });
            }
        }

        artist.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Reload with portfolio for mapping.
        Artist updated = await db.Artists
            .Include(a => a.Portfolio)
            .FirstAsync(a => a.Id == command.Id, ct);

        return CreateArtistHandler.Map(updated);
    }
}
