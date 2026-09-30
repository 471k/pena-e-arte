# Runbook — Studio Currency Migration (2026-09-27)

> Read-only checks a human must run **against production before deploying** the
> `AddStudioCurrency` migration (`Pena_e_Arte.Infrastructure/Migrations/20260927185937_AddStudioCurrency.cs`),
> plus the staging card-payment checklist for the POK amount-unit question (§3.1 of
> `docs/claude/overnight-prompt-studio-currency-2026-09-27.md`).

## 1. Before deploying: run these against production (read-only, safe)

```sql
-- 1a. Real POK money that must stay ALL (expected: 0 rows in production — Finance states no
--     live card payments have run yet; staging may have sandbox rows).
SELECT Id, StudioId, Status, Currency, CreatedAt FROM payments
 WHERE Provider = 'pok' AND ProviderReferenceId IS NOT NULL AND Status IN ('Captured','Paid','Refunded');

SELECT Id, StudioId, Status, Currency, CreatedAt FROM gift_cards
 WHERE Provider = 'pok' AND ProviderReferenceId IS NOT NULL AND Status <> 'Pending';

SELECT Id, StudioId, ConfirmedAt FROM package_purchases WHERE Provider = 'pok' AND ConfirmedAt IS NOT NULL;

-- 1b. Studios whose map pin is outside Albania (their CountryCode will be set to AL regardless —
--     the migration has no network to reverse-geocode; an admin should verify these by hand).
SELECT Id, Name, City, Latitude, Longitude FROM studios
 WHERE NOT (Latitude BETWEEN 39.6 AND 42.7 AND Longitude BETWEEN 19.2 AND 21.1)
   AND NOT (Latitude = 0 AND Longitude = 0);
```

**If query 1a returns rows**, those were really charged in lek; the migration leaves them as
`ALL` (it never relabels a real POK-processed charge). Confirm each one with the studio before
deploying — do not treat a nonzero result as a migration bug.

**Studio names in query 1b are business names, not personal data**, but do not paste query
results into chat tools or tickets outside this runbook's process regardless.

## 2. What the migration does (for the person running it)

1. Adds `studios.CountryCode`/`Currency`, `gift_cards.Currency`, `booth_rent_charges.Currency`,
   `package_purchases.Currency`/`Amount` as nullable columns.
2. Widens every Flow A money column (deposits, prices, hourly rates, gift-card balances, session
   splits, booth-rent amounts) from `decimal(18,2)` to `decimal(18,4)`. Flow B / subscription
   columns are untouched.
3. Backfills: every studio gets `CountryCode = 'AL'`, `Currency = 'EUR'`; every payment/gift-card/
   package-purchase gets `EUR` **except** a real POK-processed row (query 1a above), which keeps
   `ALL`; `package_purchases.Amount` is backfilled from the purchased package's current price.
4. Locks the new columns to `NOT NULL`.
5. Drops the `DEFAULT 'ALL'` MySQL left on `payments.Currency` — a payment inserted without an
   explicit currency now fails loudly instead of silently becoming lek.

Every backfill statement is guarded (`WHERE col IS NULL`, or "still labelled ALL and not a real
POK charge") so **running the migration twice changes nothing the second time** — verified by an
integration test (`tests/Pena_e_Arte.IntegrationTests/Infrastructure/StudioCurrencyMigrationTests.cs`).

`Down()` reverses the schema only. It does **not** attempt to restore the old `'ALL'` labels —
those labels were simply wrong (every existing price was typed into a field labelled €), so there
is nothing correct to roll back to.

## 3. §3.1 — POK amount-unit staging checklist (before enabling any studio for live card deposits)

`PokPaymentProvider`'s highest-risk unverified assumption: does POK's `amount` field expect
whole-unit decimals (`50.00`) or minor-unit integers (`5000`)? Tonight's change keeps the current
whole-unit assumption (now centralised in `PokPaymentProvider.ToPokAmount`), but does **not**
verify it against a real transaction. Before any studio takes a live card deposit:

1. Pick one already-connected staging studio (POK sandbox merchant).
2. Create a real deposit hold for a small, distinctive amount (e.g. 12.34 EUR or 1234 ALL).
3. Compare what POK's sandbox dashboard/API actually recorded as charged against what the app
   sent. If POK recorded 100x or 1/100x the intended amount, the assumption is wrong — fix
   `PokPaymentProvider.ToPokAmount` only (it is the single method that owns this conversion).
4. Only after this checklist passes should any studio be allowed to take a live card deposit.
   **No studio should be enabled for live card deposits until this is done** — this is a release
   gate, not a code change.
