using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Services.Commands;

public record DeleteServiceCommand(Guid Id) : IRequest;

public class DeleteServiceHandler(IAppDbContext db)
    : IRequestHandler<DeleteServiceCommand>
{
    public async Task Handle(DeleteServiceCommand command, CancellationToken ct)
    {
        Service? service = await db.Services
            .FirstOrDefaultAsync(s => s.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(Service), command.Id);

        // Soft delete only — Appointment.ServiceId is a nullable FK with OnDelete(SetNull),
        // so historical appointments referencing this service are unaffected.
        service.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
