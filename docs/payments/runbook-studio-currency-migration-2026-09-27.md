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

### §3.1 result — POK staging release gate, run 2026-10-14 (Day 2: 2026-10-15)

**Environment:** local docker-compose stack at commit `068952ea` (branch
`fix/pok-staging-release-gate-2026-10-07`), POK staging (`https://api-staging.pokpay.io`),
dev-mode Vault, webhook callback through a Cloudflare tunnel. Operator: Phi (POK keys, POK
dashboard checks, every money click approved with "go"). Driver/observer: Claude Code (Playwright on
localhost, the two published POK test cards typed with Phi's approval and staging-only guards, API
logs, read-only DB). Prompt: `docs/claude/overnight-prompt-pok-staging-release-gate-2026-10-07.md`.

**Verdict:** GATE PASSED for ALL; EUR PASSED.
**Go-live: NOT approved by this gate** — remaining blockers in
`docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md`.

| Test | App sent / did | POK recorded | POK flags (confirmed / canBeCaptured / captured / completed / canceled / refunded) | App state after | Result |
|---|---|---|---|---|---|
| T9 aging setup | hold 1234 ALL | amount 1234 ALL, "awaiting_capture" | true / true / false / false / false / false | Captured (held), HoldExpiresAt = order expiresAt | PASS (aging: see below) |
| T9b +8 day setup | hold 1234 ALL | amount 1234 ALL | true / true / false / false / false / false | Captured (held) | PASS (capture ≥ 2026-10-22) |
| T1 hold | 1234 ALL | amount 1234, finalAmount 1234 ALL, no conversion; dashboard "On hold" | true / true / false / false / false / false | Captured (held), PaidAt NULL | PASS |
| T2 full capture | Capture deposit | capturedAmount 1234; dashboard "Paid" | — / false / true / true / false / false | Paid, PaidAt set, DepositStatus Paid | PASS |
| T3 partial refund | refund 234 | partial refund −234.00 ALL ("Partially refunded"; 228.15 + 5.85 bank commission) | — / false / true / true / false / false | Refunded, RefundedAmount 234 | PASS |
| T4 cash switch | declare cash on an unpaid order | order canceled | false / false / false / false / true / false | CashPending, Cash, no order | PASS |
| T5a studio cancel (B3) | cancel appointment, held deposit | hold released, nothing captured | — / false / false / false / true / false | Refunded 1234, appointment Cancelled/Refunded | PASS |
| T5b late client cancel (B3) | cancel, 50 % rule | capturedAmount 617, rest released | — / false / true / true / false / false | Refunded 617, PaidAt set, Cancelled/Refunded | PASS |
| T6 paid on POK's page (B1) | nothing (never confirmed by the app) | authorised on `pay-staging.pokpay.io` ("Pay 1,234.00 ALL") | true / true / false / false / false / false | webhook → reconciliation "healed to held" → Captured; re-test 2026-10-15: after paying, POK redirects to its default `redirectUrl` (doesn't load, G-12) | PASS |
| T7 webhook | — | one call per authorisation; none on capture, refund, partial capture, cancel | — | each call → reconciliation run, 200 | OBSERVED |
| T8 3-D Secure fail/abandon | test card ending 1091, wrong codes; re-test 2026-10-15: 10 wrong codes, then the challenge's CANCEL (below its visible area) | still "created" | false / false / false / false / false / false | Pending, unchanged; after CANCEL POK's form shows "Error! Something went wrong!" | PASS |
| T1E hold EUR | 12.34 EUR | amount 12.34 EUR, originalCurrencyCode EUR, rate 1 | true / true / false / false / false / false | Captured (held) 12.34 EUR | PASS |
| T2E capture EUR | Capture deposit | capturedAmount 12.34 | — / false / true / true / false / false | Paid | PASS |
| T3E refund EUR | refund 2.34 | partial refund −2.34 EUR (2.28 + 0.06 commission) | — / false / true / true / false / false | Refunded, RefundedAmount 2.34 | PASS |

"—" = not returned by POK's public read (the app's own flag log has it for reads it made).

**Held-state shape observed:** `{isConfirmed:true, canBeCaptured:true, isCaptured:false,
isCompleted:false, isCanceled:false, isRefunded:false, capturedAmount:0, status:"awaiting_capture"}`
— the pre-gate `MapStatus` would not have mapped it to Captured (isCompleted stayed false); the
held-before-paid precedence is kept anyway, as POK's own dashboard uses it.
**Amount unit:** whole units confirmed on create, full capture, partial capture and partial refund
for ALL and EUR. `ToPokAmount` unchanged.
**Webhook:** received for authorisations only (T1, T5a, T5b, T6, T9, T9b, T1E; re-test: T8, G1b, G10b, G3,
E2), each within ~3 s; none for capture, refund, partial capture, cancel, cash switch or a failed/cancelled
3-D Secure (re-tested 2026-10-15).
**Aging probe:** PASS — T9 (dd7e1fce, authorised 2026-10-14 15:07:39Z, `expiresAt` 2026-10-15 14:37:42Z) captured
2026-10-15 17:13:09Z, i.e. **26 h 05 min after authorisation and 2 h 35 min after the order expired**: app 200 "Paid",
toast "Deposit captured.", DB Paid; POK merchant read `paid`, captured 1234, net 1183.15; POK Lek balance
+1,183.15. Before the capture the public read already returned 410 "Order is expired", while the merchant read
showed `awaiting_capture` / `can.capture true` and the frozen balance still held the funds. The 02:00 UTC
reconciliation run (15 Oct) ran before `expiresAt` and didn't touch T9.
T9b at +8 days: pending (from 2026-10-22 15:36Z).
**Probe run, 2026-10-16** (direct POK staging orders R2-R4 + one app booking R1; T9b untouched; end balances matched exactly): refund on an uncaptured hold is rejected (400 "Order not eligible for refund"); several partial refunds are allowed and the one that completes the amount returns the whole fee incl. the fixed 20 ALL; `redirectUrl`/`failRedirectUrl` are accepted and honoured after POK's own result screen (G-12 unblocked); displayed fee lines are rounded half-up one by one. Details: go-live blockers doc, C-6 "Probe run 2026-10-16".
**Re-test of every gate step, 2026-10-15** (new bookings and test-card payments; each money click on Phi's "go";
POK balances read by Phi before and after): capture (T8, T6, T9, EUR E2), full refund (T8), partial refund (T6,
E2), owner cancel releasing a hold (G1b), client late cancel with a 50 % rule (G10b: 500 of 1,000 captured) and
with no rule (G3: whole deposit captured), cash switch (C2), hosted-page payment and 3-D Secure cancel — all PASS,
and POK's Lek and Euro balances matched the predictions to the cent (Lek 4,258.22, Euro 30.93 at the end).
New facts: a **full** refund returns POK's whole commission, fixed 20 ALL included (partial refunds return only the
2.5 % share); the 3-D Secure challenge has a Cancel button below its visible area; POK redirects hosted-page
payers to its own default `redirectUrl`. New follow-ups G-10 to G-15 are in the blockers doc.
**Fixes made in this run:** 1817d414 MapStatus precedence + flag log; fc41a5fb B1+B2 reconciliation
asks POK first; a58b229b B3 cancel releases/partly captures a hold; afcaadf1 B4 no expired-order
resume + 24 h link expiry; 1de7fcdb B5 "On hold" label; a1e52c26 B6 PaidAt on Complete/No-show;
068952ea observed held fixture.
**Evidence hygiene:** order references are 8-character prefixes; no keys, merchant id, emails or
card data appear in this repo. Full evidence is in Phi's local gate folder.
