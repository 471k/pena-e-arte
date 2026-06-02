using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

public record AddTattooRecordCommand(Guid ClientId, AddTattooRecordRequest Request)
    : IRequest<TattooRecordResponse>;

public class AddTattooRecordHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<AddTattooRecordCommand, TattooRecordResponse>
{
    public async Task<TattooRecordResponse> Handle(AddTattooRecordCommand command, CancellationToken ct)
    {
        bool clientExists = await db.Clients.AnyAsync(c => c.Id == command.ClientId, ct);
        if (!clientExists)
            throw new NotFoundException(nameof(Client), command.ClientId);

        bool artistExists = await db.Artists.AnyAsync(a => a.Id == command.Request.ArtistId, ct);
        if (!artistExists)
            throw new NotFoundException(nameof(Artist), command.Request.ArtistId);

        AddTattooRecordRequest req = command.Request;

        TattooRecord record = new()
        {
            StudioId      = tenant.StudioId,
            ClientId      = command.ClientId,
            ArtistId      = req.ArtistId,
            AppointmentId = req.AppointmentId,
            Description   = req.Description,
            BodyLocation  = req.BodyLocation,
            PhotoUrls     = req.PhotoUrls,
            CompletedAt   = req.CompletedAt,
        };

        db.TattooRecords.Add(record);
        await db.SaveChangesAsync(ct);

        return Map(record);
    }

    internal static TattooRecordResponse Map(TattooRecord t) =>
        new(t.Id, t.ClientId, t.ArtistId, t.AppointmentId,
            t.Description, t.BodyLocation, t.PhotoUrls, t.CompletedAt, t.CreatedAt);
}
