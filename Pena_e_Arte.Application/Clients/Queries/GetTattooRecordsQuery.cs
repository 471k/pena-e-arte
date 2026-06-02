using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Clients.Queries;

public record GetTattooRecordsQuery(Guid ClientId) : IRequest<List<TattooRecordResponse>>;

public class GetTattooRecordsHandler(IAppDbContext db)
    : IRequestHandler<GetTattooRecordsQuery, List<TattooRecordResponse>>
{
    public async Task<List<TattooRecordResponse>> Handle(GetTattooRecordsQuery query, CancellationToken ct) =>
        await db.TattooRecords
            .Where(t => t.ClientId == query.ClientId)
            .OrderByDescending(t => t.CompletedAt)
            .Select(t => AddTattooRecordHandler.Map(t))
            .ToListAsync(ct);
}
