# Studio Currency — Backlog Specs (2026-09-27)

These are specified in full — entities, endpoints, migration shape, open questions — but
deliberately **not implemented** as part of the 2026-09-27 studio-currency work. Each needs a
real decision (mostly Finance/product, one Legal) before a single line of code is written.

---

## 1. Admin change of a locked studio's currency

Once a studio's currency locks (`StudioCurrencyLock` — first `Payment`, `GiftCard`,
`PackagePurchase`, or `BoothRentCharge` row), the owner can no longer change it in-app; the UI
sends them to support. This section specs the support/admin-side tool that would actually perform
that change. **Not built tonight.**

**Endpoint:** `POST /api/v1/platform/studios/{studioId}/currency`, `AdminOnly`,
`.RequireRateLimiting(...)` not needed (authenticated admin-only, low volume).

**Command:** `AdminChangeStudioCurrencyCommand(Guid StudioId, string NewCurrency, string Reason)`
— implements `IAuditableCommand` (new `AuditActions.StudioCurrencyChangedByAdmin` constant, distinct
from the existing owner-initiated `StudioCurrencyChanged`). `Reason` is required free text, stored
verbatim in the audit metadata — this is a rare, high-impact action and needs a human-readable
justification on record, unlike routine owner self-service changes.

**Data access:** reads `StudioCurrencyLock`'s underlying tables (`Payments`, `GiftCards`,
`PackagePurchases`, `BoothRentCharges`) cross-tenant to report *what* is being overridden, not just
*that* it's locked — needs its own `IgnoreQueryFilters()` approved-usage row in
`docs/claude/architecture.md`'s table (the existing row #54 is scoped to the lock check itself,
called with the ambient tenant; this is a cross-tenant admin read of a specific `studioId` the
admin does not belong to).

**Migration shape:** none required — `Studio.Currency`/`CountryCode` already exist; this is a
command-only addition. Optionally, a new `Studio.PricesNeedReviewSince` (`DateTime?`) column if
open question (d) below resolves toward "flag for review" rather than "leave" or "wipe."

**Open questions (Finance, not engineering):**
- **(a) Existing gift cards and package purchases denominated in the old currency.** Refund
  outstanding balances? Convert at a dated FX rate (see §3)? Leave the balance/sessions intact and
  simply block further redemption until the client and studio sort it out manually?
- **(b) Upcoming appointments whose `Appointment.DepositAmount` was quoted (and possibly already
  paid) in the old currency.** A `Payment` row keeps its own currency permanently regardless (by
  design — see the Decisions Log), but a *pending, unpaid* `DepositAmount` on a future appointment
  was set assuming the old currency; does it need re-quoting, and does the client need to be
  notified before their card is charged in a different currency than they expected at booking?
- **(c) `BoothRentSchedule.AmountFixed`** (a price *setting*, no currency of its own) — same
  question as price settings generally: does it silently start reading in the new currency (the
  default "price settings inherit" rule), or does an admin-forced change warrant a one-time
  reconfirmation prompt to the owner given they didn't choose this change themselves?
- **(d) Do other price settings** (`Service.Price`, `DepositRule.AmountFixed`, `PromoCode.AmountFixed`,
  `Package.Price`, `Artist.HourlyRate`) get wiped to force re-entry, flagged for review via a new
  `Studio.PricesNeedReviewSince` timestamp (owner sees a banner until they've reviewed each one),
  or left as-is (numbers unchanged, now read in the new currency — the same behavior as a
  voluntary pre-lock change, just happening to a studio that no longer controls the decision)?
- **(e) Owner notification.** Email, in-app banner, both? An admin overriding a locked financial
  setting on someone else's studio is exactly the kind of action that needs to be surfaced to the
  owner proactively, not discovered later by accident.

---

## 2. Multi-currency inside one studio

**Rejected 2026-09-27; revisit only with a new Finance decision.** A studio prices and charges in
exactly one currency, full stop — no per-service or per-artist currency override. Out of scope,
not specified further.

---

## 3. "≈" client-currency estimate at checkout

A future, purely informational "≈ $54 USD" estimate shown alongside the studio's real
currency-denominated amount — **never feeding any stored or charged amount.** Not built tonight;
today's checkout shows only the studio's own currency, full stop.

**New entity:** `FxRate(Guid Id, string FromCurrency, string ToCurrency, decimal Rate, DateTime AsOf, string Source)`
— a dated snapshot, not a live rate. `(FromCurrency, ToCurrency, AsOf)` unique. Populated by a
scheduled Hangfire job (daily), not fetched synchronously per request.

**New endpoint:** `GET /api/v1/public/fx-estimate?from={studioCurrency}&to={clientCurrency}&amount={amount}`
— `AllowAnonymous`, rate-limited (`public-read`), returns the latest `FxRate` for the pair plus the
converted estimate and its `AsOf` date. Needs its own row in the AllowAnonymous Exceptions table.

**Display rule:** the estimate is always labelled clearly as an estimate (e.g. "≈ $54 USD, rates as
of 26 Sep") and rendered visually secondary to the real, charged amount. It must never be
selectable as "the amount to pay," never passed to `IPaymentProvider`, and never stored on a
`Payment` row.

**Provider options (not chosen — list only):**
- **ECB reference rates** — free, daily, EUR-based (every rate is `EUR → X`, so a non-EUR-studio
  estimate needs a two-hop conversion through EUR). No API key, no cost, no new secret to manage.
- **Bank of Albania daily fixing** — authoritative for `ALL`, matches what a client physically
  comparing to lek would see quoted locally; narrower currency coverage than ECB.
- A commercial FX API (not named) if broader live coverage is ever needed — this would be a new
  third-party integration and must be flagged explicitly per the no-new-integrations rule; ECB/BoA
  are free public data feeds, not vendor integrations, so they don't trigger that flag the same way.

---

## 4. Cross-studio payment totals for the admin/platform role

**Verified via `git grep -n "Payment" -- Pena_e_Arte.Application/Platform`: nothing in the
platform-admin surface reads the Flow A `Payments` table cross-studio today** — the only
cross-studio money reads there are `SubscriptionInvoicePayments` (Flow B, platform's own EUR
subscription revenue, already single-currency by design and unaffected by this work). This section
specs what a *future* Flow A cross-studio total would need, in case that's ever requested — no
existing feature needs to change.

**If/when built:** any admin-facing view that sums Flow A payment amounts across studios of
different currencies must convert each to a single reporting currency (EUR) using a dated `FxRate`
(§3) and label the total explicitly as an estimate (e.g. "≈ €12,400 across 6 currencies, rates as
of 26 Sep") — never a bare, unlabelled sum, and never silently summing raw amounts across
currencies (the exact bug class the per-studio revenue report was built to avoid — see the
Decisions Log's "Studio currency" row).

---

## 5. Additional card providers for non-ALL/EUR-currency studios

`IPaymentProvider.Capabilities.SupportedCurrencies` already gates card availability per provider,
per currency — adding a second provider for currencies POK doesn't support requires no change to
that gating mechanism, only a new `IPaymentProvider` implementation and the studio-side connection
UI to configure it (mirroring `PokSettingsCard`). Not specified in detail — this section only
confirms the extension point already exists and lists candidate providers as open, unevaluated
options for whenever a studio outside `ALL`/`EUR` actually needs card support:

- A direct Stripe account per studio (Stripe's Albania block is specific to this entity and to
  Stripe Connect's sub-merchant model — an unrelated foreign studio opening its own ordinary
  Stripe account was not independently ruled out by that finding, per
  `ADR-0001-amendment-B-flow-a-zero-commission.md`'s "What this amendment does not decide").
- Adyen (documents its own ISO-4217-minor-unit deviations explicitly, same shape
  `IPaymentProvider` already assumes per-provider).
- Any other regional EMI/PSP with per-merchant-account onboarding, evaluated case-by-case per the
  studio's actual country — no single processor needs to span every currency, since each provider
  is wired in independently behind the same interface.

None of these are evaluated, contacted, or chosen by this spec — purely a list of where to start
looking if this becomes a real, prioritized need.
