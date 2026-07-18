using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Plan
{
    public Guid            Id                    { get; init; } = Guid.NewGuid();
    public string          Name                  { get; set; }  = string.Empty;
    public BillingInterval BillingInterval       { get; set; }
    public decimal         PriceMonthly          { get; set; }
    public decimal         PriceYearly           { get; set; }
    public int             YearlyDiscountPercent { get; set; }  = 17;
    public bool            AllowBrandingRemoval  { get; set; }  = false;
    public string?         StripePriceIdMonthly  { get; set; }
    public string?         StripePriceIdYearly   { get; set; }
    public DateTime        CreatedAt             { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Plan feature/usage limits. Null means unlimited (no PlanLimitBehavior check for
    /// that dimension). See docs/claude/architecture.md Decisions Log — "Plan usage limits".
    /// </summary>
    public int?  MaxArtists             { get; set; }
    public int?  MaxAppointmentsPerMonth { get; set; }
    public int?  MaxNotificationsPerMonth { get; set; }
    public int?  MaxStorageGb           { get; set; }
    public int?  MaxLocations           { get; set; }
    public bool  AllowApiAccess         { get; set; } = false;
    public bool  PrioritySupport        { get; set; } = false;

    /// <summary>
    /// Points to the sibling row that represents the same feature tier at the other
    /// billing interval (e.g. "Growth" Monthly ↔ "Growth" Yearly). BillingInterval stays
    /// locked per-row by design (see Decisions Log — "Plan billing interval stays
    /// locked per-row"); this field only links the pair so UpdatePlanHandler can keep
    /// their limit/feature fields in sync without touching price or Stripe IDs. Null for
    /// a plan with no yearly/monthly counterpart.
    /// </summary>
    public Guid? PairedPlanId           { get; set; }

    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
