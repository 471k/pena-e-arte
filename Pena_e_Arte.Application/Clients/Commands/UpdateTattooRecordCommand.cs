using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Clients.Commands;

public record UpdateTattooRecordCommand(Guid ClientId, Guid Id, UpdateTattooRecordRequest Request)
    : IRequest<TattooRecordResponse>;

public class UpdateTattooRecordHandler(IAppDbContext db)
    : IRequestHandler<UpdateTattooRecordCommand, TattooRecordResponse>
{
    public async Task<TattooRecordResponse> Handle(UpdateTattooRecordCommand command, CancellationToken ct)
    {
        TattooRecord? record = await db.TattooRecords
            .FirstOrDefaultAsync(t => t.Id == command.Id && t.ClientId == command.ClientId, ct);

        if (record is null)
            throw new NotFoundException(nameof(TattooRecord), command.Id);

        UpdateTattooRecordRequest req = command.Request;
        record.Description  = req.Description;
        record.BodyLocation = req.BodyLocation;
        record.PhotoUrls    = req.PhotoUrls;
        record.CompletedAt  = req.CompletedAt;
        record.UpdatedAt    = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return AddTattooRecordHandler.Map(record);
    }
}
