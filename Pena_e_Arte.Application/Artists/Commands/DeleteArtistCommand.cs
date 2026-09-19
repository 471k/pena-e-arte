using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record DeleteArtistCommand(Guid Id) : IRequest;

public class DeleteArtistHandler(IAppDbContext db, IIdentityService identity)
    : IRequestHandler<DeleteArtistCommand>
{
    public async Task Handle(DeleteArtistCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        bool hasUpcomingAppointments = await db.Appointments.AnyAsync(a =>
            a.ArtistId == command.Id
            && a.Date > DateTime.UtcNow
            && a.Status != AppointmentStatus.Cancelled
            && a.Status != AppointmentStatus.Completed
            && a.Status != AppointmentStatus.NoShow, ct);

        if (hasUpcomingAppointments)
            throw new BusinessRuleViolationException(
                "This artist has upcoming appointments and cannot be deleted.");

        // Clients assigned to this artist must actually become Unassigned, not just look that
        // way. db.Clients.Include(c => c.Artist) respects Artist's own DeletedAt query filter,
        // so once this artist is soft-deleted, the Include navigation silently returns null
        // for these clients while Client.ArtistId still points at the now-deleted row — masking
        // the stale FK as "Unassigned" in every read path instead of it actually being
        // Unassigned. Clearing it here is the real fix, not a display-layer workaround.
        // Change-tracker update (not ExecuteUpdateAsync) — the latter isn't supported by the
        // EF Core InMemory provider FakeDbContext uses for unit tests.
        List<Client> affectedClients = await db.Clients.Where(c => c.ArtistId == command.Id).ToListAsync(ct);
        foreach (Client client in affectedClients)
        {
            client.ArtistId = null;
            client.UpdatedAt = DateTime.UtcNow;
        }

        artist.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Free the Identity account's claim on this studio so the person can be invited as an
        // artist elsewhere afterward. Without this, CreateArtistHandler's reuse guard never
        // recognizes the account as available again — a removed artist would be permanently
        // stuck, unable to join any studio, which no booking SaaS in this category does
        // (artists routinely change studios). RemoveTenantClaimAsync also clears the
        // ActiveTenantId token if it pointed at this studio.
        //
        // Only for an independently-invited artist (role "artist"). A studio owner's own
        // dual-role linked profile (CreateOwnArtistProfileCommand — "Stop working as an
        // artist") shares this exact same tenant_id claim mechanism with their owner access
        // to this exact studio: removing it here would strip the owner's own access to their
        // studio, not just their artist seat. Confirmed live on staging (2026-09-17) — an
        // owner who stopped working as their own artist lost every tenant-scoped page until
        // the claim was restored by hand.
        if (artist.UserId is not null)
        {
            IReadOnlyList<string> roles = await identity.GetUserRolesAsync(artist.UserId.Value, ct);
            if (roles.Contains("artist"))
                await identity.RemoveTenantClaimAsync(artist.UserId.Value, artist.StudioId, ct);
        }
    }
}
