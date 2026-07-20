# Bug Report: Issuer "Plans" Page Shows Stale/Incorrect Plan Data

**Reported by:** Phi
**Page:** Platform Admin → Plans (`/platform/plans` or equivalent issuer route)
**Severity:** High — issuer-facing dashboard is showing numbers that do not match the
canonical plan definitions in `DataSeeder.cs`, which will mislead the issuer about what
studios are actually entitled to and could misinform pricing/support decisions.

---

## 1. Summary

The Plans page in the issuer dashboard displays two distinct data problems:

1. **All four paid plans (Starter, Growth, Premium, Pro) show "Unlimited" for Artists,
   Appointments/mo, and Storage (GB)** — both on the summary card and inside the edit
   form (placeholder text "Unlimited" sits in empty inputs). Only the **Free** plan
   shows real numbers (1 artist · 15 appts/mo · 1 GB).
2. **Premium's pricing is wrong**: the card shows **€30/mo · €200/yr · "Save 44%
   annually" · "Billed yearly only"**. The current seed source (`DataSeeder.cs`)
   defines Premium at **€79/mo · €790/yr · 17% yearly discount**, with **both** a
   Monthly and a Yearly row (`PremiumMonthlyPlanId` / `PremiumYearlyPlanId`), not a
   single yearly-only plan.

Both issues point to the same root cause: **stale seed data that was never
re-applied**, not a rendering bug. See Root Cause below.

---

## 2. Observed vs. Expected (per plan)

Expected values below are taken directly from
`Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs`, `SeedPlansAsync()` (lines
147–239) and `SeedFreePlanAsync()` (lines 245–262) — this is the canonical source for
seeded plan data.

| Plan | Field | Observed on screen | Expected (current `DataSeeder.cs`) | Match? |
|---|---|---|---|---|
| Free | Artists / Appts/mo / Storage | 1 / 15 / 1 GB | 1 / 15 / 1 GB | ✅ |
| Starter | Price | €29/mo, €290/yr, 17% | €29/mo, €290/yr, 17% | ✅ |
| Starter | Artists / Appts/mo / Storage | Unlimited / Unlimited / Unlimited | 1 / 40 / 2 GB | ❌ |
| Starter | Notifications/mo, Locations (not shown on card) | n/a | 150, 1 | — |
| Growth | Price | €59/mo, €590/yr, 17% | €59/mo, €590/yr, 17% | ✅ |
| Growth | Artists / Appts/mo / Storage | Unlimited / Unlimited / Unlimited | 3 / 150 / 10 GB | ❌ |
| Premium | Price / discount / billing | €30/mo (ref.), €200/yr, "Save 44%", "Billed yearly only" | €79/mo, €790/yr, 17% — **two rows**: Monthly (`PremiumMonthlyPlanId`) and Yearly (`PremiumYearlyPlanId`), linked via `PairedPlanId` | ❌ |
| Premium | Artists / Appts/mo / Storage | Unlimited / Unlimited / Unlimited | 6 / 400 / 25 GB | ❌ |
| Pro | Price | €99/mo, €990/yr, 17% | €99/mo, €990/yr, 17% | ✅ |
| Pro | Artists / Appts/mo / Storage | Unlimited / Unlimited / Unlimited | 10 / 1000 / 50 GB | ❌ |

Note: Starter/Growth/Pro **prices** are correct on screen — it's specifically the
**Max\* limit fields** and **Premium's price/discount** that are wrong.

---

## 3. Root Cause (confirmed in source, not just inferred from the UI)

`DataSeeder.cs` has an idempotency guard that skips re-seeding once plans already
exist:

```csharp
// SeedAsync(), lines 129–137
// Always run: the Free plan is seeded independently of the one-time entity seed
// guard below, so a database that already has Starter/Growth/etc. still picks it
// up on the next deploy without re-running the full seed.
if (!await db.Plans.AnyAsync(p => p.Id == FreePlanId))
    await SeedFreePlanAsync(db);

// Guard: run entity seeding only once (when plans don't yet exist)
if (await db.Plans.AnyAsync(p => p.Id == StarterPlanId))
    return;

await SeedPlansAsync(db);   // <-- never runs again once Starter already exists
```

`SeedPlansAsync()` is the method that defines Starter/Growth/Premium/Pro, including
`PriceMonthly`, `PriceYearly`, `YearlyDiscountPercent`, and all five `Max*` fields
(`MaxArtists`, `MaxAppointmentsPerMonth`, `MaxNotificationsPerMonth`, `MaxStorageGb`,
`MaxLocations`). Because of the early `return` on line 137, **this method only ever
executes once, on the very first deploy against a fresh database.** Any change made to
the values inside `SeedPlansAsync()` after that first deploy — including the
`Max*` fields being added later (Feature #24, "Plan Usage Limits") and Premium's price
apparently being corrected from a placeholder (€30/€200/44%) to €79/€790/17% — **never
reaches an environment where Starter already exists in the `Plans` table.**

`SeedFreePlanAsync()` is guarded independently (its own `FreePlanId` check, line 132),
by design, specifically so the Free tier could be added retroactively to already-seeded
databases (per the inline comment). That's why **Free is the only plan showing correct,
non-null `Max*` values** — it's the only one seeded after those fields existed. The
other four plans' rows in this environment's database were written before `Max*` was
part of `SeedPlansAsync()` (or before Premium's pricing was corrected), and the guard on
line 136 has been silently preventing any update from landing since.

Per `docs/claude/architecture.md` (line ~998), `Max*` fields are `int?`, and **`null` is
defined to mean "unlimited."** That is exactly what the frontend is displaying — it is
correctly rendering `null` as "Unlimited." The bug is that these fields are `null` in
the database for Starter/Growth/Premium/Pro, not that the frontend is mis-rendering
non-null values.

**In short: this is a data/migration gap, not a display logic bug.** The issuer's
database is running an outdated snapshot of `SeedPlansAsync()`'s output that predates
both the `Max*` limit fields and Premium's corrected pricing.

---

## 4. Recommended Fix

Do not just re-run `SeedPlansAsync()` — this environment's `Plans` rows already exist
and it will no-op per the guard. Options for the team to choose between:

1. **One-time data migration / backfill script** that updates the existing Starter,
   Growth, Premium (both rows), and Pro rows in place to match current
   `SeedPlansAsync()` values (safest — no seeding-logic changes, works for any
   environment already in this stale state, including production if it's affected).
2. **Change the seeding strategy** from "insert-once, skip forever" to an
   upsert/reconcile pattern (match by `Id`, update mutable fields like `Max*`, price,
   and discount if they differ) — same pattern already used for the Free plan, applied
   to `SeedPlansAsync()` too. More durable long-term, prevents this class of drift from
   recurring, but is a bigger change and needs a decision on which fields are
   safe to overwrite (e.g., should it ever overwrite a `Plan.Id` an issuer has since
   hand-edited via `UpdatePlanCommand`?).
3. At minimum, **add a startup/deploy-time check** (or extend the existing
   `GetPlanUsageReportHandler` / issuer reports tooling) that flags when a live `Plan`
   row's values diverge from the current seed source, so this doesn't silently
   reappear after the next seed value change.

Whichever approach is chosen, please also confirm/decide the **Premium display
question**: should the Plans page show one card per billing interval (Monthly +
Yearly, matching the two actual `Plan` rows and their `PairedPlanId` link), or one
merged card with a toggle? Right now it renders a single card labeled "Billed yearly
only," which doesn't match how the other three tiers are shown (each as a single
monthly-only card with a yearly reference line) and doesn't reflect that Premium
actually has two independent `Plan` rows in the database.

---

## 5. Other Discrepancy Worth a Second Look (not confirmed as a bug)

The small person-icon counts in the top-right of each card (Free: 0, Starter: 6,
Premium: 1, Growth: 1, Pro: 1) weren't verified against real subscription data for this
report — flagging only so the team can confirm whether that number is
supposed to represent active subscribed studios per plan and, if so, whether it's
reading from the right source.

---

## 6. Verification Steps (for whoever picks this up)

1. Query the `Plans` table directly for this environment and confirm: Starter/Growth/
   Premium/Pro rows have `NULL` in `MaxArtists`, `MaxAppointmentsPerMonth`,
   `MaxNotificationsPerMonth`, `MaxStorageGb`, `MaxLocations`; and Premium's
   `PriceMonthly`/`PriceYearly`/`YearlyDiscountPercent` read `30`/`200`/`44` (or
   similar) rather than `79`/`790`/`17`.
2. Confirm there are exactly two Premium rows (`PremiumMonthlyPlanId`,
   `PremiumYearlyPlanId`) or just one, to settle the display question in §4.
3. After the fix, reload the Plans page and confirm all five cards show numeric
   values (not "Unlimited") for Artists/Appointments/Storage, and Premium reads
   €79/mo · €790/yr · 17%.
