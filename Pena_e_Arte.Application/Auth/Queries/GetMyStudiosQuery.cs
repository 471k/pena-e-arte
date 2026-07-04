using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Queries;

public record GetMyStudiosQuery : IRequest<List<MyStudioResponse>>;

public class GetMyStudiosHandler(
    IAppDbContext  db,
    IIdentityService identity,
    ICurrentUser   currentUser)
    : IRequestHandler<GetMyStudiosQuery, List<MyStudioResponse>>
{
    public async Task<List<MyStudioResponse>> Handle(
        GetMyStudiosQuery query, CancellationToken ct)
    {
        // All studios this user holds a tenant_id claim for
        IReadOnlyList<Guid> tenantIds =
            await identity.GetTenantIdsAsync(currentUser.UserId, ct);

        if (tenantIds.Count == 0) return [];

        // Studios are not themselves tenant-scoped (Studio IS the tenant) —
        // no IgnoreQueryFilters() needed here.
        List<Domain.Entities.Studio> studios = await db.Studios
            .Where(s => tenantIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return studios
            .Select(s => new MyStudioResponse(
                s.Id, s.Name, s.Slug, s.City, s.CoverImageUrl, s.IsActive))
            .ToList();
    }
}
