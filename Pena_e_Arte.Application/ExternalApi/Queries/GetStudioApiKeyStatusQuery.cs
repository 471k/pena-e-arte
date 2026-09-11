using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.ExternalApi.Queries;

public record GetStudioApiKeyStatusQuery : IRequest<StudioApiKeyStatusResponse>;

public class GetStudioApiKeyStatusHandler(IAppDbContext db)
    : IRequestHandler<GetStudioApiKeyStatusQuery, StudioApiKeyStatusResponse>
{
    public async Task<StudioApiKeyStatusResponse> Handle(GetStudioApiKeyStatusQuery query, CancellationToken ct)
    {
        StudioApiKey? active = await db.StudioApiKeys
            .Where(k => k.RevokedAt == null)
            .FirstOrDefaultAsync(ct);

        return active is null
            ? new StudioApiKeyStatusResponse(false, null, null, null)
            : new StudioApiKeyStatusResponse(true, active.KeyPrefix, active.CreatedAt, active.LastUsedAt);
    }
}
