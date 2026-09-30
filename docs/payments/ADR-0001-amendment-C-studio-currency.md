# ADR-0001 Amendment C — Studio currency, not native lek, drives every Flow A charge

**Date:** 27 September 2026 · **Status:** Accepted
**Amends:** `ADR-0001-payment-providers.md`'s "Flow A — POK" section and the `CreateDepositPaymentCommand`
comment it produced ("ADR-0001: POK is native-ALL; every deposit is quoted and charged in lek") —
superseded in full. `ADR-0001-amendment-B-flow-a-zero-commission.md` is unaffected (still governs
the zero-commission rule; this amendment only changes what currency a charge is quoted and settled
in, not who it settles to or whether a cut is taken).
**Decider:** Phi (via Finance hand-off, 27 September 2026)
**Triggered by:** the first live card deposit would have charged a European-priced Albanian studio
€50 shown but 5,000 lek (≈€50 × 100) actually authorised — the backend hardcoded every Flow A
amount and currency to `"ALL"` while every frontend screen displayed `€`. Caught before any real
money moved.

---

## The decision

**Each studio prices and charges in exactly one currency of its own (`Studio.Currency`, ISO 4217),
not natively in Albanian lek.** It defaults to the currency of the studio's country
(`Studio.CountryCode`, via .NET's `RegionInfo`) at registration; the owner may pick any other ISO
4217 currency instead, until the studio's first `Payment`, `GiftCard`, `PackagePurchase`, or
`BoothRentCharge` row exists, at which point it locks. Clients pay exactly the amount shown, in
the studio's currency — no conversion anywhere in any charge path.

This directly reverses ADR-0001's original assumption that POK being "native-ALL" meant every
Flow A deposit should be quoted and charged in lek regardless of the studio. That assumption was
never actually true for the product: studios were always allowed to be Albanian *or not*, and the
frontend always displayed `€`, never `ALL` — the backend's lek-hardcoding was a bug baked into the
original integration, not a deliberate design choice this amendment is walking back.

---

## Why this changes more than a currency code

ADR-0001 picked POK because it's a BoA-licensed EMI with native ALL support and a real sandbox —
all still true and unaffected by this amendment. What ADR-0001 did *not* separately decide was
**what currency a studio's own prices should be denominated in**, because at the time every studio
in scope was assumed to be Albanian and the currency question was assumed to be "obviously lek."
Once the frontend was built showing `€` everywhere (a genuine, shipped product decision, not an
oversight) without the backend ever being updated to match, the two layers diverged silently:
every `CreateDepositPaymentCommand` call quoted POK in `"ALL"` while the client saw a `€` amount
with the same numeric value. At today's ALL/EUR rate, that is roughly a 100x undercharge waiting
to happen the moment a real card is used.

**What doesn't change:** POK remains the only Flow A card provider, still a per-studio merchant
account, still zero platform commission (Amendment B unaffected). A studio whose currency is `ALL`
or `EUR` can still take card deposits through POK exactly as before — this amendment only removes
the *assumption* that every studio's currency is `ALL`, it doesn't remove `ALL` as a valid,
supported choice.

**What does change:** `IPaymentProvider` implementations now own their own wire-format conversion
internally (`PokPaymentProvider.ToPokAmount`, `CurrencyCatalog.Round`), rather than a caller
assuming POK's wire format needs no rounding because "it's always lek, which has no decimals."
Card availability is now gated per-currency (`PaymentCapabilitiesResponse.CardUnavailableReason =
"provider_unsupported_currency"` when the studio's currency isn't in the provider's
`SupportedCurrencies`) instead of assumed universally available. This mirrors real provider
behavior Stripe and Adyen both document explicitly — Stripe sends ISK/UGX as whole units, not
minor units; Adyen does the same for CLP/CVE/IDR/ISK — provider-specific deviations belong inside
that provider's own integration, never in a shared "money formatter" trying to special-case every
provider's exceptions.

---

## What supersedes what

| ADR-0001 said (or implied) | This amendment says |
|---|---|
| "POK is native-ALL" → every Flow A deposit is quoted and charged in lek (`CreateDepositPaymentCommand`'s own comment) | Every Flow A deposit is quoted and charged in the studio's own `Currency` — `ALL` for a studio that has it, but never assumed as the universal default. |
| Money amounts pass through `IPaymentProvider` as raw decimals with an implicit "it's lek, no decimals to worry about" assumption | `IPaymentProvider` implementations own wire-format conversion explicitly (`CurrencyCatalog.Round`, `MinorUnits`), because different currencies and different providers round differently — never assumed uniform. |
| Card payments always available once POK is connected | Card payments available only when `IPaymentProvider.Capabilities.SupportedCurrencies` includes the studio's `Currency` — cash remains available in every currency, always. |
| `Payment.Currency` defaulted to `"ALL"` in the domain model (C# initializer) | `Payment.Currency` has no default — always explicitly set from `Studio.Currency` at creation; the MySQL column default is dropped too (no silent fallback at either layer). |

Everything else in ADR-0001 and Amendments A/B stands unchanged: POK plus always-on cash for Flow
A, Polar (Paddle fallback) for Flow B, easyPos for fiscalization, zero platform commission ever,
no platform balance/ledger/payout queue for client funds.

---

## Consequences for the codebase

1. **`Studio.CountryCode`/`Studio.Currency`** (new columns) are the source of truth for what
   currency a studio's prices and payments are in — not a hardcoded constant anywhere.
2. **Every money record keeps its own `Currency` column**, snapshotted at creation —
   `Payment`, `GiftCard`, `PackagePurchase` (which also gained an `Amount` column it never had),
   `BoothRentCharge` — so a later currency change never rewrites what a historical record meant.
3. **Price settings carry no currency of their own** — `Service`, `DepositRule`, `PromoCode`,
   `Package`, `BoothRentSchedule`, `Artist.HourlyRate` — and are always read in the studio's
   *current* `Currency`, safe only because the currency locks once real money moves
   (`StudioCurrencyLock`).
4. **`IPaymentProvider.Capabilities` gates card availability per currency**, not per
   "is POK connected." `CardUnavailableReasons.ProviderUnsupportedCurrency` is a distinct reason
   from `ProviderNotConnected`/`ProviderDisabled`, surfaced to the frontend so the UI can show the
   right explanation instead of a generic "unavailable."
5. **Reports never sum across currencies.** `GetRevenueSummaryQuery`/`GetMyEarningsQuery` filter
   to the studio's own currency and report an `ExcludedOtherCurrencyCount` for anything else —
   the same bug class as the pre-July MRR-from-wrong-field bug, caught this time before shipping.
6. **Existing studios were backfilled to `EUR`, not `ALL`** — because every studio's prices were
   always displayed in `€` on the frontend before this migration, regardless of what the backend
   silently assumed. Backfilling to `ALL` would have been "technically what ADR-0001 originally
   assumed" but wrong for what users actually saw and typed.
7. **Frontend:** one shared `formatCurrency`/`MoneyInput`/`currencyLabel` (never a hardcoded `€` or
   a hardcoded locale like `pt-PT`) — see `docs/claude/frontend.md`'s "Currency" section.
8. **Docs and Help copy:** any user-facing or internal text implying a fixed lek/euro amount must
   be corrected to reference "your studio's currency" generically. In scope per this project's
   rule 7: `helpContent.ts`, the standalone manual, and the owner onboarding tour — see
   `docs/claude/report-studio-currency-2026-09-27.md` for the full list of articles touched.

---

## What this amendment does not decide

- **An admin-side tool to change a locked studio's currency.** Tonight's work only builds the
  owner-side change-before-lock flow; a support/admin override is tracked as a backlog spec
  (`docs/specs/studio-currency-backlog-2026-09-27.md` §1).
- **Rejecting or converting a client-side "pay in a different currency" request.** Out of scope —
  clients always pay in the studio's currency, full stop; a future "≈ estimate in another
  currency" display is tracked in the same backlog spec (§3).
- **Adding a payment provider for a currency POK doesn't support.** POK's `SupportedCurrencies`
  (`ALL`, `EUR`) is unchanged by this amendment — a studio whose currency isn't one of those two
  sees cash-only card unavailability, by design, until a second provider is added (backlog §5).
- **Cross-studio admin-level revenue totals.** The platform-admin side still has no aggregate
  revenue view across studios in different currencies — tracked in the same backlog spec (§4).
