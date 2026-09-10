using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Packages.Queries;

public record GetMyPackagePurchasesQuery : IRequest<List<PackagePurchaseResponse>>;

public class GetMyPackagePurchasesHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyPackagePurchasesQuery, List<PackagePurchaseResponse>>
{
    public async Task<List<PackagePurchaseResponse>> Handle(GetMyPackagePurchasesQuery query, CancellationToken ct)
    {
        Client client = await db.FindClientForUserAsync(currentUser, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        return await db.PackagePurchases
            .Where(p => p.ClientId == client.Id)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PackagePurchaseResponse(
                p.Id, p.StudioId, p.PackageId, p.Package.Name, p.ClientId, p.SessionsRemaining, p.CreatedAt))
            .ToListAsync(ct);
    }
}
