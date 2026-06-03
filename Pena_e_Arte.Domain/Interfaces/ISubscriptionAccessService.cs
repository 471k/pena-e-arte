using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

public interface ISubscriptionAccessService
{
    Task<SubscriptionSnapshot?> GetSnapshotAsync(Guid studioId, CancellationToken ct = default);
}

public sealed record SubscriptionSnapshot(
    SubscriptionStatus Status,
    DateTime           TrialExpiresAt,
    DateTime           GracePeriodEnd);
