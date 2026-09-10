using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Packages.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Packages.Queries;

public record GetPackagesQuery : IRequest<List<PackageResponse>>;

public class GetPackagesHandler(IAppDbContext db)
    : IRequestHandler<GetPackagesQuery, List<PackageResponse>>
{
    public async Task<List<PackageResponse>> Handle(GetPackagesQuery query, CancellationToken ct) =>
        await db.Packages
            .OrderByDescending(p => p.IsActive)
            .ThenBy(p => p.CreatedAt)
            .Select(p => CreatePackageHandler.Map(p))
            .ToListAsync(ct);
}
