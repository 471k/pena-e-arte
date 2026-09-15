using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Services.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Services.Queries;

public record GetServiceQuery(Guid Id) : IRequest<ServiceResponse>;

public class GetServiceHandler(IAppDbContext db)
    : IRequestHandler<GetServiceQuery, ServiceResponse>
{
    public async Task<ServiceResponse> Handle(GetServiceQuery query, CancellationToken ct)
    {
        Service? service = await db.Services
            .FirstOrDefaultAsync(s => s.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(Service), query.Id);

        return CreateServiceHandler.Map(service);
    }
}
