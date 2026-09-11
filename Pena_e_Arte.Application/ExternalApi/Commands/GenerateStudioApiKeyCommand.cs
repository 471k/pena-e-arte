using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ExternalApi.Commands;

public record GenerateStudioApiKeyCommand : IRequest<GenerateApiKeyResponse>;

/// <summary>
/// One active key per studio — generating a new one revokes whatever was active before,
/// rather than accumulating keys. Simpler to reason about for a first version; a studio
/// that needs more than one live integration at once can be revisited later.
/// </summary>
public class GenerateStudioApiKeyHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GenerateStudioApiKeyCommand, GenerateApiKeyResponse>
{
    public async Task<GenerateApiKeyResponse> Handle(GenerateStudioApiKeyCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .Include(s => s.Subscription)
            .ThenInclude(sub => sub == null ? null : sub.Plan)
            .FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), tenant.StudioId);

        bool planAllows = studio.Subscription?.Plan?.AllowApiAccess ?? false;
        if (!planAllows)
            throw new BusinessRuleViolationException(
                "Your current plan does not include API access.");

        List<StudioApiKey> existingActive = await db.StudioApiKeys
            .Where(k => k.RevokedAt == null)
            .ToListAsync(ct);
        foreach (StudioApiKey old in existingActive)
            old.RevokedAt = DateTime.UtcNow;

        (string rawKey, string keyPrefix, string keyHash) = ApiKeyHasher.GenerateNew();

        StudioApiKey key = new()
        {
            StudioId = tenant.StudioId,
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
        };
        db.StudioApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        return new GenerateApiKeyResponse(rawKey, keyPrefix, key.CreatedAt);
    }
}
