using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Packages.Commands;

public record UpdatePackageCommand(Guid Id, UpdatePackageRequest Request) : IRequest<PackageResponse>;

public class UpdatePackageHandler(IAppDbContext db)
    : IRequestHandler<UpdatePackageCommand, PackageResponse>
{
    public async Task<PackageResponse> Handle(UpdatePackageCommand command, CancellationToken ct)
    {
        UpdatePackageRequest req = command.Request;

        Package package = await db.Packages
            .FirstOrDefaultAsync(p => p.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(Package), command.Id);

        package.Name = req.Name;
        package.SessionCount = req.SessionCount;
        package.Price = req.Price;
        package.IsActive = req.IsActive;
        package.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return CreatePackageHandler.Map(package);
    }
}
