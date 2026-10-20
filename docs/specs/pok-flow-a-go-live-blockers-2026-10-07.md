# POK Flow A — go-live blockers and follow-ups (after the 2026-10-14 staging gate)

Source: `docs/claude/overnight-prompt-pok-staging-release-gate-2026-10-07.md` §17, with the gate's
results filled in. Gate result: `docs/payments/runbook-studio-currency-migration-2026-09-27.md`,
"§3.1 result". Order references here are 8-character prefixes; no keys, merchant id, emails or
card data appear in this file.

**Bottom line:** the gate PASSED for ALL and EUR (amounts, status mapping, capture, refund,
partial capture, hold release, reconciliation healing). **Card deposits are still not approved
for go-live.** The blocking items below must be decided or built first.

| Item | Owner | Blocks go-live? | Status after the gate | Next action |
|---|---|---|---|---|
| C-1 Staging Vault | Phi (infra) | No (staging parity only) | Open | Build when deployed-staging card testing is wanted |
| C-2 POK host + webhook per environment | Phi | **Yes — this is the switch** | Open (production still calls POK staging) | Ship staging config any time; production only at go-live with C-11/C-13 |
| C-3 Gift-card / package card purchases | Phi (product) | No (separate flow) | Open, not tested by the gate | Decide option (a) hide or (b) build |
| C-4 When a deposit is charged | Phi + Finance + Legal | **Yes, if holds don't last** | Holds survive day 2 (T9 captured 26 h after authorisation); day 8 pending (T9b, from 2026-10-22) | Decide (a)/(b)/(c); Help now says "charged by the studio, usually at your appointment" |
| C-5 Webhook fan-out | Engineering | No (before real volume) | Open; gate saw 1 webhook per authorisation only | Debounce + own rate-limit policy |
| C-6 Questions to POK | Phi | Partly (answers feed C-4, C-12, Help) | Open — items 2, 7, 11 and the refund part of 10 answered by our own staging probe on 2026-10-16 (see C-6); email not sent yet | Send in writing; the answered items become "please confirm" |
| C-7 Check POK keys at connect | Engineering | No | **Keys checked** (branch `feat/pok-keys-check-at-connect`): connect logs in to POK once with the entered Key ID + Key Secret before saving; refused → 422 "POK didn't accept…", nothing saved; POK unreachable → 503, nothing saved. POK staging answers unknown keys with 400 (2026-10-21). Help tip replaced | Merchant ID not checked yet — needs a read POK refuses for a wrong merchant (staging probe pending) |
| C-8 Vault resilience | Phi (infra) + Engineering | **Yes** | **Done in code + alerting** (branch `fix/vault-outage-not-reconnect`): 503 vs 422 split, connect-POK 503, 5-minute Vault health probe + `vault-unavailable` alert, runbook | Phi: keep the unseal key shares reachable; first real alert test after merge (see C-8) |
| C-9 Saved-card tokenisation | Engineering | No (Saved card tab must stay unused) | Open, not tested by the gate | Separate prompt |
| C-10 "Stripe PI" labels | Engineering | No | Open | Cosmetic rename |
| C-11 Production first-transaction protocol | Phi | **Yes** | Not started | Run after C-2, C-8, C-13 |
| C-12 Fees and settlement | Finance | No (reporting accuracy) | Fee rules measured on staging (2.5 % + 20 ALL per capture; release free; a partial refund returns the 2.5 % share; a full refund, or partial refunds adding up to the full amount, return the whole fee; display rounding half-up per line) — Help + manual now explain them to owners and clients (2026-10-16) | Finance decides how reports and refund screens show POK fees; confirm production pricing with POK |
| C-13 Kill switch + owner tour step | Engineering | **Yes** | **Kill switch built** (branch `feat/card-deposits-kill-switch`): `Payments:CardDepositsEnabled`, off unless set; tour step still to ship with go-live | At go-live: set `Payments__CardDepositsEnabled=true` for production together with C-2, and ship the "Connect POK" tour step in that change |
| C-14 Several partial refunds | Engineering | No | Open in our app (one refund per payment, re-confirmed on main 2026-10-16); **POK allows several** — proven 2026-10-16 (R1) | Build: allow refunds until the refunded total reaches the amount (industry standard) |
| C-15 Tell the owner when a held deposit is lost | Engineering | No | Open | Owner notification |
| C-16 Cancellation side effects before provider call | — | — | **Done** (folded into B3, a58b229b) | None |
| C-18 Store POK's fee and net per payment | Engineering, after Finance (C-12) | No (recommendation) | Not started — today `Payment` keeps only gross `Amount` and `RefundedAmount`; owners must use POK Business for net figures | After C-12: store POK's `receiverAmount` (net, unrounded) and the fee per capture/refund, show them on the payment page and offer net in reports/CSV |
| C-17 Stop framework URI logging for the POK client | Engineering | **Yes** (one-line fix) | **Done** (branch `fix/pok-client-no-uri-logging`): the "Pok" client has `RemoveAllLoggers()`; a unit test proves no log line carries the merchant id or order id | None |
| Legal: EUR deposits for Albanian studios | Legal | Yes for EUR | Open; the gate only proves EUR works technically | Legal sign-off |

## What shipped with the gate (branch `fix/pok-staging-release-gate-2026-10-07`)

| Commit | Item | Verified on staging |
|---|---|---|
| 1817d414 | `MapStatus` held-before-paid precedence + flag log | T1/T9: held shape → Authorized |
| fc41a5fb | B1 + B2 reconciliation asks POK first; handles dead holds | T1 and T6: "healed to held" after the webhook |
| a58b229b | B3 cancel releases or partly captures a held deposit | T5a: hold released (isCanceled); T5b: 617 captured |
| afcaadf1 | B4 never resume an expired order; 24 h link expiry | HoldExpiresAt = order expiresAt on every order |
| 1de7fcdb | B5 "On hold" label | Owner list/detail show "On hold"; POK's dashboard uses the same words |
| a1e52c26 | B6 PaidAt on Complete / No-show | Not exercised on staging (unit tests only) |
| 068952ea | Observed held fixture replaces the provisional ones | — |

## C-1 — Staging Vault (go-live: no; staging parity: yes)
Unchanged from the gate prompt §17. Deployed staging can't run a POK card flow without it; the gate
therefore ran on the local stack with dev-mode Vault.

## C-2 — POK host and webhook per environment (go-live: **yes**)
Unchanged. Gate note: with `Pok__WebhookCallbackBaseUrl` set, POK called the webhook within seconds
of every authorisation (T1, T5a, T5b, T6, T9, T9b, T1E). Staging value: `https://staging.tattooos.co`;
production: `https://app.tattooos.co` — production only at go-live.

## C-3 — Gift-card and package card purchases (product decision)
Unchanged; not tested by the gate (out of scope).

## C-4 — When a deposit is charged (go-live: **yes, if holds don't last**)
**Gate result: holds survive day 2.** T9 (order dd7e1fce, authorised 2026-10-14 15:07:39Z) was captured
2026-10-15 17:13:09Z — 26 h 05 min after authorisation, 2 h 35 min after the order's `expiresAt`. POK's
public read returned 410 after `expiresAt`, but the merchant read still showed `can.capture true` and the
funds stayed in the frozen balance until the capture. The upper limit is still unknown: T9b (order
f6cdc236) is captured at ≥ 8 days, from 2026-10-22 15:36Z, and C-6 item 1 asks POK. The client-facing
charge-timing strings (Phase 8.3b) and the client-deposit Help tip now say the deposit is charged "by the
studio, usually at your appointment"; no hold-window tip yet (needs the day-8 result).
Options (a) charge at booking, (b) capture before the hold lapses, (c) hybrid — unchanged. The six
client charge-timing strings (Phase 8.3b) and the Help charge-timing tip follow the decision.

## C-5 — Webhook fan-out
Unchanged. Gate evidence: one webhook per authorisation; none on capture, refund, partial capture or
cancel. Each one enqueued a full reconciliation run.

## C-6 — Questions to POK, in writing (with the gate's evidence)
1. How long can an authorised (`autoCapture: false`) order be captured after its `expiresAt`? — At least
   2 h 35 min (T9, captured 26 h after authorisation on 2026-10-15); T9b tests 8 days. Still ask for the limit.
2. What does `refund` do on an authorised, uncaptured order? — Not tested (B3 shipped, so the app
   cancels the hold instead; T5a: `isCanceled: true`, nothing captured).
3. EUR without a EUR account: rejected or converted? — Not tested: Phi created a EUR account first.
   With it, a 12.34 EUR order stayed 12.34 EUR (`originalCurrencyCode` EUR, exchange rate 1). The
   EUR account shows an "Activate" button next to Lek's "Primary"; EUR worked without pressing it.
   Ask what "Activate" does.
4. Which events send webhooks? Retry policy? Signing? — Observed: authorisation only. Ask whether
   capture, refund and cancel are meant to send one.
5. Card fees and settlement (staging Lek balance vs gross receipts). — **Answered from staging data:** the
   44 ALL gap of 2026-10-07 is POK's card fee (2.5 % + 20 ALL per payment: 100 → 22.50, 60 → 21.50 = 44.00).
   Still ask: is that the production price, does it vary by card type or contract, how is it rounded, and when
   does "Balance" become withdrawable?
6. "POK credit" (wallet) on `autoCapture: false` orders.
7. Can an order take several partial refunds?
8. Will `isConfirmed`, `isCaptured`, `capturedAmount`, `refundableAmount`, `status`, `can` be documented
   and kept stable? — The public `GET /sdk-orders/{id}` returns `null` for `isConfirmed`, `status`,
   `can` and `refundableAmount`; only the merchant-scoped read has them. After `expiresAt` it returns
   410 "Order is expired" — re-tested 2026-10-15 on T9, an authorised order still waiting to be captured.
9. **Privacy:** the unauthenticated `GET /sdk-orders/{id}` returns the merchant's contact details.
10. **New — refund bank commission** (partly answered from staging data, see C-12). Still ask: does the
    cardholder's card receive the full refund (234.00 / 2.34) — the dashboard says "Paid Amount" 234.00?
    (A FULL refund returns the whole commission, fixed 20 ALL included — measured 2026-10-15, see C-12; still
    ask whether several partial refunds adding up to the full amount get the fixed fee back.) What does `merchant.coversCommissionFees: true` mean exactly, and what does the
    order-level `commissions{}` object (0 on 2026-10-03, absent today) describe? Blocks the Help refund wording.
11. **New — hosted page result.** After paying on `pay-staging.pokpay.io/sdk-orders/{id}` (T6), the
    page showed no clear success message. **Explained by the T6 re-test (2026-10-15, on T8's order):** POK
    confirms the payment (`guest-confirm` 200) and about 4 s later redirects the browser to the order's
    `redirectUrl`, which the app never sets — POK filled in its own default `https://develop.d30szd04tw89xg.amplifyapp.com`,
    which doesn't load (the client ends on the browser's network-error page). `failRedirectUrl` is null. One
    earlier attempt the same minute got "An internal server error occurred" from the hosted page with no
    state change. Ask: can `redirectUrl` / `failRedirectUrl` be set when creating the SDK order, does POK
    show any result screen before redirecting, and what was the 500? See G-12.
12. **New — 3-D Secure exit.** T8 (POK's published 3-D Secure test card ending 1091): wrong codes give "The code entered was
    incorrect. Please try again." indefinitely. **Corrected by the T8 re-test (2026-10-15):** 10 wrong codes in a
    row never ended the challenge; the challenge does offer SUBMIT, RESEND CODE and **CANCEL**, but CANCEL sits
    below the visible part of the challenge window (frame 370×460 px, CANCEL about 64 px below its bottom edge), so
    the cardholder has to scroll inside it; Escape and clicking outside do nothing. CANCEL closes the challenge,
    POK's card form then shows "Error! Something went wrong!", the SDK's `onError` passes only "Something went
    wrong!" (shown by the app as a toast), the order stays `created` and the payment
    stays Pending (no webhook); "Finish deposit payment" resumes the same order. Ask: do repeated failures ever
    end the attempt (`3DS_VALIDATION`)? Which test card or code simulates a *rejected* 3-D Secure? Can the SDK
    report a cancelled/failed 3-D Secure with a specific code so the app can show a clear message (G-10)? Can the
    challenge be sized so CANCEL is visible?

**Probe run 2026-10-16 (our own staging tests; local stack from 2026-10-14; T9b untouched).** Orders created
directly at POK staging (R2–R4, 100 ALL each) and one app booking (R1, 1,234 ALL), all closed afterwards; end
balances matched the predictions exactly (ALL Balance 4,282.10, Frozen 1,183.15 = T9b only).
- **Item 2 — refund on an authorised, uncaptured order (R2):** POK **rejects** it: 400 "Order not eligible for
  refund"; the order stays `awaiting_capture`, nothing charged. The held order already reports `can.refund false`,
  `refundableAmount 0`. Cancel released it (free). Our cancel-not-refund choice (B3) is right.
- **Item 7 — several partial refunds (R1):** **yes.** 234 refunded in the app, then 1,000 directly → `refunded`,
  `refundableAmount 0`. Our app still allows only one (C-14).
- **Item 10, refund part (R1):** when partial refunds add up to the full amount, **the last one returns the rest of
  the 2.5 % plus the fixed 20 ALL**: 234 → studio debited 228.15 (fee back 5.85); 1,000 → debited 955.00 (fee back
  45.00); total fee returned 50.85 = the whole original fee. POK labels the completing refund "Refund", not "Partial
  refund". Still for POK: what the cardholder's card receives, and what `commissions{}` is (absent on every read
  today; `merchant.coversCommissionFees: true`).
- **Item 11 — hosted page (R3, R4):** **`redirectUrl` and `failRedirectUrl` are accepted on create** and echoed with
  `&orderId=<id>` appended. Paid (R3, R2): POK shows "Payment Complete — Your payment has been successfully processed.
  Thank you!", then ~1.5 s later redirects to `redirectUrl`. 3-D Secure cancelled (R4, card 1091): POK shows "Payment
  failed", then ~2.5 s later redirects to `failRedirectUrl`. Fixes G-12. Still for POK: the one-off 500 of 15 Oct.
- **Rounding (feeds C-12):** R3 captured 45 of a 100 hold; POK stores the net unrounded (`receiverAmount` 23.875)
  and rounds each displayed line half-up on its own: "Bank commission 21.13", "Total received 23.88" (sum 45.01 for
  a 45.00 payment). The earlier 35.425 → 35.42 fits half-up on a binary float.
- **Tokens (measured 2026-10-16 20:27Z):** POK's SDK access token is a JWT valid for **600 s (10 min)**; the login
  response also returns `expiresIn` and `expiresAt`, with no refresh token; a read at 9.6 min → 200, at 10.1 min →
  401 "Expired token". `PokAuthClient` already caches the token in Redis per studio and renews it 60 s before
  `expiresAt` — no change. (An earlier "about 5 minutes" here was a guess without the login time.)
- **Payouts (item 5, screen only):** POK Business "Withdraw money" (= the Dashboard's "Transfer to bank") offers
  only the Balance, never the frozen balance, to a bank account added under Accounts → Bank Accounts, once or as a
  "Recurring transfer". No fee, minimum or timing is shown — still for POK.

## C-7 — Check POK keys at connect time
Unchanged. Gate friction supports it: the API Keys dialog shows the three values once; a wrong
value is only discovered at checkout.

## C-8 — Vault resilience before go-live (**yes**)
Gate note: the dev-mode Vault had to stay up overnight; any restart would have lost the keys and
needed a reconnect.

**Done (approach: manual unseal + alert, as ADR-0002 chose — no cloud KMS):**
- `ISecretsProvider` tells the two failures apart: `SecretNotFoundException` (Vault answered, nothing
  stored) vs `SecretsStoreUnavailableException` (sealed 503, unreachable, erroring).
- `PokAuthClient`: missing → 422 "Reconnect POK"; unavailable → **503 "Card payments are temporarily
  unavailable. Please try again later or pay in cash."** plus an error log "Secrets store unavailable"
  (studio id and error type only). Before, every Vault failure told owners to reconnect.
- `ConnectPokAccountCommand`: a Vault outage reverts the half-made connection (as before) and returns
  **503 "We couldn't save your POK keys because our secure key store is temporarily unavailable.
  Nothing was changed — please try again in a few minutes."** instead of a bare 500.
- Cancelling an appointment while Vault is down is refused and retried, never marked released on a
  guess (the release path's status re-read fails too); a forfeit keeps the deposit on hold.
- `VaultHealthCheckJob` (Hangfire, every 5 min) probes the unauthenticated `/v1/sys/health` and logs
  `VaultHealthCheckFailed` when Vault is sealed, uninitialised, unhealthy or unreachable; it skips
  environments without Vault (staging).
- Grafana rule `vault-unavailable` (Loki, production namespace) emails ops@ on either log line.
- Runbook: `docs/infra/vault-self-hosted-runbook.md`, "When the 'Vault sealed or unreachable' alert
  fires"; `docs/infra/alerting-runbook.md` lists the rule; ADR-0002 records the resolution.
- Help + manual: the owner's connect-POK tip for the new 503.

**Still for Phi:** keep three of the five unseal key shares reachable for whoever is on call; after the
merge, one real alert test is worthwhile (e.g. during a quiet window, restart the Vault pod, confirm the
email arrives within ~6 minutes, unseal, confirm it resolves). Auto-unseal stays an option if restarts
become frequent.

## C-9 — Saved-card tokenisation
Unchanged; the gate never used the Saved card tab.

## C-10 — Cosmetic labels
Unchanged ("Stripe PI" → "POK order").

## C-11 — Production first-transaction protocol (**yes**)
Unchanged.

## C-12 — Fees and settlement reconciliation (Finance)
**Measured on POK staging, 2026-10-14** (merchant-scoped order reads, the Transactions page and both account
balances; PROVEN on staging unless marked):

| Event | Merchant cost | Evidence (8-char order refs) |
|---|---|---|
| Card payment captured | **2.5 % + 20.00 ALL (0.20 EUR)**, on the captured amount | a650760e 1,234 → 50.85; 9fe2af20 617 captured of 1,234 → 35.425 (shown 35.42); f7aad8b0 12.34 EUR → 0.5085 (shown 0.51); a 2026-10-03 payment 60 → 21.50 |
| Hold authorised, not captured | fee shown, net held as **Frozen balance** | dd7e1fce, f6cdc236, e2a84569: 3 × 1,183.15 = 3,549.45 frozen |
| Hold released (cancel) | **none** | cdd8e625: in neither balance, no commission line |
| Partial capture | fee on the captured part only; the released rest is free | 9fe2af20 |
| Partial refund of R | merchant debited **R − 2.5 % × R**; the 2.5 % share of the original fee comes back, the fixed 20 / 0.20 doesn't | a650760e refund 234 → 228.15; f7aad8b0 refund 2.34 → 2.28 |
| Partial refunds adding up to the full amount | **the last one returns the remaining 2.5 % plus the fixed 20 ALL** — the whole fee comes back in total (2026-10-16) | b16afb26: 234 → 228.15, then 1,000 → 955.00 (fee back 45.00) |
| Display rounding | each displayed line rounded **half-up** separately; net stored unrounded | e380b551: 45 captured → commission 21.13, total received 23.88 (`receiverAmount` 23.875) |
| Full refund | **the whole commission comes back, fixed 20 ALL included**: merchant debited the net 1,183.15 ("Amount 1,183.15 + Bank commission 50.85 = Paid Amount 1,234.00"); balance 5,202.02 → 4,018.87 (re-test 2026-10-15) | 8f868704 |

Balances match exactly: ALL 116.00 + 1,183.15 + 581.575 − 228.15 = 1,652.575 (shown 1,652.57); EUR 11.8315 − 2.28
= 9.5515 (shown 9.55). The old 44 ALL gap (C-6 item 5) is this fee. `coversCommissionFees: true` on every order
is consistent with "the merchant pays the card fee" (INFERRED). What reaches the cardholder's card is not
observable with sandbox cards (UNKNOWN).

**For Finance (a decision, not built here):**
- Revenue reports use gross (`Payment.Amount`). A studio actually receives gross − 2.5 % − 20 ALL per card
  deposit. Decide whether reports show POK fees or a net figure.
- Owner refund screen: a partial refund of R costs the studio R − 2.5 %·R from its POK balance and the fixed
  20 ALL isn't returned; a full refund (or partials adding up to it) returns the whole fee. Decide whether the
  refund dialog says so. Help and the manual now explain it (owner article "Where card deposit money goes").
- Cost case for B3 (no change needed): releasing a hold is free, while "capture in full, then refund in full"
  costs nothing either (a full refund returns the whole fee — proven 2026-10-15/16). A late-cancel partial
  capture costs 20 + 2.5 % of the kept share — the same as capturing in full and refunding the rest
  (50.85 − 15.425 = 35.425). So holds beat charge-at-booking on cost only for full cancellations; C-4 still
  depends on how long holds last.
- **Done 2026-10-16 (Phi's call: be transparent now):** Help and the manual explain where card deposit money goes —
  hold → Frozen balance, charge → Balance minus POK's fee, refunds, payouts, where to see each figure — with the
  staging fee as a labelled example ("your contract with POK sets your rate"). Re-check the wording when POK
  confirms production pricing (C-6 item 5) and Finance decides C-12.
- The cash no-show branch question stays.

## C-13 — Kill switch and the go-live owner tour step (**yes**)
**Kill switch built.** `Payments:CardDepositsEnabled` (`CardDepositOptions`), `false` in
`appsettings.json`, so production and staging have card payments off until it's set to `true` there;
`docker-compose.yml` turns it on for the local stack (`PAYMENTS_CARD_DEPOSITS_ENABLED`, default true).
`PokPaymentProvider` reports it as `PaymentProviderCapabilities.CardPaymentsEnabled`:
- `GetPaymentCapabilitiesQuery` returns `provider_disabled` (clients see "Card payments are
  temporarily unavailable" and Cash), before any per-studio check;
- `CardCurrencyGuard` refuses every new card payment (deposits, saved-card deposits, owner payment
  links, gift cards, packages) with "Card payments aren't available right now. Please pay in cash.",
  and `CreatePaymentHoldAsync` refuses too (defence in depth); resuming an unpaid order created
  before the switch-off is refused as well (`EnsureCardPaymentsEnabled` in the resume branch);
- an order that was already handed out can still be paid on POK's own hosted page until it expires
  (24 h) — the app can't stop that, but the webhook/reconciliation records it correctly as held;
- capture, refund and release of payments already taken are not affected, so turning it off never
  strands a held deposit;
- the owner's "Card deposits — POK" card says "Card deposits aren't switched on in TattooOS yet" and
  still lets the owner link an account (Help + manual tip added).

**Still to do at go-live:** set `Payments__CardDepositsEnabled=true` for production in the same change
as C-2 (k8s production overlay or secret), and ship the dedicated "Connect POK" onboarding-tour step
(`data-tour="owner-pok-card"` on `PokSettingsCard`, a step with `route: "/studios/me"`) in that change.
To switch card payments off again in an emergency: set it back to `false` and redeploy.

## C-14 — Several partial refunds per payment
Confirmed in the UI: after a partial refund (T3: 234 of 1,234) the payment shows "Refunded" and the
Refund button disappears, so the remaining 1,000 can't be refunded from the app. Still true on main
(`RefundPaymentCommand` accepts only `Status == Paid` and sets `Refunded`). **POK allows several partial
refunds (proven 2026-10-16, R1)**, and the payments products in this category (Stripe-based ones) refund until
the refunded total reaches the amount. Build: keep the payment refundable while `RefundedAmount < Amount`, add
each refund to `RefundedAmount`, set `Refunded` only when it reaches the amount. Help says "refunded once" until
then.

## C-18 — Store POK's fee and net per payment (recommendation, after C-12)
Today `Payment` keeps only the gross `Amount` and `RefundedAmount`; POK's fee and the net that reached the
studio exist only in POK Business, so owners reconcile there (Help says so). After Finance decides C-12: read
POK's `transaction.receiverAmount` (net, unrounded) after each capture and refund, store the fee and net on the
payment, show them on the owner's payment page ("Client paid · POK fee · You received"), and offer net in the
revenue report and CSV. Needs a migration and a test per path (capture, partial capture, partial/full refund,
release).

## C-15 — Tell the owner when a held deposit is lost
Unchanged.

## C-16 — Cancellation side effects
Done in B3 (a58b229b): reminder jobs are deleted after the save.

## C-17 — Stop framework URI logging for the POK client (**yes**)
Confirmed during the gate: the `System.Net.Http.HttpClient.Pok.*` request lines carry the merchant
id and full order ids. **Fixed:** `AddPokHttpClient()` registers the client with `RemoveAllLoggers()`,
so `IHttpClientFactory` adds no logging handlers to it (outgoing calls aren't traced either — only
ASP.NET Core instrumentation is on). `PokHttpClientLoggingTests` checks both that the POK client logs
nothing with the URI and that an ordinary client still would.

## Found during the gate — not POK-specific
| # | Finding | Where | Severity | Next action |
|---|---|---|---|---|
| G-1 | Booking **submit** compares the slot's **UTC** time with studio-local hours. Re-tested 2026-10-15: 09:30 Tirana (07:30Z) → 422 "Outside studio hours (09:00–18:00)."; a request for 18:30 Tirana (16:30Z) is **accepted** (the UI greys the button, the server doesn't refuse). The check-slot pre-check is correct (it says 09:00/10:00 "This slot is available"), so the client sees "available" and then a refusal. | `CreateAppointmentCommand` → `ArtistAvailabilityExtensions` (`TimeOfDay`, `DayOfWeek`); check guest and reschedule paths too | High (every Albanian studio) | **Fixed** (branch `fix/booking-hours-studio-timezone`): availability helpers and artist assignment compare on the studio's timezone, conflicts stay UTC, slots past local midnight are refused, the pre-check sends a UTC instant like the submit; 7 Tirana tests (summer/winter, day boundary, midnight, conflicts, time off); Help + manual say hours follow the studio's timezone. Known limit: a client in another timezone types times in their own device's time |
| G-2 | Deposit amount unformatted until the currency loads: "Secure your slot with a deposit of 1234." (also "12.34.", "1000.") and the Cash panel "Your deposit of 1234 will be collected…". Re-tested 2026-10-15: 3 of 5 fresh-session booking confirmations unformatted. | `BookAppointmentForm` (studioCurrency), `PaymentMethodSelector` `CashInfoPanel` (capabilities.currency) — both fall back to the raw number | Low (cosmetic, frequent) | Wait for the currency before rendering, or format with the payment's own currency |
| G-3 | Client self-service uses a **24 h** default notice window; the backend uses **48 h**. Re-tested 2026-10-15 at a studio with no deposit rule, 24.8 h before the appointment: the client app offers Reschedule and says "**You'll receive a full refund.**"; the server refuses the reschedule (422 "less than 48 hours away") and, per `ClientCancellationPolicy`, a cancel would refund **0 %** (the whole deposit is kept). | `MyBookingsSection.tsx` `PLATFORM_DEFAULT_CANCELLATION_WINDOW_HOURS` vs `AppointmentSelfServiceDefaults.CancellationWindowHours` | **High** (wrong money promise to the client) | **Fixed** (branch `fix/client-cancel-window-matches-server`): the client default is now 48 h, matching `AppointmentSelfServiceDefaults.CancellationWindowHours` and Help; 2 tests at 30 h. Still two copies of the number — having the API return the effective window is the cleaner long-term fix |
| G-4 | A refund succeeds silently — no toast, only the badge changes (re-tested 2026-10-15 with a toast recorder armed before the click: none). | `PaymentDetailPage` `RefundSection` | Low | Add a success toast ("Refund issued") |
| G-5 | A **partial** refund labels the whole payment "Refunded". | Payment status model | Low | **Fixed** (branch `fix/payment-status-wording`, with G-13): shown as "Partly refunded" with "1,000 kept · 234 refunded"; the stored status is unchanged (C-14 still open) |
| G-6 | A released hold (studio cancel, T5a) shows "Refunded" and offers "Download receipt" although the card was never charged. | `PaymentDetailPage` | Low | **Fixed** (branch `fix/payment-status-wording`, with G-13): shown as "Released · Nothing was charged" (client: "Hold released"); Download receipt only when money was charged or collected |
| G-7 | `docker-compose.yml`'s `minio/minio` image is no longer pullable from Docker Hub ("pull access denied"); the gate ran the stack without MinIO. | `docker-compose.yml` | Low (local dev only) | Pin a reachable image (e.g. a Quay/registry mirror) |
| G-9 | The frontend container reports "unhealthy" from the moment it starts (local stack): its `HEALTHCHECK` (`wget --spider http://localhost:8080/`) gets "Connection refused" inside the container, while the app serves 200 through the published port. | `frontend/Dockerfile` HEALTHCHECK | Low (misleading status; check whether deployed probes share it) | Use `http://127.0.0.1:8080/` — verified 2026-10-15 inside the container: nginx listens on IPv4 only, `localhost` resolves to `::1` first (refused), `127.0.0.1` answers |
| G-8 | The cancellation email failed locally ("Failed to send cancellation email") — expected with no Resend key; noted only so it isn't mistaken for a regression. | Local `.env` | None | — |
| G-10 | After the client cancels POK's 3-D Secure challenge (T8 re-test), POK's form shows "Error! Something went wrong!" and the app shows a red toast "Something went wrong!" (the SDK's `onError` message passed through unchanged) — neither says nothing was charged or how to retry. Help now explains it (client-deposit-pay tip, manual callout). | `PaymentMethodSelector` `handleError` → `MyBookingsSection` / `BookAppointmentForm` `toast.error(message)` | Medium (client confusion at the payment step) | Map the SDK's 3-D Secure cancel/failure to an app message ("Payment not completed — nothing was charged. Try again or pay in cash."); depends on C-6 item 12 |
| G-12 | A client who pays on POK's hosted page (owner payment link, T6 path) is redirected to POK's default `redirectUrl` (a POK development site that doesn't load) — they never see a confirmation or come back to TattooOS. The payment itself is fine (webhook → "healed to held"). | `PokPaymentProvider` order creation (no `redirectUrl`/`failRedirectUrl` sent) | High for payment links (client sees an error page after paying) | **Unblocked 2026-10-16:** POK accepts both fields and honours them after its own result screen (C-6 item 11); an app-created order (R1) still carried POK's dead default. **Fixed** (branch `fix/payment-return-pages-and-redirects`): every order sends `redirectUrl` / `failRedirectUrl` = `{App:BaseUrl}/payment-result?status=paid|failed&payment=<id>` (https only, so a local http stack never risks a refused order); new public page `/payment-result` says "Payment received" or "Payment not completed — nothing was charged" with Try again; the in-app SDK form ignores the fields; Help + manual |
| G-13 | The owner's payment page shows only the original amount and one status: a 234 partial refund (kept 1,000), a released hold (kept 0) and a late-cancel partial capture (kept 617) all read "ALL 1,234 · Refunded". The owner can't see how much the studio actually kept. The statuses are also inconsistent: a late cancel where the studio keeps 100 % ends "Paid / Forfeited", one where it keeps 50 % ends "Refunded / Refunded" (re-test 2026-10-15). The client sees the same confusion: My bookings says "Deposit refunded" when the studio kept half, and "Deposit paid" after a partial or full refund (G-14). | `PaymentDetailPage` (amount rows) | Medium (owner reconciliation) | **Fixed** (branch `fix/payment-status-wording`): the API now returns `refundedAmount` and the appointment's status; owner list + detail and the client's My bookings show what happened — On hold, Paid, Partly refunded, Refunded, Released, Partly kept, Kept — with a line such as "1,000 kept · 234 refunded"; the appointment page's "Forfeited" badge reads "Kept"; Help + manual updated. The stored statuses are unchanged |
| G-14 | After an owner refunds a captured deposit in full, the appointment's `DepositStatus` stays "Paid", so the client's My bookings still says "Deposit paid" (re-test 2026-10-15, T8). No money risk: a later cancel skips the refund because the payment is already Refunded. | `RefundPaymentCommand` (never updates `Appointment.DepositStatus`) | Medium (wrong status shown to the client) | **Fixed** (branch `fix/payment-status-wording`): a full refund sets `DepositStatus = Refunded`; a partial refund leaves it Paid and My bookings shows "Partly refunded · 234 refunded to your card"; 3 handler tests |
| G-15 | After an owner cancels from the appointment page, the page calls `navigate(-1)`. When the appointment was opened from a direct link (new tab, notification, email), "back" leaves TattooOS. Seen once in the 2026-10-15 re-test (INFERRED from code + that run). | `AppointmentDetailPage.handleCancelConfirmed` | Low | **Fixed** (branch `fix/payment-return-pages-and-redirects`): `useGoBack(fallback)` goes back only when there's an in-app page behind (`location.key !== "default"`), otherwise to the Schedule; same hook on the five other history-back buttons (change password, tattoo record, intake builder, consent/intake forms) |
| G-16 | Any unknown URL silently redirected (guests to `/discover`, signed-in users to their home) — found when the probe's `/pok-probe/...` return URL landed on Discover. Hides broken links and sends people somewhere they didn't ask for. | `router.tsx` `CatchAllRedirect` | Low | **Fixed** (same branch): a real "Page not found" page with the user's home (guests: home page + Discover), FAQ entry; also two dead in-page links in the user manual repointed |
| G-11 | Long notice windows are shown in hours: "Less than 720 hours away — contact the studio directly to reschedule." (a 30-day window). | `MyBookingsSection.tsx` (`noticeWindowHours`) | Low (cosmetic) | Show days when the window is 48 h or more |
