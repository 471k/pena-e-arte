using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Services.Commands;

public record UpdateServiceCommand(Guid Id, UpdateServiceRequest Request) : IRequest<ServiceResponse>;

public class UpdateServiceHandler(IAppDbContext db)
    : IRequestHandler<UpdateServiceCommand, ServiceResponse>
{
    public async Task<ServiceResponse> Handle(UpdateServiceCommand command, CancellationToken ct)
    {
        UpdateServiceRequest req = command.Request;

        Service? service = await db.Services
            .FirstOrDefaultAsync(s => s.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(Service), command.Id);

        service.Name = req.Name;
        service.Description = req.Description;
        service.DurationMinutes = req.DurationMinutes;
        service.Price = req.Price;
        service.DepositAmount = req.DepositAmount;
        service.IsActive = req.IsActive;
        service.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return CreateServiceHandler.Map(service);
    }
}
