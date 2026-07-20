using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Instagram.Commands;

public record ToggleInstagramPostVisibilityCommand(Guid ArtistId, Guid PostId, bool IsVisible)
    : IRequest<Unit>;

public class ToggleInstagramPostVisibilityHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ToggleInstagramPostVisibilityCommand, Unit>
{
    public async Task<Unit> Handle(ToggleInstagramPostVisibilityCommand request, CancellationToken ct)
    {
        bool artistExists = await db.Artists.AnyAsync(a => a.Id == request.ArtistId, ct);
        if (!artistExists) throw new NotFoundException("Artist", request.ArtistId);

        if (currentUser.Role == "artist")
        {
            bool ownsProfile = await db.Artists
                .AnyAsync(a => a.Id == request.ArtistId && a.UserId == currentUser.UserId, ct);
            if (!ownsProfile) throw new ForbiddenException();
        }

        InstagramPost? post = await db.InstagramPosts
            .FirstOrDefaultAsync(p => p.Id == request.PostId && p.ArtistId == request.ArtistId, ct);

        if (post is null) throw new NotFoundException(nameof(InstagramPost), request.PostId);

        post.IsVisible = request.IsVisible;
        post.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
