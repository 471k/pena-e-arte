# Overnight Prompt — Studio Currency: One Currency per Studio, Stored on Every Money Record, Charged Exactly as Shown

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact files,
> exact current code (re-read from live source on 2026-09-27 against `main` at `475e7dc9`, the
> merge of PR #196), exact target behaviour, exact tests, exact docs to sync. **Read the whole
> file before writing anything.** Where this file quotes current code, it was copied from the
> live repo that day; if your checkout disagrees, trust your checkout, record the drift in your
> final report, and do not silently change approach.

**Date logged:** 2026-09-27
**Requested by:** Phi (Finance project → Engineering Consultation)
**Origin:** Finance's doc "Flow A Currency Mismatch — Risk Report" (tabs *Risk report* and
*Engineering hand-off*, 27 Sep 2026). Finance decided the business rule; this prompt is
Engineering Consultation's implementation design for it. §2 is decided — do not re-litigate it.
**Mode:** Fully autonomous, no user present. Do not stop to ask questions. Open product/legal
questions are in §3 with the default you must apply; surface them verbatim in the final report.
**Size:** Large (schema + ~20 backend handlers + 34 frontend files + Help). Seven phases, one
commit each. If you run out of time, stop at a phase boundary with a green build — every phase
below leaves the app working.

**Before starting**, run:

```bash
git checkout main && git pull
git add -A && git commit -m "checkpoint: before studio currency" --allow-empty
git checkout -b feature/studio-currency-2026-09-27
```

Commit at the end of each phase (§7–§13), using the commit message given at the end of each.

---

## 1. The problem, in one paragraph

Every studio-side screen labels and formats money as euros (34 frontend files hard-code `€`,
`"EUR"` or `Intl.NumberFormat("pt-PT", { currency: "EUR" })`), but the backend stores and
charges as Albanian lek: `CreateDepositPaymentCommand` hard-codes `DepositCurrency = "ALL"`,
`PurchaseGiftCardCommand` and `PurchasePackageCommand` pass a literal `"ALL"` to the provider,
`Payment.Currency` defaults to `"ALL"` (C# initializer **and** a MySQL column `DEFAULT 'ALL'`
left by migration `20260731194633`), and `CreatePaymentIntentCommand` sends the caller's
currency (`"EUR"` from the UI) to POK while saving the row with the default `"ALL"`. 1 EUR ≈
91.8 ALL, so the first live card deposit would charge ~1% (or ~100×) of what the client agreed
to. Cash is the only live path, so no one has been charged wrongly yet — but **every recorded
payment already carries the wrong currency label**. On top of that, every conversion to the
provider multiplies by a fixed 100, which is wrong for JPY (0 decimals) and KWD (3 decimals),
and the client checkout page shows an amount read from the **URL query string**, not the
server — so "charge exactly what was shown" is not currently enforceable at all.

Applicable `CLAUDE.md` rules: #1 (tenant isolation — every new query is tenant-scoped; the only filter bypasses are the
already-approved `BoothRentChargeJob` (§2.9) and the new, explicitly re-scoped currency-lock check (§2.7)), #2 (RBAC on the owner endpoint; one documented `AllowAnonymous` public read — §2.7), #3 (no PII in
logs — currency-exclusion logs carry ids and counts only), #5 (Serilog only), #6 (benchmark —
§12), #7 (Help sync — woven into every phase **and** consolidated in Phase G).

---

## 2. Decisions already made — implement exactly as specified

### 2.1 The business rule (Finance, 27 Sep 2026 — fixed)

1. **Each studio prices and charges in exactly one currency of its own** (`Studio.Currency`,
   ISO 4217).
2. It **defaults to the currency of the studio's country** (`Studio.CountryCode`, ISO 3166-1
   alpha-2). The owner may pick any other ISO 4217 currency instead.
3. **Clients pay in the studio's currency. The amount charged is exactly the amount shown.** No
   conversion anywhere in any charge path. (A "≈" estimate in another currency is out of scope
   tonight — §4.3.)
4. **Existing studios move to EUR**, because every price they ever entered was typed into a
   field labelled €.
5. **Card deposits are offered only when the active provider supports the studio's currency**
   (POK today: `ALL`, `EUR`). Cash works in every currency from day one.
6. **The currency locks once money has moved** — the first `Payment`, `GiftCard`,
   `PackagePurchase` or `BoothRentCharge` row for the studio. After that the owner cannot change
   it in-app; the message sends them to support. (The admin-side change tool is a backlog spec
   — §4.1 — not built tonight.)
7. Subscription billing (Flow B: `PlanPrice`, `Subscription.BilledCurrency`,
   `MrrRules.PlatformCurrency = "eur"`) is **out of scope and must not change** — fixed
   separately in PR #196.

### 2.2 Where currency lives (data model)

The studio owns the currency. **Price settings inherit it; records of money that moved or is
owed keep their own copy**, so history never changes meaning even if a studio's currency is
later changed by an admin.

| Field | Change | Why |
|---|---|---|
| `Studio.CountryCode` | **New.** `string`, `varchar(2)`, required, upper-case ISO 3166-1 alpha-2 | Picks the default currency. `Studio` today has only `City`, `Latitude`/`Longitude`, `Timezone`. |
| `Studio.Currency` | **New.** `string`, `varchar(3)`, required, upper-case ISO 4217 | The one currency every price and payment in this studio uses. |
| `Payment.Currency` | **Exists.** Remove the C# initializer `= "ALL"` → `= string.Empty`. **Drop the MySQL column default** (§8.3). Always set from `Studio.Currency` on create. | The default is why every cash payment is stored as lek today. |
| `GiftCard.Currency` | **New.** `varchar(3)`, required, set from `Studio.Currency` at creation | Prepaid money — keeps its own copy. |
| `BoothRentCharge.Currency` | **New.** `varchar(3)`, required, set from the studio's currency when the job writes the row | Money owed — keeps its own copy. |
| `PackagePurchase.Amount` + `PackagePurchase.Currency` | **New.** `decimal(18,4)` + `varchar(3)`, both required, snapshotted from `Package.Price` / `Studio.Currency` at purchase | **Not in Finance's hand-off — added by Engineering Consultation.** `PackagePurchase` today stores no amount at all, only `PackageId`, so a later `Package.Price` edit silently rewrites what a past purchase "cost". Same rule as the others: a record of money that moved keeps its own copy. Backfill from the current `Package.Price` (§2.13, §8.3). |
| Price settings: `Service.Price`, `Service.DepositAmount`, `DepositRule.AmountFixed`, `Package.Price`, `BoothRentSchedule.AmountFixed`, `PromoCode.AmountFixed`, `Design.Price`, `Artist.HourlyRate`, `StudioJoinInvite.HourlyRate`, `Appointment.DepositAmount` | **No new column** — always in `Studio.Currency` | Safe because the currency locks once money moves. |
| `SessionSplit.Amount` | **No new column** — uses its `Payment.Currency` | A split is part of one payment. |
| `Payment.RefundedAmount` | **No new column** — uses `Payment.Currency` | Same record. |

### 2.3 Decimals follow the currency (ISO 4217 minor unit)

Currencies don't all have 2 decimal places (JPY 0, KWD/BHD/OMR/JOD/TND/LYD/IQD 3). Design:

- **Storage:** widen every Flow A money column listed in §8.2 from `decimal(18,2)` to
  `decimal(18,4)`. (Precision stays 18 → 14 integer digits, far above any real price.) Do **not**
  touch subscription/plan columns (`decimal(10,2)` ones, `PlanPrice.Price`).
- **Rounding:** every amount is rounded to its currency's ISO 4217 minor unit **when it is
  computed or saved in the Application layer**, with `MidpointRounding.AwayFromZero`, via one
  helper: `CurrencyCatalog.Round(decimal amount, string currency)`. Validators reject input with
  more decimals than the currency allows (a JPY deposit of `3000.5` is a 400, not a silent
  round).
- **Provider wire format is the provider's job, not the caller's.** Remove the fixed `× 100`
  from every caller. `PaymentHoldRequest` and `IPaymentProvider.RefundAsync` carry a
  **major-unit `decimal Amount` plus `string Currency`**; each `IPaymentProvider` implementation
  converts to whatever its API expects. Reason, verified today: providers deviate from ISO 4217
  (Stripe represents ISK and UGX as two-decimal on the wire; Adyen uses its own exponents for
  CLP, CVE, IDR, ISK) — a caller-side exponent would be wrong for some provider sooner or later.
  POK's unverified whole-unit-vs-minor-unit question (§3.1) then lives in exactly one method.

### 2.4 Currency reference data — no new packages

- **Backend:** new static class `Pena_e_Arte.Domain/Money/CurrencyCatalog.cs` holding the
  **active ISO 4217 codes and their minor units** as a `FrozenDictionary<string, int>` (source:
  the ISO 4217 "List One" published by SIX; include only active, tradable currencies — exclude
  funds codes like `XAU`, `XDR`, `XXX`, `CLF`, `UYW`, `BOV`, `CHE`, `CHW`, `COU`, `MXV`,
  `USN`). Exposes `bool IsSupported(string code)`, `int MinorUnits(string code)`,
  `decimal Round(decimal amount, string code)`, `bool HasAtMostMinorUnits(decimal amount, string code)`,
  and `IReadOnlyCollection<string> AllCodes`. This is a currency→minor-unit table, which .NET
  does not expose per currency; it is **not** a country→currency list (that comes from .NET, next
  bullet). Put a header comment with the source and the date you transcribed it.
- **Country → default currency:** `Pena_e_Arte.Domain/Money/CountryCurrency.cs`,
  `static string? DefaultCurrencyFor(string countryCode)` using
  `new RegionInfo(countryCode).ISOCurrencySymbol` (AL → ALL, PL → PLN, JP → JPY, XK → EUR,
  ME → EUR). Catch `ArgumentException` → return `null`; the caller then requires an explicit
  currency. If .NET returns a code `CurrencyCatalog` doesn't list, also return `null`. The API
  image is `mcr.microsoft.com/dotnet/aspnet:10.0` (not chiseled/alpine; `InvariantGlobalization`
  is not set anywhere in the repo — verified), so ICU region data is present; still add a unit
  test for AL/PL/JP/XK/ME/US/GB so a future invariant-globalization switch fails loudly in CI.
- **Country validation:** `CountryCurrency.IsKnownCountry(string code)` = `RegionInfo` construct
  succeeds **and** the code is two upper-case letters.
- **Frontend:** no new npm package. Country list: reuse the pattern in
  `frontend/src/shared/utils/phoneCountries.ts` (`getCountries()` from the already-installed
  `libphonenumber-js` + `Intl.DisplayNames`). Currency list and minor units:
  `Intl.supportedValuesOf("currency")` and
  `new Intl.NumberFormat("en", { style: "currency", currency }).resolvedOptions().maximumFractionDigits`.
  Filter out the same non-tradable codes the backend excludes (export
  `EXCLUDED_CURRENCY_CODES` from `frontend/src/shared/utils/currencies.ts`, a copy of the
  backend's exclusion list with a comment pointing at `CurrencyCatalog.cs`). The **server
  validator is authoritative**: if a browser ever offers a code the backend rejects, the
  registration/settings form shows the 400 message.
- **Country → default currency on the frontend:** browsers have no API for it, and a
  hand-kept frontend table is exactly what Finance ruled out. So the registration and sign-up
  forms ask the server: `GET /api/v1/public/countries/{countryCode}/default-currency` (§2.7),
  which wraps `CountryCurrency.DefaultCurrencyFor`. One source of truth, no list anywhere.

### 2.5 The one money-formatting function (frontend)

`frontend/src/shared/utils/formatCurrency.ts` currently is (verbatim):

```ts
export function formatCurrency(amount: number, currencyCode: string): string {
  return new Intl.NumberFormat("en", {
    style: "currency",
    currency: currencyCode,
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
    maximumFractionDigits: 2,
  }).format(amount);
}
```

Target:

```ts
/** The app is English-only today (no i18n library in package.json — verified 2026-09-27).
 *  When a language switcher ships, this constant becomes the user's language; every call site
 *  already goes through here, so that is a one-line change. */
export const APP_LOCALE = "en";

export function currencyMinorUnits(currencyCode: string): number {
  return new Intl.NumberFormat(APP_LOCALE, { style: "currency", currency: currencyCode })
    .resolvedOptions().maximumFractionDigits ?? 2;
}

export function formatCurrency(amount: number, currencyCode: string): string {
  const digits = currencyMinorUnits(currencyCode);
  return new Intl.NumberFormat(APP_LOCALE, {
    style: "currency",
    currency: currencyCode,
    // Whole amounts stay compact ("€29", "ALL 5,000", "¥3,000"); fractional ones show the
    // currency's full minor unit ("€98.60", "KWD 12.345") — never "€98.6".
    minimumFractionDigits: Number.isInteger(amount) ? 0 : digits,
    maximumFractionDigits: digits,
  }).format(amount);
}

/** The symbol/code to put in an input label or adornment, e.g. "€", "ALL", "¥". */
export function currencyLabel(currencyCode: string): string { /* formatToParts → type "currency" */ }
```

**Divergence from Finance's hand-off, deliberately:** the hand-off asked for "the user's app
language". The app has no language setting, so the locale is a single constant, not the browser
locale — using `navigator.language` would make the same screen render differently per visitor
and make every snapshot test machine-dependent. Record this in the Decisions Log (Phase G).

Plan/subscription screens already call `formatCurrency(amount, plan.currency)`; they keep
working unchanged (EUR has 2 minor units, so output is identical — the existing
`formatCurrency.test.ts` cases must still pass untouched).

### 2.6 One API shape for "what currency is this"

- `StudioResponse` gains `string CountryCode`, `string Currency`, `bool CurrencyLocked` (append
  as optional positional params at the end, matching the existing optional tail — do not reorder).
- `PublicStudioResponse` gains `string Currency` (guest booking, public portfolio, gift-card
  purchase by slug all need it).
- `PaymentResponse` gains `string Currency`.
- `GiftCardResponse`, `BoothRentChargeResponse`, `PackagePurchaseResponse` gain `string Currency`
  (and `PackagePurchaseResponse` gains `decimal Amount`).
- `RevenueSummaryResponse`, `ArtistEarningsResponse` gain `string Currency` and
  `int ExcludedOtherCurrencyCount`.
- `PaymentClientTokenResponse(string ClientToken)` → `PaymentClientTokenResponse(string ClientToken, decimal Amount, string Currency)`.
- `PaymentCapabilitiesResponse(bool CardPaymentsAvailable, string? PokEnvironment = null)` →
  add `string? Currency = null` and `string? CardUnavailableReason = null` where the reason is one
  of the string constants `"provider_unsupported_currency"`, `"provider_not_connected"`,
  `"provider_disabled"` (put them in `Pena_e_Arte.Contracts/Responses/PaymentCapabilitiesResponse.cs`
  as `public static class CardUnavailableReasons`).
- Frontend types (`payment.types.ts`, studio types, `publicApi.ts`, reports types) updated to
  match — no `any`.

### 2.7 New endpoints (exactly two)

| Method + route | Handler | Policy | Validator |
|---|---|---|---|
| `GET /api/v1/public/countries/{countryCode}/default-currency` (register in `PublicEndpoints.cs` inside the existing `/api/v1/public` group) | `GetCountryDefaultCurrencyQuery(string CountryCode)` → `CountryDefaultCurrencyResponse(string CountryCode, string? Currency)` in `Pena_e_Arte.Application/Public/Queries/` | `.AllowAnonymous().RequireRateLimiting("public-read")` — identical to every other `/public` read. **Add a row** to `architecture.md`'s "AllowAnonymous Exceptions" table in the same commit: reason "Registration/sign-up pages (anonymous) need a country's default currency", mechanism "None — static reference data from .NET `RegionInfo`, no tenant or user data; `public-read` rate limit". | `GetCountryDefaultCurrencyValidator`: `NotEmpty`, `Length(2)`, `Matches("^[A-Za-z]{2}$")`. Unknown country → 200 with `Currency: null` (the form then asks the owner to pick). |
| `PUT /api/v1/studios/me/currency` (register in `StudioEndpoints.cs` next to `PUT /me`, line ~27) | `UpdateStudioCurrencyCommand(string Currency)` in `Pena_e_Arte.Application/Studios/Commands/UpdateStudioCurrencyCommand.cs`, request body `UpdateStudioCurrencyRequest(string Currency)` in Contracts | `.RequireAuthorization("OwnerOnly")` + `.RequireRateLimiting("billing")` | `UpdateStudioCurrencyValidator`: `NotEmpty`, `Length(3)`, `Must(CurrencyCatalog.IsSupported)` |

`UpdateStudioCurrencyCommand` implements `IAuditableCommand` with a new
`AuditActions.StudioCurrencyChanged = "Studio.CurrencyChanged"` (add to
`Pena_e_Arte.Domain/Constants/AuditActions.cs`), `AuditTargetType = AuditTargetTypes.Studio`.
Mirror `UpdateStudioSlugCommand`'s shape (lock check → no-op if unchanged → save).

Lock check. Verified 2026-09-27: every tenant query filter in `AppDbContext.cs` (lines ~133+) is
`StudioId == tenant.StudioId && DeletedAt == null`, and `TenantEntity` has `DeletedAt`. Money
that moved and was later soft-deleted **still moved**, so the lock must see soft-deleted rows —
which means bypassing the filter and re-applying the tenant scope by hand:

```csharp
// IgnoreQueryFilters() is deliberate: the tenant filter also hides soft-deleted rows, and a
// soft-deleted payment still means money moved in this currency. Tenant scope is re-applied
// explicitly on every line. Registered in architecture.md "IgnoreQueryFilters() Approved Usages".
Guid studioId = tenant.StudioId;
bool moneyHasMoved =
       await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.StudioId == studioId, ct)
    || await db.GiftCards.IgnoreQueryFilters().AnyAsync(g => g.StudioId == studioId, ct)
    || await db.PackagePurchases.IgnoreQueryFilters().AnyAsync(p => p.StudioId == studioId, ct)
    || await db.BoothRentCharges.IgnoreQueryFilters().AnyAsync(c => c.StudioId == studioId, ct);
```

Add the row to `architecture.md`'s **"IgnoreQueryFilters() Approved Usages"** table **in the
same commit** (Phase D): file `StudioCurrencyLock.cs`, reason as in the comment, mitigation
"explicit `StudioId == tenant.StudioId` predicate on every query; read-only `AnyAsync`; no data
returned". Add a unit test that a soft-deleted payment in the studio locks it **and** a payment in
another studio does not. If locked →
`BusinessRuleViolationException("Your studio's currency can't be changed after the first payment, gift card, package or booth-rent charge has been recorded. Contact support if you need to change it.")`.

Expose the same computation as `CurrencyLocked` on `StudioResponse` via one shared internal
static helper `StudioCurrencyLock.IsLockedAsync(IAppDbContext db, CancellationToken ct)` in
`Pena_e_Arte.Application/Studios/` — used by both the command and the `GET /studios/me` handler,
never duplicated.

### 2.8 Card gating by currency

`GetPaymentCapabilitiesHandler` today (verbatim):

```csharp
if (!paymentProvider.Capabilities.SupportsAuthCapture)
    return new PaymentCapabilitiesResponse(CardPaymentsAvailable: false);

(bool connected, _) = await PokConnectionCheck.ResolveAsync(db, tenant.StudioId, ct);
return new PaymentCapabilitiesResponse(
    CardPaymentsAvailable: connected,
    PokEnvironment: connected ? paymentProvider.Capabilities.Environment : null);
```

Target order of checks: (1) provider disabled → `provider_disabled`; (2) load the studio's
`Currency`; if not in `paymentProvider.Capabilities.SupportedCurrencies` (case-insensitive) →
`CardPaymentsAvailable: false`, reason `provider_unsupported_currency`, `Currency` populated;
(3) not connected → `provider_not_connected`; (4) otherwise available. Always populate
`Currency`. **And enforce server-side too**: `CreateDepositPaymentCommand`,
`PayDepositWithSavedCardCommand` (via the shared `ResolveOrCreateHoldAsync`),
`CreatePaymentIntentCommand`, `PurchaseGiftCardCommand`, `PurchasePackageCommand` throw
`BusinessRuleViolationException("Card payments aren't available in {currency} for this studio. Please pay in cash.")`
when the studio currency isn't supported — before calling the provider. Put the check in one
helper `CardCurrencyGuard.EnsureSupported(IPaymentProvider provider, string currency)` in
`Pena_e_Arte.Application/Payments/`.

### 2.9 Cross-tenant code that must learn the currency

- `Pena_e_Arte.Infrastructure/Jobs/BoothRentChargeJob.cs` (already `IgnoreQueryFilters()`,
  already in the approved-usages table): load the distinct `StudioId`s of the due schedules,
  fetch `db.Studios.Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Currency)`
  (`Studio` has no tenant filter), set `Currency` on each new `BoothRentCharge`. One query, not
  N+1. No new approved-usage row needed — say so in the Decisions Log entry.
- `PurchaseGiftCardCommand` resolves the studio by slug (public) — use `studio.Currency` from the
  entity it already loads.

### 2.10 The client sees the server's amount, never the URL's

`DepositCheckoutPage.tsx` today reads `const amount = searchParams.get("amount");` and renders
"You are authorising a deposit of **{amount}**". `CreatePaymentIntentPage.tsx` builds that URL:
`` `${window.location.origin}/pay/${result.paymentId}?amount=${amount.toFixed(2)}+EUR` ``.
That makes the displayed amount forgeable and currency-blind. Target: the page renders
`formatCurrency(data.amount, data.currency)` from `GET /payments/{id}/client-token`
(§2.6), ignores any `amount` query param, and `CreatePaymentIntentPage` stops appending it.
(Old links in the wild still work — the param is just ignored.)

### 2.11 Reports never add different currencies together

`GetRevenueSummaryQuery`, `GetMyEarningsQuery`, `ExportRevenueCsvQuery` currently sum
`RetainedAmount()` (`PaymentExtensions.cs:14`, `Math.Max(0m, p.Amount - (p.RefundedAmount ?? 0m))`)
with no currency check. Target, mirroring `MrrRules`' exclusion pattern:

- Load the studio's `Currency`. Sum **only** payments whose `Currency` equals it
  (ordinal, case-insensitive). Count the others into `ExcludedOtherCurrencyCount` and log one
  structured line: `logger.LogWarning("Revenue query {Query} excluded {ExcludedCount} payment(s) not in studio currency {StudioCurrency}", ...)`
  — counts and codes only, no ids of clients, no names.
- Return `Currency` on every response.
- CSV export: add a `Currency` column immediately after the amount columns; format amounts with
  `F{minorUnits}` instead of the hard-coded `F2`, invariant culture.
- Per-artist and trend breakdowns follow the same filter (they sum the same rows).

Also fix the same bug class in `ExportAppointmentsCsvQuery` (`a.DepositAmount.ToString("F2")`,
line ~48): add a `Currency` column from the studio, format with the currency's minor units.

### 2.12 Everything the backend renders for humans

| File | Current (verified) | Target |
|---|---|---|
| `Pena_e_Arte.Infrastructure/Services/PaymentInvoiceService.cs:163` | `amount.ToString("C2", new CultureInfo("pt-PT"))` | `MoneyText.Format(amount, payment.Currency)` |
| `Pena_e_Arte.Application/Payments/Commands/SendDepositCapturedNotificationCommand.cs:49` | `payment.Amount.ToString("C", new CultureInfo("pt-PT"))` | same helper |
| `Pena_e_Arte.Application/Payments/Commands/SendPaymentRefundedNotificationCommand.cs:49` | same | same helper (refunded amount, payment currency) |
| `Pena_e_Arte.Application/Appointments/Queries/GetAppointmentIcsQuery.cs:39` | `DESCRIPTION:Deposit: {appt.DepositAmount:F2} EUR` | `DESCRIPTION:Deposit: {MoneyText.Format(appt.DepositAmount, studio.Currency)}` |
| `Pena_e_Arte.Domain/Entities/Artist.cs:12` | doc comment "Hourly rate in EUR" | "Hourly rate in the studio's currency (Studio.Currency)" |

`MoneyText.Format(decimal amount, string currency)` lives in `Pena_e_Arte.Domain/Money/MoneyText.cs`:
format as `"{amount:N<minorUnits>} {CODE}"` with `CultureInfo.InvariantCulture`
(e.g. `"5,000 ALL"`, `"50.00 EUR"`, `"3,000 JPY"`, `"12.345 KWD"`). ISO-code-after-amount is
deliberately locale-neutral and unambiguous in emails/PDFs/calendar files that may be read in
any locale; the symbol form ("€50") stays a frontend concern. Check the two email templates
(`MailKit/Templates/DepositCaptured.html`, `PaymentRefunded.html`) contain no literal `€` — they
receive the pre-formatted string; if either has a literal currency sign, remove it.

### 2.13 Existing data — migrate to EUR, safely, idempotently

- `Studio.CountryCode`: `'AL'` for every existing studio. Rationale: every studio so far is in
  Albania (`Timezone` defaults to `Europe/Tirane`, POK is Albania-only), a migration has no
  network to reverse-geocode, and `CountryCode` only drives the *default* currency, which for
  existing studios is overridden to EUR anyway. Owners can correct the country in Studio Settings
  (it never changes the currency). The runbook (§8.6) lists any studio whose coordinates fall
  outside Albania's bounding box so an admin can check it.
- `Studio.Currency`: `'EUR'` for every existing studio, including Albanian ones.
- `Payment.Currency`: `'EUR'` on every row **except** real provider-processed card rows:
  `WHERE NOT (Provider = 'pok' AND ProviderReferenceId IS NOT NULL AND Status IN ('Captured','Paid','Refunded'))`.
  Those excepted rows keep their stored value and are listed by the runbook for a human to
  confirm (they should not exist in production — Finance states no live card payments have run —
  but staging may have sandbox rows, and a migration must never relabel real money).
  `PaymentStatus` is stored as a string (`HasConversion<string>()`, verified) — match on names.
- `GiftCard.Currency`, `BoothRentCharge.Currency`: `'EUR'` on every existing row, same POK
  exception for gift cards with `Provider = 'pok'` and a non-null `ProviderReferenceId` and
  `Status` not `Pending` (they were sent to POK as `ALL`) — keep those as `'ALL'`.
- `PackagePurchase.Currency`: `'EUR'` (same POK exception: `ConfirmedAt IS NOT NULL AND Provider = 'pok'` → `'ALL'`);
  `PackagePurchase.Amount`: backfilled from the current `Package.Price` via
  `UPDATE package_purchases pp JOIN packages p ON p.Id = pp.PackageId SET pp.Amount = p.Price WHERE pp.Amount = 0`
  (verify real table/column names from the model snapshot before writing SQL).
- **Idempotent:** every backfill statement is guarded so a second run changes nothing (e.g.
  add columns nullable → backfill `WHERE col IS NULL` → alter to `NOT NULL`; for
  `Payment.Currency`, `WHERE Currency = 'ALL' AND NOT (<pok exception>)` — a second run finds
  none because they're now `EUR`).
- **Down():** reverses schema changes; it does **not** try to restore `'ALL'` labels (document in
  a comment that the Up() relabel is intentionally one-way because the old labels were wrong).

---

## 3. Flag, don't decide — apply the default, surface verbatim in the final report

| # | Question | Owner | Default tonight |
|---|---|---|---|
| 3.1 | **POK amount unit.** Does POK's `amount` expect whole units (`50.00`) or minor units (`5000`)? `PokPaymentProvider.cs` itself calls this "THE SINGLE HIGHEST-RISK UNVERIFIED ASSUMPTION … a 100x over/undercharge". | Engineering + one real staging transaction by a human with POK staging credentials | Keep the current assumption (whole-unit decimal) — it is now `PokAmount(decimal amount, string currency) => CurrencyCatalog.Round(amount, currency)` inside `PokPaymentProvider`, one place. Keep the warning comment, updated. **Do not enable any studio for live card deposits** — not a code change, a release note in the final report. |
| 3.2 | **Albanian pricing rules.** May an Albanian-registered business display and charge consumer prices in EUR instead of lek? (Repo holds Law 55/2020 on Payment Services, which may not be the only applicable rule.) | **Legal** | Build per Finance's decision (EUR is selectable for AL studios; existing studios are migrated to EUR). Add nothing that hides it. Flag prominently. |
| 3.3 | **Short list of "major currencies"** shown first in the picker after the country's own currency. | Product/Finance | `EUR, USD, GBP, CHF` — one exported constant `MAJOR_CURRENCIES` in `frontend/src/shared/utils/currencies.ts`, trivially changeable. |
| 3.4 | **Existing Albanian studios** — tell them they may switch to ALL via support, or leave silent? | Product/Finance | Silent. Help text (Phase G) says currency changes after the first payment go through support. No banner, no email. |
| 3.5 | **Cash deposit display for a studio whose currency POK doesn't support** (e.g. a JPY studio). | Product | Card option hidden with the explanatory line from §12.4; cash only. No other behaviour change. |
| 3.6 | `architecture.md` "Payment Architecture" says `NullPaymentProvider` "is the DI default" — **stale**: `InfrastructureServiceExtensions.cs:188` registers `PokPaymentProvider`. | Docs | Correct the sentence in Phase G; list it under "drift found". |

---

## 4. "Do not build blind" — specify fully, do NOT implement

Write each of these as a section in a new doc,
`docs/specs/studio-currency-backlog-2026-09-27.md`, with entities, endpoints, migration shape,
and open questions — and nothing in code.

### 4.1 Admin change of a locked studio currency
AdminOnly `POST /api/v1/platform/studios/{studioId}/currency` → `AdminChangeStudioCurrencyCommand(StudioId, NewCurrency, string Reason)`,
`IAuditableCommand` (`Studio.CurrencyChangedByAdmin`), cross-tenant read of the lock tables
(needs an `IgnoreQueryFilters()` approved-usage row). Open questions to list: what happens to
(a) active gift cards and unconsumed package sessions denominated in the old currency (refund?
convert at a dated rate? keep and block redemption?), (b) upcoming appointments whose
`DepositAmount` was quoted in the old currency, (c) booth-rent schedules, (d) whether prices are
wiped, flagged for review (`Studio.PricesNeedReviewSince`?), or left, (e) owner notification.
This is a money-semantics decision → Finance, not engineering.

### 4.2 Multi-currency inside one studio
Out of scope by Finance's decision. One line: "Rejected 2026-09-27; revisit only with a new Finance decision."

### 4.3 "≈" client-currency estimate
Needs an FX-rate source (new third-party integration → flag, per the no-new-integrations rule),
a dated cache, and a display rule that it never feeds any stored/charged amount. Spec the
`FxRate(From, To, Rate, AsOf, Source)` entity and a `GET /public/fx-estimate` shape; list the
provider options (ECB reference rates — free, daily, EUR-based; Bank of Albania daily fixing for
ALL) without choosing.

### 4.4 Cross-studio payment totals for the admin role
If/when platform admin shows Flow A totals across studios, convert to EUR at dated rates and
label as estimates. Nothing shows Flow A totals cross-studio today — verify with a grep and state
it in the spec.

### 4.5 Additional card providers for non-ALL/EUR countries
`IPaymentProvider` already abstracts this. Spec only: capability-driven gating already built
tonight means a new provider "just works" for its currencies. List candidate providers as open.

---

## 5. Scope boundary — do not touch

| Path | Why |
|---|---|
| `Pena_e_Arte.Domain/Entities/PlanPrice.cs`, `Subscription.cs`, `SubscriptionInvoicePayment.cs`, `SubscriptionRefund.cs`, `SubscriptionRevenueEvent.cs` and their configurations | Flow B — fixed in PR #196, EUR by design. |
| `Pena_e_Arte.Application/Platform/Revenue/MrrRules.cs`, `Pena_e_Arte.Application/Billing/**`, `Pena_e_Arte.Infrastructure/Services/Stripe*.cs` | Flow B. Read `MrrRules` only as the pattern for §2.11. |
| `frontend/src/features/billing/**`, `frontend/src/features/platform/**`, `frontend/src/features/public/components/PricingPage*`, `planHighlights*` | Subscription prices, EUR by design. Their tests that assert `€` stay as they are. |
| `Pena_e_Arte.Infrastructure/Services/Pok/PokAuthClient.cs`, `PokCardTokenService.cs`, `MapStatus` in `PokPaymentProvider.cs` | Unrelated POK auth/tokenization; the second unverified assumption (`MapStatus`) is not this task. |
| Every migration **before** the one you add | Never edit history. |
| `docs/payments/ADR-0001-payment-providers.md` and Amendments A/B | Don't edit an accepted ADR; add **Amendment C** instead (Phase G). |
| Legacy root docs (`bug-report-*.md`, `feature-request-*.md`, `spec-plan-*.md`) | Not this task. |

---

## 6. Constraints (restated — apply to every phase)

- **No new NuGet or npm packages.** If you believe one is required, stop that sub-task and list it
  as a prerequisite decision in the final report. (§2.4 shows how to do this without any.)
- **No `useEffect` for data fetching** — RTK Query hooks only. Approved exceptions are only those
  already listed in `docs/claude/frontend.md`; the Nominatim geocode hook
  (`useAddressGeocode`) is existing code you may extend, not a new exception.
- TypeScript strict, **no `any`**. C#: explicit types, no `var` for non-obvious types.
- **No business logic in endpoints** — MediatR + FluentValidation only; every new endpoint has a
  validator and `.RequireAuthorization("<policy>")`.
- **Tenant isolation** via EF Core global query filters everywhere; the only filter bypasses are
  `BoothRentChargeJob` (already approved) and `StudioCurrencyLock` (new — §2.7, re-scoped to the
  tenant by hand). Every new `IgnoreQueryFilters()` = new row in `architecture.md` in the same commit.
- **Never log PII.** Currency logs: studio id, counts, currency codes. Never client names/emails.
- Serilog structured logs only (`{Placeholders}`, no string interpolation into the template).
- Decimal arithmetic throughout; no `double` for money; round only via `CurrencyCatalog.Round`.
- **Tests ship with every phase** (unit for Application/Domain, integration for the migration and
  endpoints, frontend component tests covering loading/error/empty/success states).
- **Help sync is part of each phase's definition of done** — see each phase's "Help" bullet; Phase
  G consolidates and checks.

---

## 7. Phase A — Money primitives (Domain), no behaviour change

**Files (new):** `Pena_e_Arte.Domain/Money/CurrencyCatalog.cs`, `CountryCurrency.cs`, `MoneyText.cs`.

**Tests (new):** `tests/Pena_e_Arte.UnitTests/Domain/Money/CurrencyCatalogTests.cs`,
`CountryCurrencyTests.cs`, `MoneyTextTests.cs`:

| Case | Expect |
|---|---|
| `MinorUnits("EUR")`, `("ALL")`, `("JPY")`, `("KWD")`, `("BHD")`, `("ISK")` | 2, 2, 0, 3, 3, 0 |
| `IsSupported("eur")` / `("XXX")` / `("EURO")` | true (case-insensitive) / false / false |
| `Round(12.3456m, "KWD")`, `Round(2999.5m, "JPY")`, `Round(10.005m, "EUR")` | 12.346, 3000, 10.01 (AwayFromZero) |
| `HasAtMostMinorUnits(3000.5m, "JPY")` | false |
| `DefaultCurrencyFor("AL"/"PL"/"JP"/"XK"/"ME"/"US"/"GB")` | ALL/PLN/JPY/EUR/EUR/USD/GBP |
| `DefaultCurrencyFor("ZZ")` | null (no throw) |
| `MoneyText.Format(5000m,"ALL")`, `(50m,"EUR")`, `(3000m,"JPY")`, `(12.345m,"KWD")` | `"5,000.00 ALL"` (ISO 4217 gives ALL 2 minor units), `"50.00 EUR"`, `"3,000 JPY"`, `"12.345 KWD"` |

Help: none (no user-visible surface) — state this in the commit body.

Commit: `feat(money): ISO 4217 currency catalog, country default currency, money text formatter (Phase A)`

---

## 8. Phase B — Schema, migration, backfill, runbook

### 8.1 Entities

- `Studio.cs`: add after `Timezone`:
  ```csharp
  /// <summary>ISO 3166-1 alpha-2, upper-case (e.g. "AL"). Chosen at registration (prefilled from
  /// the geocoded address); owner-editable anytime. Drives only the DEFAULT currency — changing
  /// it never changes Currency.</summary>
  public string CountryCode { get; set; } = string.Empty;

  /// <summary>ISO 4217, upper-case (e.g. "ALL", "EUR"). The one currency every price and payment
  /// in this studio uses. Locked once money has moved (see StudioCurrencyLock). Never a silent
  /// toggle — architecture.md Decisions Log, "Studio currency (2026-09-27)".</summary>
  public string Currency { get; set; } = string.Empty;
  ```
- `Payment.cs:26-27` — replace
  ```csharp
  /// <summary>ISO 4217 currency of the payment. Defaults to Albanian lek.</summary>
  public string Currency { get; set; } = "ALL";
  ```
  with
  ```csharp
  /// <summary>ISO 4217 currency of this payment — copied from Studio.Currency when the row is
  /// created and never changed after. No default: a missing value must fail, not become lek.</summary>
  public string Currency { get; set; } = string.Empty;
  ```
- `GiftCard.cs`, `BoothRentCharge.cs`: add `public string Currency { get; set; } = string.Empty;` with the same doc comment style.
- `PackagePurchase.cs`: add `public decimal Amount { get; set; }` and `public string Currency { get; set; } = string.Empty;` with a comment explaining the snapshot (§2.2).

### 8.2 Configurations

- `StudioConfiguration` (find it): `CountryCode` `HasMaxLength(2).IsFixedLength().IsRequired()`,
  `Currency` `HasMaxLength(3).IsFixedLength().IsRequired()`.
- `GiftCardConfiguration`, `BoothRentChargeConfiguration`, `PackagePurchaseConfiguration`:
  `Currency` `HasMaxLength(3).IsRequired()`; `PackagePurchase.Amount` `HasColumnType("decimal(18,4)").IsRequired()`.
- **Widen to `decimal(18,4)`** (change `HasColumnType("decimal(18,2)")` → `"decimal(18,4)"`):
  `AppointmentConfiguration.DepositAmount`, `ArtistConfiguration.HourlyRate`,
  `BoothRentChargeConfiguration.Amount`, `BoothRentScheduleConfiguration.AmountFixed`,
  `DepositRuleConfiguration.AmountFixed`, `DesignConfiguration.Price`,
  `GiftCardConfiguration.InitialBalance` + `RemainingBalance`, `PackageConfiguration.Price`,
  `PaymentConfiguration.Amount` + `RefundedAmount`, `PromoCodeConfiguration.AmountFixed`,
  `ServiceConfiguration.Price` + `DepositAmount`, `SessionSplitConfiguration.Amount`,
  `StudioJoinInviteConfiguration.HourlyRate`. Leave every `decimal(5,2)` percent column and every
  subscription `decimal(10,2)` column alone.

### 8.3 Migration `AddStudioCurrency`

`dotnet ef migrations add AddStudioCurrency --project Pena_e_Arte.Infrastructure`, then hand-edit
`Up()` into this order (MySQL/Pomelo — verified provider; `Currency` on `payments` is
`varchar(3)` with a server default `'ALL'` from `20260731194633`):

1. Add `studios.CountryCode`, `studios.Currency`, `gift_cards.Currency`,
   `booth_rent_charges.Currency`, `package_purchases.Currency`, `package_purchases.Amount` as
   **nullable**.
2. Widen the decimal columns (`AlterColumn`).
3. Backfill per §2.13 with `migrationBuilder.Sql(...)`, each statement guarded to be re-runnable.
4. Alter the new columns to `NOT NULL`.
5. **Drop the payments default:** `migrationBuilder.Sql("ALTER TABLE payments ALTER COLUMN Currency DROP DEFAULT;")`
   (confirm table/column casing from the snapshot). EF's snapshot never recorded that default, so
   `AlterColumn` would not emit this — it must be raw SQL.
6. Model snapshot must end up matching the entity configuration exactly (run
   `dotnet ef migrations has-pending-model-changes` → none).

### 8.4 Seeders

`Pena_e_Arte.Infrastructure/Persistence/Seed/DataSeeder.cs` creates two studios (lines ~348, ~378)
and payments (~1067, ~1775). Set the first seeded studio `CountryCode = "AL", Currency = "EUR"`
(matches migrated reality — seeded prices are written as € in comments, e.g. "New Client Fixed (€50)")
and the second `CountryCode = "AL", Currency = "ALL"` with its seeded prices **multiplied to
realistic lek amounts** (e.g. €50 → 5,000 ALL) so dev/staging always has one studio of each kind
to click through. Every seeded `Payment`, `GiftCard`, `PackagePurchase`, `BoothRentCharge` sets
`Currency` from its studio. Update `Seeded Test Accounts.txt` **only if** it describes studios by
price (it's a repo-root text file, not code — if it doesn't mention prices, leave it).

### 8.5 Tests

Integration (`tests/Pena_e_Arte.IntegrationTests/Infrastructure/`, Testcontainers MySQL — verified in the csproj):

| Test | Expect |
|---|---|
| Apply all migrations to an empty DB, then seed a pre-migration shape (studio, cash payment with `Currency='ALL'`, POK card payment `Provider='pok'`, `ProviderReferenceId='x'`, `Status='Paid'`, `Currency='ALL'`) — i.e. migrate to the previous migration, insert, migrate up | Studio `AL`/`EUR`; cash payment `EUR`; POK paid payment still `ALL` |
| Run the backfill SQL a second time | Zero rows affected |
| Insert a payment via raw SQL without `Currency` after migration | Fails (NOT NULL, no default) |
| `package_purchases.Amount` after migrate | Equals its package's price |

Commit: `feat(db): studio country + currency, currency on every money record, widen money columns to 4dp, EUR backfill (Phase B)`

### 8.6 Runbook (docs, same commit)

Create `docs/payments/runbook-studio-currency-migration-2026-09-27.md` containing the read-only SQL a
human must run **against production before deploying**, and what to do with the results:

```sql
-- 1. Real POK money that must stay ALL (expected: 0 rows in production)
SELECT Id, StudioId, Status, Currency, CreatedAt FROM payments
 WHERE Provider = 'pok' AND ProviderReferenceId IS NOT NULL AND Status IN ('Captured','Paid','Refunded');
SELECT Id, StudioId, Status, Currency, CreatedAt FROM gift_cards
 WHERE Provider = 'pok' AND ProviderReferenceId IS NOT NULL AND Status <> 'Pending';
SELECT Id, StudioId, ConfirmedAt FROM package_purchases WHERE Provider = 'pok' AND ConfirmedAt IS NOT NULL;
-- 2. Studios whose map pin is outside Albania (country will be set to AL; admin to verify)
SELECT Id, Name, City, Latitude, Longitude FROM studios
 WHERE NOT (Latitude BETWEEN 39.6 AND 42.7 AND Longitude BETWEEN 19.2 AND 21.1)
   AND NOT (Latitude = 0 AND Longitude = 0);
```

(Use real table names from the snapshot.) Include: "If query 1 returns rows, those were really
charged in lek; the migration leaves them as ALL. Confirm each with the studio before deploying.
Studio names above are business names, not personal data, but do not paste results into chat
tools." Also include the staging-transaction checklist for §3.1.

---

## 9. Phase C — Every money record takes the studio's currency; providers own the wire format

### 9.1 `IPaymentProvider` contract (`Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs`)

Replace `long AmountInCents` in `PaymentHoldRequest` with `decimal Amount` (major units, already
rounded to the currency's minor unit), keep `string Currency`. Replace
`RefundAsync(Guid studioId, string providerReferenceId, long? amountInCents, CancellationToken ct)`
with `RefundAsync(Guid studioId, string providerReferenceId, decimal? amount, string currency, CancellationToken ct)`.
Update the XML docs to say "major units; the provider converts to its own wire format".
Update `NullPaymentProvider` and every test double.

### 9.2 `PokPaymentProvider.cs`

- Replace `AmountInCentsToPok(long)` (`Math.Round(amountInCents / 100m, 2)`) with
  `private static decimal ToPokAmount(decimal amount, string currency) => CurrencyCatalog.Round(amount, currency);`
  Keep — and update — the "HIGHEST-RISK UNVERIFIED ASSUMPTION" comment: it now reads "assumes
  POK takes whole-unit decimals in the currency's ISO minor unit; if the staging transaction
  shows minor-unit integers, change only this method."
- In `CreatePaymentHoldAsync`, before building the body, reject any currency not in
  `Capabilities.SupportedCurrencies` with `BusinessRuleViolationException` (defence in depth
  behind §2.8's guard).
- Refund: pass `ToPokAmount(amount, currency)`.

### 9.3 Handlers

| File | Current (verified) | Target |
|---|---|---|
| `Payments/Commands/CreateDepositPaymentCommand.cs` | `private const string DepositCurrency = "ALL";` + comment; `long amountInCents = (long)(appointment.DepositAmount * 100);`; `Currency = DepositCurrency` on create and convert-in-place | Delete the constant and its ADR comment. In `ResolveOrCreateHoldAsync`, load `string currency = await db.Studios.Where(s => s.Id == tenant.StudioId).Select(s => s.Currency).SingleAsync(ct);`, call `CardCurrencyGuard.EnsureSupported(paymentProvider, currency)`, pass `Amount: appointment.DepositAmount, Currency: currency`, set `Currency = currency` on both branches. Note: `(long)(x * 100)` also **truncated** (never rounded) — gone with this change. |
| `Payments/Commands/CreatePaymentIntentCommand.cs` | takes `req.Currency`; `(long)(req.Amount * 100)`; never sets `Payment.Currency` | Remove `Currency` from `CreatePaymentIntentRequest` (Contracts) and its rule in `CreatePaymentIntentValidator` (lines 13–16). Use studio currency; guard; round `req.Amount` via `CurrencyCatalog.Round` after validating it has ≤ minor units (validator needs the studio currency → do the minor-unit check in the handler and throw `ValidationException`-equivalent `BusinessRuleViolationException("Amount has more decimal places than {currency} allows.")`). Set `Currency` on create and on the retry branch. |
| `Payments/Commands/DeclareCashDepositCommand.cs` | new `Payment { … }` without `Currency` (→ `"ALL"` today); convert-in-place branch doesn't touch `Currency` | Set `Currency = studioCurrency` on the new row **and** on the convert-in-place branch (a card→cash conversion must end up in the studio currency too). No card guard (cash is always allowed). |
| `GiftCards/Commands/PurchaseGiftCardCommand.cs` | `(long)(req.Amount * 100)`, literal `"ALL"` | `studio.Currency` (already loaded by slug); guard; round; set `giftCard.Currency`. `PurchaseGiftCardValidator`: amount > 0; minor-unit check in handler as above. |
| `Packages/Commands/PurchasePackageCommand.cs` | `(long)(package.Price * 100)`, literal `"ALL"`; `PackagePurchase` without amount | studio currency; guard; `Amount = package.Price`, `Currency = currency` on the purchase. |
| `Payments/Commands/RefundPaymentCommand.cs` | `long amountInCents = (long)(refundAmount * 100);` | Validate `refundAmount` minor units vs `payment.Currency`; `RefundAsync(..., refundAmount, payment.Currency, ct)`. |
| `Appointments/Commands/CancelAppointmentCommand.cs:117-120` | `Math.Round(... , 2, AwayFromZero)` then `(long)Math.Round(refundAmount * 100m, ...)` | `decimal refundAmount = CurrencyCatalog.Round(appointment.DepositAmount * refundPercent / 100m, payment.Currency);` → `RefundAsync(..., refundAmount, payment.Currency, ct)`. Full refund path: `RefundAsync(..., null, payment.Currency, ct)`. |
| `Appointments/Commands/CreateAppointmentCommand.cs:226,249,263` | promo/referral discounts use `Math.Round(..., 2, ...)` or none | Round via `CurrencyCatalog.Round(x, studioCurrency)` (load it once at the top of the handler). |
| `Domain/Services/DepositCalculator.cs` | `Calculate(DepositRule? rule, decimal? artistHourlyRate, int durationMinutes)` rounds to 2 | Add `string currency` parameter; round via `CurrencyCatalog.Round`. Update both callers (`CreateAppointmentCommand.cs:183`, `AssignAppointmentArtistCommand.cs:126`). |
| `GiftCards/Commands/RedeemGiftCardCommand.cs` | no currency check | After loading the card: `if (!string.Equals(giftCard.Currency, studioCurrency, OrdinalIgnoreCase)) throw new BusinessRuleViolationException("This gift card is in {giftCard.Currency} and can't be used for a {studioCurrency} deposit.");` (can't happen while the lock holds; it guards the §4.1 future). |
| `Infrastructure/Jobs/BoothRentChargeJob.cs` | new `BoothRentCharge` without currency | §2.9 |
| `Payments/Queries/GetPaymentCapabilitiesQuery.cs` | §2.8 current code | §2.8 target |
| `Payments/Queries/GetPaymentClientTokenQuery.cs` | returns `new PaymentClientTokenResponse(payment.ClientToken)` | return `(payment.ClientToken, payment.Amount, payment.Currency)` |
| `Payments/PaymentExtensions.cs` `ToResponse()` | no currency | include `Currency` |

Also grep for any other `PaymentHoldRequest(`, `RefundAsync(`, `* 100`, `"ALL"`, `"EUR"` in
`Pena_e_Arte.Application` / `.Infrastructure` outside Flow B and fix them the same way; list every
hit and its disposition in the final report.

### 9.4 Tests (unit, `tests/Pena_e_Arte.UnitTests/Payments`, `GiftCards`, `Appointments`, `Services`, `Jobs`)

Update existing tests that assert the old behaviour: `CreateDepositPaymentHandlerTests.cs:48`
(`r.AmountInCents == 8000 && r.Currency == "ALL"`), `GetPaymentCapabilitiesHandlerTests.cs`
(lines 47, 64, 78), `NullPaymentProviderTests.cs:25`, `PokPaymentProviderTests.cs` (85, 106, 134).
New/updated cases:

| Scenario | Expect |
|---|---|
| ALL studio, 5,000 ALL card deposit | `PaymentHoldRequest.Amount == 5000m`, `Currency == "ALL"`; POK body `amount: 5000.00`, `currencyCode: "ALL"`; `Payment.Currency == "ALL"` |
| EUR studio, €50 card deposit | hold `50m`/`"EUR"`; POK body `50.00`/`"EUR"` |
| EUR studio, €80.555 computed deposit | stored/charged `80.56` (AwayFromZero) |
| JPY studio, card deposit | `BusinessRuleViolationException`, provider never called (NSubstitute `DidNotReceive`) |
| JPY studio, cash declaration of 3,000 | `Payment.Currency == "JPY"`, `Amount == 3000m` |
| KWD studio, cash deposit 12.345 | stored `12.345`, not `12.35` |
| Cash declaration converted from a pending card hold | `Currency` = studio currency |
| `CreatePaymentIntent` — request no longer has `Currency`; EUR studio | hold `"EUR"`, row `"EUR"` (previously row stayed `"ALL"`) |
| Gift card purchase, ALL studio | hold `"ALL"`, `GiftCard.Currency == "ALL"` |
| Package purchase, EUR studio, price 120 | `PackagePurchase.Amount == 120m`, `Currency == "EUR"` |
| Partial refund 50% of €45.55 | `RefundAsync(…, 22.78m, "EUR")` |
| Redeem ALL gift card at EUR studio | rejected |
| Capabilities: EUR + connected / JPY + connected / EUR + not connected | available / `provider_unsupported_currency` / `provider_not_connected`; `Currency` always set |
| `BoothRentChargeJob` two studios (EUR, ALL) due | each charge has its studio's currency; one `Studios` query (assert via a query counter or by structure) |
| `DepositCalculator` percent rule, JPY | whole yen |

Help (this phase's share): note in the phase commit body which Help articles Phase G will change
because of the card-gating behaviour (`owner-connect-pok`, `client-deposit-pay`, `faq-cash-vs-card`).

Commit: `fix(payments): every money record takes the studio currency; providers own wire-format conversion; card gated by currency (Phase C)`

---

## 10. Phase D — Registration, studio settings, currency lock, API shapes

### 10.1 Requests / commands

- `RegisterStudioRequest` (Contracts) is a positional record whose tail is already optional
  (`AddressLine2`, `PostalCode`, `ReferralCode`). Append **at the end**:
  `string? CountryCode = null, string? Currency = null` (source-compatible), and make the
  validator — not the type — require `CountryCode`.
- `RegisterStudioValidator`: `RuleFor(x => x.Request.CountryCode).NotEmpty().Length(2).Must(CountryCurrency.IsKnownCountry)`;
  `RuleFor(x => x.Request.Currency).Must(CurrencyCatalog.IsSupported!).When(x => x.Request.Currency is not null)`;
  plus a rule that when `Currency` is null, `CountryCurrency.DefaultCurrencyFor(CountryCode)` is
  non-null (message: "Please choose your studio's currency.").
- `RegisterStudioHandler` (`RegisterStudioCommand.cs:64-81`): set
  `CountryCode = req.CountryCode!.ToUpperInvariant()`,
  `Currency = (req.Currency ?? CountryCurrency.DefaultCurrencyFor(req.CountryCode!))!.ToUpperInvariant()`.
  Add both to the `StudioResponse` it returns.
- **Solo path:** `RegisterSoloArtistRequest(string Email, string Password, string FirstName, string LastName)`
  → append `string? CountryCode = null, string? Currency = null`. The solo sign-up form collects
  country (prefilled — §12.3). `RegisterSoloArtistCommand` sets both on the auto-provisioned
  studio; if `CountryCode` is null (old clients), default `"AL"`/`"ALL"` and log
  `logger.LogInformation("Solo studio {StudioId} created without country; defaulted to AL", ...)`.
  Add validator rules mirroring the studio ones (country optional here only for backward compat).
- `UpdateStudioRequest` — append `string? CountryCode = null`. `UpdateMyStudioHandler`: if
  non-empty, validate + set; **never** touches `Currency`. Validator rule as above, `.When(not null)`.
- New `UpdateStudioCurrencyCommand` + endpoint per §2.7.
- New `GetCountryDefaultCurrencyQuery` + public endpoint per §2.7, and its "AllowAnonymous
  Exceptions" row in `architecture.md` **in this same commit** (CLAUDE.md rule #2 requires the
  row to land with the endpoint, not in Phase G).

### 10.2 Responses

Per §2.6. `GET /studios/me` and every other place that builds `StudioResponse` (grep
`new StudioResponse(` — at least `RegisterStudioCommand`, `UpdateMyStudioCommand`, the get-me
query) passes `CountryCode`, `Currency`, `CurrencyLocked` (the latter only where cheap — the
get-me query and the update-currency command; elsewhere pass the helper's result too, don't
leave it defaulted to a misleading `false`).
`GetPublicStudioQuery` → `Currency`. Everything that maps `GiftCard`, `BoothRentCharge`,
`PackagePurchase`, `Payment` to a response → `Currency`.

### 10.3 Tests

| Scenario | Expect |
|---|---|
| Register a studio in Albania, no currency | `CountryCode = AL`, `Currency = ALL` |
| Register in Japan | `JPY` |
| Register with `CountryCode = "AL", Currency = "EUR"` | `EUR` |
| Register with `CountryCode = "ZZ"` | 400 |
| Register with `Currency = "EURO"` | 400 |
| Solo sign-up with `PL` | studio `PLN` |
| Solo sign-up without country | `AL`/`ALL`, info log |
| Owner changes currency before any money record | 200, audit row `Studio.CurrencyChanged` |
| Owner changes currency after one cash `Payment` (or one `GiftCard`, `PackagePurchase`, `BoothRentCharge`) | 422/409 per existing `BusinessRuleViolationException` mapping, clear message; audit row **not** written |
| Change to same currency | no-op 200 |
| Artist/client calls `PUT /studios/me/currency` | 403 |
| Update studio country `AL → XK` | `CountryCode` changes, `Currency` unchanged |
| `GET /studios/me` after first payment | `currencyLocked: true` |
| Endpoint integration test (`tests/Pena_e_Arte.IntegrationTests/Endpoints/`) for the currency endpoint: auth, lock, happy path | as above |
| `GET /public/countries/al/default-currency` anonymous | 200 `{ countryCode: "AL", currency: "ALL" }` (input upper-cased) |
| `GET /public/countries/ZZ/default-currency` | 200 `{ currency: null }` |
| `GET /public/countries/ALB/default-currency` | 400 |

Help (this phase): Phase G items G1–G3 below are **caused** by this phase — do not skip them.

Commit: `feat(studios): country + currency at registration (studio and solo), owner currency setting with lock, currency on every money response (Phase D)`

---

## 11. Phase E — Reports, exports, PDFs, emails, calendar

Implement §2.11 and §2.12 exactly.

Tests:

| Scenario | Expect |
|---|---|
| Revenue summary, EUR studio, 3 EUR payments + 1 planted `ALL` payment | totals = sum of 3; `Currency = "EUR"`; `ExcludedOtherCurrencyCount = 1`; one warning log with count and codes |
| Earnings, same data, artist view | same exclusion |
| CSV export | header has `Currency`; ALL row amounts formatted with 2 dp; a JPY studio's rows with 0 dp |
| Appointments CSV | `Currency` column present |
| Invoice PDF for an ALL payment (`PaymentInvoiceService` — extract the text via the same approach existing invoice tests use; if none exist, test `MoneyText` inputs by asserting the formatter call) | contains `ALL`, no `€` |
| Deposit-captured and refunded notifications | body uses `MoneyText` with the payment's currency |
| ICS for a JPY studio appointment | `Deposit: 3,000 JPY` |

Help: `owner-reports` and `artist-earnings` gain the "totals are in your studio's currency" tip (Phase G).

Commit: `fix(reports): never sum across currencies; currency on every total, export, invoice, email and calendar entry (Phase E)`

---

## 12. Phase F — Frontend

### 12.1 Shared

- `shared/utils/formatCurrency.ts` per §2.5 (+ `currencyLabel`, `currencyMinorUnits`, `APP_LOCALE`).
- New `shared/utils/currencies.ts`: `MAJOR_CURRENCIES = ["EUR","USD","GBP","CHF"] as const`,
  `EXCLUDED_CURRENCY_CODES` (§2.4),
  `buildCurrencyOptions(countryDefault: string | null)` (source: `Intl.supportedValuesOf("currency")` minus exclusions) →
  ordered `[countryDefault, ...MAJOR_CURRENCIES (minus dupes), ...rest alphabetical]`, each with
  `Intl.DisplayNames(["en"], { type: "currency" })` name.
- New `shared/hooks/useStudioCurrency.ts`: returns `{ currency: string | undefined; isLoading: boolean }`
  from the existing RTK Query studio-me hook in `features/studios/studiosApi.ts` (line ~177,
  `query: () => "studios/me"`). For public pages, components take the currency from
  `PublicStudioResponse` props — do not call the owner hook there.
- New `shared/components/ui/currency-select.tsx` (searchable, uses the existing combobox/select
  primitive the phone-country picker uses — reuse, don't invent) and
  `shared/components/ui/money-input.tsx` (number input with a leading currency adornment from
  `currencyLabel`, `step` = `10 ** -minorUnits`, rejects extra decimals client-side with an inline
  error matching the backend message).
- `shared/hooks/useAddressGeocode.ts`: the Nominatim request already has `addressdetails=1`; add
  `countryCode: (a.country_code ?? "").toUpperCase()` to `GeocodedLocation` (type + mapper).
  Same for `location-picker.tsx`'s `reverseGeocode` (`a.country_code`).

### 12.2 Replace every hard-coded euro (34 studio-side files — verified list)

Every one of these switches to `formatCurrency(amount, currency)` for display and
`MoneyInput`/`currencyLabel(currency)` for input labels; delete each file's local
`Intl.NumberFormat("pt-PT", { … "EUR" })` helper:

- **Appointments & booking:** `features/appointments/components/AppointmentCard.tsx`,
  `AppointmentDetailPage.tsx`, `BookAppointmentForm.tsx`, `MyBookingsSection.tsx`,
  `features/booking/components/GuestBookAppointmentForm.tsx`
- **Artists:** `features/artists/components/ArtistDetailPage.tsx`, `ArtistListPage.tsx`,
  `CreateArtistPage.tsx`, `features/public/components/ArtistPortfolioPage.tsx` (line ~261)
- **Money set-up:** `features/deposit-rules/components/CreateDepositRulePage.tsx`,
  `DepositRuleCard.tsx`, `DepositRuleDetailPage.tsx`, `features/services/components/CreateServicePage.tsx`,
  `ServiceCard.tsx`, `ServiceDetailPage.tsx`, `features/session-packages/components/PackageListPage.tsx`,
  `PurchasePackagePage.tsx`, `features/gift-cards/components/GiftCardListPage.tsx`,
  `PurchaseGiftCardPage.tsx`, `features/promo-codes/components/CreatePromoCodePage.tsx`,
  `PromoCodeCard.tsx`, `PromoCodeDetailPage.tsx`, `features/booth-rent/components/BoothRentManagementPage.tsx`,
  `MyBoothRentSection.tsx`
- **Payments:** `features/payments/components/CashDepositConfirmButton.tsx`,
  `CreatePaymentIntentPage.tsx` (also: stop sending `currency: "EUR"` at line ~327, remove the
  "Deposit amount (EUR)" label ~406 and the `EUR` adornment ~433, and drop `?amount=…+EUR` from
  the checkout URL at line ~69), `PaymentDetailPage.tsx`, `PaymentListPage.tsx`,
  `PaymentMethodSelector.tsx`, `SessionSplitsEditor.tsx`
- **Reports:** `features/dashboard/components/DashboardPage.tsx`,
  `features/reports/components/MyEarningsPage.tsx`, `ReportsPage.tsx`, `RevenueTrendChart.tsx`
  (use `response.currency`; if `excludedOtherCurrencyCount > 0`, show a muted one-line note
  under the total: "{n} payment(s) in another currency aren't included in this total.")

Amounts that belong to a **record** (payment, gift card, booth-rent charge, package purchase)
format with **that record's** `currency`; price settings format with the studio's currency.
Promo-code percent values are not money — leave them.

After the sweep, this must return **nothing** outside `features/billing/**`, `features/platform/**`,
`features/public/components/PricingPage*`, `planHighlights*`, `formatCurrency.ts` and
`helpContent.ts`:

```bash
cd frontend/src && git grep -n "€\|\"EUR\"\|'EUR'\|pt-PT" -- . ':!**/__tests__/**'
```

Put that exact command and its (empty) output in the final report.

### 12.3 Registration, solo sign-up, studio settings

- `features/studios/components/RegisterStudioPage.tsx`: add **Country** (select, prefilled from
  `useAddressGeocode`'s new `countryCode` when the owner hasn't touched it; falls back to `AL`)
  and **Currency** (the `CurrencySelect`, preselected to the country's default fetched from
  `GET /public/countries/{code}/default-currency` via a new RTK Query endpoint in
  `features/public/publicApi.ts` — **no** frontend country→currency table; if the server returns
  `null`, the select starts empty and is required). Helper text: "Prices, deposits and payments
  in your studio use this currency. You can change it until your first payment is recorded."
  Changing country re-defaults currency **only if** the owner hasn't manually picked one.
  Loading state on the select while the default is fetched; on fetch error, leave it empty and
  required (never silently fall back to a guessed currency).
- Solo sign-up form (find it via `RegisterSoloArtistRequest` usage in `features/auth/**`): Country
  select, prefilled from the browser region (`new Intl.Locale(navigator.language).maximize().region`),
  falling back to `AL`; currency from the same public default-currency endpoint, with the same picker behind a "Change currency" link to
  keep sign-up short.
- `features/studios/components/StudioProfilePage.tsx` (Studio Settings): add a **Currency** card
  next to the existing details card, `data-tour="owner-studio-currency-card"`:
  - unlocked → `CurrencySelect` + Save (calls the new mutation; toast on success; error state
    shows the API message);
  - locked → read-only value, lock icon, text "Locked because payments have been recorded in this
    currency. Contact support to change it." with a link to the existing support/escalation entry
    point (reuse whatever the Help menu's "Contact support" uses);
  - loading skeleton; error state with retry; **confirmation dialog** before saving
    ("Change currency to {X}? Existing prices will be shown in {X} without conversion — review your
    services, deposit rules, packages and gift-card amounts after changing.").
  - Country field added to the existing edit form (select), saved via `PUT /studios/me`.

### 12.4 Client checkout

`DepositCheckoutPage.tsx`: render the server `amount`/`currency` (§2.10); remove
`searchParams.get("amount")`. `PaymentMethodSelector.tsx`: when capabilities say
`provider_unsupported_currency`, hide Card and show "Card payments aren't available in
{currency} for this studio yet — you can pay the deposit in cash." (Distinct copy from the
existing not-connected message; keep that one as is.)

### 12.5 Frontend tests

Update the ~15 studio-side test files that assert `€` (verified: `appointments/__tests__/BookPage.test.tsx`,
`deposit-rules/__tests__/CreateDepositRulePage.test.tsx`, `DepositRuleDetailPage.test.tsx`,
`DepositRuleListPage.test.tsx`, `payments/__tests__/CreatePaymentIntentPage.test.tsx`,
`DepositCheckoutPage.test.tsx`, `PaymentListPage.test.tsx`, `promo-codes/__tests__/CreatePromoCodePage.test.tsx`,
`PromoCodeDetailPage.test.tsx`, `PromoCodeListPage.test.tsx`, `public/__tests__/ArtistPortfolioPage.test.tsx`,
`reports/__tests__/MyEarningsPage.test.tsx`, `ReportsPage.test.tsx`,
`services/__tests__/ServiceDetailPage.test.tsx`, `ServiceListPage.test.tsx`) so their MSW fixtures
return a `currency` and assertions follow it. **Do not** weaken an assertion to a regex that
matches anything. Add:

| Test | Expect |
|---|---|
| `formatCurrency.test.ts` new cases | `(5000,"ALL")` → `"ALL 5,000"`; `(3000,"JPY")` → `"¥3,000"`; `(12.345,"KWD")` → `"KWD 12.345"`; existing EUR cases unchanged |
| `buildCurrencyOptions` | country default first, majors next, no dupes |
| `ServiceCard` with an ALL studio | shows `ALL 5,000`, no `€` anywhere in the DOM |
| Studio Settings currency card | loading, error+retry, unlocked save (confirm dialog → mutation called with `{ currency: "ALL" }`), locked (read-only, support link) |
| RegisterStudioPage | geocode result `country_code: "pl"` preselects Poland + PLN; manual currency choice survives a later country change |
| DepositCheckoutPage | shows server amount; a forged `?amount=1 EUR` in the URL is not rendered |
| PaymentMethodSelector | `provider_unsupported_currency` → Card hidden, cash-only copy shown |
| Reports | excluded-count note appears only when > 0 |

Run `pnpm lint`, `pnpm test`, `pnpm build` (the stricter `tsc -b` — phoneCountries.ts documents a
real case where `tsc --noEmit` passed and `pnpm build` failed).

Help: the new Studio Settings card, the registration fields and the checkout copy are all
user-visible — Phase G items G1, G2, G4, G6 are this phase's obligation.

Commit: `feat(frontend): studio currency everywhere — one formatter, currency picker at registration and in settings, server-sourced checkout amount (Phase F)`

---

## 13. Phase G — Help, manual, tour, architecture, ADR (definition of done for the whole task)

Currently live manual: **`frontend/public/user-manual/index.html`** (CLAUDE.md rule #7 names it;
it has 37 `€` occurrences vs 5 in the older `docs/user-manual.html` — verified). Update the
`frontend/public` copy. Do **not** edit `docs/user-manual.html`; add a note in the final report
that it's stale and duplicated.

### 13.1 `frontend/src/features/help/helpContent.ts` (article ids verified)

- **G1 — `owner-studio-profile`** (line ~1217): add `"currency"`, `"country"`, `"lek"`, `"euro"`
  to `keywords`; add a step: "In the Currency card, choose the currency your studio prices and
  takes payments in. It defaults to your country's currency; you can pick another (for example
  EUR) until your first payment, gift card, package sale or booth-rent charge is recorded — after
  that it's locked and only support can change it." Add a warning: "Changing currency doesn't
  convert your prices — the numbers stay the same and are read in the new currency. Review your
  services, deposit rules, packages and gift-card amounts after changing."
- **G2 — New article `owner-studio-currency`** (roles `[Owner]`, route `/studios/me`, keywords
  `currency, lek, ALL, euro, EUR, change currency, locked currency, card not available`):
  where it's set, why it locks, why card deposits may be unavailable in some currencies ("card
  deposits are available only in currencies your card provider supports — POK supports ALL and
  EUR; cash always works"), that clients always pay exactly the amount shown in your currency.
  `relatedArticleIds: ["owner-studio-profile", "owner-connect-pok", "owner-payments"]`.
- **G3 — `owner-connect-pok`** (~1143): add a tip: "POK takes card deposits in ALL and EUR. If
  your studio's currency is anything else, clients see Cash only even after you connect."
- **G4 — `client-deposit-pay`** (~340) and **`faq-cash-vs-card`** (~1957): "You pay the
  deposit in the studio's currency — exactly the amount shown. If card isn't offered, the studio's
  currency isn't supported for card payments yet; pay in cash."
- **G5 — `owner-deposit-rules`/`owner-deposit-rule-create`** (~1058/1072) and **`owner-promo-codes`**
  (~1093/1103/1108): replace "a set euro amount" → "a set amount in your studio's currency";
  "floored at €0" → "floored at 0".
- **G6 — `owner-reports`** (~1428) and **`artist-earnings`** (~1462): tip "Totals are in your
  studio's currency. Payments recorded in any other currency are never mixed into a total — the
  report tells you how many were left out."
- **G7 —** `owner-services`, `owner-service-create`, `owner-gift-cards`, `owner-packages`,
  `owner-booth-rent`, `client-gift-cards`, `client-packages`, `owner-payment-create`: sweep for
  `€`/"euro" and neutralise to "your studio's currency". `owner-payment-create`: remove any
  mention of choosing a currency; the checkout link now shows the client the server amount.
- Leave `admin-*` plan articles (`€0` at ~1695) — subscription, EUR by design.
- After editing, run the existing Help tests (`features/help/__tests__/`, incl.
  `planNamesInDocs.test.ts`) and any article-id integrity test.

### 13.2 Manual — `frontend/public/user-manual/index.html`

Mirror G1–G7 in the matching sections (Studio Settings, Deposit rules, Promo codes, Payments,
Reports, Gift cards, Packages, Booth rent, Client: paying a deposit). Add a new "Your studio's
currency" subsection under Studio Settings with the G2 content. Replace every studio-side `€`
example with a currency-neutral phrasing or an explicit "(example in EUR)". Subscription/pricing
sections keep `€`.

### 13.3 Onboarding tours (`frontend/src/features/help/tours/`)

- `ownerTour.ts` — **update** the `owner-studio-profile-nav` step body (currently "Edit your
  studio's public details, branding, booking widget, QR code, referral code, API/webhook access,
  and your POK account for card deposits here.") to add "…your studio's currency, …". **Add**
  one step after `owner-studio-hours-card` targeting `[data-tour="owner-studio-currency-card"]`,
  `route: "/studios/me"`, title "Your currency", body "Prices and payments use this currency.
  Set it before your first payment — it locks after that."
- `clientTour.ts`, `artistTour.ts`, `adminTour.ts`: **no change** — state why in the commit
  body (client: no step touches prices/checkout — verify by reading the file and cite the step
  ids you checked; artist: earnings step, if any, has no currency wording — verify; admin:
  subscription-only money, out of scope).

### 13.4 Architecture docs

- `docs/claude/architecture.md` **Decisions Log** — new row "Studio currency (2026-09-27)":
  one currency per studio, defaulted from country via `RegionInfo`, stored on every money
  record (`Payment`, `GiftCard`, `PackagePurchase`, `BoothRentCharge`), price settings inherit;
  lock on first money record; providers own wire-format conversion (why: ISK/UGX/HUF-style
  provider deviations); `decimal(18,4)` + ISO rounding; reports never sum across currencies
  (same bug class as the pre-July MRR one); fixed `APP_LOCALE` (divergence from Finance's
  "user language" — no i18n exists); existing studios → EUR; `PackagePurchase.Amount` added
  beyond Finance's list and why. Confirm the exactly-one new `IgnoreQueryFilters()` approved-usage row
  (`StudioCurrencyLock`, §2.7) and the exactly-one new `AllowAnonymous` row (§2.7, country default
  currency) both landed in Phase D, and that `BoothRentChargeJob` needed no new row.
- `architecture.md` **Payment Architecture** section: add a "Currency" paragraph (the §2.1 rule
  in four lines), and fix the stale "`NullPaymentProvider` is the DI default" sentence (§3.6).
- **Feature Module Map**: update the Payments/Studios rows to mention currency.
- `docs/claude/database.md`: document the new columns and the `decimal(18,4)` money convention
  for Flow A (and that Flow B stays `decimal(10,2)`).
- `docs/claude/frontend.md`: "All money goes through `formatCurrency`/`MoneyInput`; never
  hard-code a currency or locale" as a convention.
- **`docs/payments/ADR-0001-amendment-C-studio-currency.md`** (new): supersedes the
  `CreateDepositPaymentCommand` comment "ADR-0001: POK is native-ALL; every deposit is quoted and
  charged in lek" — charges are in the studio's currency, gated by provider support. Link it from
  the Decisions Log row.
- `docs/specs/studio-currency-backlog-2026-09-27.md` — §4 content.

Commit: `docs: studio currency in Help, manual, owner tour, architecture, database/frontend conventions, ADR-0001 Amendment C, backlog spec (Phase G)`

---

## 14. Industry-standard benchmark note (CLAUDE.md rule #6)

Checked 2026-09-27 (sources in the final report):

- **Square** fixes currency by the account's country and does not let an existing account change
  it ("The country of your account will dictate the currency symbol and currency formatting…
  You're unable to change the country of an existing Square account") — our lock-after-first-money
  rule is *more* flexible than Square's and still safe.
- **Stripe** and **Adyen** express amounts in each currency's minor unit and document provider-
  specific deviations from ISO 4217 (Stripe: ISK/UGX sent as two-decimal; Adyen: CLP, CVE, IDR,
  ISK) — the reason §2.3 puts wire conversion inside each provider.
- Fresha/Vagaro/Mindbody/Boulevard/Booksy all price per business/location in one local currency
  and charge in it; none convert at checkout. Finance's reasoning (studio sets prices and receives
  the money; client-location pricing would put FX risk on the studio) matches. Their public help
  pages didn't expose the setting's lock semantics when checked — treat "one currency per
  business, set at onboarding" as the category norm, not a verified per-vendor detail.
- **Deliberate divergences, flagged:** (a) owner-selectable non-local currency (EUR in Albania)
  is a local-market need, pending the Legal check in §3.2; (b) the lock trigger includes gift
  cards, packages and booth rent, stricter than "first payment" — because those are money records
  too.
- **Category-standard UX this change includes:** confirmation before a destructive setting change,
  explicit lock state with a support escape hatch, loading/error/empty states on the new card,
  server-authoritative checkout amount.

---

## 15. Final verification checklist (run all before declaring done)

- [ ] `dotnet build` clean (no new warnings), `dotnet test` green (unit + integration).
- [ ] `pnpm lint`, `pnpm test`, `pnpm build` green in `frontend/`.
- [ ] `dotnet ef migrations has-pending-model-changes` → none; migration applied twice on a
      Testcontainers DB changes nothing the second time.
- [ ] `git grep -n "DepositCurrency\|\"ALL\"" -- Pena_e_Arte.Application Pena_e_Arte.Infrastructure ':!*Migrations*' ':!*Seed*'`
      → only `PokPaymentProvider.SupportedCurrencies` and test-free references you justify in the report.
- [ ] `git grep -n "\* 100\b" -- Pena_e_Arte.Application` → only Flow B / percent math, each listed.
- [ ] The §12.2 frontend grep returns nothing outside the allowed paths.
- [ ] No file in §5's do-not-touch list appears in `git diff --stat main`.
- [ ] No log template contains a name, email, phone or card field; currency logs carry ids/counts/codes only.
- [ ] `PUT /studios/me/currency` has `.RequireAuthorization("OwnerOnly")` + validator; the public
      default-currency endpoint has `.AllowAnonymous().RequireRateLimiting("public-read")` +
      validator + its row in the "AllowAnonymous Exceptions" table. No other new endpoints.
- [ ] Help: G1–G7 done in `helpContent.ts`; manual mirrors them; `ownerTour.ts` updated + new
      step; client/artist/admin tours' "no change" justified in the Phase G commit body.
- [ ] Every §10.3/§9.4/§11/§12.5 scenario has a test, and the Finance "Done means" table is fully
      covered (Albania ALL, EUR switch before payment, Japan JPY no-decimals + no card + cash,
      KWD 3 dp, cash deposit currency, ALL card request, EUR card request, lock refusal, reports
      exclusion, ALL invoice, backfill idempotent, Help).
- [ ] Seven commits on `feature/studio-currency-2026-09-27`.

---

## 16. Final deliverable spec

**Commits (in order):**
1. `feat(money): ISO 4217 currency catalog, country default currency, money text formatter (Phase A)`
2. `feat(db): studio country + currency, currency on every money record, widen money columns to 4dp, EUR backfill (Phase B)`
3. `fix(payments): every money record takes the studio currency; providers own wire-format conversion; card gated by currency (Phase C)`
4. `feat(studios): country + currency at registration (studio and solo), owner currency setting with lock, currency on every money response (Phase D)`
5. `fix(reports): never sum across currencies; currency on every total, export, invoice, email and calendar entry (Phase E)`
6. `feat(frontend): studio currency everywhere — one formatter, currency picker at registration and in settings, server-sourced checkout amount (Phase F)`
7. `docs: studio currency in Help, manual, owner tour, architecture, database/frontend conventions, ADR-0001 Amendment C, backlog spec (Phase G)`

**Docs written/updated:** `docs/claude/architecture.md` (Decisions Log row, Payment Architecture
currency paragraph + stale-sentence fix, Feature Module Map, any registry rows),
`docs/claude/database.md`, `docs/claude/frontend.md`,
`docs/payments/ADR-0001-amendment-C-studio-currency.md`,
`docs/payments/runbook-studio-currency-migration-2026-09-27.md`,
`docs/specs/studio-currency-backlog-2026-09-27.md`.

**Final report (write to `docs/claude/report-studio-currency-2026-09-27.md` and print it):**
per-phase file list; every drift between this prompt and the checkout; every `"ALL"`/`* 100`/
`€` grep hit and its disposition; the §3 flags verbatim with the defaults applied; the
pre-deploy runbook reminder ("run §8.6 SQL against production before deploying; do not enable
any studio for live card deposits until the POK staging transaction in §3.1 is done"); the
`docs/user-manual.html` staleness note; and the benchmark sources.

**Do not** open a PR or merge; leave the branch for review.
