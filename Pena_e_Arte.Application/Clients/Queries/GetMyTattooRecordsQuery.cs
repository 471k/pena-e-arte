using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Queries;

public record GetMyTattooRecordsQuery : IRequest<List<TattooRecordResponse>>;

public class GetMyTattooRecordsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyTattooRecordsQuery, List<TattooRecordResponse>>
{
    public async Task<List<TattooRecordResponse>> Handle(GetMyTattooRecordsQuery query, CancellationToken ct)
    {
        Client? client = await db.Clients
            .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct);

        if (client is null)
            throw new NotFoundException(nameof(Client), currentUser.UserId);

        return await db.TattooRecords
            .Where(t => t.ClientId == client.Id)
            .OrderByDescending(t => t.CompletedAt)
            .Select(t => AddTattooRecordHandler.Map(t))
            .ToListAsync(ct);
    }
}
