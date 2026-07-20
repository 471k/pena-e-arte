# Bug Report: Duplicate "Premium" Plan Card — Orphaned Legacy Row Not Cleaned Up

**Reported by:** Phi
**Page:** Platform Admin → Plans
**Related to:** `bug-report-plans-page-data-mismatch.md` (this is a side effect of that
fix landing, not a new unrelated bug)
**Severity:** Medium-High — issuer-facing dashboard shows an extra, incorrect Premium
card, and it's still attached to a live `Subscription`, so it can't be deleted
without a migration.

---

## 1. Summary

The Plans page now shows **three** cards named "Premium" instead of two:

1. A card labeled "Billed yearly only," **€200/yr · €30/mo ref. · Save 44%
   annually**, **Unlimited** artists/appointments/storage, subscriber count **1**.
   This is the pre-fix, stale data.
2. A card labeled "Billed monthly," **€79/mo · €790/yr ref. · Save 17% annually**,
   **6 artists · 400 appts/mo · 25 GB**, White-label + Priority support, subscriber
   count **0**.
3. A card labeled "Billed yearly only," **€79/mo ref. · €790/yr · Save 17%
   annually**, same limits as #2, subscriber count **0**.

Cards #2 and #3 are correct — they match `PremiumMonthlyPlanId` /
`PremiumYearlyPlanId` in the current `DataSeeder.cs`. Card #1 is a **leftover row
from before Premium was split into Monthly/Yearly rows**, and the plan-reconciliation
fix that corrected everything else did not, and structurally cannot, touch it.

---

## 2. Root Cause (confirmed in source)

`ReconcileCorePlansAsync()` (`Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs`,
lines 180–304) replaced the old one-time seed guard. It upserts exactly five plans,
matched **by a fixed set of hardcoded IDs**:

```csharp
// lines 19–24
internal static readonly Guid StarterPlanId        = new("aaaa0001-0000-0000-0000-000000000000");
internal static readonly Guid GrowthPlanId         = new("aaaa0002-0000-0000-0000-000000000000");
internal static readonly Guid ProPlanId            = new("aaaa0003-0000-0000-0000-000000000000");
internal static readonly Guid PremiumMonthlyPlanId = new("aaaa0004-0000-0000-0000-000000000000");
internal static readonly Guid PremiumYearlyPlanId  = new("aaaa0005-0000-0000-0000-000000000000");
```

```csharp
// lines 271–301
Guid[] canonicalIds = canonical.Select(p => p.Id).ToArray();
Dictionary<Guid, Plan> existingById = await db.Plans
    .Where(p => canonicalIds.Contains(p.Id))
    .ToDictionaryAsync(p => p.Id);

foreach (Plan source in canonical)
{
    if (existingById.TryGetValue(source.Id, out Plan? row))
    {
        // ... overwrite mutable fields on the existing row
    }
    else
    {
        db.Plans.Add(source);   // <-- inserts a brand-new row
    }
}
```

Before this fix, Premium existed as **one row under a different, pre-split ID** (this
was before the "Plan billing interval stays locked per-row" decision introduced two
separate Premium rows — see Decisions Log in `architecture.md`). That legacy row's ID
is not `PremiumMonthlyPlanId` or `PremiumYearlyPlanId`, so:

- The `Where(p => canonicalIds.Contains(p.Id))` query never selects it.
- The `foreach` loop never sees it, so it's neither updated nor deleted.
- Both `PremiumMonthlyPlanId` and `PremiumYearlyPlanId` were missing from the
  database (since Premium had never existed as two rows before), so the `else`
  branch fired for both — **inserting two new rows** alongside the untouched legacy
  one.

Net result: 1 old orphaned Premium row (stale price/discount, null limits → shown as
"Unlimited") + 2 new correct Premium rows = 3 cards.

**This is expected, mechanical behavior given how the reconciler is scoped — not a
new logic bug.** The reconciler was written to fix drift on five *known* IDs; it was
never intended to discover or retire rows outside that set. That's a real gap, just
not the same kind of bug as the original one.

---

## 3. Why It Can't Just Be Deleted

The legacy row shows a subscriber count of **1**, meaning at least one
`Subscription.PlanId` in the database still points at it. `DataSeeder`'s own demo data
does not wire any subscription to a Premium plan at all (its two seeded subscriptions
point to `GrowthPlanId` and `StarterPlanId` — see lines 354 and 383), so **this
subscription is real, non-seed data** — likely created by manual testing on this
issuer's environment, or by whatever process originally exercised the pre-split
Premium plan. Either way, deleting the row directly would either violate the FK
constraint on `Subscription.PlanId` or (if the constraint allows nulls/cascades)
silently break that studio's subscription. Do not delete without reassigning first.

---

## 4. Recommended Fix

### Step 1 — Identify the orphan(s)

Don't hardcode an assumed legacy ID — query for it, since other environments (or a
second bad Premium row) may not share the exact same GUID this environment has:

```sql
SELECT * FROM Plans
WHERE Name = 'Premium'
  AND Id NOT IN (
    'aaaa0004-0000-0000-0000-000000000000', -- PremiumMonthlyPlanId
    'aaaa0005-0000-0000-0000-000000000000'  -- PremiumYearlyPlanId
  );
```

Run this against every environment (staging, prod, any other issuer instance), not
just the one in the screenshot — any environment that had Premium seeded before the
Monthly/Yearly split will have the same orphan.

### Step 2 — Reassign any live subscriptions off the orphan row(s)

```sql
SELECT * FROM Subscriptions WHERE PlanId IN (/* orphan Plan IDs from Step 1 */);
```

For each match, reassign `PlanId` to `PremiumMonthlyPlanId` or `PremiumYearlyPlanId`
based on the **orphan row's own `BillingInterval`** (the screenshot's "Billed yearly
only" label plus a €/yr-first price display strongly suggests the orphan's
`BillingInterval` is `Yearly` — but confirm from the actual row, don't assume from the
UI label alone). Do this as a real migration/script, not a manual UPDATE typed by
hand, so it's repeatable across environments and reviewable in a PR.

### Step 3 — Delete the orphan row(s)

Once no `Subscription` references the orphan ID(s), delete them. Confirm no other FK
(e.g. `ReferralRedemption`, `Payment`, anything else that might reference a
`PlanId`) points at them first — grep the codebase for `PlanId` foreign keys before
assuming `Subscriptions` is the only table involved.

### Step 4 — Guard against recurrence

Consider adding a lightweight check (could live in the same
`ReconcileCorePlansAsync`, or in the issuer plan-usage reporting tooling already
mentioned in the previous bug report) that flags any `Plan` row named "Starter,"
"Growth," "Premium," "Pro," or "Free" whose `Id` is **not** one of the six canonical
IDs. That turns "a duplicate silently reappears" into "a duplicate gets flagged on
the next report run."

---

## 5. Not a Bug — For Awareness Only

Starter's price may show something other than €29/mo (e.g. after a manual edit via
`PlanManagementPage`) until the next app restart/deploy. This is intentional,
documented behavior: `ReconcileCorePlansAsync` runs on every boot and forces Starter,
Growth, Premium (x2), and Pro back to their canonical values every time (see the
comment at lines 174–178 of `DataSeeder.cs`). If someone edited Starter for a test,
it will self-correct on the next restart — no fix needed, just don't be alarmed by it
mid-session.

---

## 6. Verification Steps

1. Run the Step 1 query in every environment; confirm zero rows returned after the
   fix ships.
2. Confirm the previously-orphaned subscription(s) now point at
   `PremiumMonthlyPlanId` or `PremiumYearlyPlanId` and that the affected studio's
   billing/plan display is unaffected (same price, same limits) from the studio
   owner's point of view.
3. Reload the issuer Plans page and confirm exactly **five** cards: Free, Starter,
   Growth, Premium (Monthly), Premium (Yearly), Pro — six total, one Premium
   duplicate resolved. (Six cards total, not five — Premium legitimately renders
   twice, once per billing interval; the bug was the *third*, orphaned card.)
4. Re-run whichever test created the original stray subscription (if known) to make
   sure it now lands on a canonical Premium row instead of creating a new orphan.
