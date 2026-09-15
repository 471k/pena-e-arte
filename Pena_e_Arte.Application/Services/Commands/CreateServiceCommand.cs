using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Services.Commands;

public record CreateServiceCommand(CreateServiceRequest Request) : IRequest<ServiceResponse>;

public class CreateServiceHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreateServiceCommand, ServiceResponse>
{
    public async Task<ServiceResponse> Handle(CreateServiceCommand command, CancellationToken ct)
    {
        CreateServiceRequest req = command.Request;

        Service service = new()
        {
            StudioId = tenant.StudioId,
            Name = req.Name,
            Description = req.Description,
            DurationMinutes = req.DurationMinutes,
            Price = req.Price,
            DepositAmount = req.DepositAmount,
            IsActive = req.IsActive
        };

        db.Services.Add(service);
        await db.SaveChangesAsync(ct);

        return Map(service);
    }

    internal static ServiceResponse Map(Service s) => new(
        s.Id, s.StudioId, s.Name, s.Description,
        s.DurationMinutes, s.Price, s.DepositAmount,
        s.IsActive, s.CreatedAt, s.UpdatedAt);
}
