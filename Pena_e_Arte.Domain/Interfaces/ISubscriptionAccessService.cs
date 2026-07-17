using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

public interface ISubscriptionAccessService
{
    Task<bool>                  IsStudioActiveAsync(Guid studioId, CancellationToken ct = default);
    Task<SubscriptionSnapshot?> GetSnapshotAsync(Guid studioId, CancellationToken ct = default);
    Task                        InvalidateCacheAsync(Guid studioId, CancellationToken ct = default);
}

public sealed record SubscriptionSnapshot(
    SubscriptionStatus Status,
    DateTime?          TrialExpiresAt,
    DateTime           GracePeriodEnd);
