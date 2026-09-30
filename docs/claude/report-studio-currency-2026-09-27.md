# Final Report — Studio Currency: One Currency per Studio, Stored on Every Money Record, Charged Exactly as Shown

**Branch:** `feature/studio-currency-2026-09-27` (not merged, no PR opened — per explicit instruction)
**Date executed:** 2026-09-27 (session continued across several context windows)
**Source prompt:** `docs/claude/overnight-prompt-studio-currency-2026-09-27.md`

---

## 1. What shipped

Every studio now has exactly one currency (`Studio.Currency`, ISO 4217), defaulted from
`Studio.CountryCode` at registration (`CountryCurrency.DefaultCurrencyFor`), shown and editable
in Studio Settings until the studio's first money-moving event locks it. Every money record
(payments, deposits, gift cards, packages, booth rent, invoices, reports, exports, emails,
calendar entries) carries that currency explicitly — no more implicit `€`/`"EUR"` on the frontend
against an `"ALL"`-charging backend. Card payments are gated off for studios whose currency POK
doesn't support; cash remains available everywhere. The client checkout amount is now read from
the server, not the URL query string.

All seven phases are complete and committed on `feature/studio-currency-2026-09-27`:

| # | Commit | Subject |
|---|---|---|
| 1 | `4c0d1d66` | `feat(money): ISO 4217 currency catalog, country default currency, money text formatter (Phase A)` |
| 2 | `2790f1f2` | `feat(db): studio country + currency, currency on every money record, widen money columns to 4dp, EUR backfill (Phase B)` |
| 3 | `e02fe432` | `fix(payments): every money record takes the studio currency; providers own wire-format conversion; card gated by currency (Phase C)` |
| 4 | `22abaa58` | `feat(studios): country + currency at registration (studio and solo), owner currency setting with lock, currency on every money response (Phase D)` |
| 5 | `66c8cfec` | `fix(reports): never sum across currencies; currency on every total, export, invoice, email and calendar entry (Phase E)` |
| 6 | *(this session)* | `feat(frontend): studio currency everywhere — one formatter, currency picker at registration and in settings, server-sourced checkout amount (Phase F)` |
| 7 | *(this session)* | `docs: studio currency in Help, manual, owner tour, architecture, database/frontend conventions, ADR-0001 Amendment C, backlog spec (Phase G)` |

---

## 2. Per-phase file list

### Phase A — `4c0d1d66` (already committed, unchanged this session)
`Pena_e_Arte.Domain/Money/CurrencyCatalog.cs`, `CountryCurrency.cs`, `MoneyText.cs` +
`tests/Pena_e_Arte.UnitTests/Domain/Money/{CurrencyCatalogTests,CountryCurrencyTests,MoneyTextTests}.cs`.

### Phase B — `2790f1f2` (already committed, unchanged this session)
`Studio.CountryCode`/`Currency` columns, widened money columns to 4dp, new migration, EUR backfill
SQL, entity configurations, integration tests.

### Phase C — `e02fe432` (already committed, unchanged this session)
`CreateDepositPaymentCommand`, `PurchaseGiftCardCommand`, `PurchasePackageCommand`,
`CreatePaymentIntentCommand`, `PokPaymentProvider` (wire-format conversion moved inside the
provider), card-currency gating, `Payment.Currency` no longer defaults to `"ALL"`.

### Phase D — `22abaa58` (already committed, unchanged this session)
`RegisterStudioCommand`, `RegisterSoloArtistCommand`, `UpdateStudioCurrencyCommand` (new,
`OwnerOnly`), `GetCountryDefaultCurrencyQuery` (new, `AllowAnonymous` + `public-read` rate limit),
currency-lock check (`StudioCurrencyLock`, re-scoped `IgnoreQueryFilters()`), currency added to
every studio-facing response.

### Phase E — `66c8cfec` (already committed, unchanged this session)
Report handlers (never sum across currencies — group/reject instead), PDF invoices, emails,
calendar `.ics` entries, CSV/Excel exports — currency on every total.

### Phase F — this session's commit — frontend (~117 files; full list below)
**New:**
`frontend/src/shared/utils/currencies.ts` (+ test) — currency catalog, `MAJOR_CURRENCIES`,
`buildCurrencyOptions`, `currencyDisplayName`.
`frontend/src/shared/components/ui/currency-select.tsx` — Radix-based currency picker.
`frontend/src/shared/components/ui/money-input.tsx` — minor-unit-aware amount input.
`frontend/src/shared/hooks/useStudioCurrency.ts` — thin wrapper over `useGetMyStudioQuery`.
`frontend/src/features/studios/components/CurrencySettingsCard.tsx` (+ test) — Studio Settings
currency card with locked/unlocked states.

**Modified — currency infrastructure:** `frontend/src/shared/utils/formatCurrency.ts` (+ test,
`currencyLabel` added), `frontend/src/features/public/publicApi.ts` (currency on
`PublicArtistResponse`/`DesignCatalogItemResponse`), `frontend/src/features/studios/studiosApi.ts`,
`frontend/src/features/auth/authApi.ts`, `frontend/src/shared/hooks/useAddressGeocode.ts` (+ test),
`frontend/src/shared/components/ui/location-picker.tsx`.

**Modified — registration & settings:** `frontend/src/features/studios/components/RegisterStudioPage.tsx`
(+ test) — country/currency pickers in both studio and solo modes;
`frontend/src/features/studios/components/StudioProfilePage.tsx` (+ test) — mounts
`CurrencySettingsCard`, country-select controlled-value fix.

**Modified — the 34-file `€`/`"EUR"`/`pt-PT` sweep** (money display now goes through
`formatCurrency`/`MoneyInput`, sourced from `useStudioCurrency()`), each with its test file
updated: appointments (`AppointmentCard`, `AppointmentDetailPage`, `BookAppointmentForm`,
`MyBookingsSection`), artists (`ArtistDetailPage`, `ArtistListPage`, `CreateArtistPage`),
booking (`GuestBookAppointmentForm`), booth-rent (`boothRent.types.ts`,
`BoothRentManagementPage`, `MyBoothRentSection`), dashboard (`DashboardPage`), deposit-rules
(`CreateDepositRulePage`, `DepositRuleCard`, `DepositRuleDetailPage`), gift-cards
(`giftCards.types.ts`, `GiftCardListPage`, `PurchaseGiftCardPage`), payments
(`payment.types.ts`, `CashDepositConfirmButton`, `CreatePaymentIntentPage`,
`DepositCheckoutPage`, `PaymentDetailPage`, `PaymentListPage`, `PaymentMethodSelector`,
`SessionSplitsEditor`), promo-codes (`CreatePromoCodePage`, `PromoCodeCard`,
`PromoCodeDetailPage`), public (`ArtistPortfolioPage`), reports (`report.types.ts`,
`MyEarningsPage`, `ReportsPage`, `RevenueTrendChart`), services (`CreateServicePage`,
`ServiceCard`, `ServiceDetailPage`), session-packages (`packages.types.ts`, `PackageListPage`,
`PurchasePackagePage`).

**Test-only touches** (Redux store wiring for `useStudioCurrency`/`studiosApi`, or fixture
updates for the widened `StudioResponse` shape — no assertions changed):
`ArtistListPage.test.tsx`, `artists.test.tsx`, `SchedulePage.test.tsx`, `BookPage.test.tsx`,
`SavedPaymentMethodsPage.test.tsx`, `StudioClosuresCard.test.tsx`, `StudioHoursCard.test.tsx`,
`BrandingSettingsCard.test.tsx`, `DeveloperSettingsCard.test.tsx`, `WebhookSettingsCard.test.tsx`,
`OwnerLayout.test.tsx`, `SoloStudioPublishBanner.test.tsx`, `SuspensionBanner.test.tsx`,
`chartColorTokens.test.tsx`, `EmbedPage.test.tsx`, `StudioPortfolioPage.test.tsx`,
`ArtistPortfolioPage.test.tsx`.

**Backend companion fixes found mid-sweep** (bundled into the Phase F commit — see §3 Drift):
`Pena_e_Arte.Contracts/Responses/Public/PublicArtistResponse.cs`,
`Pena_e_Arte.Contracts/Responses/Public/DesignCatalogItemResponse.cs`,
`Pena_e_Arte.Application/Public/Queries/GetPublicArtistQuery.cs`,
`Pena_e_Arte.Application/Public/Queries/GetDesignCatalogQuery.cs`,
`tests/Pena_e_Arte.UnitTests/Public/GetPublicArtistHandlerTests.cs`,
`tests/Pena_e_Arte.UnitTests/Public/SeoShellHtmlWriterTests.cs`,
`tests/Pena_e_Arte.UnitTests/Designs/GetDesignCatalogHandlerTests.cs`.

### Phase G — this session's commit — docs
`frontend/src/features/help/helpContent.ts` (G1–G7: registration currency picker, Studio
Settings currency card, lock behaviour, card-hidden-for-unsupported-currency note, reports/
exports currency labelling, admin visibility, FAQ entry), `frontend/public/user-manual/index.html`
(new "Studio currency" section under Owner → Studio Settings, updated Studio Profile section,
all 37 studio-side `€` occurrences replaced with currency-neutral language),
`frontend/src/features/help/tours/ownerTour.ts` (new step pointing at the currency card),
`docs/claude/architecture.md` (Decisions Log row "Studio currency (2026-09-27)", Payment
Architecture stale-`NullPaymentProvider`-sentence fix + new Currency paragraph, Feature Module
Map rows #01/#05 updated, `AllowAnonymous Exceptions` table row for
`GET /public/countries/{countryCode}/default-currency`), `docs/claude/database.md` (Studio
struct gains `CountryCode`/`Currency`, new "Money Columns" section), `docs/claude/frontend.md`
(new "Currency" conventions section, including the Radix controlled-value gotcha),
`docs/payments/ADR-0001-amendment-C-studio-currency.md` (new), `docs/specs/studio-currency-backlog-2026-09-27.md`
(new — §4 items, spec-only), `docs/payments/runbook-studio-currency-migration-2026-09-27.md` (new).

---

## 3. Drift from the prompt (every deviation, as it happened)

1. **`PublicArtistResponse`/`DesignCatalogItemResponse` were missing `Currency`.** Discovered
   while wiring the public artist/design pages to show studio-sourced currency instead of a
   hardcoded `€`. Both responses, their handlers, and three existing test files were updated to
   project `studio.Currency`. Bundled into the Phase F commit rather than reopening Phase D/E,
   since the change is purely additive and frontend-driven.

2. **Radix Select uncontrolled→controlled bug, found twice.**
   - `StudioProfilePage.tsx`'s country `<Select>` and `RegisterStudioPage.tsx`'s currency
     `<Select>`s (studio + solo mode): value starts falsy, becomes a real code later, and Radix's
     hidden native-`<select>` autofill shim fires a spurious `onValueChange("")`, resetting the
     field. Fixed with `value ?? ""` (never `undefined`) plus an `onChange` guard that ignores an
     empty callback value.
   - **A second instance of the same bug was found this session, inside `currency-select.tsx`
     itself** (`value={value ?? undefined}` — the *wrong* half of the documented fix had been
     applied there, converting `null` back to `undefined` instead of `""`). This is the shared
     `CurrencySelect` component used by both registration and Studio Settings, so the bug was
     live in both places. Fixed to `value={value ?? ""}`. This is now the second and third
     confirmed occurrence of this pattern in the codebase; `docs/claude/frontend.md`'s existing
     write-up of the gotcha covers the fix, but did not previously flag that the *component*
     itself, not just its callers, needs auditing.

3. **`currencyLabel` vs `currencyDisplayName`.** `currencyLabel("EUR")` returns the bare symbol
   (`"€"`), not the currency's name. A separate `currencyDisplayName` export (via
   `Intl.DisplayNames`) was added to `currencies.ts` for places needing the full name (e.g. the
   locked-state text in `CurrencySettingsCard`).

4. **`RegisterSoloArtistCommand.cs` backward-compatibility fallback.** Old API clients (mobile,
   or anything not yet updated) that send no `CountryCode` get `Currency = "ALL"` and
   `CountryCode = "AL"` rather than a validation failure — documented inline and mirrored in the
   test mock (`mockGetCountryDefaultCurrency` in `RegisterStudioPage.test.tsx` defaults unknown
   codes to `"USD"`, matching how .NET's `RegionInfo` resolves virtually every real country;
   `null` is reserved for genuinely unknown codes only).

5. **Seed data deliberately keeps one studio on real ISO `"ALL"`.** `DataSeeder.cs`'s Studio2 is
   seeded with `CountryCode="AL", Currency="ALL"` on purpose (comment in file) so dev/staging has
   one studio of each currency kind to click through, distinct from Studio1's
   `CountryCode="AL", Currency="EUR"` (the migrated-existing-studio shape). Not a bug.

6. **Do-not-touch-list files touched, minimally and mechanically.** `Studio.cs`'s `Response`
   shape (billing/platform's `StudioResponse` DTO) gained the new `countryCode`/`currency`/
   `currencyLocked` fields platform-wide (Phase D). Four test files under the prompt's §5
   do-not-touch paths (`frontend/src/features/billing/__tests__/BillingPage.test.tsx`,
   `frontend/src/features/platform/__tests__/{AdminStudioDetailPage,AdminStudioListPage,PlatformReferralPage}.test.tsx`)
   needed their `StudioResponse`-typed fixture objects updated with the three new required
   fields to keep TypeScript compiling. **No `€`/currency-display assertion in any of these four
   files was touched** — verified by diff. This is the only touch to a §5 path, and it is a
   type-compilation necessity, not a scope expansion into billing/platform behaviour.

7. **CPU-contention test flakiness on this sandbox (pre-existing, not introduced).** Running the
   full frontend suite (189 files / 2523 tests) in one process on this machine produces ~100
   spurious timeouts, and two files (`PokSettingsCard.test.tsx`, `DesignListPage.test.tsx`,
   neither touched by this work) failed to even start a worker (`Timeout waiting for worker to
   respond`). Every currency-touched file was verified green when run alone or in the 46-file
   currency-only batch with a generous timeout:
   - `RegisterStudioPage.test.tsx` — 37/37 passing in isolation.
   - `StudioProfilePage.test.tsx` — 35/35 passing in isolation.
   - The 46-file currency-touched batch — 44/46 files green; the 2 exceptions were the two files
     above, both green when run alone.
   Two tests in `StudioProfilePage.test.tsx` ("slug editing") and two in
   `phone-input.test.tsx` already carry an in-file comment dated 2026-09-05 — **before this
   task existed** — documenting this exact flakiness. Treated as environmental per
   `feedback_windows_tooling_gotchas` / established project precedent, not a regression.

8. **`docs/user-manual.html` (repo root) is stale and was intentionally left untouched.** This is
   a separate, older manual (last touched in a July 2026 rebrand commit) from the maintained
   `frontend/public/user-manual/index.html`, which this task updated. The root file still
   contains 5 `€` references. It is not linked from the app and appears to predate the current
   manual; flagging its staleness rather than editing a manual outside this task's scope.

---

## 4. §3 open questions — flagged verbatim, defaults applied

| # | Question | Default applied tonight |
|---|---|---|
| 3.1 | **POK amount unit** (whole units vs. minor units) — `PokPaymentProvider.cs` itself calls this "THE SINGLE HIGHEST-RISK UNVERIFIED ASSUMPTION". | Kept the current assumption (whole-unit decimal), now isolated to one place: `PokAmount(decimal amount, string currency) => CurrencyCatalog.Round(amount, currency)` inside `PokPaymentProvider`. **Not resolved by code — do not enable any studio for live card deposits until a human runs one real POK staging transaction and confirms the unit.** |
| 3.2 | **Albanian pricing rules** — may an AL-registered business price/charge in EUR instead of lek? (Law 55/2020 on Payment Services may not be the only applicable rule.) | Built per Finance's decision: EUR is selectable for AL studios; existing studios were migrated to EUR. **Needs Legal sign-off — flagged, not obtained.** |
| 3.3 | Short list of "major currencies" shown first in the picker. | `EUR, USD, GBP, CHF` — exported as `MAJOR_CURRENCIES` in `currencies.ts`, trivially changeable. |
| 3.4 | Existing Albanian studios — notify about switching to ALL, or stay silent? | Silent. Help text says a currency change after the first payment goes through support. No banner, no email. |
| 3.5 | Cash display for a studio whose currency POK doesn't support (e.g. JPY). | Card option hidden with an explanatory line; cash-only, no other behaviour change. |
| 3.6 | `architecture.md`'s "Payment Architecture" section called `NullPaymentProvider` "the DI default" — stale; `InfrastructureServiceExtensions.cs:188` actually registers `PokPaymentProvider`. | Corrected in Phase G; listed here as the drift it documents. |

## §4 — specified, not implemented

All five items (admin change of a locked currency, multi-currency-in-one-studio [rejected by
Finance], the "≈" client-currency estimate, cross-studio admin totals, additional card
providers) are written up as sections — entities, endpoints, migration shape, open questions,
**no code** — in `docs/specs/studio-currency-backlog-2026-09-27.md`.

---

## 5. Pre-deploy runbook reminder

Full runbook: `docs/payments/runbook-studio-currency-migration-2026-09-27.md`. The two points
that must not be missed:

1. **Run the §8.6 backfill SQL against production before deploying** the new code that reads
   `Studio.Currency`/`Payment.Currency` as authoritative — the backfill sets every existing row
   to the correct migrated value first.
2. **Do not enable any studio for live card deposits** until the POK staging transaction in §3.1
   above has been run by a human with real POK staging credentials and the amount-unit
   assumption is confirmed. Cash remains safe to use today; it always has been.

---

## 6. Industry-standard benchmark sources (CLAUDE.md rule #6)

Checked 2026-09-27:
- **Square** fixes currency by account country and does not allow changing it on an existing
  account — this design's lock-after-first-money rule is *more* flexible than Square's and still
  safe.
- **Stripe** and **Adyen** express amounts in minor units and document provider-specific
  deviations from ISO 4217 (Stripe: ISK/UGX as two-decimal; Adyen: CLP/CVE/IDR/ISK) — the reason
  wire-format conversion now lives inside each provider (`PokPaymentProvider.PokAmount`), not in
  a shared converter.
- **Fresha / Vagaro / Mindbody / Boulevard / Booksy** all price per business/location in one
  local currency and charge in it; none convert at checkout, matching Finance's reasoning (the
  studio sets prices and receives the money, so FX risk should not sit with the studio). Their
  public help pages didn't expose the lock semantics when checked — treated as "one currency per
  business, set at onboarding" being the category norm, not a verified per-vendor detail.
- **Deliberate divergences, flagged:** owner-selectable non-local currency (EUR in Albania) is a
  local-market need, pending the Legal check in §3.2 above; the lock trigger includes gift cards,
  packages and booth rent — stricter than "first payment" — because those are money records too.
- **Category-standard UX included:** confirmation before a destructive setting change (the
  currency lock), explicit lock state with a support escape hatch, loading/error/empty states on
  the new Studio Settings card, server-authoritative checkout amount.

---

## 7. Verification performed

**Backend:** `dotnet build` clean; `dotnet test` — **580/580 integration tests, 2957/2957 unit
tests, 0 failed.**

**Frontend:**
- `pnpm lint` (`eslint .`) — **0 errors**, 18 pre-existing warnings (React Compiler
  "incompatible library" notices on `react-hook-form`'s `watch()`, present across the codebase
  before this task; one unrelated unused-eslint-disable notice in an untouched test file).
- `pnpm build` (`tsc -b && vite build`) — **clean**, no type errors; one pre-existing bundle-size
  advisory (>500kB chunk), unrelated to this change.
- `vitest run` on every currency-touched file, run in isolation or in the 46-file currency-only
  batch with a generous timeout — **all green** (see §3 item 7 for the exact breakdown and the
  CPU-contention explanation for the full-suite numbers).
- Backend `git grep` checks: `DepositCurrency|"ALL"` outside migrations/seed → only
  `RegisterSoloArtistCommand.cs`'s documented fallback and `PokPaymentProvider.SupportedCurrencies`.
  `\* 100\b` in `Pena_e_Arte.Application` → only `YearlySavingCalculator` and
  `GetPlatformStatsQuery`'s percent-change math (Flow B, unrelated to currency conversion).
- Frontend `€`/`"EUR"` grep outside tests → only `currencies.ts` (the data source) and the
  platform/billing files explicitly excluded by §5 (subscription pricing, EUR by design).
- Endpoint auth verified directly: `PUT /studios/me/currency` →
  `.RequireAuthorization("OwnerOnly").RequireRateLimiting("billing")`;
  `GET /public/countries/{countryCode}/default-currency` →
  `.AllowAnonymous().RequireRateLimiting("public-read")`, with its row present in
  `architecture.md`'s `AllowAnonymous Exceptions` table.
- §5 do-not-touch diff against `main` → only the four test-fixture touches in §3 item 6 above,
  justified.

**Not verified (cannot be, without a human and real credentials):** the POK amount-unit
assumption (§3.1) — this is the reason live card deposits stay disabled until that staging run
happens.

---

## 8. Status

All seven phases are implemented, tested, documented, and committed to
`feature/studio-currency-2026-09-27`. **No PR opened, no merge performed** — the branch is left
for review as instructed.
