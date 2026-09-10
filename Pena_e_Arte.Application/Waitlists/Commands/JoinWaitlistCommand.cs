using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Commands;

public record JoinWaitlistCommand(JoinWaitlistRequest Request) : IRequest<WaitlistEntryResponse>;

public class JoinWaitlistHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser)
    : IRequestHandler<JoinWaitlistCommand, WaitlistEntryResponse>
{
    public async Task<WaitlistEntryResponse> Handle(JoinWaitlistCommand command, CancellationToken ct)
    {
        JoinWaitlistRequest req = command.Request;

        Guid studioId;
        Client? client = null;

        // Mirrors CreateGuestAppointmentCommand's guest-vs-account duality. This endpoint is
        // AllowAnonymous, but authentication middleware still runs — a valid JWT still populates
        // ICurrentUser even though authorization was not required, so a signed-in client is
        // recognized and resolved through their own ambient tenant scope. An anonymous caller has
        // no ambient tenant (ICurrentTenant.StudioId stays Guid.Empty), so they must name the
        // studio explicitly via StudioSlug — same resolution CreateGuestAppointmentCommand uses.
        if (currentUser.IsAuthenticated && currentUser.Role == "client")
        {
            client = await db.FindClientForUserAsync(currentUser, ct)
                ?? throw new NotFoundException(nameof(Client), currentUser.UserId);
            studioId = tenant.StudioId;
        }
        else
        {
            Studio studio = await db.GetPublishedStudioBySlugAsync(req.StudioSlug!, ct)
                ?? throw new NotFoundException(nameof(Studio), req.StudioSlug ?? string.Empty);
            studioId = studio.Id;
        }

        if (req.ArtistId is Guid artistId)
        {
            bool artistExists = await db.Artists.IgnoreQueryFilters()
                .AnyAsync(a => a.StudioId == studioId && a.DeletedAt == null && a.Id == artistId, ct);
            if (!artistExists)
                throw new NotFoundException(nameof(Artist), artistId);
        }

        Waitlist entry = new()
        {
            StudioId = studioId,
            ArtistId = req.ArtistId,
            ClientId = client?.Id,
            GuestName = client is null ? req.GuestName : null,
            GuestEmail = client is null ? req.GuestEmail : null,
            GuestPhone = client is null ? req.GuestPhone : null,
            PreferredDateFrom = req.PreferredDateFrom,
            PreferredDateTo = req.PreferredDateTo,
            Status = WaitlistStatus.Waiting,
            Notes = req.Notes,
        };

        db.WaitlistEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        return Map(entry, null, client is null ? null : $"{client.FirstName} {client.LastName}");
    }

    internal static WaitlistEntryResponse Map(Waitlist w, string? artistName = null, string? clientName = null) => new(
        w.Id, w.StudioId, w.ArtistId, artistName, w.ClientId, clientName,
        w.GuestName, w.GuestEmail, w.GuestPhone,
        w.PreferredDateFrom, w.PreferredDateTo, w.Status.ToString(),
        w.NotifiedAt, w.Notes, w.CreatedAt);
}
