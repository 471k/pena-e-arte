using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

public record CreateClientCommand(CreateClientRequest Request) : IRequest<ClientResponse>;

public class CreateClientHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser)
    : IRequestHandler<CreateClientCommand, ClientResponse>
{
    public async Task<ClientResponse> Handle(CreateClientCommand command, CancellationToken ct)
    {
        CreateClientRequest req = command.Request;

        bool exists = await db.Clients.AnyAsync(c => c.Email == req.Email, ct);
        if (exists)
            throw new BusinessRuleViolationException($"A client with email '{req.Email}' already exists in this studio.");

        // An artist can only ever create clients assigned to themselves — any artistId supplied
        // in the request is ignored rather than trusted. Mirrors CreateDesignCommand's fix for the
        // identical defect class (see docs/claude/architecture.md, 2026-07-01 artist QA pass).
        Guid artistId = req.ArtistId;
        if (currentUser.Role == "artist")
        {
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (myArtistId is null)
                throw new ForbiddenException();
            artistId = myArtistId.Value;
        }

        // Validate up front for a clean 404/business-rule error instead of an FK violation, and
        // load the entity needed to denormalize ArtistName into the response.
        Artist artist = await ResolveActiveArtistAsync(db, artistId, ct);

        Client client = new()
        {
            StudioId = tenant.StudioId,
            FirstName = req.FirstName,
            LastName = req.LastName,
            Email = req.Email,
            Phone = req.Phone,
            ArtistId = artist.Id
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        return Map(client, artist);
    }

    // ArtistId always comes from the client's own scalar FK, never from `artist?.Id` — a
    // c.Artist Include navigation silently returns null once that artist is soft-deleted
    // (Artist's own query filter applies to the join), which would otherwise make this
    // response falsely report "Unassigned" for a client whose ArtistId is actually still set.
    // ArtistName legitimately stays null in that case — there's no name to show for a
    // filtered-out artist.
    internal static ClientResponse Map(Client c, Artist? artist = null) =>
        new(c.Id, c.StudioId, c.FirstName, c.LastName, c.Email, c.Phone, c.CreatedAt, c.UserId,
            c.ArtistId, artist is null ? null : $"{artist.FirstName} {artist.LastName}");

    /// <summary>Shared by CreateClientCommand and UpdateClientArtistCommand — the two places a
    /// client's artist assignment is set from a caller-supplied id — so the not-found/inactive
    /// checks can't silently drift apart between create and reassign.</summary>
    internal static async Task<Artist> ResolveActiveArtistAsync(IAppDbContext db, Guid artistId, CancellationToken ct)
    {
        Artist artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == artistId, ct)
            ?? throw new NotFoundException(nameof(Artist), artistId);
        if (!artist.IsActive)
            throw new BusinessRuleViolationException("Cannot assign a client to an inactive artist.");
        return artist;
    }
}
