using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Clients.Queries;

public record GetTattooRecordQuery(Guid ClientId, Guid Id) : IRequest<TattooRecordResponse>;

public class GetTattooRecordHandler(IAppDbContext db)
    : IRequestHandler<GetTattooRecordQuery, TattooRecordResponse>
{
    public async Task<TattooRecordResponse> Handle(GetTattooRecordQuery query, CancellationToken ct)
    {
        TattooRecord? record = await db.TattooRecords
            .FirstOrDefaultAsync(t => t.Id == query.Id && t.ClientId == query.ClientId, ct);

        if (record is null)
            throw new NotFoundException(nameof(TattooRecord), query.Id);

        return AddTattooRecordHandler.Map(record);
    }
}
