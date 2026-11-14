using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Queries;

public record GetMyWaitlistEntriesQuery : IRequest<List<WaitlistEntryResponse>>;

public class GetMyWaitlistEntriesHandler(IAppDbContext db, ICurrentUser currentUser, IWaitlistClaimTokens claimTokens)
    : IRequestHandler<GetMyWaitlistEntriesQuery, List<WaitlistEntryResponse>>
{
    public async Task<List<WaitlistEntryResponse>> Handle(GetMyWaitlistEntriesQuery query, CancellationToken ct)
    {
        Client? client = await db.FindClientForUserAsync(currentUser, ct);
        if (client is null) return [];

        List<Waitlist> entries = await db.WaitlistEntries
            .AsNoTracking()
            .Where(w => w.ClientId == client.Id)
            .Include(w => w.Artist)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        return entries
            .Select(w => JoinWaitlistHandler.Map(
                w,
                w.Artist != null ? w.Artist.FirstName + " " + w.Artist.LastName : null,
                null,
                // C09: the caller's own entry with a still-open offer gets the same claim token the email
                // carries, so "Book this slot" goes through the one claim path rather than a second one.
                IsOpenOffer(w, now) ? claimTokens.Mint(w) : null))
            .ToList();
    }

    private static bool IsOpenOffer(Waitlist w, DateTime now) =>
        w.Status == WaitlistStatus.Notified
        && w.NotifiedAt is DateTime notifiedAt
        && now < WaitlistClaimWindow.EndsAt(notifiedAt)
        && w.NotifiedSlotStart is not null;
}
