namespace Pena_e_Arte.Application.Billing;

/// <summary>
/// D7 — the yearly saving is computed from real prices, never typed or read from
/// Plan.YearlyDiscountPercent. Shared by GetPlansHandler (owner-facing plan catalogue)
/// and TrialExpiryWarningJob (trial warning email copy) so the rule lives in one place.
/// See architecture.md Decisions Log.
/// </summary>
public static class YearlySavingCalculator
{
    /// <summary>Null when Monthly is 0 or the computed saving isn't positive.</summary>
    public static decimal? MonthsFree(decimal monthly, decimal yearly)
    {
        if (monthly <= 0) return null;
        decimal saving = monthly * 12 - yearly;
        return saving > 0 ? Math.Round(saving / monthly, 2) : null;
    }

    /// <summary>Floored, so it never overstates the saving. Null under the same conditions as MonthsFree.</summary>
    public static int? PercentFloor(decimal monthly, decimal yearly)
    {
        if (monthly <= 0) return null;
        decimal saving = monthly * 12 - yearly;
        return saving > 0 ? (int)Math.Floor((1 - yearly / (monthly * 12)) * 100) : null;
    }
}
