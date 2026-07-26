using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetIssuerStudioSummaryQuery(Guid StudioId) : IRequest<IssuerStudioSummaryResponse>;

public class GetIssuerStudioSummaryHandler(IAppDbContext db, IIdentityService identity)
    : IRequestHandler<GetIssuerStudioSummaryQuery, IssuerStudioSummaryResponse>
{
    public async Task<IssuerStudioSummaryResponse> Handle(
        GetIssuerStudioSummaryQuery query,
        CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #24 — issuer cross-tenant studio summary
        // (studio + client/appointment/artist counts). IssuerOnly. See architecture.md.
        Domain.Entities.Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == query.StudioId, ct);

        if (studio is null)
            throw new NotFoundException("Studio", query.StudioId);

        // Owner lookup — Approach A: Studio.OwnerEmail stores the owner's email directly,
        // no OwnerId/ApplicationUser join needed. Display name falls back to the
        // GivenName Identity claim set at registration, else the email itself.
        string ownerEmail = string.IsNullOrWhiteSpace(studio.OwnerEmail) ? "—" : studio.OwnerEmail;
        string? givenName = ownerEmail == "—" ? null : await identity.GetUserDisplayNameAsync(ownerEmail, ct);
        string ownerDisplayName = givenName ?? ownerEmail;

        int artistCount = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == query.StudioId)
            .CountAsync(ct);

        int clientCount = await db.Clients
            .IgnoreQueryFilters()
            .Where(c => c.StudioId == query.StudioId)
            .CountAsync(ct);

        int appointmentCount = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == query.StudioId)
            .CountAsync(ct);

        return new IssuerStudioSummaryResponse(
            ownerEmail,
            ownerDisplayName,
            artistCount,
            clientCount,
            appointmentCount);
    }
}
