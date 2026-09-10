using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Packages.Commands;

public record CreatePackageCommand(CreatePackageRequest Request) : IRequest<PackageResponse>;

public class CreatePackageHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreatePackageCommand, PackageResponse>
{
    public async Task<PackageResponse> Handle(CreatePackageCommand command, CancellationToken ct)
    {
        CreatePackageRequest req = command.Request;

        Package package = new()
        {
            StudioId = tenant.StudioId,
            Name = req.Name,
            SessionCount = req.SessionCount,
            Price = req.Price,
            IsActive = req.IsActive,
        };

        db.Packages.Add(package);
        await db.SaveChangesAsync(ct);

        return Map(package);
    }

    internal static PackageResponse Map(Package p) => new(
        p.Id, p.StudioId, p.Name, p.SessionCount, p.Price, p.IsActive, p.CreatedAt);
}
