using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Waitlists.Common;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Commands;

/// <summary>
/// Closes the loop after a booking made from a waitlist claim (C09). Sent by CreateAppointmentHandler and
/// CreateGuestAppointmentHandler once the appointment is saved, only when the request carried a claim token.
/// Replaces the old MarkWaitlistEntryBookedCommand, whose frontend caller never existed.
///
/// <list type="number">
/// <item>Marks the entry Booked, if the token verifies, belongs to this studio and this notification, the entry
/// is still Notified, and the booking is for the entry's own person: the same client, or the guest email it
/// was made with. A forwarded email therefore can't end someone else's place on the waitlist.</item>
/// <item>Offers the slot this entry was given to the next person in line, through ClaimNextMatchAsync, which
/// first confirms the slot is free. If this booking took that slot, it isn't free and nothing happens; if the
/// person booked another time, the slot is still open and moves on.</item>
/// </list>
///
/// The token never authorises the booking itself, and nothing here may fail it: the appointment already exists,
/// so every problem is logged (entry id only, never the token or email) and swallowed.
/// </summary>
public record CompleteWaitlistClaimCommand(Guid StudioId, Guid ClientId, string Token) : IRequest;

public class CompleteWaitlistClaimHandler(
    IAppDbContext db,
    IWaitlistClaimTokens claimTokens,
    ISender sender,
    ILogger<CompleteWaitlistClaimHandler> logger)
    : IRequestHandler<CompleteWaitlistClaimCommand>
{
    public async Task Handle(CompleteWaitlistClaimCommand command, CancellationToken ct)
    {
        try
        {
            Guid? cascadedTo = await CompleteAsync(command, ct);
            if (cascadedTo is Guid next)
                await sender.Send(new SendWaitlistSlotAvailableNotificationCommand(next), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Waitlist claim completion failed after a booking in studio {@StudioId}", command.StudioId);
        }
    }

    private async Task<Guid?> CompleteAsync(CompleteWaitlistClaimCommand command, CancellationToken ct)
    {
        if (!claimTokens.TryRead(command.Token, out WaitlistClaimRef claim) || claim.StudioId != command.StudioId)
        {
            logger.LogInformation("Waitlist claim token on a booking in studio {@StudioId} ignored: did not verify",
                command.StudioId);
            return null;
        }

        Waitlist? entry = await db.WaitlistEntries.FirstOrDefaultAsync(w => w.Id == claim.EntryId, ct);
        if (entry is null || !claim.Matches(entry))
        {
            logger.LogInformation("Waitlist claim on a booking ignored for entry {@WaitlistEntryId}: missing or superseded",
                claim.EntryId);
            return null;
        }

        if (entry.Status != WaitlistStatus.Notified)
        {
            logger.LogInformation("Waitlist claim on a booking ignored for entry {@WaitlistEntryId}: status {Status}",
                entry.Id, entry.Status);
            return null;
        }

        // The booked client's email, for an entry made as a guest (only GuestEmail) by someone now signed in, or
        // by a guest whose booking just created their client row with the same email.
        string? clientEmail = await db.Clients
            .Where(c => c.Id == command.ClientId)
            .Select(c => c.Email)
            .FirstOrDefaultAsync(ct);

        if (!IsSamePerson(entry, command.ClientId, clientEmail))
        {
            logger.LogInformation(
                "Waitlist claim on a booking ignored for entry {@WaitlistEntryId}: the booking is for someone else",
                entry.Id);
            return null;
        }

        entry.Status = WaitlistStatus.Booked;
        entry.UpdatedAt = DateTime.UtcNow;

        Guid? next = entry.NotifiedSlotStart is DateTime slotStart && entry.NotifiedSlotDurationMinutes is int duration
            ? await db.ClaimNextMatchAsync(
                entry.StudioId, entry.NotifiedArtistId, slotStart, duration, freedAppointmentId: null, ct)
            : null;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Waitlist entry {@WaitlistEntryId} marked Booked from its claim; slot cascaded to {@NextWaitlistEntryId}",
            entry.Id, next);
        return next;
    }

    private static bool IsSamePerson(Waitlist entry, Guid clientId, string? clientEmail) =>
        (entry.ClientId is Guid entryClient && entryClient == clientId)
        || (!string.IsNullOrWhiteSpace(entry.GuestEmail)
            && !string.IsNullOrWhiteSpace(clientEmail)
            && string.Equals(entry.GuestEmail.Trim(), clientEmail.Trim(), StringComparison.OrdinalIgnoreCase));
}
