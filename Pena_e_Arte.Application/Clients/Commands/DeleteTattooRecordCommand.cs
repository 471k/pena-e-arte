using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Clients.Commands;

public record DeleteTattooRecordCommand(Guid ClientId, Guid Id) : IRequest;

public class DeleteTattooRecordHandler(IAppDbContext db)
    : IRequestHandler<DeleteTattooRecordCommand>
{
    public async Task Handle(DeleteTattooRecordCommand command, CancellationToken ct)
    {
        TattooRecord? record = await db.TattooRecords
            .FirstOrDefaultAsync(t => t.Id == command.Id && t.ClientId == command.ClientId, ct);

        if (record is null)
            throw new NotFoundException(nameof(TattooRecord), command.Id);

        record.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
