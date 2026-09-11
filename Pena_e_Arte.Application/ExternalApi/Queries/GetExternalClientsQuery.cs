using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.ExternalApi;

namespace Pena_e_Arte.Application.ExternalApi.Queries;

public record GetExternalClientsQuery(int Page = 1, int PageSize = 50)
    : IRequest<List<ExternalClientResponse>>;

public class GetExternalClientsHandler(IAppDbContext db)
    : IRequestHandler<GetExternalClientsQuery, List<ExternalClientResponse>>
{
    private const int MaxPageSize = 200;

    public async Task<List<ExternalClientResponse>> Handle(
        GetExternalClientsQuery query, CancellationToken ct)
    {
        int page = Math.Max(query.Page, 1);
        int pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        return await db.Clients
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new ExternalClientResponse(
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.CreatedAt))
            .ToListAsync(ct);
    }
}
