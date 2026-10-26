# Spec — EUR studios in Albania: show the lek amount next to every euro amount a client sees

| | |
|---|---|
| Date | 2026-10-26 |
| Author / owner | Claude Desktop (consultation). Requester: Phi |
| For | Claude Code, full repo access, **fully autonomous, no user present** (see §0) |
| Checked against | `main` = `origin/main` **1d49791d** (merge of PR #295, 2026-10-25 22:55 CET) |
| Origin | Phi, 26 Oct 00:43–00:52 CET: charging clients in EUR is allowed for every client, whatever their card; the lek price must be shown next to the euro price; rate = Bank of Albania official daily rate, taken **when the client pays**; rounding, storage and the POK card page = industry standard. Recorded in `handoffs\desktop-production-setup-done-2026-10-26.md` §2a |
| Not a go-live blocker | Go-live (C-11) runs in ALL. This feature is what lets an Albanian studio use EUR afterwards |

## 0. Autonomy contract — read first

Phi is not available during this run. **Never stop to ask and never wait for an answer** (no `AskUserQuestion`, no
"shall I continue?"). Finish every phase you can; record what you couldn't do. The run is done when §10 is
complete or every remaining item is a recorded hard stop.

**When something is unclear, decide with this ladder and keep going:**
1. This spec.
2. **The live code wins over this spec's line numbers and quoted shapes.** If a file moved, a name changed or a line
   number is off, find the equivalent and adapt. If the code contradicts a *decision* in §2, follow §2 and note it.
3. The repo's conventions (`CLAUDE.md`, `docs/claude/*.md`) and the nearest existing example in the code.
4. The default this spec states (§3 defaults, §9 industry norms).
5. Otherwise, the **most reversible** option. Implement it and add a row to the PR's "Decisions I made" table
   (what, why, how to undo).

**Hard stops — the only reasons not to do an item.** Skip just that item, record it, and carry on with everything
that doesn't depend on it:
- it would need a secret, credential or API key, or access to production (database, deploy, POK production);
- it would move real money;
- it would add an npm or NuGet package;
- it would touch anything in §4, merge a PR, push to `main`, or delete or stage Phi's untracked files.

**Failures:**
- A failing test you caused: fix it (as many attempts as it takes while you're making progress; if the same fix
  fails 3 times, try another approach; after 3 approaches, mark that test `Skip` with a reason, record it, continue).
- A test that also fails on untouched `main` (check in a separate worktree: `git worktree add ../main-check main`):
  not yours. Leave it, list it in the PR, continue.
- A flaky test: re-run once; if it passes, note it.
- Docker / MySQL not available for integration tests: run everything else, mark the integration tests "not run here"
  in the PR, continue.
- **BoA page unreachable in Phase 0:** do **not** stop. Build the parser against the table structure described in
  §2 D2 with a hand-made fixture named `boa-official-rate-SYNTHETIC.html`, mark the real-page parser test
  `[Fact(Skip = "BoA page not reachable from the build machine — replace fixture")]`, finish every phase, and make
  this the first item in the PR's follow-ups. The app is safe without a rate: it shows EUR only (§6 Phase 4).

**Phase dependencies** (if one is blocked, continue with what doesn't depend on it): 1 → 2 → 3; 4 needs Phase 1's
endpoint and Phase 2's response fields; 5 last.

**Keep the work safe and visible:**
- Commit at the end of each phase and `git push -u origin feat/eur-lek-reference-display` after each commit, so
  nothing is lost if the run is interrupted.
- After each phase, append one line to
  `C:\Users\User\Documents\Projects\Pena_e_Artë_AIO_Studio\claude-shared\log.md`
  (`YYYY-MM-DD HH:MM (Europe/Paris) | Claude Code | EUR/lek phase N: <result> | <branch or PR>`).
- At the end, open a **draft** PR with `gh pr create --draft`. If `gh` isn't available or authenticated, leave the
  branch pushed and put the PR body in the report below.
- Write the final report to `claude-shared\handoffs\code-report-eur-lek-display-<date>.md`: what's done per phase,
  test results, the "Decisions I made" table, every hard stop and skipped test, Q1–Q7, the PR link. Never put secrets,
  ids or personal data in it.

Before starting:
```bash
git checkout main && git pull --ff-only
git status --short            # untracked files listed in §4 are Phi's; leave them alone
git checkout -b feat/eur-lek-reference-display
```
Copy this file into the repo as `docs/specs/spec-eur-with-lek-display-2026-10-26.md` in the first commit (the shared
folder is outside the repo; the repo copy is what the Decisions Log row links to).

---

## 1. What is being built (one paragraph)

When a studio is in Albania (`Studio.CountryCode == "AL"`) and prices in euros (`Studio.Currency == "EUR"`), every euro
amount **a client sees** also shows its lek equivalent, converted at the **Bank of Albania (BoA) official exchange rate**.
Before payment the lek figure uses the latest published rate and is marked as approximate. **At the moment the client
pays, the rate is frozen onto the money record** (rate, rate date, lek amount) and every later view of that payment —
receipt PDF, emails, SMS, refunds, the client's booking list — uses the frozen values, never today's rate. The card is
still charged in **EUR**; nothing about the POK charge changes.

## 2. Decisions already made — do not re-litigate

| # | Decision | Source |
|---|---|---|
| D1 | Applies to **every client** of an AL + EUR studio, whatever their card or nationality. No per-client or per-card rule. | Phi, 00:45 |
| D2 | Rate source: **BoA official rate ("Fix")**, EUR row, "Albanian Lek per Foreign Currency Unit", page `https://www.bankofalbania.org/Markets/Official_exchange_rate/`. Set from interbank quotes in the 11:30–12:00 window and published on **business days only** (page read 26 Oct 00:5x CET: last update "23.10.2026, 12:16:19", EUR 90.74). No API, CSV or usable RSS exists (the RSS feed carries no rates). On days with no publication the latest published rate applies. | Phi 00:52; BoA page |
| D3 | **"When they pay"** = the moment the payment is initiated on our side: when the provider order is created for a card payment (with C-4 "charge at booking" that is the moment the client pays), or when the studio confirms a cash payment as received. The values frozen then are the ones the client saw on our page right before paying. A new provider order (e.g. B4: an expired order is never resumed, a fresh one is created) takes a fresh snapshot. | Phi 00:52, interpreted here — see §3 Q1 if Phi wants the authorisation/capture time instead |
| D4 | **Rounding (industry standard):** lek = EUR amount × rate, computed in `decimal` with **no intermediate rounding**, then rounded **once** to **whole lek**, **midpoint away from zero** (same rule as `CurrencyCatalog.Round`). Whole lek because qindarka are not used in practice, CLDR/`Intl` already displays ALL with 0 decimals (`formatCurrency` → "ALL 12,346"), and POK takes ALL in whole units (`ToPokAmount`, gate 2026-10-14). Convert **each displayed amount itself** (a total is converted from the EUR total, never summed from converted lines). | Bulgaria dual-display rules 2025–26 (exact rate, no rounding during calculation, standard rounding); see §9 |
| D5 | **Store the snapshot** on each client money record: reference currency, rate (6 dp), rate date, source, lek amount. Never recompute a stored payment's lek from a later rate. **Refunds use the payment's stored rate** (the lek shown for a refund = refunded EUR × the original rate). | Stripe: refunds use "the same exchange rate used in the transaction" (§9) |
| D6 | **POK hosted card page** shows EUR only and is outside our control: accepted as is. Our own checkout page, immediately before the client pays, shows both amounts plus one line: *"Your card is charged in euros. If your card is in lek, your bank converts it at its own rate, which can differ slightly."* | Phi 00:52 ("default / industry standard") |
| D7 | **Presentation:** EUR first (it is what is charged), lek **next to it, same font size and weight**, with the currency code. Before payment: `€50 (≈ 4,537 ALL)`. After payment (frozen): `€50 (4,537 ALL)`. Receipts and payment emails add a rate line: `Exchange rate: 1 EUR = 90.74 ALL, Bank of Albania official rate of 23 Oct 2026`. | Bulgaria rules: "next to each other", "same font size", receipts "include the official exchange rate" (§9) |
| D8 | **No new NuGet or npm package.** The BoA page is parsed with a narrow, validated regex over the EUR table row (see §6 Phase 1), not an HTML library. | Constraints |
| D9 | Studios that are **not** AL + EUR see **no change at all** (ALL studios, EUR studios outside Albania, any other currency). | Phi's requirement is Albanian |

## 3. Open questions — do NOT decide these; implement the stated default and list them in the PR

| # | Question | Default built in this branch | Who decides |
|---|---|---|---|
| Q1 | Is "when they pay" the order-creation moment (D3), or the card authorisation / capture time? | D3 (order creation; for cash, confirmation) | Phi |
| Q2 | **Stale rate.** If the newest stored BoA rate is older than **7 calendar days** (BoA down, page changed, parser broke): what happens? | Lek is still shown from the newest rate, labelled with its date; an **Error** log + the existing job-failure alerting fire daily; **card checkout is not blocked**. Blocking EUR card checkout while stale is a one-line switch to add later if Phi/Legal want it | Phi (+ Legal if the lek display must never be stale) |
| Q3 | Should **staff screens** (owner/artist payment list, payment detail, gift-card list, package list, reports) also show lek? | No — client-facing only (§5). Owner reports must never sum lek anyway | Phi |
| Q4 | **Existing EUR records** created before this ships (demo/staging studios were backfilled to EUR, architecture.md "Studio currency (2026-09-27)"): show lek on their receipts? | No backfill. Records without a snapshot show EUR only, exactly as today | Phi |
| Q5 | The receipt PDF says "This document is your official receipt." Albanian **fiscalisation** (e-invoicing with the tax authority) is a separate legal obligation this spec does not touch | Unchanged | Legal / Finance |
| Q6 | TattooOS's **own** subscription prices to studios (Flow B: `PricingPage`, `BillingPage`, `SubscribePage`) are B2B and out of scope | Unchanged | Phi |
| Q7 | A written basis for "EUR charging is allowed" is not yet recorded | — | Phi (where the confirmation is kept) |

## 4. Do not touch

| Path | Why |
|---|---|
| `Pena_e_Arte.Infrastructure/Services/PokPaymentProvider.cs` (incl. `ToPokAmount`), `PaymentHoldRequest` in `Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs` | The charge stays EUR, unchanged. The lek figure is display/record only and never reaches POK |
| `Pena_e_Arte.Domain/Money/CurrencyCatalog.cs` | ALL stays 2 minor units there (ISO 4217). Whole-lek rounding is specific to the reference amount (D4) and lives in its own helper |
| `StudioCurrencyLock`, `CardCurrencyGuard`, `GetPaymentCapabilitiesQuery`, `Payments:CardDepositsEnabled` / `SavedCardsEnabled` | Unrelated gates; EUR card support already exists |
| `GetRevenueSummaryQuery`, `GetMyEarningsQuery`, any report | Reports never mix or convert currencies (architecture.md "Studio currency") |
| Flow B billing: `features/billing/**`, `features/public/components/PricingPage.tsx`, `features/platform/**`, Stripe services | Q6 |
| Staff-only pages listed in §5.3 | Q3 |
| Untracked files in the working tree (`.claude/settings.local.json`, `Claude outputs/…`, `docs/claude/TODO.md`, `docs/claude/legal-review-brief-yearly-refund-terms.md`, three `docs/specs/*-2026-10-01/02.md`, `frontend/.claude/`, `frontend/vite.verify.config.ts`) | Phi's; never stage or delete them |

## 5. Where clients see euro amounts today (verified on 1d49791d)

### 5.1 Frontend (all go through `frontend/src/shared/utils/formatCurrency.ts` → `formatCurrency(amount, currencyCode)`)

| File:line | What | Route / role | Before or after payment |
|---|---|---|---|
| `features/appointments/components/BookAppointmentForm.tsx:204` | estimated session price | `/book`, client | before |
| same `:221` | deposit amount | same | before |
| same `:569` | deposit on the booked confirmation | same | before (until paid) |
| `features/booking/components/GuestBookAppointmentForm.tsx:139, :154` | estimate, deposit | `/book` guest | before |
| `features/appointments/components/MyBookingsSection.tsx:269` | "Paying … in cash at the studio" | client | before |
| same `:287` | "Pay deposit — …" button | client | before |
| `features/payments/components/DepositCheckoutPage.tsx:91` | amount on the checkout page | `/pay/:paymentId` | **the moment of paying** → add the D6 line here |
| `features/payments/components/PaymentMethodSelector.tsx:440` | cash tab "Your deposit of …" | same | before |
| `features/payments/depositOutcome.ts:30` (`describeDeposit`, `money()`) | client labels/details; used by `MyBookingsSection` (client) and staff pages | client + staff | after → frozen values. Change only the **client** strings; owner strings stay EUR (Q3) |
| `features/public/components/ArtistPortfolioPage.tsx:262, :613` | price items, hourly rate | `/artist/:slug`, public | before |
| `features/gift-cards/components/PurchaseGiftCardPage.tsx:92` | preset amounts | `/s/:slug/gift-cards/buy`, public | before |
| `features/session-packages/components/PurchasePackagePage.tsx:67` | package price | client | before |

`StudioPortfolioPage.tsx` and `EmbedPage.tsx` show no money amounts (checked). `PaymentResultPage` shows none.

### 5.2 Backend text a client receives (all use `Pena_e_Arte.Domain/Money/MoneyText.cs` → `MoneyText.Format`)

| File:line | What |
|---|---|
| `Pena_e_Arte.Application/Payments/Commands/SendDepositCapturedNotificationCommand.cs:55` | deposit-received email + SMS (`amountFormatted`) |
| `Pena_e_Arte.Application/Payments/Commands/SendPaymentRefundedNotificationCommand.cs:52` | refund email + SMS |
| `Pena_e_Arte.Infrastructure/Jobs/DepositHoldLostNotificationJob.cs:66` | hold-lost notification |
| `Pena_e_Arte.Infrastructure/Services/PaymentInvoiceService.cs:164` (`FormatCurrency`) | receipt PDF (QuestPDF) |
| `Pena_e_Arte.Application/Appointments/Queries/GetAppointmentIcsQuery.cs:45` | calendar file "Deposit: …" (before payment → approximate) |

`RefundPaymentCommand.cs:56` uses `MoneyText` in an **owner-facing** validation error: unchanged.

### 5.3 Staff-only, unchanged (Q3)
`AppointmentCard` (used only by `SchedulePage`), `AppointmentDetailPage`, `ArtistDetailPage`, `CreatePaymentIntentPage`,
`PaymentDetailPage`, `PaymentListPage`, `GiftCardListPage`, `PackageListPage`, `CashDepositConfirmButton`,
`SessionSplitsEditor`, booth rent, deposit rules, promo codes, services, dashboard, reports.

### 5.4 Where a client money record is created (snapshot points, D3)

| File:line | Record |
|---|---|
| `Pena_e_Arte.Application/Payments/Commands/CreateDepositPaymentCommand.cs:166` (`CreatePaymentHoldAsync`) | `Payment` (client card deposit) |
| `Pena_e_Arte.Application/Payments/Commands/CreatePaymentIntentCommand.cs:54` | `Payment` (owner card link the client pays) |
| `Pena_e_Arte.Application/Payments/Commands/PayDepositWithSavedCardCommand.cs` | `Payment` (saved card, C-9, switch off — include so it is right when switched on) |
| `Pena_e_Arte.Application/Payments/Commands/ConfirmCashDepositCommand.cs` | `Payment` (cash, at confirmation) |
| `Pena_e_Arte.Application/GiftCards/Commands/PurchaseGiftCardCommand.cs:45` | `GiftCard` (`InitialBalance`) |
| `Pena_e_Arte.Application/Packages/Commands/PurchasePackageCommand.cs:42` | `PackagePurchase` (`Amount`) |

Read each before editing: the line numbers are from 1d49791d; match on the call, not the number.

## 6. Phases — each phase's definition of done includes its tests and its Help changes

### Phase 0 — Ground truth (no code)
1. Confirm nothing like this exists: `grep -rIi "exchangerate\|exchange rate\|bankofalbania"` over `Pena_e_Arte.*` and
   `frontend/src` returns nothing (true on 1d49791d).
2. Fetch `https://www.bankofalbania.org/Markets/Official_exchange_rate/` (English page) once, **save the raw HTML** as
   `tests/Pena_e_Arte.UnitTests/Fixtures/boa-official-rate-<yyyy-mm-dd>.html` (the `Fixtures` folder doesn't exist
   yet: create it and set the file to `CopyToOutputDirectory=PreserveNewest` in the test csproj). Find the EUR row's markup and the
   "last update" date. If the page cannot be fetched from this machine, follow §0 ("BoA page unreachable") — never
   switch to a third-party rate service (that is a new integration: Phi's decision).
3. Re-check D2 against the page you fetched (EUR quoted per 1 EUR; date format "dd.MM.yyyy, HH:mm:ss"). If anything
   differs, adapt the parser to what is there and note it in the PR.

### Phase 1 — Rate store, fetch job, read endpoint (backend only; no user-visible change)
- **Entity** `Pena_e_Arte.Domain/Entities/ExchangeRate.cs` — **global, not a `TenantEntity`** (a public rate belongs to
  no studio; so no query filter and no `IgnoreQueryFilters()` row is needed):
  `Guid Id; string BaseCurrency ("EUR"); string QuoteCurrency ("ALL"); DateOnly RateDate (BoA's stated date);
  decimal Rate (decimal(18,6), lek per 1 EUR); string Source ("BoA-Fix"); DateTime PublishedAtUtc (BoA's stated
  time, converted from Europe/Tirane); DateTime FetchedAtUtc`. Unique index (BaseCurrency, QuoteCurrency, RateDate, Source).
- **Interface** `Pena_e_Arte.Domain/Interfaces/IExchangeRateProvider.cs`:
  `Task<ExchangeRateSnapshot?> GetLatestAsync(string baseCurrency, string quoteCurrency, CancellationToken ct)` returning
  `record ExchangeRateSnapshot(decimal Rate, DateOnly RateDate, string Source, bool IsStale)` (`IsStale` = RateDate older
  than 7 calendar days in Europe/Tirane, Q2). Implementation reads the DB (cache 10 min via the existing distributed
  cache if one is already used for similar reads; otherwise none).
- **Helper** `Pena_e_Arte.Domain/Money/ReferenceAmount.cs`: `static decimal ToWholeLek(decimal eurAmount, decimal rate)
  => Math.Round(eurAmount * rate, 0, MidpointRounding.AwayFromZero)` plus `static bool AppliesTo(Studio)` (`CountryCode
  == "AL" && Currency == "EUR"`, case-insensitive). One place for D4 and D9.
- **Fetcher** `Pena_e_Arte.Infrastructure/Services/BankOfAlbaniaRateFetcher.cs`: named `HttpClient` `"BankOfAlbania"`,
  timeout 15 s, registered beside the others in `InfrastructureServiceExtensions.cs` (~lines 225–258). Parses the EUR
  row and the "last update" date from the fixture's markup. **Validation before saving** (reject, log Error, save
  nothing): rate between 50 and 200; rate within ±5 % of the previous stored rate (if any); date not in the future;
  date ≥ previous stored date. Same date already stored → no-op.
- **Job** `Pena_e_Arte.Infrastructure/Jobs/ExchangeRateFetchJob.cs`, registered in `Program.cs` in the
  `isOneTimeSetupRun` block next to the other `recurringJobs.AddOrUpdate` calls (~line 128): weekdays at **12:40,
  14:00 and 17:00 Europe/Tirane** (cron with that time zone), so a late publication is still picked up. A failure is
  logged through the existing `HangfireJobFailureLogFilter`. Daily at 09:00: if `IsStale`, log Error
  `"BoA EUR/ALL rate is stale: newest {RateDate}"` (Q2).
- **Endpoint** in `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs`, same shape as `/geo/visitor`:
  `group.MapGet("/exchange-rates/eur-all", …).AllowAnonymous().RequireRateLimiting("public-read")` →
  `ExchangeRateResponse { decimal Rate; string RateDate; string Source; bool IsStale }` (Contracts) via a MediatR
  query `GetLatestEurAllRateQuery`. 404 when no rate has ever been stored. Anonymous on purpose: guests see prices on
  `/book`, `/artist/:slug` and the gift-card page; a public central-bank rate is not sensitive. Response
  `Cache-Control: public, max-age=600`.
- **Migration** `AddExchangeRates` (new table only; zero-downtime).
- **Tests:** unit — parser on the saved fixture (EUR value and date exact), parser on a fixture with the EUR row
  removed (rejects), validation bounds and ±5 % rule, `ToWholeLek` (50 × 90.74 = 4,537; 0.50 × 90.74 = 45.37 → 45;
  a .5 midpoint rounds up), `AppliesTo` matrix (AL/EUR yes; AL/ALL, XK/EUR, AL/USD no); integration (real MySQL, as
  the other endpoint tests) — endpoint 404 empty / 200 with row / `IsStale` after 8 days; job is idempotent for the
  same date.
- **Help:** none in this phase — no user-visible change. Say so in the commit body.

### Phase 2 — Freeze the rate on client money records
- Add to `Payment`, `GiftCard`, `PackagePurchase` (all nullable — null = no reference, D9/Q4):
  `string? ReferenceCurrency` ("ALL"), `decimal? ReferenceRate` (18,6), `DateOnly? ReferenceRateDate`,
  `string? ReferenceRateSource`, `decimal? ReferenceAmount` (18,4, whole lek). XML doc comments in the same style as
  `Payment.Currency` ("copied … when the row is created and never changed after").
- At each snapshot point in §5.4: if `ReferenceAmount.AppliesTo(studio)` and a rate exists, set the five fields
  **before** the record is saved (for card: right before `CreatePaymentHoldAsync`; for cash: in
  `ConfirmCashDepositCommand` when it sets `PaidAt`). No rate at all → leave null and log Warning (the payment must
  not fail because of a display feature).
- `PaymentResponse` (`Pena_e_Arte.Contracts/Responses/PaymentResponse.cs`) and the client-facing gift-card / package
  responses gain `ReferenceAmount`, `ReferenceCurrency`, `ReferenceRate`, `ReferenceRateDate` (nullable). Frontend
  types (`payment.types.ts` etc.) follow, strict, no `any`.
- **Refunds:** no new columns. The lek for a refund = `ToWholeLek(refundedEur, payment.ReferenceRate)` at display
  time (D5).
- **Migration** `AddMoneyReferenceSnapshot` — nullable columns only, no backfill (Q4); zero-downtime.
- **Tests:** each snapshot point sets the five fields for AL/EUR and leaves them null for AL/ALL and for no-rate;
  values never change on capture, refund or reconciliation (`PaymentReconciliationJob` must not touch them); the
  stored lek equals `ToWholeLek(Amount, ReferenceRate)`.
- **Help:** none yet (nothing shown). Say so in the commit body.

### Phase 3 — Backend text the client receives
- `MoneyText`: add `FormatWithReference(decimal amount, string currency, decimal? referenceAmount, string?
  referenceCurrency, bool approximate)` → `"50.00 EUR (4,537 ALL)"` / `"50.00 EUR (≈ 4,537 ALL)"`; whole lek, "N0".
  `Format` itself is unchanged (it is used elsewhere).
- Use it at the five sites in §5.2: deposit-captured and refunded email/SMS and hold-lost use the **stored** snapshot
  (refund: original rate × refunded EUR); the ICS file uses the latest rate, approximate.
- Receipt PDF (`PaymentInvoiceService`): amount rows show both; add a row **"Exchange rate"** →
  `1 EUR = 90.74 ALL — Bank of Albania official rate of 23 Oct 2026`. Only when the payment has a snapshot.
- Logs: never log amounts with client identifiers; structured logs only (no change to existing log lines).
- **Tests:** template/renderer tests for each message with and without a snapshot; PDF test asserts the rate row
  text (the existing PDF tests show how the text is extracted).
- **Help (part of done):**
  - `helpContent.ts` article **`client-deposit-pay`** (line ~342): replace the tip *"You pay the deposit in the studio's
    currency — exactly the amount shown. …"* with: *"You pay the deposit in the studio's currency — exactly the amount
    shown. At studios in Albania that price in euros, the amount in lek is shown next to it, converted at the Bank of
    Albania's official rate. Your receipt shows the rate used on the day you paid. Your card is charged in euros; if
    your card is in lek, your bank converts it at its own rate, which can differ slightly. If card isn't offered, the
    studio's currency isn't supported for card payments yet; pay in cash."* Add keywords `"lek"`, `"exchange rate"`,
    `"euro"`.
  - Manual `frontend/public/user-manual/index.html` section **`client-deposit`** (line ~1369): same sentence.
  - Tours: no change (no new screen or navigation item).

### Phase 4 — Frontend display
- `frontend/src/shared/utils/formatCurrency.ts`: add `formatWithReference(amount, currency, reference)` where
  `reference = { amount: number; currency: string; approximate: boolean } | null` → `"€50 (≈ ALL 4,537)"`;
  `null` → exactly `formatCurrency(amount, currency)`. Keep `formatCurrency` unchanged.
- `frontend/src/shared/hooks/useEurAllRate.ts` (TanStack Query, key `["exchange-rates","eur-all"]`, `staleTime`
  10 min; **no `useEffect` for fetching**). `enabled` only when the studio is AL + EUR, so other studios make no call.
- A shared `<Money amount currency studio? snapshot? />` component in `frontend/src/shared/components/` that picks: stored
  snapshot (exact, no ≈) → else latest rate for AL + EUR (≈) → else plain `formatCurrency`. EUR and lek in the **same
  font size and weight** (D7). While the rate loads, render the EUR amount alone (never a bare number; keeps G-2).
  Rate error → EUR alone, no error message to the client (log via the existing frontend error reporter if there is one).
- Replace the client-facing calls in §5.1 with `<Money …/>` (or `formatWithReference` in string contexts such as the
  "Pay deposit — …" button and `describeDeposit`'s **client** strings only).
- `DepositCheckoutPage.tsx` (~line 91): under the amount, for AL + EUR only, add the D6 sentence.
- If `IsStale` is true, the lek part shows its date: `"(≈ ALL 4,537 at the 23 Oct rate)"` (Q2).
- Accessibility: the lek part is plain text in the same element (screen readers read both); no colour-only cues.
  Check 320 px width: the pair may wrap onto two lines but must not truncate.
- **Tests (Vitest + Testing Library):** `formatWithReference` (approximate, exact, null, ALL whole numbers);
  `<Money>` states: loading (EUR only), error (EUR only), AL/EUR with rate (≈), stored snapshot (no ≈), AL/ALL studio
  (unchanged, **no network call**), stale (date shown); each page in §5.1 renders the pair for an AL/EUR fixture and
  is unchanged for an ALL fixture; `DepositCheckoutPage` shows the D6 line only for AL/EUR.
- **Help (part of done):**
  - `helpContent.ts`: **`client-book-appointment`** (line ~30) — tip: *"At studios in Albania that price in euros,
    prices and deposits also show the amount in lek (≈), at the Bank of Albania's latest official rate. The lek amount
    is fixed when you pay."* **`client-gift-cards`** (~132) and **`client-packages`** (~149) — the same one-line tip.
    **`owner-studio-currency`** (~1286) — replace the tip *"Clients always pay exactly the amount shown, in your
    studio's currency — there's no conversion at checkout."* with: *"Clients always pay exactly the amount shown, in
    your studio's currency — there's no conversion at checkout. If your studio is in Albania and prices in EUR, clients
    also see the lek amount next to each euro price, at the Bank of Albania's official rate; the rate on the day they
    pay is saved with the payment and printed on their receipt. Your own payment screens and reports stay in EUR."*
  - Manual `frontend/public/user-manual/index.html`: sections **`client-book`** (~956), **`client-gift-cards`**
    (~1070), **`client-packages`** (~1096), **`owner-studio-currency`** (~3136) — same text as above.
  - Tours (`clientTour.ts`, `ownerTour.ts`): **no new step** — the change is inside screens the tours already point at
    and adds no control or navigation item.

### Phase 5 — Docs and self-check
- `docs/claude/architecture.md` Decisions Log: new row **"EUR studios in Albania show lek (2026-10-26)"** — what
  (D1–D9), why (Phi's legal requirement), the benchmark divergence (§9), open questions Q1–Q7. Add `ExchangeRate`
  to the Feature Module Map and to `docs/claude/database.md`.
- PR description lists Q1–Q7 under "Needs a decision".

## 7. Constraints (restated)
- No new npm or NuGet packages (D8). If one becomes unavoidable, stop and flag it as a prerequisite decision.
- No `useEffect` for data fetching — TanStack Query only (the approved exceptions listed in
  `docs/claude/frontend.md` stay the only ones). TypeScript strict, no `any`. Explicit C# types, no `var` where the
  codebase avoids it.
- No business logic in endpoints: MediatR + FluentValidation. Every endpoint has the correct auth policy; the one
  new endpoint is `.AllowAnonymous()` **by design** (§6 Phase 1), rate-limited `"public-read"`, like the other public reads.
- Tenant isolation: `ExchangeRate` is global by design; every other change is on tenant entities and goes through
  the existing global query filters. No new `IgnoreQueryFilters()` usage.
- Never log PII; structured logs only. The rate is not PII.
- Money: `decimal` everywhere on the backend; the frontend computes the ≈ lek only for display; every stored lek
  figure comes from the backend.
- Migrations: additive and nullable only.

## 8. Do not build blind (spec only, not in this branch)
- **Blocking EUR card checkout when the rate is stale** (Q2): a capability flag on `PaymentCapabilitiesResponse`,
  read by `PaymentMethodSelector`, reusing the C-13 "temporarily unavailable" message.
- **Lek on staff screens** (Q3): the same `<Money>` component on the §5.3 pages, behind a decision.
- **Back-filling lek on old EUR receipts** (Q4): only possible for payments whose `PaidAt` date has a stored BoA rate.
- **Fiscalised e-invoices** (Q5): Legal/Finance first.

## 9. Benchmark note
- **Vertical booking SaaS** (Fresha, Vagaro, Mindbody, Boulevard, Booksy, Square Appointments) price each location in
  one local currency and charge in it; none shows a second currency (`docs/claude/report-studio-currency-2026-09-27.md`,
  architecture.md "Studio currency (2026-09-27)"). Showing lek next to EUR is a **deliberate divergence**, driven by
  Phi's local legal requirement, not by the benchmark.
- **Dual-display conventions** are taken from the closest public rulebook, Bulgaria's lev/euro dual pricing (8 Aug 2025 –
  31 Dec 2026): both prices "next to each other", "in the same font size", conversion at the exact rate "with no
  rounding during calculation", standard rounding of the result, and receipts showing both amounts and "the official
  exchange rate" ([CMS](https://cms.law/en/bgr/legal-updates/dual-pricing-obligations-immediate-measures-for-businesses),
  [evroto.bg business FAQ](https://evroto.bg/en/euro/business/faq)). Not Albanian law; used as the industry pattern.
- **Payments industry:** Stripe refunds a converted payment "using the same exchange rate used in the transaction"
  ([Stripe support](https://support.stripe.com/questions/automatic-currency-conversion)) → D5.
- **Rate source:** [Bank of Albania, Official exchange rate](https://www.bankofalbania.org/Markets/Official_exchange_rate/)
  (lek per unit; set from the 11:30–12:00 interbank window; business days).

## 10. Final self-check (run before declaring done)
- [ ] `dotnet build` clean (no new warnings); `dotnet test` green; `npm run build`, `npm run lint`, `npm test` green.
- [ ] Both migrations apply on a fresh DB and on a copy of the current schema; both are additive/nullable.
- [ ] An AL/ALL studio: every §5.1 page and §5.2 message is **byte-identical** to before (snapshot tests or explicit
      assertions), and the frontend makes **no** exchange-rate request.
- [ ] An AL/EUR studio: every §5.1 page shows the pair; the checkout page shows the D6 line; a paid deposit's email,
      SMS and PDF show the frozen lek and the rate line; a partial refund shows lek at the original rate.
- [ ] Nothing in §4 changed (`git diff --stat main` reviewed against the list).
- [ ] No PII in any new log line; no new `IgnoreQueryFilters()`.
- [ ] Help: `client-deposit-pay`, `client-book-appointment`, `client-gift-cards`, `client-packages`,
      `owner-studio-currency` updated in `helpContent.ts` **and** the five manual sections; tours: no-change reasoning in
      the PR.
- [ ] Decisions Log row, Feature Module Map entry, `database.md` updated; spec copied to `docs/specs/`.
- [ ] PR lists Q1–Q7 under "Needs a decision".

## 11. Deliverables and commit
- Code and tests per §6; docs per Phase 5; `docs/specs/spec-eur-with-lek-display-2026-10-26.md`.
- One PR from `feat/eur-lek-reference-display`, **draft**, not merged (Phi merges). PR body sections: Summary,
  Phases done, Tests (run / skipped / not run here), Decisions I made, Hard stops, Needs a decision (Q1–Q7), Follow-ups.
- Commit messages per phase, e.g. `feat(money): BoA EUR/ALL rate store, fetch job and public read (phase 1)`; final
  squash title: `feat(money): show lek next to euro amounts for Albanian EUR studios`.
- The final report in `claude-shared\handoffs\` (§0). Don't edit `claude-shared\README.md` (Claude Desktop owns it);
  the report is how Desktop learns the PR number.
