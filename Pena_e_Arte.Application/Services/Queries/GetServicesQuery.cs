using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Services.Commands;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Services.Queries;

public record GetServicesQuery : IRequest<List<ServiceResponse>>;

public class GetServicesHandler(IAppDbContext db)
    : IRequestHandler<GetServicesQuery, List<ServiceResponse>>
{
    public async Task<List<ServiceResponse>> Handle(GetServicesQuery query, CancellationToken ct) =>
        await db.Services
            .OrderByDescending(s => s.IsActive)
            .ThenBy(s => s.Name)
            .Select(s => CreateServiceHandler.Map(s))
            .ToListAsync(ct);
}
