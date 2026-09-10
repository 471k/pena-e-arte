using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Queries;

public record GetMyWaitlistEntriesQuery : IRequest<List<WaitlistEntryResponse>>;

public class GetMyWaitlistEntriesHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyWaitlistEntriesQuery, List<WaitlistEntryResponse>>
{
    public async Task<List<WaitlistEntryResponse>> Handle(GetMyWaitlistEntriesQuery query, CancellationToken ct)
    {
        Client? client = await db.FindClientForUserAsync(currentUser, ct);
        if (client is null) return [];

        return await db.WaitlistEntries
            .Where(w => w.ClientId == client.Id)
            .Include(w => w.Artist)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => JoinWaitlistHandler.Map(
                w, w.Artist != null ? w.Artist.FirstName + " " + w.Artist.LastName : null, null))
            .ToListAsync(ct);
    }
}
