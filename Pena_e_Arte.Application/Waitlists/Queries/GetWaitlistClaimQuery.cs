using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Waitlists.Common;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Queries;

/// <summary>
/// Reads a waitlist offer for the claim page (C09), anonymous or signed in. Side-effect free: email link
/// scanners open links before people do, so viewing never uses up the offer.
///
/// <para><b>One 404 for everything that isn't a valid, current token</b>: a token that doesn't verify, an entry
/// that's gone, a studio mismatch, a token from an older notification, or a row with no recorded slot. The
/// response and message are identical; only an Information log (entry id when one could be read, never the
/// token) tells them apart. The distinct states are shown only to someone holding a verified token, which
/// already proves they received the offer, so they reveal nothing to anyone else.</para>
///
/// <para>Tenant: taken from the verified token and set with SetTenant, the CreateGuestAppointmentCommand
/// precedent, so every read below goes through the normal tenant filter (no IgnoreQueryFilters).</para>
/// </summary>
public record GetWaitlistClaimQuery(string Token) : IRequest<WaitlistClaimResponse>;

public class GetWaitlistClaimHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IWaitlistClaimTokens claimTokens,
    ILogger<GetWaitlistClaimHandler> logger)
    : IRequestHandler<GetWaitlistClaimQuery, WaitlistClaimResponse>
{
    public async Task<WaitlistClaimResponse> Handle(GetWaitlistClaimQuery query, CancellationToken ct)
    {
        if (!claimTokens.TryRead(query.Token, out WaitlistClaimRef claim))
        {
            logger.LogInformation("Waitlist claim rejected: token did not verify");
            throw NotFound();
        }

        tenant.SetTenant(claim.StudioId);

        Waitlist? entry = await db.WaitlistEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == claim.EntryId, ct);

        if (entry is null || !claim.Matches(entry)
            || entry.NotifiedSlotStart is not DateTime slotStart
            || entry.NotifiedSlotDurationMinutes is not int duration)
        {
            logger.LogInformation(
                "Waitlist claim rejected for entry {@WaitlistEntryId}: missing, superseded or no recorded slot",
                claim.EntryId);
            throw NotFound();
        }

        // Studio has no tenant filter; read it explicitly by the verified id.
        Studio? studio = await db.Studios.AsNoTracking().FirstOrDefaultAsync(s => s.Id == claim.StudioId, ct);
        if (studio is null)
        {
            logger.LogInformation("Waitlist claim rejected for entry {@WaitlistEntryId}: studio not found", entry.Id);
            throw NotFound();
        }

        string? artistName = entry.NotifiedArtistId is Guid artistId
            ? await db.Artists
                .Where(a => a.Id == artistId)
                .Select(a => a.FirstName + " " + a.LastName)
                .FirstOrDefaultAsync(ct)
            : null;

        DateTime windowEndsAt = WaitlistClaimWindow.EndsAt(entry.NotifiedAt!.Value);
        string state = await StateOfAsync(entry, slotStart, duration, windowEndsAt, ct);

        return new WaitlistClaimResponse(
            state, studio.Slug, studio.Name, studio.Timezone,
            entry.NotifiedArtistId, artistName, slotStart, duration, windowEndsAt);
    }

    private async Task<string> StateOfAsync(
        Waitlist entry, DateTime slotStart, int duration, DateTime windowEndsAt, CancellationToken ct) =>
        entry.Status switch
        {
            WaitlistStatus.Booked => WaitlistClaimStates.Booked,
            WaitlistStatus.Cancelled => WaitlistClaimStates.Cancelled,
            WaitlistStatus.Expired => WaitlistClaimStates.WindowEnded,
            // Still Notified, but the daily expiry job hasn't run yet: the window is what counts.
            WaitlistStatus.Notified when DateTime.UtcNow >= windowEndsAt => WaitlistClaimStates.WindowEnded,
            WaitlistStatus.Notified => await WaitlistMatchExtensions.IsSlotFreeAsync(
                    db, entry.StudioId, entry.NotifiedArtistId, slotStart, duration, freedAppointmentId: null, ct)
                ? WaitlistClaimStates.Claimable
                : WaitlistClaimStates.SlotTaken,
            // Waiting has no NotifiedAt, so claim.Matches already refused it; kept explicit for safety.
            _ => throw NotFound(),
        };

    // The same message whatever the cause: nothing in it comes from the token.
    private static NotFoundException NotFound() => new("Waitlist offer", "link");
}

public class GetWaitlistClaimValidator : AbstractValidator<GetWaitlistClaimQuery>
{
    public GetWaitlistClaimValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
    }
}
