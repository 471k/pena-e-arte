using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One billing cadence a Plan (tier) is actually offered under. A tier that offers
/// only Monthly has one row; a tier offering both has two. Adding a third interval in
/// the future (e.g. Quarterly) needs a new BillingInterval enum member and new rows —
/// no schema change. See architecture.md Decisions Log — "Plan/PlanPrice split".
/// </summary>
public class PlanPrice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public BillingInterval Interval { get; set; }
    public decimal Price { get; set; }

    /// <summary>
    /// Account-specific — never hardcoded/reconciled by DataSeeder once set. Populated
    /// by StripeDemoSeeder or an admin via PlanManagementPage. Null means this
    /// interval is defined (shows in the admin's editor) but not yet purchasable
    /// online — see IsActive below for the distinct "temporarily disabled" case.
    /// </summary>
    public string? StripePriceId { get; set; }

    /// <summary>
    /// Lets an interval be retired (hidden from SubscribePage, rejected by checkout)
    /// without deleting pricing history for studios already on it.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public Plan Plan { get; set; } = null!;
}
