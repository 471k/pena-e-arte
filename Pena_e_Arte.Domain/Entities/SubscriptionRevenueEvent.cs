using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>Append-only ledger row, written at the moment an MRR-affecting transition actually
/// happens — so MRR history on or after the ledger's start is recorded, not reconstructed from
/// current state. Deliberately not a TenantEntity, same reasoning as Subscription/
/// SubscriptionRefund — admin-only, cross-tenant aggregate reads.
///
/// MrrBefore/MrrAfter are the subscription's contracted monthly-equivalent MRR. Churn's
/// MrrAfter is always 0 (the contract ends); Paused/PastDue's MrrAfter equals MrrBefore (the
/// contract is unchanged, only its billing inclusion changes — derived from Type at query time).</summary>
public class SubscriptionRevenueEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public Guid StudioId { get; set; }
    public DateTime OccurredAt { get; set; }
    public RevenueEventType Type { get; set; }
    public Guid? PlanId { get; set; }
    public BillingInterval? Interval { get; set; }
    public decimal MrrBefore { get; set; }
    public decimal MrrAfter { get; set; }

    /// <summary>The handler/job class name that wrote it.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Idempotency key: the real Stripe event id for webhook sources, a deterministic
    /// synthetic key for every other source (see RevenueEventRecorder).</summary>
    public string? StripeEventId { get; set; }

    /// <summary>Insertion time — tiebreak for same-OccurredAt events, never itself the query axis.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
