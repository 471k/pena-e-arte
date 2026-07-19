namespace Pena_e_Arte.Domain.Entities;

public class Plan
{
    public Guid     Id                    { get; init; } = Guid.NewGuid();
    public string   Name                  { get; set; }  = string.Empty;

    /// <summary>
    /// Marketing/display figure for "Save X% annually" copy and the issuer editor's
    /// suggested-yearly-price helper — NOT itself a price. Real prices live on
    /// PlanPrice, one row per interval this tier actually offers. See architecture.md
    /// Decisions Log — "Plan/PlanPrice split".
    /// </summary>
    public int      YearlyDiscountPercent { get; set; }  = 17;
    public bool     AllowBrandingRemoval  { get; set; }  = false;
    public DateTime CreatedAt             { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Plan feature/usage limits. Null means unlimited (no PlanLimitBehavior check for
    /// that dimension). See docs/claude/architecture.md Decisions Log — "Plan usage limits".
    /// </summary>
    public int?  MaxArtists              { get; set; }
    public int?  MaxAppointmentsPerMonth { get; set; }
    public int?  MaxNotificationsPerMonth { get; set; }
    public int?  MaxStorageGb            { get; set; }
    public int?  MaxLocations            { get; set; }
    public bool  AllowApiAccess          { get; set; } = false;
    public bool  PrioritySupport         { get; set; } = false;

    public ICollection<PlanPrice>    Prices        { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
