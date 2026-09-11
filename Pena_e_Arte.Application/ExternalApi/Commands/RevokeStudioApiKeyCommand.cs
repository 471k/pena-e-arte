using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ExternalApi.Commands;

public record RevokeStudioApiKeyCommand : IRequest<Unit>;

public class RevokeStudioApiKeyHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<RevokeStudioApiKeyCommand, Unit>
{
    public async Task<Unit> Handle(RevokeStudioApiKeyCommand command, CancellationToken ct)
    {
        List<StudioApiKey> active = await db.StudioApiKeys
            .Where(k => k.RevokedAt == null)
            .ToListAsync(ct);

        foreach (StudioApiKey key in active)
            key.RevokedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
