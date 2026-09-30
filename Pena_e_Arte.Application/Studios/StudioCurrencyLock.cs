using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;

namespace Pena_e_Arte.Application.Studios;

/// <summary>
/// A studio's currency locks the moment money has moved in it — the first Payment, GiftCard,
/// PackagePurchase or BoothRentCharge row. Shared by UpdateStudioCurrencyCommand (which rejects a
/// change once locked) and every place that reports CurrencyLocked on StudioResponse, so the rule
/// is defined exactly once. See docs/claude/architecture.md Decisions Log, "Studio currency".
/// </summary>
public static class StudioCurrencyLock
{
    /// <summary>
    /// IgnoreQueryFilters() is deliberate: the tenant filter also hides soft-deleted rows, and a
    /// soft-deleted payment still means money moved in this currency. Tenant scope is re-applied
    /// explicitly on every line. Registered in architecture.md "IgnoreQueryFilters() Approved
    /// Usages" as StudioCurrencyLock.
    /// </summary>
    public static async Task<bool> IsLockedAsync(IAppDbContext db, Guid studioId, CancellationToken ct)
        => await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.StudioId == studioId, ct)
        || await db.GiftCards.IgnoreQueryFilters().AnyAsync(g => g.StudioId == studioId, ct)
        || await db.PackagePurchases.IgnoreQueryFilters().AnyAsync(p => p.StudioId == studioId, ct)
        || await db.BoothRentCharges.IgnoreQueryFilters().AnyAsync(c => c.StudioId == studioId, ct);
}
