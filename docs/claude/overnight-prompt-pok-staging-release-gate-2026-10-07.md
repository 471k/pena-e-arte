# Overnight Prompt — POK Staging Release Gate (Flow A card deposits): prove the amount unit and the hold → capture → refund status mapping against real POK sandbox transactions

> Feed this file to Claude Code (main **Pena e Artë - Engineering** project, full repo write
> access) as the task prompt. **Read the whole file before doing anything.** Every code quote below
> was copied from `main` at `d8b743b` (merge of PR #238) on 2026-10-07, with line numbers from that
> commit; `main` was still at `d8b743b` on 2026-10-13. If your checkout disagrees, trust your
> checkout, record the drift in your final report, and do not silently change approach.

**Date logged:** 2026-10-07 (finalised 2026-10-13)
**Requested by:** Phi
**Origin:** Claude Code's open task "POK staging transaction" (the release gate for Flow A card
deposits), which points at `docs/payments/runbook-studio-currency-migration-2026-09-27.md` §3
("§3.1 — POK amount-unit staging checklist"). The Engineering Consultation project then
investigated from 2026-10-03 to 2026-10-07: it crawled POK's public API docs, ran test orders in
POK's staging dashboard, read live order states and re-read every file on the card path. Its full
notes are in the claude.ai Project doc `claude/pok-api-dashboard-reference-2026-10-03.md`, which is
private and not in this repo. §2 of this file is the part of that evidence you need.

**Mode: ATTENDED.** This is not the usual "fully autonomous, no user present" run, and that is
deliberate. The gate needs three things only Phi can do:
- enter POK credentials;
- type test-card numbers into POK's payment form;
- read the POK merchant dashboard.

The run alternates autonomous phases, where you work alone, with checkpoints C0–C5. At each
checkpoint you **stop, show Phi exactly what you need, and wait**. If Phi is not there, stop at that
phase boundary with a green build and a short status note. Never guess an answer to a §5 decision.

**Size:**
- **Day 1:** about 1–3 h of autonomous code, depending on which §10 fixes Phi approves, plus about
  2 h attended.
- **Day 2 (at least 25 h later):** about 25 min attended (C4 ~10 min, C5 ~15 min). Phase 8 waits for
  Phase 7's result, so both checkpoints fall on Day 2.
- **Optional:** a day-8 check.

Work on one branch and make one commit per phase. Every phase leaves the build green.

### Checkpoint map

| Checkpoint | When | What Phi does | Time |
|---|---|---|---|
| **C0** | end of Phase 0 | answers D1–D6 (§5) | ~10 min |
| **C1** | end of Phase 3 setup | creates the test studios and data, and types the POK keys into the app | 30–45 min |
| **C2** | Phase 4 | ALL run: pays with test cards and clicks Capture / Refund / Cancel when told | 45–60 min |
| **C3** | Phase 5 | EUR run (only if D3 = yes) | ~20 min |
| **C4** | Phase 7, ≥ 25 h after T9 is authorised | clicks "Capture deposit" on the aging hold | ~10 min |
| **C5** | end of Phase 8 | reviews the runbook entry, Help diffs and PR | ~15 min |

### Before starting

```bash
git status --porcelain
```

As of 2026-10-07, Phi's working tree is **not clean**. Everything expected to be dirty is a doc:

- **From the 2026-10-02 consultation session:**
  - modified: `docs/payments/ADR-0001-amendment-A-verified-repo-state.md`,
    `ADR-0001-amendment-C-studio-currency.md`, `ADR-0001-payment-providers.md`,
    `implementation-readiness-status-2026-07-31.md`, `market-scan-both-flows.md` and
    `pok-assessment.md`;
  - untracked: `docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md` and
    `docs/claude/overnight-prompt-pok-final-decision-sync-2026-10-02.md`.
- **This file:** `docs/claude/overnight-prompt-pok-staging-release-gate-2026-10-07.md`.
- **Unrelated untracked docs:** `docs/claude/TODO.md`,
  `docs/claude/legal-review-brief-yearly-refund-terms.md` and three files under `docs/specs/`.

**If anything outside `docs/` is dirty, stop and ask Phi.** Never `git add -A` in this run: the
unrelated docs must not ride along in a payments PR.

```bash
(
  set -e   # any failed step stops the block, so nothing lands on the wrong branch
  git checkout main
  git pull --ff-only
  git config core.hooksPath .githooks   # gitleaks pre-commit hook (CONTRIBUTING.md), if not already enabled
  git checkout -b fix/pok-staging-release-gate-2026-10-07
  if git diff --cached --quiet; then
    git commit --allow-empty -m "checkpoint: before POK staging release gate"
  else
    echo "STOP: staged changes present — nothing committed; ask Phi"
  fi
)
```

Run this block **once**, at the very start. If any step fails (for example, the pull is refused
because upstream changed one of the modified docs, or the branch already exists), the block stops
before committing anything: stop and ask Phi.

Enable the hook before the first commit, so every commit in this run is scanned. If the block prints
STOP, the branch exists but nothing was committed. Ask Phi, because a commit now would sweep
whatever is staged into it.

**Resuming (Day 2, or after any stop) — don't re-run the block blindly:**
- **The checkpoint commit exists** (`git log --oneline main..fix/pok-staging-release-gate-2026-10-07`
  is non-empty, and its last line is the checkpoint): run
  `git checkout fix/pok-staging-release-gate-2026-10-07`.
- **The block printed STOP** (that log is empty): once Phi has dealt with the staged changes, run
  `git checkout fix/pok-staging-release-gate-2026-10-07 && git diff --cached --quiet && git commit --allow-empty -m "checkpoint: before POK staging release gate"`.
- **The block failed before `git checkout -b`** (e.g. the pull was refused): nothing was created.
  Once Phi has fixed the cause, run the block again.
- **The branch already existed before this run:** ask Phi; never reset or delete it.

The hook uses a local `gitleaks` binary if there is one. Otherwise it pulls and runs the official
gitleaks Docker image. That is the repo's own documented hook, not a new tool under §7. If neither
gitleaks nor Docker is available, the hook fails closed and every commit fails, so stop and ask Phi.

The docs checkpoint commit for Amendment D and the related files waits for Phi's answer to D6 at
C0 (§5).

Three rules hold for every phase:
- The constraints in §7 apply everywhere.
- Help sync is part of each phase's definition of done (CLAUDE.md rule 7). §19 collects it in one
  table.
- Benchmark notes are given per phase and collected in §20 (rule 6).

---

## 1. The problem, and what "done" means

`PokPaymentProvider` was written from POK's documentation before any sandbox credentials existed.
Two of its assumptions decide whether card deposits move the right amount of money. They also
decide whether the app knows a deposit was only **held** or actually **charged**.

1. **Amount unit (`ToPokAmount`).** The app sends whole (major) units, rounded to the currency's
   minor unit: `12.34` for EUR, `1234.00` for ALL. If POK expected minor units, every charge would
   be off by a factor of 100.
2. **Status mapping (`MapStatus`).** The code infers "authorised but not yet captured" from
   `canBeCaptured`, but it tests `isCompleted` before `canBeCaptured` (only `isRefunded` and
   `isCanceled` come earlier). Evidence gathered on 2026-10-07 (§2, E2)
   shows that POK's own dashboard checks the held state *before* anything that reads as paid.
   - If POK sets `isCompleted` on a held order, today's code marks the deposit **Paid without ever
     capturing it**.
   - The hold then lapses, and the studio silently never receives the money.

Reading the code paths this gate exercises also turned up money-loss bugs right next to those two
methods (§3):
- reconciliation cancels authorised holds without checking first;
- cancelling an appointment "refunds" a hold that was never captured;
- a forfeited deposit is never collected;
- a deposit captured by completing the appointment or marking a no-show never gets `PaidAt`, so
  every revenue report leaves it out.

It also turned up two untested facts about POK that the deposit model depends on:
- POK's public endpoint stops returning an order 24 h after creation;
- nobody knows whether a held order can still be captured after that.

**Done means all of the following:**

- **Amounts match.** A real, app-created POK sandbox order in **ALL** records exactly the amount the
  app sent: at creation, on partial capture where it applies, and on partial refund. The same holds
  in **EUR**, unless Phi defers EUR at C0.
- **Statuses map correctly.** `Authorized`, `Captured`, `Canceled`, `Refunded`, `Pending` and
  `Failed` map correctly from the flag sets POK *actually returns*. Those flag sets are recorded as
  evidence and frozen as unit-test fixtures.
- **Webhook checked.** The webhook reaches `/api/v1/webhooks/pok` and triggers reconciliation, or
  the reason it could not be checked is written down.
- **Aging probe recorded.** The ≥ 25 h result is written down: can a held deposit still be captured
  after the POK order's 24 h expiry?
- **Runbook updated.** `docs/payments/runbook-studio-currency-migration-2026-09-27.md` §3.1 has a
  dated, sanitized result entry that lists every fix made.
- **Gate passed is not go-live.** The runbook entry lists separately the go-live blockers that
  remain (§17). This run does not enable any studio for live card deposits.

---

## 2. Evidence already gathered (2026-10-03 → 07) — do not re-derive

Everything here was read directly, from one of four sources:
- POK's public API docs;
- POK's staging API;
- POK's own merchant dashboard (its UI, its network traffic and its shipped client code);
- this repo.

Nothing here comes from memory or guesswork. Where a point is deduced from what was read rather
than observed directly, it says so. Order ids appear as 8-character prefixes because this repo is
public (§4, item 9).

**E1 — POK order flags, as returned by `GET /merchants/{merchantId}/sdk-orders/{id}`.** This is the
merchant-scoped endpoint that `PokPaymentProvider.GetOrderAsync` calls. The flags below were read on
2026-10-07. The response carries six fields POK does not document: `isConfirmed`, `isCaptured`,
`capturedAmount`, `refundableAmount`, `status` and `can`.

| Order | State | autoCapture | isConfirmed | canBeCaptured | isCaptured | isCompleted | isCanceled | isRefunded | capturedAmount | `status` | `can` |
|---|---|---|---|---|---|---|---|---|---|---|---|
| `86d07767` | created, unpaid (1,234 ALL); read before its expiry | false | false | false | false | false | false | false | 0 | `created` | cancel only |
| `2b8d3d2d` | unpaid; 3DS failed; past expiry (40,000 ALL) | true | false | false | false | false | false | false | 0 | `expired` | cancel only |
| `ae4506ec` | paid, auto-capture (100 ALL) | true | true | false | true | true | false | false | 100 | `paid` | refund; refundable 100 |
| `7373e46c` | manual capture, 60 of 100 ALL captured | false | true | false | true | true | false | false | 60 | `paid` | refund; refundable **60** |
| — | **authorised, not yet captured** | false | ? | ? | ? | ? | ? | ? | ? | ? | ? |

The last row is the gap this gate closes (T1 in Phase 4). Order `86d07767` was created on
2026-10-07 to fill that row early, but it was never paid. On 2026-10-13 POK's dashboard still listed
it as unpaid, with no transaction. It passed its 24 h expiry on 2026-10-08, so it very likely can
no longer be paid (E3). **No held-state flags have been observed yet.** T9 (run first, §12) gives
the first observation on an app-created order, and T1 records it as this row.

**E2 — How POK's own dashboard labels an order.** This was read from the dashboard's shipped client
code on 2026-10-07 and is paraphrased here, not quoted. The checks run in this order:

1. *Refunded*, if `isRefunded`.
2. **"On hold", if `isConfirmed` and `canBeCaptured`.**
3. *Paid*, if `isCompleted` and the order's transaction status is "confirmed".
4. *Partially refunded*, if `isCompleted` and the transaction status is "partially-refunded".
5. *Canceled*, if `isCanceled`.
6. Otherwise *Unpaid*.

The held check comes **before** the paid check. `MapStatus` does the opposite: it maps `isCompleted`
to Captured before it maps `canBeCaptured` to Authorized. Two more points:
- **Deduced from that order:** a partially refunded order must have `isRefunded` false, or it would
  be labelled *Refunded* at step 1. T3 confirms this.
- **Read directly:** POK's English label for the held state is **"On hold"**.

**E3 — The public order read expires.** `GET /sdk-orders/{id}` (no auth) returns
`HTTP 410 {"error":"Gone","message":"Order is expired"}` for **every** order once its `expiresAt` has
passed, whatever its state, paid orders included. The merchant-scoped endpoint (E1) still returns
`200` for those same orders. Consequences:
- you can use the public read (curl, Appendix B) **only within 24 h** of an order's creation;
- after that, read through the app's own logs (Phase 1 adds a flag log line);
- POK's checkout almost certainly loads the order through that public read, so an expired order
  very likely cannot be paid. No test pays an expired order on purpose; if it comes up, record it.

**E4 — Orders last 24 hours.** POK's default `expiresAt` is `createdAt` + 24 h.
- For client deposits the app sends `expiresAfterMinutes: 1440` (`CreateDepositPaymentHandler`'s
  private const `HoldDurationMinutes`, `CreateDepositPaymentCommand.cs` line 30).
- For owner payment links, gift cards and packages it sends `"expiresAfterMinutes": null`, so POK
  presumably applies its 24 h default.
- The request DTO (`CreateOrderRequestBody`, `PokPaymentProvider.cs` lines 184–192) has no
  `JsonIgnore`, and `JsonContent.Create` writes nulls. So `expiresAfterMinutes` and `webhookUrl`
  go out as explicit `null`s whenever they're unset. Whether POK accepts an explicit `null` is
  untested.
- The gate creates only client deposits. They send `expiresAfterMinutes: 1440` and a
  `merchantCustomReference` (`PokPaymentProvider.cs` lines 65–69), so the only `null` a gate order
  can send is `webhookUrl`, and only if D4 gave no URL. In that case T9 (run first in Phase 4, §12)
  is the first test of it, and T1 repeats it. With a D4 URL, record "explicit null untested" in the
  evidence log and the runbook entry. If POK rejects the nulls, §14 covers the fix.

**What happens to an *authorised* order at `expiresAt` is unknown.** That is the aging probe in
Phase 7. Separately, card networks keep a customer-initiated, card-not-present authorisation for
about 7 days (§20).

**E5 — How POK represents amounts.** This is prior evidence, not proof:
- an order created in POK's own dashboard for 1,234.00 ALL reads back as `amount: 1234` and
  `finalAmount: 1234`;
- a 60-of-100 capture reads back as `capturedAmount: 60`.

So POK's API represents ALL in whole units. The gate still has to show the same thing for an order
**the app** created, and for EUR.

**E6 — Partial capture.** POK allows **one** capture per order, capped at the authorised amount. Its
own dialog says the captured value cannot exceed the order total. After 60 of 100 is captured,
`refundableAmount` is 60, not 100.

**E7 — Authentication works.** `POST /auth/sdk/login` with Phi's staging key (tag
`engineering-test`) returns 200 with `accessToken`, `expiresIn` (`"600000"`, milliseconds as a
string), `tokenType` (`bearer`) and `expiresAt`. `PokAuthClient.LoginAsync` already uses
`expiresAt`.

**E8 — Where an owner finds the Merchant ID.** In POK Business, English UI:
1. Go to **E-payments → API Keys**.
2. Create a key.
3. A one-time dialog shows **Merchant ID, Key ID and Key Secret** together, and warns that the key
   values can't be accessed again.

The Merchant ID is not shown on the API Keys list, Configurations, Branches or Profile pages (all
checked on 2026-10-07).

**E9 — The account balance is not the order amount.** POK's Transactions list shows card payments at
gross value (+100.00 ALL, +60.00 ALL), yet the staging Lek account balance shows 116.00 ALL. That
leaves 44 ALL unexplained, possibly fees. **Always compare order amounts, never the account
balance.** The fee question belongs to Finance and POK (§17, C-12).

**E10 — Test cards (from POK's docs):**
- `4111 1111 1111 1111`: no 3DS; verified working on staging.
- `4000 0000 0000 1091`: triggers a 3DS challenge; if the challenge isn't completed, the payment
  fails at the `3DS_VALIDATION` step.
- Expiry `12/26`, CVV `111`. Any test email and name; country Albania.

**E11 — Which POK host each environment calls.** This answers the task's "confirm staging uses POK's
staging host".
- `Pena_e_Arte.API/appsettings.json`, lines 49–52:
  `"Pok": { "BaseUrl": "https://api-staging.pokpay.io", "WebhookCallbackBaseUrl": "" }`.
- No `Pok__*` key exists in `k8s/base/api-configmap.yaml`,
  `k8s/overlays/staging/api-config-patch.yaml`, `k8s/overlays/production/api-config-patch.yaml` or
  `.github/workflows/cd.yml`.
- `docker-compose.yml` sets
  `Pok__BaseUrl: ${POK_BASE_URL:-https://api-staging.pokpay.io}` and
  `Pok__WebhookCallbackBaseUrl: ${POK_WEBHOOK_CALLBACK_BASE_URL:-}`.

So:
- deployed staging calls POK staging ✔;
- **deployed production also calls POK staging.** This is a go-live blocker (§17, C-2). For now it
  works as an accidental kill switch: production keys fail at POK staging's login;
- **no deployed environment sends a `webhookUrl` value.** The field goes out as `null` (E4).

**E12 — Deployed staging cannot connect a POK account.**
- `k8s/overlays/staging/vault-exclude-patch.yaml` deletes the Vault StatefulSet, Service and
  ConfigMap from staging.
- `cd.yml`'s staging secrets step leaves out `Vault__Address` and `Vault__Token` on purpose:
  pointing staging at production's Vault would leak credentials across environments.

Here is what happens when an owner tries to connect on staging:
1. `ConnectPokAccountCommand` commits `PokMerchantId` and the `StudioCredentialRef`.
2. It then calls `ISecretsProvider.SetSecretAsync`, which fails because no Vault is configured.
3. The handler restores the previous `PokMerchantId`, removes the `StudioCredentialRef` if this
   call created it (always so on a first connect), saves, and rethrows.
4. `ExceptionMiddleware` has no mapping for that exception, so the owner sees
   `500 "An unexpected error occurred."`

Runbook §3.1 step 1 ("Pick one already-connected staging studio") is therefore impossible on
deployed staging today.

**E13 — The local stack can connect.** `docker-compose.yml` has everything needed:
- a dev-mode Vault: `hashicorp/vault:1.18`, in-memory, root token
  `${VAULT_DEV_ROOT_TOKEN:-dev-only-not-for-prod}`, port 8200;
- the API reads `Vault__Address: ${VAULT_ADDR:-}` and `Vault__Token: ${VAULT_TOKEN:-}`;
- the app runs on `http://localhost:8081` (nginx proxies `/api/` to the API);
- the API runs on `http://localhost:8080`;
- MySQL `pena_e_arte_dev`, user root/root, host port `${MYSQL_HOST_PORT:-3306}`. Phi's own `.env`
  overrides the port to 3307, according to his local run notes.

**E14 — How code reaches staging.** `cd.yml` deploys production, then staging, on every CI-green push
to `main`. `workflow_dispatch` redeploys staging only, and pins `ghcr.io/471k/tattooos-api:${SHA}`
(`SHA` is `github.sha` on a dispatch; `cd.yml` lines 365 and 462) without building that API image.
So staging can only run code that has already merged to `main`.

**E15 — Uncommitted and unexecuted work from 2026-10-02.**
- Amendment D and the 2026-10-02 sync prompt exist only as untracked files on Phi's machine.
- That prompt has not been run: `main` has no
  `FlowB_NeverReferencesPokTypes_UntilAmendmentDsContingencyIsLifted` test. Yet
  `docs/claude/architecture.md`'s Decisions Log row "POK Flow-B guard test shipped" (line 1793)
  says it shipped.
- That prompt's §6.4 would append a "blocked on Phi — no studio connected" paragraph to runbook
  §3.1. This run supersedes that paragraph (Phase 8).

---

## 3. Findings in the code — what this gate will run into

Each finding gives the location on `d8b743b`, the failure, and where it gets fixed:
- Findings 1–4 also name the gate test that exposes them (§12–§15).
- Findings 5, 7 and 14 are fixed by B4, B5 and B6. B4 and B6 have unit tests and no dedicated gate
  test; T2 checks B5's badge.
- Finding 11 is fixed in Phase 8 (8.3, 8.3b and 8.4).
- Findings 6, 8, 9, 10, 12 and 13 name their §17 backlog item. Finding 12's doc half is fixed in
  Phase 8.7.

**Finding 1 — `MapStatus` precedence.**
- **Where:** `Pena_e_Arte.Infrastructure/Services/PokPaymentProvider.cs`, lines 164–171.
- **Failure:** if POK returns `isCompleted: true` on a held order, `MapStatus` returns
  `Captured`. Then:
  - `ConfirmCardPaymentCommand` (lines 58–72) sets the payment to `Paid` and the appointment's
    `DepositStatus` to `Paid`;
  - `CreateDepositPaymentCommand`'s resume branch (lines 108–116) does the same;
  - `DeclareCashDepositCommand` (lines 67–76), which runs when a client switches to cash, does the
    same and then refuses the cash switch with "already been paid by card". T4 runs this command,
    but on an unpaid order, so it takes the `Pending` → `CancelAsync` branch (lines 79–80), not
    this one;
  - the owner never sees "Capture deposit", and the hold lapses;
  - the gift-card and package jobs would *activate* purchases on the same mis-map (Finding 6).
- **Exposed by:** T1.
- **Fix:** Phase 1 (authorised by the task).

**Finding 2 — Reconciliation never asks POK about pending payments.**
- **Where:** `Pena_e_Arte.Infrastructure/Jobs/PaymentReconciliationJob.cs`.
  `CancelStalePendingAsync` (lines 53–81) and `ReleaseExpiredHoldsAsync` (lines 89–117) call
  `CancelAsync` without checking with POK first.
- **Failure:** take a client who authorised, but whose confirm call never ran (closed tab, dropped
  connection, 3DS redirect).
  - At the first run after `HoldExpiresAt` (24 h), their hold is cancelled. The job runs daily at
    02:00 UTC, plus once per webhook.
  - The deposit is lost and the payment is marked `Failed`.
  - The webhook does not help, because all it does is re-run this job.
  - `docs/claude/architecture.md`'s AllowAnonymous row for the webhook (line 1239) says the job "re-fetches
    every in-flight payment's real status". That is not true for `Pending` payments.
- **Exposed by:** T6.
- **Fix:** B1.

**Finding 3 — Held reconciliation is incomplete.**
- **Where:** `ReconcileCapturedAsync` (lines 19–51).
- **Failure:**
  - It promotes `Captured` to `Paid` (with `PaidAt`) without setting `Appointment.DepositStatus =
    Paid`. The confirm path, the resume branch and `CaptureDepositCommand` (lines 42–52) set it.
    `CompleteAppointmentCommand` doesn't either (Finding 14).
  - It ignores a held payment whose POK hold has died, so the owner keeps seeing "Capture deposit"
    for a hold that no longer exists.
- **Exposed by:** T9 (Phase 7).
- **Fix:** B2.

**Finding 4 — Cancelling an appointment treats a held deposit as if it had been charged.**
- **Where:** `Pena_e_Arte.Application/Appointments/Commands/CancelAppointmentCommand.cs`, lines
  99–133. A held payment (status `Captured`) takes the same branch as a paid one.
- **Failure, by refund percentage:**
  - **100%:** it calls `RefundAsync` on an order that was never captured.
    - POK's dashboard offers no refund on a held order, and the API's answer is untested.
    - The likely result is a 4xx, which becomes a `ServiceUnavailableException`, so the user gets a
      503 and the appointment is **not** cancelled.
  - **1–99%:** it requests a partial refund of money that was never captured.
  - **0%:** the comment (lines 127–128) says the deposit "is forfeited outright, no Stripe call: the
    studio keeps the already-authorized/captured amount". For a held deposit nothing was ever
    captured, so the forfeited deposit simply lapses.
- **Side effect on any provider failure:** the reminder jobs are deleted first (line 75), before the
  provider call, and nothing is saved until line 149. So when `RefundAsync` throws, the appointment
  stays active but has lost its 48 h and 24 h reminders. Record this if T5a hits it. B3 moves the
  reminder deletion after the save; if Phi declines B3, the move stays in the backlog (§17, C-16).
- **Existing test:** `CancelAppointmentHandlerTests.Handle_CapturedCardPayment_RefundsViaStripe`
  (around line 133) encodes the wrong behaviour.
- **Exposed by:** T5a and T5b.
- **Fix:** B3.

**Finding 5 — An expired hold can be resumed.**
- **Where:** `CreateDepositPaymentCommand`'s resume branch (lines 97–99).
- **Failure:**
  - If the provider says `Pending`, it resumes the same order even when it is past `expiresAt`.
    POK's checkout almost certainly can't load an expired order (E3), so the client's card form
    breaks until the next 02:00 job cancels the order.
  - `CreatePaymentIntentCommand` (lines 51–52) passes no `HoldDurationMinutes` and sets no
    `HoldExpiresAt`, so the expiry pass never releases payment-link holds.
- **Fix:** B4.

**Finding 6 — Gift-card and package purchases by card can't be completed.**
- **Where:** `PurchaseGiftCardCommand` and `PurchasePackageCommand`.
- **Failure:**
  - Each creates an `autoCapture: false` POK order. The UI tells the buyer to "check your email to
    complete payment", but no email and no page exist to pay it. `GiftCardReconciliationJob` and
    `PackagePurchaseReconciliationJob` activate a purchase only when the provider reports
    `Captured`, and nothing ever captures these orders.
  - Combined with Finding 1, a paid-but-uncaptured order *would* read as `Captured` and activate a
    gift card the studio was never paid for. Phase 1 closes that hole, which leaves these purchases
    stuck as `Pending` instead (fail-safe).
  - Step 2 of Help's `client-gift-cards` and `client-packages` ("Check your email to complete
    payment once your purchase is confirmed.") is false in both.
- **Scope:** out of scope (§17, C-3). **Do not exercise these flows in the gate.**

**Finding 7 — Owners see the held state labelled "Captured".**
- **Where:** `PaymentListPage.tsx` `STATUS_LABELS` (lines 25–32); `PaymentDetailPage.tsx`
  `StatusBadge` (lines 38–52), which renders the raw enum. The label appears on the same page as the
  "Capture deposit" button.
- **What others do:** POK's dashboard calls this state "On hold"; Stripe calls it "Uncaptured".
- **Fix:** B5.

**Finding 8 — Webhook fan-out.**
- **Where:** `PaymentEndpoints.HandlePokWebhook`.
- **Failure:** each anonymous call enqueues a full, cross-tenant `PaymentReconciliationJob`. The
  `billing` rate limiter partitions by user id, so all anonymous callers share one `"unknown"`
  bucket of 20 per minute. Anyone can make the API re-poll POK for every held (`Captured`) card
  payment in every studio, and re-run the cancel passes, 20 times a minute. After B1, every pending
  card payment is polled too.
- **Scope:** §17, C-5.

**Finding 9 — A Vault outage reads as "reconnect POK".**
- **Where:** `PokAuthClient.ResolveCredentialsAsync` (lines 117–127).
- **Failure:** every secrets failure becomes `PaymentProviderNotConnectedException`
  ("…Reconnect POK in Payment Settings."), including "Vault sealed" and "Vault unreachable".
  - Production Vault must be unsealed by hand after any restart (ADR-0002). After that, every owner
    is told to reconnect.
  - Reconnecting then fails with a 500 (the E12 path).
- **Scope:** §17, C-8.

**Finding 10 — Keys are saved without a test login.**
- **Where:** `ConnectPokAccountCommand`.
- **Failure:** a typo in the keys only shows up as a 503 at the first client checkout.
- **Scope:** §17, C-7.

**Finding 11 — The copy disagrees about when a deposit is charged.**

| Where | What it says |
|---|---|
| `PaymentMethodSelector.tsx` (lines 84, 160) | "charged when the studio confirms your session" |
| `DepositCheckoutPage.tsx` (lines 60, 132) | "The studio will capture the deposit before your appointment" |
| `DepositCheckoutPage.tsx` (line 83) | "not be charged until the studio confirms your appointment" |
| `MyBookingsSection.tsx` (line 199; the client's My Bookings, shown when the payment is `Captured`) | "Deposit authorised — charged when the studio confirms" |
| Help `client-deposit-pay` | "captures the payment closer to your appointment" |
| Manual `client-deposit` (line 1372, wireframe lines 1379–1380) | "isn't charged until the studio confirms the appointment" |
| The code | captures on Complete, on No-show, or when the owner clicks "Capture deposit" (and, after B3, on a late cancellation) |

Phase 8 aligns all of it to what the code does: Help and the manual in Phase 8.3/8.4, the six
in-app strings in Phase 8.3b. The hold-window sentence waits for the aging probe (Phase 7).

**Finding 12 — Stale labels and docs.**
- "Stripe PI" still labels the POK order id in `PaymentDetailPage.tsx` (line 347, and its test at
  line 184) and in `PaymentInvoiceService.cs` (line 149). That's §17, C-10.
- `docs/claude/frontend.md`, "Payment UI Pattern" (lines 282–319; line 320 is the `---`
  separator), still describes Stripe Elements for Flow A. Phase 8.7 fixes the doc.

**Finding 13 — The saved-card path is broken.**
- **Where:** `PokCardTokenService`.
- **Failure:**
  - it calls the wrong endpoint;
  - its `csFlexCard` lacks three required fields that the SDK's `AddCardForm` never provides;
  - its response DTO doesn't match what POK returns.
- **Scope:** §17, C-9. **Never use the "Saved card" tab during the gate.**

**Finding 14 — Two capture paths skip the bookkeeping, so revenue reports miss the money.**
- **Where:**
  - `Pena_e_Arte.Application/Appointments/Commands/CompleteAppointmentCommand.cs`, lines 46–51;
  - `Pena_e_Arte.Application/Appointments/Commands/MarkNoShowCommand.cs`, lines 42–49 (the card
    branch).
- **Failure:**
  - Both capture the held deposit and set `payment.Status = PaymentStatus.Paid`, but never set
    `PaidAt`.
  - Every Flow A revenue query filters on `PaidAt != null`: `GetRevenueSummaryQuery` (line 41),
    `ExportRevenueCsvQuery` (line 38) and `GetMyEarningsQuery` (line 54). So a deposit collected
    the normal way — by completing the appointment — never shows up in the studio's revenue, its
    CSV export or the artist's earnings.
  - `CompleteAppointmentCommand` also leaves `Appointment.DepositStatus` as `Pending`. The confirm
    path leaves it `Pending` while the deposit is held (`ConfirmCardPaymentCommand.cs` lines
    52–56).
  - `MarkNoShowCommand` sets `DepositStatus = Forfeited` (line 32), which is intended, so only its
    `PaidAt` is missing.
  - `CaptureDepositCommand` (lines 42–52) does all three correctly. It is the model to copy.
- **Exposed by:** unit tests only. The gate's capture test (T2) uses the "Capture deposit" button,
  which is already correct.
- **Fix:** B6.

---

## 4. Decisions already made — do not re-litigate

1. **POK is the Flow A provider.** It has been the `IPaymentProvider` default since 2026-09-12, and
   ADR-0001 Amendment D (2026-10-02) made it final.
2. **Flow A takes zero platform commission.** There is no `splitWith`, no `PlatformFeeAmount` and no
   `SupportsSplit` (ADR-0001 Amendment B).
   `PaymentArchitectureTests.NoMember_IsNamedPlatformFeeAmountOrSupportsSplit` enforces the last
   two, in the Domain and Application assemblies only. No test checks `splitWith`, which appears in
   no `.cs` file today. Don't add it.
3. **A webhook is a trigger, never a source of payment state** (ADR-0001). The test
   `PokWebhookHandler_NeverReadsRequestBodyOrAssignsPaymentState` must stay green.
4. **Each studio's POK keys live in Vault.** They never go into MySQL, logs, chat, docs or commits
   (ADR-0001, ADR-0002, CLAUDE.md rule 4).
5. **The client pays exactly the amount shown, in the studio's currency, with no conversion
   anywhere** (Finance, 2026-09-27; studio-currency prompt §2.1).
6. **POK supports ALL and EUR only** (`PokPaymentProvider.Capabilities`).
7. **No studio is enabled for live card deposits until this gate passes (runbook §3.1, step 4) *and*
   Phi approves go-live (§17).** Per Phi's own task note (2026-10-07), nothing is live yet:
   production has no studios and no payments.
8. **Only two code changes are pre-authorised by the task:** `ToPokAmount` and `MapStatus`, "if
   they're wrong". Phase 1 hardens `MapStatus` and adds its diagnostics up front, under that
   authority:
   - the change is correct under every flag shape POK can return for the held state (§9);
   - it fails safe on any shape not yet seen;
   - Phi may veto it at C0 (§8, the C0 list, item 5).

   Everything in §10 (B1–B6) needs Phi's explicit "go" at C0. Everything in §17 is out of scope.
9. **The repo is public. Evidence hygiene:**
   - **Never commit or paste into the PR:** credentials, the merchant id, full POK order ids, emails,
     phone numbers, names, or card data beyond the published test numbers.
   - **Order references:** use the first 8 characters only.
   - **Leave out of raw notes:** POK's `merchant` object contains the test merchant's contact
     details. Filter it out of anything you print (Appendix B shows how).
10. **Only Phi types card numbers and POK keys.**
    - You never type them, even on `localhost`. You never ask for them in chat, and you never read
      them back from any screen, file or log.
    - If Phi pastes a secret into chat, stop. Tell him to rotate that key in POK (create a new key,
      then delete the old one), and continue only after he has.

---

## 5. Decisions Phi makes at C0 — do not guess

Present these six questions together, each with its recommended default, and wait. Record the
answers verbatim in your evidence log.

**D1 — Where does the gate run?**
- **A. Recommended: the local docker-compose stack on Phi's machine, against POK's staging
  sandbox.** It uses the dev-mode Vault (E13). It needs no infra change, you get direct access to
  logs and the DB, and a fix can be rebuilt in minutes. It runs this branch's code, fixes included,
  against the same POK host staging uses. Deployed staging only ever runs merged `main` (E14).
  - *What it does not prove:* the deployed k8s staging config. That doesn't matter much: deployed
    staging has no Vault anyway, and production's own path is checked by the go-live smoke test
    (§17, C-11).
- **B. Provision a staging Vault first.** This is an infra change:
  - remove `vault-exclude-patch.yaml` from the staging overlay, which pulls in base's Vault
    StatefulSet, Service, ConfigMap and its 1Gi volume claim (`k8s/base/kustomization.yaml`,
    `vault-statefulset.yaml`);
  - init and unseal it by hand;
  - add `STAGING_VAULT_*` secrets to `cd.yml`;
  - fix the staging config gap (E11).
  - The overlay itself warns that this is "a deliberate follow-up decision, not something to
    silently reverse", and cluster capacity is a stated concern.
  - **If Phi picks B, stop after C0.** Report back; a separate staging-Vault prompt is needed (§17,
    C-1).
- **C. Point staging at production's Vault.** **Rejected.** `cd.yml` documents this as a
  cross-environment credential leak.

**D2 — Which money-path fixes (§10) do you build before the run?** List B1–B6 and let Phi tick them.
- **Recommended:** all six.
- They are each small and fully specified. The gate re-verifies B1 (T6), B3 (T5a/T5b) and B5 (T2)
  on Day 1, and B2 (T9) on Day 2. B4 and B6 are covered by unit tests only; B6 is pure bookkeeping.
- B3 adds one method to `IPaymentProvider` (partial capture). If Phi declines B3, T5a runs as an
  *observation* of today's behaviour and T5b is skipped.
- **One sub-question if B3 is ticked:** if POK refuses, or can't be reached, while an appointment
  with a held deposit is being cancelled, what should happen? §10's B3 spells out the recommended
  answer:
  - **Full release** (the studio cancels, or the client cancels in time): if POK refuses the
    release but reports that the hold is already gone, the cancellation goes through, because
    there is nothing left to release. If the hold is still there or was already charged, or POK
    can't be reached, the cancellation is refused so it can be retried. The money may have moved,
    so the app never marks it released on a guess.
  - **0% (forfeit):** the cancellation always goes through, and the deposit stays on hold for the
    owner to capture.
  - **1–99%:** the cancellation is refused (503, or 422/500 when the studio isn't connected or POK
    is unreachable), so the client retries. Today's refund path for an already-charged deposit
    behaves the same way.

  Phi accepts that or names another outcome.

**D3 — Run EUR now?** Two things are needed first:
1. a **EUR account on the POK staging merchant**. Phi creates it in POK Business →
   Accounts → "Create account" (Albanian UI: "Llogaritë → Krijo Llogari"). Right now the merchant
   has only a Lek account;
2. a second, fresh app studio whose currency is EUR.
- **Recommended:** yes, if the account takes Phi a minute to create.
- Otherwise run ALL only, and record the gate as "ALL passed, EUR pending". EUR card deposits stay
  disabled until a follow-up EUR run.
- **Separate from all this:** whether Albanian studios may charge consumers in EUR at all is a
  **Legal** sign-off, not part of this gate. The EUR run proves only that the mechanics work.

**D4 — How do we check the webhook?** It needs a public HTTPS URL that reaches the local API.
- **Recommended:** Phi's existing Cloudflare Tunnel (`test.tattooos.co`, mentioned in
  `docs/infra/staging-environment.md`), if it is running and points at the compose frontend on
  `http://localhost:8081` (nginx proxies `/api/` to the API). Then set
  `POK_WEBHOOK_CALLBACK_BASE_URL=https://test.tattooos.co`.
- **Ask Phi where the tunnel points first.** `frontend/vite.config.ts` allows the host
  `test.tattooos.co` and proxies `/api` to `http://localhost:5078`. That port is a `dotnet run` API,
  not the compose API on 8080. So the tunnel probably targets the Vite dev server today. If it does,
  POK's calls would reach no API, or the wrong one. Phi must re-point the tunnel at
  `http://localhost:8081` for the run.
- Otherwise skip the check, record "webhook not verified (no public URL)", and list it under go-live.
- Do **not** install a new tunnelling tool without Phi's OK. That would be a new dependency
  (§7).

**D5 — Is Phi available ≥ 25 h after T9's authorisation, for about 10 minutes?** That's the aging
probe.
- **Recommended:** yes. It decides whether "hold at booking, capture at the appointment" can work at
  all (§17, C-4).

**D6 — Commit the 2026-10-02 payments docs as the branch's first real commit?** The files are:
- `docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md`;
- `docs/claude/overnight-prompt-pok-final-decision-sync-2026-10-02.md`;
- the six modified `docs/payments/*.md` listed in "Before starting";
- this prompt file.

**Recommended:** yes. `docs/claude/architecture.md` on `main` already links to Amendment D, which is
a dead link today. If yes, run:

```bash
git add docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md \
        docs/claude/overnight-prompt-pok-final-decision-sync-2026-10-02.md \
        docs/claude/overnight-prompt-pok-staging-release-gate-2026-10-07.md \
        docs/payments/ADR-0001-amendment-A-verified-repo-state.md \
        docs/payments/ADR-0001-amendment-C-studio-currency.md \
        docs/payments/ADR-0001-payment-providers.md \
        docs/payments/implementation-readiness-status-2026-07-31.md \
        docs/payments/market-scan-both-flows.md \
        docs/payments/pok-assessment.md
git commit -m "docs(payments): commit ADR-0001 Amendment D, the 2026-10-02 sync prompt and the POK release-gate prompt"
```

If no, leave them uncommitted and continue. This prompt doesn't need them in git. Phase 8.2's
one-line edit to the 2026-10-02 sync prompt then stays an uncommitted local change too (§22).

---

## 6. Scope boundary — do not touch

| Path / area | Why it is off-limits in this run |
|---|---|
| `k8s/**`, `.github/workflows/**`, `docker-compose.yml`, `appsettings*.json` | No infra, CI or config-default changes under D1 = A. The staging and production `Pok__*` config and prod Vault unseal are go-live items (§17, C-2, C-8). The staging Vault is a staging-parity item (C-1). Per-run local settings go in Phi's gitignored `.env` or in a compose override file kept **outside** the repo (§11). |
| `Pena_e_Arte.Infrastructure/Services/Pok/PokAuthClient.cs`, `Pena_e_Arte.Infrastructure/Services/PokOptions.cs` | Credential resolution, token caching and host selection already work (E7). Finding 9 is §17, C-8. |
| `Pena_e_Arte.Infrastructure/Services/Pok/PokCardTokenService.cs`, `Pena_e_Arte.Domain/Interfaces/IPokCardTokenService.cs`, `frontend/src/features/saved-payment-methods/**`, `PayDepositWithSavedCardCommand` | The saved-card path is broken (Finding 13) and needs its own prompt (§17, C-9). **One exception:** B4 changes the shared `ResolveOrCreateHoldAsync` in `CreateDepositPaymentCommand.cs`, which `PayDepositWithSavedCardCommand` also calls. That handler's own file stays untouched; only its unit test gains the B4 case. |
| `Pena_e_Arte.Application/Appointments/**` | Only three handlers change: `CancelAppointmentCommand` (B3), `CompleteAppointmentCommand` and `MarkNoShowCommand` (B6). |
| `Pena_e_Arte.Application/GiftCards/**`, `Pena_e_Arte.Application/Packages/**`, `GiftCardReconciliationJob.cs`, `PackagePurchaseReconciliationJob.cs` | Finding 6 is a product decision (§17, C-3). Don't test these flows either: they leave orphan POK orders. |
| Flow B: `StripeBillingService.cs`, `StripeDiscountService.cs`, `Pena_e_Arte.Application/Billing/**` | ADR-0001 Amendment D: Flow B stays on Stripe until POK recurring billing is generally available. |
| `PaymentEndpoints.HandlePokWebhook` and its route | Webhook fan-out is §17, C-5. The architecture test pins this handler's shape. |
| `ExceptionMiddleware.cs` | No new mappings in this run (Findings 9 and 10 are §17). |
| `tests/Pena_e_Arte.UnitTests/Architecture/PaymentArchitectureTests.cs` | Its existing tests must stay green, untouched. Adding the Flow B guard test is the 2026-10-02 sync prompt's job, not this run's. |
| Migrations | This run needs no schema change. If a B-item seems to need one, stop and report. |
| Historical overnight prompts in `docs/claude/` | Leave them untouched. **One exception:** Phase 8.2 adds a one-line "superseded" note to the *unexecuted* 2026-10-02 sync prompt's §6.4. |
| `PaymentInvoiceService.cs`, the "Stripe PI" label | Cosmetic (§17, C-10). Not part of the gate. |

---

## 7. Constraints (restated for every phase)

**Dependencies and frontend:**
- **No new NuGet or npm packages,** and no new CLI tools installed on Phi's machine. If you think
  one is needed, stop that sub-task and list it as a prerequisite decision for Phi.
- **No `useEffect` for data fetching.** Use RTK Query hooks only. The approved exceptions are
  resize, keyboard, outside-click, scroll-to, clipboard, timer side-effects, browser API calls inside
  event handlers, the existing `useDocumentMeta` hook and the existing OAuth-toast effect. Separately,
  don't refactor existing effects this run doesn't need to touch, such as the card tab's
  create-on-open effect in `PaymentMethodSelector.tsx`.
- **TypeScript strict, no `any`,** and no default exports on components.

**Backend:**
- **Explicit types in C#.** No `var` for non-obvious types.
- **No business logic in endpoints:** MediatR plus FluentValidation only. No endpoint is added or
  changed in this run; if that becomes necessary, stop and report.
- **Tenant isolation.** The only `IgnoreQueryFilters()` uses touched here are the approved ones:
  `PaymentReconciliationJob` is usage #36, a Hangfire job with no tenant scope. Add no new bypass.
- **Every endpoint keeps `.RequireAuthorization()`** with its current policy.
- **Decimal for money throughout.** Round only with `CurrencyCatalog.Round`. No `double`.

**Logging:**
- **Never log PII.** POK log lines the app writes may carry 8-character order-id prefixes, studio
  ids, payment ids, booleans, amounts, currency codes and POK's `status` string. They must never
  carry:
  - the raw POK response body, which includes merchant contact details and card or billing data;
  - a full order id. Anyone holding one can read the order, merchant contact details included,
    through POK's unauthenticated public endpoint (E3) for 24 h.
- **Known exception, out of scope for this run:** `IHttpClientFactory`'s own Information lines for
  the `"Pok"` client (`System.Net.Http.HttpClient.Pok.*`, "Start processing HTTP request …" /
  "Sending HTTP request …") already log every request URI. Its path carries the merchant id and
  the full order id; .NET redacts only the query string. Silencing them needs a config change
  (ruled out by §6) or a DI change outside this gate's scope, so it is backlog item C-17. Never
  paste or attach those lines anywhere.
- **Serilog structured logs only:** `{Placeholder}` templates, no string interpolation into the
  template, no `Console.WriteLine` or `console.log`.

**Tests and gates:**
- **Tests ship with every change** (unit; integration where a handler's database behaviour
  changes; frontend component states loading, error and empty for any UI change).
- **CI's `help-sync` gate** (`.github/workflows/ci.yml`, job `help-sync` at line 381; its `GATED`
  path regex is line 410). It fails any
  PR that changes a gated path without also changing `helpContent.ts`,
  `frontend/public/user-manual/index.html` or `frontend/src/features/help/tours/`.
  - The gated paths that matter in this run are `Pena_e_Arte.Application/Payments/` and
    `frontend/src/features/payments/`.
  - `Pena_e_Arte.Application/Appointments/`, `Pena_e_Arte.Infrastructure/`,
    `frontend/src/features/appointments/` and `tests/` are not gated.
  - The gate reads `[skip-help-sync]` only from the PR's tip commit message. Don't use it to dodge a
    real Help obligation.
- **Before each commit:** `dotnet format "Pena e Arte.slnx" --verify-no-changes` must be clean, and
  the gitleaks pre-commit hook must pass.
- **Line endings.** `.gitattributes` and `.editorconfig` make `.cs` files CRLF in the working copy
  and `.ts`/`.tsx` files LF. A `.cs` file you write or replace with LF endings fails
  `--verify-no-changes`. Write CRLF, or run `dotnet format "Pena e Arte.slnx"` before verifying.

---

## 8. Phase 0 — Preflight (autonomous) → checkpoint C0

**0.1 — Check for drift since this prompt was written.**

```bash
git log --oneline -1
git diff --stat d8b743b HEAD -- \
  Pena_e_Arte.Infrastructure/Services/PokPaymentProvider.cs \
  Pena_e_Arte.Infrastructure/Services/NullPaymentProvider.cs \
  Pena_e_Arte.Infrastructure/Services/Pok/ \
  Pena_e_Arte.Infrastructure/Services/PaymentInvoiceService.cs \
  Pena_e_Arte.Infrastructure/Jobs/ \
  Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs \
  Pena_e_Arte.Application/Payments/ \
  Pena_e_Arte.Application/Appointments/Commands/ \
  Pena_e_Arte.Application/Reports/Queries/ \
  Pena_e_Arte.API/Endpoints/PaymentEndpoints.cs \
  Pena_e_Arte.API/Middleware/ExceptionMiddleware.cs \
  Pena_e_Arte.API/appsettings.json \
  tests/Pena_e_Arte.UnitTests/ tests/Pena_e_Arte.IntegrationTests/Application/ \
  frontend/src/features/payments/ frontend/src/features/help/ frontend/public/user-manual/index.html \
  frontend/vite.config.ts docker-compose.yml .github/workflows/cd.yml k8s/ \
  docs/claude/architecture.md docs/claude/frontend.md \
  docs/payments/runbook-studio-currency-migration-2026-09-27.md \
  .env.example .github/workflows/ci.yml Pena_e_Arte.API/Program.cs \
  Pena_e_Arte.API/Extensions/RateLimitingExtensions.cs \
  Pena_e_Arte.Infrastructure/Services/PokOptions.cs \
  Pena_e_Arte.Application/Common/ArtistAvailabilityExtensions.cs \
  frontend/src/features/appointments/components/MyBookingsSection.tsx \
  docs/infra/staging-environment.md \
  Pena_e_Arte.Domain/Enums/PaymentStatus.cs Pena_e_Arte.Domain/Enums/PaymentProviderStatus.cs \
  Pena_e_Arte.Infrastructure/Services/VaultSecretsProvider.cs \
  Pena_e_Arte.Infrastructure/Services/LazyVaultClient.cs \
  Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs \
  Pena_e_Arte.Domain/Money/CurrencyCatalog.cs \
  Pena_e_Arte.Application/GiftCards/Commands/PurchaseGiftCardCommand.cs \
  Pena_e_Arte.Application/Packages/Commands/PurchasePackageCommand.cs \
  Pena_e_Arte.Domain/Entities/Payment.cs Pena_e_Arte.Domain/Entities/Appointment.cs \
  Pena_e_Arte.Domain/Enums/DepositStatus.cs Pena_e_Arte.Domain/Enums/AppointmentStatus.cs \
  Pena_e_Arte.Domain/Interfaces/IJobScheduler.cs Pena_e_Arte.Domain/Exceptions/ \
  Pena_e_Arte.Application/Waitlists/Common/WaitlistMatchExtensions.cs \
  Pena_e_Arte.Domain/Entities/TenantEntity.cs Pena_e_Arte.Application/Persistence/ \
  Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs \
  frontend/src/features/appointments/__tests__/BookPage.test.tsx
```

If anything changed, re-read it and adapt the quotes below. Record the drift.

**0.2 — Read these in full before writing any code.**
- **Docs:**
  - `CLAUDE.md`
  - `docs/claude/conventions.md`
  - `docs/payments/runbook-studio-currency-migration-2026-09-27.md`
  - `docs/payments/ADR-0001-payment-providers.md`
  - `docs/payments/ADR-0001-amendment-B-flow-a-zero-commission.md`
  - `docs/payments/ADR-0001-amendment-D-pok-final-both-flows.md` (on Phi's disk)
- **Provider:** `PokPaymentProvider.cs`, `PokAuthClient.cs`
- **Payments commands:** every command under `Pena_e_Arte.Application/Payments/Commands/`
- **Appointment commands:** `CancelAppointmentCommand.cs`, `MarkNoShowCommand.cs`,
  `CompleteAppointmentCommand.cs`
- **Job and endpoints:** `PaymentReconciliationJob.cs`, `PaymentEndpoints.cs`
- **Help surfaces:** `helpContent.ts` articles `owner-connect-pok`, `owner-payments`,
  `owner-payment-create`, `owner-deposit-rule-create`, `client-deposit-pay`,
  `client-cancel-booking`, `faq-deposit-statuses`, `faq-client-cancel-refund`,
  `client-gift-cards`, `client-packages`, `owner-reports` and `artist-earnings`
- **Manual:** sections `client-book`, `client-deposit`, `owner-payments`, `owner-payment-detail`,
  `owner-create-payment`, `owner-create-deposit-rule`, `owner-studio-profile`,
  `owner-studio-currency`, `owner-reports` and `ref-deposit-statuses`
- **Client copy:** `PaymentMethodSelector.tsx`, `DepositCheckoutPage.tsx` and
  `MyBookingsSection.tsx` (the charge-timing strings in Finding 11)
- **Tour:** `ownerTour.ts`

**0.3 — Confirm which POK host each environment uses**, for the record (answers the task's
"confirm staging uses POK's staging host").

```bash
grep -n '"Pok"' -A3 Pena_e_Arte.API/appsettings.json
grep -rnE 'Pok__|POK_' k8s/ .github/workflows/ docker-compose.yml .env.example
```

Expected result, matching E11:
- `appsettings.json` → `https://api-staging.pokpay.io`;
- no `Pok__` or `POK_` key under `k8s/` or in the workflows;
- exactly four lines: `docker-compose.yml` lines 174–175 (the two `Pok__` keys) and `.env.example`
  lines 25–26 (`POK_BASE_URL`, `POK_WEBHOOK_CALLBACK_BASE_URL`).

Write it in the evidence log as: "Deployed staging → POK staging (appsettings default). **Deployed
production → also POK staging (go-live blocker C-2).** No deployed environment sends a `webhookUrl`
value (it goes out as `null`)."

**0.4 — Check local readiness without printing any secret value.**

```bash
docker --version && docker compose version
for k in VAULT_ADDR VAULT_TOKEN POK_BASE_URL POK_WEBHOOK_CALLBACK_BASE_URL MYSQL_HOST_PORT \
         JWT_SECRET_KEY GRAFANA_ADMIN_PASSWORD HANGFIRE_DASHBOARD_USERNAME HANGFIRE_DASHBOARD_PASSWORD; do
  v=$(grep -E "^$k=" .env | head -1 | cut -d= -f2- | tr -d '\r"'); [ -n "$v" ] && echo "$k: set" || echo "$k: empty/missing"; done
# Exact-value checks for D1 = A. They print ok/mismatch only, never a value.
a=$(grep -E '^VAULT_ADDR=' .env | head -1 | cut -d= -f2- | tr -d '\r"'); [ "$a" = "http://vault:8200" ] && echo "VAULT_ADDR: ok" || echo "VAULT_ADDR: not http://vault:8200"
t=$(grep -E '^VAULT_TOKEN=' .env | head -1 | cut -d= -f2- | tr -d '\r"'); r=$(grep -E '^VAULT_DEV_ROOT_TOKEN=' .env | head -1 | cut -d= -f2- | tr -d '\r"')
[ -n "$t" ] && [ "$t" = "${r:-dev-only-not-for-prod}" ] && echo "VAULT_TOKEN: matches the dev root token" || echo "VAULT_TOKEN: missing or mismatched"
```

How to read the result:
- **Vault:** for D1 = A, `VAULT_ADDR` must be `http://vault:8200` and `VAULT_TOKEN` must equal the
  dev root token (`VAULT_DEV_ROOT_TOKEN`, default `dev-only-not-for-prod`).
  - `.env.example` (lines 77–78) also documents a host-side form, `http://127.0.0.1:8200`. That form
    reports "set", but the compose API can't reach Vault with it, so connecting POK at C1 fails with
    a 500 (the E12 path). The exact-value checks catch it.
  - If either check fails, ask Phi at C0 to fix both in `.env`, or to let you do it. They are
    dev-only values, but `.env` holds his other secrets, so don't edit it unasked.
- **Required keys:** compose refuses to start if `GRAFANA_ADMIN_PASSWORD`, `JWT_SECRET_KEY`,
  `HANGFIRE_DASHBOARD_USERNAME` or `HANGFIRE_DASHBOARD_PASSWORD` is unset (`docker-compose.yml`
  lines 135, 169, 221–222). If one is missing, Phi sets it.
- **POK:** `POK_BASE_URL` may stay empty, because the default is POK staging.
- **Line endings and quotes:** `tr -d '\r"'` keeps a Windows-style or quoted-empty (`KEY=""`) `.env`
  value from reporting as set.

**0.5 — Pull the Help search signal.** `docs/claude/architecture.md`'s "Help Search Analytics"
section (line 467) calls the search log "the single highest-signal list of missing documentation
or confusing UX"; its zero-result queries (line 485) are the sharpest part of it. Run this
read-only, against whatever database you can reach read-only: local always, staging if Phi has
already given you read-only access, production only if Phi says so.

```sql
SELECT Role, LOWER(Query) AS q, COUNT(*) AS n
FROM help_search_logs
WHERE ResultCount = 0 AND DeletedAt IS NULL
  AND (Query LIKE '%pok%' OR Query LIKE '%captur%' OR Query LIKE '%refund%' OR Query LIKE '%deposit%'
       OR Query LIKE '%card%' OR Query LIKE '%merchant%' OR Query LIKE '%hold%' OR Query LIKE '%key%')
GROUP BY Role, q ORDER BY n DESC LIMIT 50;
```

Note the terms (not user ids) in the evidence log. Phase 8 uses them to choose Help keywords.

**0.6 — Prepare the gate packet, outside the repo** (e.g. `~/pok-gate-2026-10-07/`, never
committed):
- `evidence.md`, laid out as Appendix A. Full order ids are allowed here; it is Phi's local file.
- `docker-compose.gate.yml`, used in Phase 3. It holds only the webhook-callback override, if D4
  needs one. Nothing else.
- `checklist.md`, a one-page, click-by-click version of Phase 4's test matrix for Phi, with every
  amount filled in.

**C0 — Stop. Show Phi:**
1. the drift report (0.1) and the host confirmation (0.3);
2. questions D1–D6 (§5), with defaults;
3. the `.env` status from 0.4;
4. the checklist from 0.6;
5. the Phase 1 `MapStatus` change (§9). It goes ahead unless Phi vetoes it (§4, item 8).

Wait for answers. Record them. Then, if D6 = yes, make the docs commit.

**Help sync for Phase 0:** none. Nothing user-visible changes.

---

## 9. Phase 1 — `MapStatus` hardening and status diagnostics (autonomous; authorised by the task)

**Why this happens before the run, not after it.** E2 shows how POK's own dashboard ranks states:
"held" (`isConfirmed && canBeCaptured`) comes before "paid". Today's `MapStatus` checks
`isCompleted` first. The hardened mapping below:
- is correct under **both** shapes POK could return for a held order (`isCompleted` true or false);
- reports `Captured` only on positive evidence that money moved;
- logs the raw flags, so T1 records POK's real held shape *and* lets you tell whether the old mapping
  would have been wrong.

If Phi vetoes it at C0, still do §9.2 items 1, 2 and 4 and the `CreateSut` change in §9.3: the
logger, the DTO fields and the flag log are safe on their own. Keep
`GetStatusAsync_NullUndocumentedFields_DoNotThrow`. Skip item 3 (the new `MapStatus`), the theory
replacement and `GetStatusAsync_HeldOrderWithIsCompleted_IsNotReportedAsCaptured`, which fails
under today's mapping. Then run Phase 4 against today's mapping and treat the flag log as an
observation.

### 9.1 Current code (`PokPaymentProvider.cs` @ `d8b743b`)

Line 31:
```csharp
public sealed class PokPaymentProvider(PokAuthClient authClient) : IPaymentProvider
```

Lines 118–124:
```csharp
    public async Task<PaymentProviderStatus?> GetStatusAsync(Guid studioId, string providerReferenceId, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        SdkOrderDto? order = await GetOrderAsync(http, token, merchantId, providerReferenceId, studioId, ct);
        return order is null ? null : MapStatus(order);
    }
```

Lines 156–171:
```csharp
    /// <summary>
    /// Maps POK's documented sdkOrder flags onto the normalized enum. POK does not document a
    /// distinct "authorized, not yet captured" flag — canBeCaptured is inferred from the one
    /// example that shows it (a completed autoCapture:true order has canBeCaptured:false), so an
    /// order that is not completed/canceled/refunded but reports canBeCaptured:true is treated as
    /// Authorized. Verify this against a real autoCapture:false staging order before trusting it
    /// for anything beyond the reconciliation job's own safety net.
    /// </summary>
    private static PaymentProviderStatus MapStatus(SdkOrderDto order) => order switch
    {
        { IsRefunded: true } => PaymentProviderStatus.Refunded,
        { IsCanceled: true } => PaymentProviderStatus.Canceled,
        { IsCompleted: true } => PaymentProviderStatus.Captured,
        { CanBeCaptured: true } => PaymentProviderStatus.Authorized,
        _ => PaymentProviderStatus.Pending,
    };
```

Lines 209–218:
```csharp
    private sealed class SdkOrderDto
    {
        [JsonPropertyName("id")] public string? Id { get; init; }
        [JsonPropertyName("amount")] public decimal Amount { get; init; }
        [JsonPropertyName("finalAmount")] public decimal FinalAmount { get; init; }
        [JsonPropertyName("isCompleted")] public bool IsCompleted { get; init; }
        [JsonPropertyName("isCanceled")] public bool IsCanceled { get; init; }
        [JsonPropertyName("isRefunded")] public bool IsRefunded { get; init; }
        [JsonPropertyName("canBeCaptured")] public bool CanBeCaptured { get; init; }
    }
```

### 9.2 Target

1. **Constructor** (line 31) takes a logger. Add `using Microsoft.Extensions.Logging;` as well:
   ```csharp
   public sealed class PokPaymentProvider(PokAuthClient authClient, ILogger<PokPaymentProvider> logger) : IPaymentProvider
   ```
   DI needs no change: `services.AddScoped<IPaymentProvider, PokPaymentProvider>()` resolves the
   logger.

2. **`SdkOrderDto`** gains these properties. They are all nullable, because POK doesn't document
   them and a missing or null value must never throw during deserialisation. The same DTO also
   deserialises the create, capture and refund responses, where these fields can be absent.
   ```csharp
        [JsonPropertyName("currencyCode")] public string? CurrencyCode { get; init; }
        // Undocumented by POK, but present on the merchant-scoped GET (verified 2026-10-07):
        [JsonPropertyName("isConfirmed")] public bool? IsConfirmed { get; init; }
        [JsonPropertyName("isCaptured")] public bool? IsCaptured { get; init; }
        [JsonPropertyName("capturedAmount")] public decimal? CapturedAmount { get; init; }
        // POK's own status string ("created", "expired", "paid", …) — diagnostics only, never branch on it.
        [JsonPropertyName("status")] public string? Status { get; init; }
   ```

3. **`MapStatus`** (replace lines 156–171, doc comment included):
   ```csharp
    /// <summary>
    /// Maps POK's sdkOrder flags onto the normalized enum. Held (isConfirmed && canBeCaptured) is
    /// checked BEFORE anything that reads as paid, as POK's own merchant dashboard does (verified
    /// 2026-10-07) — isCompleted alone is never treated as "money moved", because POK may set it on
    /// an authorized-but-uncaptured order. Captured requires positive evidence (isCaptured, or
    /// capturedAmount > 0). The created/expired/paid/partial-capture fixtures are real staging
    /// shapes; the held fixture is real once the release gate's T1 records it; isCompleted-only ->
    /// Failed is provisional until the gate's aging probe. See
    /// docs/payments/runbook-studio-currency-migration-2026-09-27.md §3.1.
    /// </summary>
    private static PaymentProviderStatus MapStatus(SdkOrderDto order) => order switch
    {
        { IsRefunded: true } => PaymentProviderStatus.Refunded,
        { IsCanceled: true } => PaymentProviderStatus.Canceled,
        // Held: still capturable. Treat a missing isConfirmed as true (field absent on older shapes).
        { CanBeCaptured: true, IsConfirmed: not false } => PaymentProviderStatus.Authorized,
        { IsCaptured: true } => PaymentProviderStatus.Captured,
        { CapturedAmount: > 0m } => PaymentProviderStatus.Captured,
        // Completed, but neither capturable nor captured: a lapsed or voided hold. Never read it as
        // money received. PROVISIONAL — Phase 7's aging probe records the real lapsed-hold shape.
        { IsCompleted: true } => PaymentProviderStatus.Failed,
        _ => PaymentProviderStatus.Pending,
    };
   ```

4. **`GetStatusAsync`** logs the flag set (replace lines 118–124):
   ```csharp
    public async Task<PaymentProviderStatus?> GetStatusAsync(Guid studioId, string providerReferenceId, CancellationToken ct)
    {
        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        SdkOrderDto? order = await GetOrderAsync(http, token, merchantId, providerReferenceId, studioId, ct);
        if (order is null)
            return null;

        PaymentProviderStatus mapped = MapStatus(order);
        // First 8 characters only: POK's unauthenticated public GET serves the whole order
        // (merchant contact details included) to anyone holding the full id.
        string orderRef = providerReferenceId.Length > 8 ? providerReferenceId[..8] : providerReferenceId;
        // Flags, amounts and POK's own status string only — never the raw body (it carries the
        // merchant's contact details and card/billing data; CLAUDE.md rule 3).
        logger.LogInformation(
            "POK order {OrderRef} for studio {StudioId}: confirmed={IsConfirmed} canBeCaptured={CanBeCaptured} captured={IsCaptured} completed={IsCompleted} canceled={IsCanceled} refunded={IsRefunded} amount={Amount} finalAmount={FinalAmount} capturedAmount={CapturedAmount} currency={Currency} pokStatus={PokStatus} mapped={Mapped}",
            orderRef, studioId, order.IsConfirmed, order.CanBeCaptured, order.IsCaptured,
            order.IsCompleted, order.IsCanceled, order.IsRefunded, order.Amount, order.FinalAmount,
            order.CapturedAmount, order.CurrencyCode, order.Status, mapped);

        if (mapped == PaymentProviderStatus.Failed)
            logger.LogWarning(
                "POK order {OrderRef} for studio {StudioId} is completed but neither capturable nor captured (pokStatus={PokStatus}); treating the hold as lapsed.",
                orderRef, studioId, order.Status);

        return mapped;
    }
   ```
   **When it fires.** The log is `Information` on purpose. It fires only when the app asks POK for a
   status:
   - on confirm, resume, saved-card checkout and switching to cash;
   - once per held card payment (and per pending one, if B1 ships) in every reconciliation run
     (daily at 02:00 UTC, plus one run per webhook call);
   - once per pending gift-card or package purchase in their daily jobs.

   At today's volume that is small, and the line is the gate's main evidence channel. It is also
   useful in production.

   **How to read it.** The console sink writes compact JSON (`CompactJsonFormatter`,
   `Pena_e_Arte.API/appsettings.json` lines 115–121). Each line carries:
   - the template in `@mt`;
   - each value as a top-level property: `"OrderRef"`, `"IsConfirmed"`, `"CanBeCaptured"`, …,
     `"Mapped"`.

   Read the values from those properties. The rendered `confirmed=… mapped=…` sentence does not
   appear in the output.

   Check one line's format before you rely on it:
   - compose runs the API as `Development` (`docker-compose.yml` line 164);
   - Phi's untracked `Pena_e_Arte.API/appsettings.Development.json` is gitignored but not
     dockerignored, so it can override Serilog inside the image.

   Match on the property values, whichever format you get.

5. **Update the class doc comment** (lines 18–29). Unverified item 2 is now "verified against POK's
   own dashboard precedence; confirmed on staging by the 2026-10-0X gate (runbook §3.1)". Fill in the
   date in Phase 8. Leave item 1 (amount unit) until Phase 4 has proven it.

**Side effect to expect, and leave alone.** If POK's held shape has `isCompleted: true` (shape A),
gift-card and package orders that a client authorises now read as `Authorized`, not `Captured`. Those
purchases then stay `Pending` instead of activating without the money ever being captured. That is
the fail-safe direction (Finding 6). Under shape B today's mapping already returns `Authorized`, so
nothing changes. Note which case applied in the report.

### 9.3 Tests (`tests/Pena_e_Arte.UnitTests/Services/PokPaymentProviderTests.cs`)

- `CreateSut` (line 60): `return new PokPaymentProvider(authClient, NullLogger<PokPaymentProvider>.Instance);`
- Replace the `GetStatusAsync_MapsDocumentedFlagsToNormalizedStatus` theory (lines 174–194) with
  `GetStatusAsync_MapsRealPokFlagShapesToNormalizedStatus`. Use these fixtures. The first four are
  E1 rows, sanitised to flags only (Appendix C keeps the same shapes). The two held shapes and the
  last five rows are synthetic until T1 and Phase 7 replace or confirm them:

| Fixture (inside `{"data":{"sdkOrder":{…}}}`) | Expected |
|---|---|
| `"autoCapture":false,"isConfirmed":false,"canBeCaptured":false,"isCaptured":false,"isCompleted":false,"isCanceled":false,"isRefunded":false,"capturedAmount":0,"status":"created"` | `Pending` |
| `"autoCapture":true,"isConfirmed":false,"canBeCaptured":false,"isCaptured":false,"isCompleted":false,"isCanceled":false,"isRefunded":false,"capturedAmount":0,"status":"expired"` | `Pending` |
| `"autoCapture":true,"isConfirmed":true,"canBeCaptured":false,"isCaptured":true,"isCompleted":true,"isCanceled":false,"isRefunded":false,"capturedAmount":100,"status":"paid"` | `Captured` |
| `"autoCapture":false,"isConfirmed":true,"canBeCaptured":false,"isCaptured":true,"isCompleted":true,"isCanceled":false,"isRefunded":false,"capturedAmount":60,"status":"paid"` | `Captured` |
| held, shape A: `"autoCapture":false,"isConfirmed":true,"canBeCaptured":true,"isCaptured":false,"isCompleted":true,"capturedAmount":0` | `Authorized` |
| held, shape B: `"autoCapture":false,"isConfirmed":true,"canBeCaptured":true,"isCaptured":false,"isCompleted":false,"capturedAmount":0` | `Authorized` |
| `"isRefunded":true` | `Refunded` |
| `"isCanceled":true` | `Canceled` |
| `"canBeCaptured":true` (no `isConfirmed`) | `Authorized` |
| `"isCompleted":true` (no capture evidence) | `Failed` |
| `{}` | `Pending` |

- Add `GetStatusAsync_HeldOrderWithIsCompleted_IsNotReportedAsCaptured`. It is a named regression
  test for Finding 1, using held shape A.
- Add `GetStatusAsync_NullUndocumentedFields_DoNotThrow`, with
  `"isConfirmed":null,"isCaptured":null,"capturedAmount":null,"status":null`.
- After Phase 4, replace the two "held, shape A/B" rows with the **one shape actually observed** in
  T1, labelled "observed on staging 2026-10-DD". Keep the other shape only if POK's response varies.
- Run all the unit tests:
  ```bash
  dotnet test tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj
  ```
  `PaymentArchitectureTests` must stay green.

**Help sync for Phase 1.** No Help text changes in this phase, and that is deliberate:
- the manual already explains the held state (`ref-deposit-statuses`: "Captured — Card authorised —
  funds reserved but not yet finalized") and the Capture button (`owner-payment-detail`);
- the in-app `owner-payments` article still lacks this guidance. Phase 8 adds it once T1–T3 have
  verified the behaviour.

The CI help-sync gate does not fire here, because `Pena_e_Arte.Infrastructure/**` and `tests/**`
are not gated paths.

**Benchmark note.** Mirror the provider's own lifecycle rather than invent one:
- Stripe separates `requires_capture` from `succeeded`, and labels the held state "Uncaptured" in its
  Dashboard;
- POK labels it "On hold".

Treating "completed" as "captured" would collapse two states that every major processor keeps apart
(§20).

**Commit:**
```
fix(payments): POK status mapping follows POK's own held-before-paid precedence; log flag sets

MapStatus checked isCompleted before canBeCaptured, so a held (authorised, uncaptured) POK order
could map to Captured and be marked Paid without ever being captured. Precedence now mirrors POK's
merchant dashboard (isConfirmed && canBeCaptured = On hold, checked first), and Captured requires
isCaptured or capturedAmount > 0. GetStatusAsync logs the flag set (no body, no PII) as gate
evidence. The created, expired, paid and partial-capture fixtures are real staging shapes; the held
fixtures are provisional until T1. Runbook §3.1.
```

---

## 10. Phase 2 — Money-path fixes B1–B6 (autonomous; build only the items Phi approved at C0)

One commit per item. Each item lists the code as it is, the code it should become, tests, Help
sync and a benchmark note. If Phi declined an item, skip it. The matching Phase 4 test then runs as
an observation, and the item moves to the §17 backlog doc.

### B1 — Reconciliation checks POK before touching a pending payment (Finding 2)

**Current:** the whole of `Pena_e_Arte.Infrastructure/Jobs/PaymentReconciliationJob.cs`
(118 lines). `RunAsync` runs `ReconcileCapturedAsync`, then `CancelStalePendingAsync` (pending card
payments whose appointment date is more than 3 days ago: `CancelAsync`, then `Failed`), then
`ReleaseExpiredHoldsAsync` (pending card payments with `HoldExpiresAt < now`: `CancelAsync`, then
`Failed`). Neither cancel pass asks the provider first.

**Target:** replace the file with the version below. B2's held-payment changes are folded in, since
both items rewrite the same class. If Phi approved only one of B1/B2, keep only that item's half:
`ReconcilePendingAsync` is B1's, and `ReconcileHeldAsync` (including its cancelled-appointment
release) plus `MarkPaid` are B2's.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Source of truth for Flow A card payment state (ADR-0001: webhooks and widget callbacks are
/// triggers, never facts). Every pass asks the provider BEFORE changing local state; no pass ever
/// cancels a hold the client has authorised on a live appointment (a hold authorised on a CANCELLED
/// appointment is released). Runs daily (Program.cs, Cron.Daily(2)) and on every POK webhook
/// (JobScheduler.TriggerPaymentReconciliationNow).
/// </summary>
public class PaymentReconciliationJob(IAppDbContext db, IPaymentProvider paymentProvider, ILogger<PaymentReconciliationJob> logger)
{
    /// <summary>Unpaid card payments on appointments more than this far in the past are abandoned.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    public async Task RunAsync(CancellationToken ct = default)
    {
        await ReconcileHeldAsync(ct);
        await ReconcilePendingAsync(ct);
    }

    /// <summary>
    /// Held deposits (PaymentStatus.Captured = authorised, not yet captured): promote to Paid when the
    /// provider reports the capture happened elsewhere (e.g. in the POK dashboard); mark Failed when
    /// the provider reports the hold is gone, so "Capture deposit" stops offering an impossible action;
    /// release a hold left on a CANCELLED appointment (unless it is a forfeit, which stays held).
    /// </summary>
    private async Task ReconcileHeldAsync(CancellationToken ct)
    {
        List<Payment> held = await db.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Appointment)
            .Where(p => p.Status == PaymentStatus.Captured
                     && p.Method == ClientPaymentMethod.Card
                     && p.ProviderReferenceId != null
                     && p.DeletedAt == null)
            .ToListAsync(ct);

        foreach (Payment payment in held)
        {
            try
            {
                PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
                    payment.StudioId, payment.ProviderReferenceId!, ct);
                DateTime now = DateTime.UtcNow;

                switch (status)
                {
                    case PaymentProviderStatus.Captured:
                        MarkPaid(payment, now);
                        if (payment.Appointment.Status == AppointmentStatus.Cancelled)
                            logger.LogWarning(
                                "Held deposit {PaymentId} for studio {StudioId} was captured at the provider on a cancelled appointment; refund it manually.",
                                payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Canceled or PaymentProviderStatus.Failed:
                        payment.Status = PaymentStatus.Failed;
                        payment.UpdatedAt = now;
                        logger.LogWarning(
                            "Held deposit {PaymentId} for studio {StudioId} lost its provider hold ({ProviderStatus}); marked Failed.",
                            payment.Id, payment.StudioId, status);
                        break;
                    case PaymentProviderStatus.Authorized
                        when payment.Appointment.Status == AppointmentStatus.Cancelled
                          && payment.Appointment.DepositStatus != DepositStatus.Forfeited:
                        // The cancellation saw a Pending payment, so it settled nothing; then a heal path
                        // (ConfirmCardPaymentCommand, CreateDepositPaymentCommand's resume branch, which
                        // the saved-card flow shares, or DeclareCashDepositCommand) moved the order to
                        // held without checking the appointment. Release it, exactly like
                        // ReconcilePendingAsync does. A B3 forfeit whose capture POK refused is Forfeited
                        // and stays held for the owner to capture.
                        await paymentProvider.CancelAsync(payment.StudioId, payment.ProviderReferenceId!, ct);
                        payment.Status = PaymentStatus.Refunded;
                        payment.RefundedAmount = payment.Amount;
                        payment.UpdatedAt = now;
                        payment.Appointment.DepositStatus = DepositStatus.Refunded;
                        payment.Appointment.UpdatedAt = now;
                        logger.LogWarning(
                            "Held deposit {PaymentId} for studio {StudioId} was on a cancelled appointment; hold released.",
                            payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Authorized:
                        break; // still held
                    default:
                        logger.LogWarning(
                            "Held deposit {PaymentId} for studio {StudioId} has unexpected provider status {ProviderStatus}; left unchanged.",
                            payment.Id, payment.StudioId, status);
                        break;
                }

                // Save per payment, like ReconcilePendingAsync: a provider side effect (the release
                // above) is never left unrecorded because a later payment's save failed.
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One studio's provider failure must not abort reconciliation for every other studio.
                logger.LogError(ex, "Failed to reconcile held payment {PaymentId} for studio {StudioId}.", payment.Id, payment.StudioId);
            }
        }
    }

    /// <summary>
    /// Pending card payments: ask the provider FIRST. Heal anything the client already authorised or
    /// paid on a live appointment (the widget's confirm call never ran — closed tab, dropped
    /// connection, 3DS redirect). Release anything authorised on a cancelled appointment. Only then
    /// cancel unpaid orders that are past HoldExpiresAt (legacy rows without one: on an appointment
    /// more than three days in the past) or whose appointment was cancelled. Replaces the former
    /// CancelStalePending/ReleaseExpiredHolds passes, which cancelled authorised holds without checking.
    /// </summary>
    private async Task ReconcilePendingAsync(CancellationToken ct)
    {
        List<Payment> pending = await db.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Appointment)
            .Where(p => p.Status == PaymentStatus.Pending
                     && p.Method == ClientPaymentMethod.Card
                     && p.ProviderReferenceId != null
                     && p.DeletedAt == null)
            .ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        DateTime staleCutoff = now - StaleAfter;

        foreach (Payment payment in pending)
        {
            string providerReferenceId = payment.ProviderReferenceId!;
            bool appointmentCancelled = payment.Appointment.Status == AppointmentStatus.Cancelled;
            // HoldExpiresAt is authoritative when set (every deposit; every payment link after B4). The
            // appointment-date rule only covers legacy rows without one — a brand-new link for a past
            // appointment must not be cancelled minutes after it was created.
            bool expiredOrStale = payment.HoldExpiresAt is { } expires
                ? expires < now
                : payment.Appointment.Date < staleCutoff;
            bool releasable = expiredOrStale || appointmentCancelled;
            try
            {
                PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
                    payment.StudioId, providerReferenceId, ct);

                // Provider side effects first. They target THIS order only, so they are right even if
                // B4 has meanwhile released it and minted a fresh order on the same row (B4 releases
                // the old order too).
                if ((status == PaymentProviderStatus.Pending && releasable)
                    || (status == PaymentProviderStatus.Authorized && appointmentCancelled))
                    await paymentProvider.CancelAsync(payment.StudioId, providerReferenceId, ct);

                // Then re-read right before mutating: never apply this order's verdict to a newer order.
                string? currentReference = await db.Payments
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(p => p.Id == payment.Id)
                    .Select(p => p.ProviderReferenceId)
                    .SingleAsync(ct);
                if (currentReference != providerReferenceId)
                    continue;

                switch (status)
                {
                    case PaymentProviderStatus.Authorized when appointmentCancelled:
                        // Authorised after (or racing) the cancellation: the hold was released above;
                        // never heal it to held. Same outcome as cancelling a held deposit with a
                        // full refund (B3). Deliberate: the late-cancel policy is NOT applied here.
                        // The cancellation saw a Pending payment, so it applied no policy, and this
                        // job can't apply one retroactively. The client-favourable release is
                        // recorded in the backlog doc (Phase 8.8).
                        payment.Status = PaymentStatus.Refunded;
                        payment.RefundedAmount = payment.Amount;
                        payment.UpdatedAt = now;
                        payment.Appointment.DepositStatus = DepositStatus.Refunded;
                        payment.Appointment.UpdatedAt = now;
                        logger.LogWarning(
                            "Pending payment {PaymentId} for studio {StudioId} was authorised on a cancelled appointment; hold released.",
                            payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Authorized:
                        payment.Status = PaymentStatus.Captured; // held — the owner captures it
                        payment.UpdatedAt = now;
                        logger.LogInformation(
                            "Pending payment {PaymentId} for studio {StudioId} was authorised at the provider; healed to held.",
                            payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Captured:
                        MarkPaid(payment, now);
                        if (appointmentCancelled)
                            logger.LogWarning(
                                "Payment {PaymentId} for studio {StudioId} was captured at the provider on a cancelled appointment; refund it manually.",
                                payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Pending when releasable:
                        payment.Status = PaymentStatus.Failed; // the unpaid order was cancelled above
                        payment.UpdatedAt = now;
                        break;
                    case PaymentProviderStatus.Canceled or PaymentProviderStatus.Failed:
                    case null when releasable:
                        payment.Status = PaymentStatus.Failed; // nothing left to cancel at the provider
                        payment.UpdatedAt = now;
                        break;
                    case PaymentProviderStatus.Refunded:
                        logger.LogWarning(
                            "Pending payment {PaymentId} for studio {StudioId} is refunded at the provider; left unchanged for manual review.",
                            payment.Id, payment.StudioId);
                        break;
                    // Pending or null, not yet releasable: leave for a later run.
                }

                // Save per payment: only DB work (no provider call) between the re-read and this write.
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to reconcile pending payment {PaymentId} for studio {StudioId}.", payment.Id, payment.StudioId);
            }
        }
    }

    private static void MarkPaid(Payment payment, DateTime now)
    {
        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = now;
        payment.UpdatedAt = now;
        // A forfeited deposit that is captured later (after B3's failed forfeit capture) stays
        // Forfeited: that is the more informative state. This covers a capture made in the POK
        // dashboard; B3 adds the same guard to CaptureDepositCommand for the in-app button.
        if (payment.Appointment.DepositStatus != DepositStatus.Forfeited)
            payment.Appointment.DepositStatus = DepositStatus.Paid;
        payment.Appointment.UpdatedAt = now;
    }
}
```

Points to watch:
- **The `Include` needs real appointment rows.** `Include(p => p.Appointment)` is a required
  navigation, so EF uses inner-join semantics: a payment whose appointment row is missing drops out
  of the pass. The test helpers in `PaymentReconciliationJobTests.cs` need two changes:
  - `SeedPayment` (lines 173–191) seeds a random `AppointmentId` with no appointment row when none
    is passed. Make it seed one: `AppointmentId = appointmentId ?? await SeedAppointment(DateTime.UtcNow.AddDays(5))`.
    Otherwise the held tests fail for the wrong reason.
  - `SeedAppointment` (lines 193–209) hard-codes `Status = AppointmentStatus.Completed` and
    `DepositStatus = DepositStatus.Paid` (lines 203–204). An assertion that `MarkPaid` set
    `DepositStatus.Paid` would then pass even if `MarkPaid` never touched the appointment. Give it
    parameters `DepositStatus depositStatus = DepositStatus.Pending` and
    `AppointmentStatus status = AppointmentStatus.Confirmed`. No existing test asserts either value.
  - Assert `DepositStatus.Paid` only on an appointment seeded `Pending`.
- **Provider-call volume.** The pending pass now asks POK about **every** pending card payment on
  every run, not only stale or expired ones, so that it can heal missed confirmations.
  - The old passes filtered in SQL and made no GET at all; only the held pass did.
  - Runs happen daily at 02:00 UTC, plus one per webhook call (Finding 8: up to 20 a minute from
    anonymous callers).
  - So each run costs about one GET per in-flight Flow A payment. C-5's debounce caps the webhook
    side.
- **Tenant filters.** The two list queries keep `IgnoreQueryFilters()` plus `DeletedAt == null`,
  like today's job; the re-read is by primary key. It is in the same job, so it stays within
  approved usage #36 (`architecture.md` line 1164). It is not a new bypass under §7.
- **Order of work in the pending pass:** ask POK, release the old order at POK if needed, re-read
  `ProviderReferenceId`, mutate, save. Saves are per payment, so only two DB round trips separate
  the re-read from the write; no provider call sits in that window.
  - **Residual race:** B4 re-minting inside that window. EF only writes the modified columns, so
    the row would end up with B4's new order and this run's `Failed`.
  - Closing it fully needs one of: a concurrency token (a migration); a conditional
    `ExecuteUpdateAsync` keyed on the old `ProviderReferenceId`, which `FakeDbContext`'s EF InMemory
    provider can't run; or `[DisableConcurrentExecution]` plus a Redis lock shared with
    `ResolveOrCreateHoldAsync`. Record it under C-5; add none of them in this run.
- **One failed save spoils the rest of the run.** If a payment's `SaveChangesAsync` throws, that
  payment stays `Modified` in the job's shared context, so every later save in the same run retries
  it and fails too. `IAppDbContext` exposes no `ChangeTracker` to detach it. The next run starts with
  a fresh context, so nothing is lost for good. Record it under C-5.
- **The held pass also releases a hold on a cancelled appointment.** A cancellation that finds a
  `Pending` payment settles nothing. Three heal paths can then move the order to held without
  checking the appointment:
  - `ConfirmCardPaymentCommand` (lines 52–56);
  - `CreateDepositPaymentCommand`'s resume branch (lines 101–106), which the saved-card flow shares
    (`PayDepositWithSavedCardCommand.cs` line 46);
  - `DeclareCashDepositCommand` (lines 58–62).

  The held pass releases such a hold unless the deposit is `Forfeited` (B3's refused forfeit
  capture, which must stay capturable). It also saves per payment now, like the pending pass, and
  logs a "refund it manually" warning when POK reports a capture on a cancelled appointment.

**Tests** (`tests/Pena_e_Arte.UnitTests/Jobs/PaymentReconciliationJobTests.cs`):
- **Update** `RunAsync_PendingPaymentOnPastAppointment_CancelsAndMarksFailed` and
  `RunAsync_PendingCardHoldPastExpiry_CancelsAndMarksFailed`: stub `GetStatusAsync` to return
  `PaymentProviderStatus.Pending`. They must still cancel and mark the payment `Failed`.
  - Unstubbed, NSubstitute returns `null`. The payment would then be marked `Failed` without a
    `CancelAsync` call, which fails their `Received(1).CancelAsync` assertion.
- **Keep** `RunAsync_PendingPaymentOnFutureAppointment_DoesNotCancel`,
  `RunAsync_PendingPaymentWithin3DayCutoff_DoesNotCancel` and
  `RunAsync_PendingCardHoldNotYetExpired_DoesNotCancel` (stub `Pending`).
- **Add:**
  - `RunAsync_PendingPaymentAuthorizedAtProvider_HealsToHeldAndNeverCancels`, run with an expired
    `HoldExpiresAt`: expect `Status == Captured` and `CancelAsync` *not* received. This is the core
    regression for Finding 2.
  - `RunAsync_PendingPaymentAuthorizedOnCancelledAppointment_ReleasesHold`: appointment seeded
    `Cancelled`, provider `Authorized`. Expect:
    - `CancelAsync` received once;
    - `Status == Refunded`, `RefundedAmount == Amount`;
    - `DepositStatus == Refunded`.
  - `RunAsync_PendingUnpaidOnCancelledAppointment_CancelsOrderBeforeExpiry`: provider `Pending`,
    `HoldExpiresAt` in the future, appointment `Cancelled`. Expect `CancelAsync` received and
    `Status == Failed`.
  - `RunAsync_PendingPaymentCapturedAtProvider_MarksPaidAndDepositPaid` (appointment seeded with
    `DepositStatus.Pending`).
  - `RunAsync_PendingPaymentCanceledAtProvider_MarksFailedWithoutCancelCall`.
  - `RunAsync_PendingPaymentNotFoundAndExpired_MarksFailedWithoutCancelCall`.
  - `RunAsync_PendingPaymentNotFoundNotExpired_LeftPending`.
  - `RunAsync_PendingPaymentRefundedAtProvider_LeftUnchanged`.
  - `RunAsync_PendingLinkWithFutureExpiryOnPastAppointment_NotCancelled`: `HoldExpiresAt` = now
    + 12 h, appointment 5 days ago, provider `Pending`. Expect no `CancelAsync` and `Status ==
    Pending`.
  - `RunAsync_PendingPaymentReMintedDuringRun_IsLeftAlone`. How to build it:
    - seed the payment releasable: `HoldExpiresAt` = now − 5 min. Stub `GetStatusAsync` to return
      `Pending`. Without the re-read guard the job would then mark the row `Failed`, so the test
      proves the guard;
    - build the SUT on `FakeDbContext.Create(dbName)`;
    - in the `GetStatusAsync` stub, open a second `FakeDbContext.Create(dbName)` and change the
      row's `ProviderReferenceId`;
    - expect `CancelAsync` received exactly once, for the **old** reference (releasing the old
      order is correct either way), never for the new one;
    - expect the row still `Pending` with the new reference. Read it back with a fresh
      `FakeDbContext.Create(dbName)`: the job's own context still tracks the old reference. (If you
      use `.AsNoTracking()` instead, add `using Microsoft.EntityFrameworkCore;`, which this test file
      doesn't import today.)
  - `RunAsync_HeldPaymentCapturedOnForfeitedAppointment_KeepsForfeited`: appointment seeded
    `DepositStatus.Forfeited`, provider `Captured`. Expect `Status == Paid`, `PaidAt` set and
    `DepositStatus == Forfeited`.
  - `RunAsync_HeldPaymentAuthorizedOnCancelledAppointment_ReleasesHold`: a `Captured` payment,
    appointment seeded `Cancelled` with `DepositStatus.Pending`, provider `Authorized`. Expect
    `CancelAsync` received once, `Status == Refunded`, `RefundedAmount == Amount` and
    `DepositStatus == Refunded`. This is the cancel-then-heal race: `CancelAppointmentCommand`
    settles nothing for a `Pending` payment (lines 101–103), and a heal path (see "Points to
    watch") then moves it to held without checking the appointment.
  - `RunAsync_HeldForfeitedDepositOnCancelledAppointment_StaysHeld`: the same, but with
    `DepositStatus.Forfeited`. Expect no `CancelAsync` and `Status == Captured`.
  - `RunAsync_OneStudioProviderThrows_OtherStudiosStillReconciled`. `SeedPayment` hard-codes
    `StudioId = _studioId` (line 180), so give it a `Guid? studioId = null` parameter first.

**Help sync** (wording: §16, Phase 8.3; Phase 8 checks it and does not add it again):
- `helpContent.ts` `owner-payments` gets the B1 tip: "If a client's card was authorised but they
  closed the page before it confirmed, the payment updates by itself, at the latest overnight."
  - Don't promise "within moments": no deployed environment sends a `webhookUrl` yet (E11). That
    sentence belongs in the C-2 change.
- The manual's `owner-payments` section gets the same line as a tip callout.
- Tours: no new step. No new screen or element.

Because this commit is Infrastructure-only, the CI gate wouldn't force the Help change. Make it
anyway: rule 7 requires it.

**Benchmark note.** Stripe's guidance is the same principle: webhooks can arrive late, out of order
or twice, so retrieve the object from the API and reconcile, never trust a single event (§20). A job
that cancels an authorised hold without asking falls behind that standard.

**Commit:** `fix(payments): reconciliation asks POK before cancelling a pending card payment; heals authorised/paid ones`

### B2 — Held-deposit reconciliation sets `DepositStatus` and handles a dead hold (Finding 3)

Folded into B1's file above: see `ReconcileHeldAsync` and `MarkPaid`. If B1 was declined but B2
approved, apply the same changes to the existing `ReconcileCapturedAsync`:
- `Include(p => p.Appointment)`, and set `Appointment.DepositStatus = Paid` on promotion, except
  when it is `Forfeited` (`MarkPaid`'s guard);
- on `Canceled` or `Failed`, set the payment to `Failed` and log a `Warning`;
- on `Authorized` with `Appointment.Status == Cancelled` and `DepositStatus != Forfeited`:
  `CancelAsync`, then `Refunded`, `RefundedAmount = Amount` and `DepositStatus = Refunded`;
- on `Captured` with a cancelled appointment, the "refund it manually" warning;
- save per payment inside the `try`;
- B1's `SeedPayment`/`SeedAppointment` helper changes in `PaymentReconciliationJobTests.cs` (see
  "Points to watch" above). Without them the new `Include` silently drops the seeded payments.

**Tests:**
- `RunAsync_HeldPaymentCapturedAtProvider_SetsDepositStatusPaid`. The appointment is seeded with
  `DepositStatus.Pending`, the new `SeedAppointment` default.
- `RunAsync_HeldPaymentAuthorizedOnCancelledAppointment_ReleasesHold` and
  `RunAsync_HeldForfeitedDepositOnCancelledAppointment_StaysHeld` (specified under B1's tests; they
  belong to this half if only B2 ships).
- `RunAsync_HeldPaymentCanceledAtProvider_MarksFailed`
- `RunAsync_HeldPaymentFailedAtProvider_MarksFailed`
- Don't add a held-payment "Authorized → unchanged" test: the existing
  `RunAsync_CapturedPaymentProviderAuthorized_DoesNotMarkAsPaid` (line 47) already covers it. Keep
  it green.

**Help sync** (wording: §16, Phase 8.3): `owner-payments` tip: "If POK reports that a held
deposit's authorisation expired or was cancelled, the payment shows Failed and the client can pay
again." Add the same tip to the manual's `owner-payment-detail` section. "The client can pay again"
is true: `CreateDepositPaymentCommand` treats a `Failed` payment as convertible and mints a fresh
hold on the same row.

**Benchmark note.** Stripe moves an expired, uncaptured authorisation to `canceled` (§20). Showing a
dead hold as still capturable invites an action that cannot succeed.

**Commit:** `fix(payments): held-deposit reconciliation marks the deposit Paid on capture and Failed when the hold is gone`

### B3 — Cancelling an appointment releases or partly captures a held deposit instead of "refunding" it (Finding 4)

**Interface.** Add one method to `Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs`, after the
existing `CaptureAsync` (line 66):

```csharp
    /// <summary>Captures part of a previously-authorized hold (at most the authorized amount); the
    /// provider releases the uncaptured remainder. Amount is major units, already rounded to the
    /// currency's ISO 4217 minor unit — the same convention as PaymentHoldRequest.Amount; the
    /// provider converts to its own wire format.</summary>
    Task CaptureAsync(Guid studioId, string providerReferenceId, decimal amount, string currency, CancellationToken ct);
```

**`PokPaymentProvider`** — add this next to the existing `CaptureAsync` (lines 85–104):

```csharp
    public async Task CaptureAsync(Guid studioId, string providerReferenceId, decimal amount, string currency, CancellationToken ct)
    {
        if (amount <= 0m)
            throw new BusinessRuleViolationException("Capture amount must be greater than zero.");

        (string merchantId, string token, HttpClient http) = await authClient.PrepareRequestAsync(studioId, ct);

        using HttpRequestMessage req = new(
            HttpMethod.Post, $"{authClient.BaseUrl}/merchants/{merchantId}/sdk-orders/{providerReferenceId}/capture");
        req.Headers.Authorization = new("Bearer", token);
        // Same unit conversion as the hold itself — this is the second amount-unit-sensitive write
        // path the gate verifies (Phase 4, T5b).
        decimal pokAmount = ToPokAmount(amount, currency);
        req.Content = JsonContent.Create(new { amount = pokAmount });

        CreateOrderResponse? result = await authClient.SendAsync<CreateOrderResponse>(http, req, studioId, ct, allow409: true);
        if (result is not null)
            return;

        // 409 = POK says the order was already captured, possibly IN FULL ("Capture deposit",
        // Complete, No-show or the POK dashboard; POK allows one capture per order, E6). Accept it
        // only as an idempotent retry of this same partial capture. Otherwise the caller would
        // record a released remainder that was in fact charged.
        SdkOrderDto? order = await GetOrderAsync(http, token, merchantId, providerReferenceId, studioId, ct);
        if (order?.CapturedAmount == pokAmount)
            return;

        string orderRef = providerReferenceId.Length > 8 ? providerReferenceId[..8] : providerReferenceId;
        logger.LogWarning(
            "POK order {OrderRef} for studio {StudioId} was already captured (capturedAmount={CapturedAmount}); refused a partial capture of {Amount}.",
            orderRef, studioId, order?.CapturedAmount, pokAmount);
        throw new ServiceUnavailableException("POK request failed (409): the order was already captured.");
    }
```

The existing full-capture method's comment (lines 89–91) justifies not widening the interface
because "we always capture in full", which this overload makes false. Replace those three lines
with:

```csharp
        // Full capture: fetch the order for its authorised total. A partial capture (the late-cancel
        // policy, B3) uses the overload that takes an amount.
```

`CapturedAmount` is the nullable DTO field from §9.2 item 2, which ships even if Phi vetoes the new
`MapStatus`. A null (field absent, or the order not found) never matches, so it refuses. That is the
safe direction: the caller refuses the cancellation (503), reconciliation (B2) later promotes the
payment to `Paid`, and a retried cancellation then takes the existing `Paid` partial-refund branch.

**`NullPaymentProvider`** — add:

```csharp
    public Task CaptureAsync(Guid studioId, string providerReferenceId, decimal amount, string currency, CancellationToken ct) => throw NotConfigured();
```

**`CancelAppointmentCommand.cs`.** Lines 101–133 currently read:

```csharp
            if (payment.Method == ClientPaymentMethod.Card
                && !string.IsNullOrEmpty(payment.ProviderReferenceId)
                && (payment.Status == PaymentStatus.Captured || payment.Status == PaymentStatus.Paid))
            {
                int refundPercent = isClient
                    ? await ResolveClientRefundPercentAsync(appointment, ct)
                    : 100;

                if (refundPercent >= 100)
                {
                    await paymentProvider.RefundAsync(payment.StudioId, payment.ProviderReferenceId, null, payment.Currency, ct);
                    payment.Status = PaymentStatus.Refunded;
                    payment.RefundedAmount = payment.Amount;
                    appointment.DepositStatus = DepositStatus.Refunded;
                }
                else if (refundPercent > 0)
                {
                    decimal refundAmount = CurrencyCatalog.Round(
                        appointment.DepositAmount * refundPercent / 100m, payment.Currency);
                    await paymentProvider.RefundAsync(payment.StudioId, payment.ProviderReferenceId, refundAmount, payment.Currency, ct);
                    payment.Status = PaymentStatus.Refunded;
                    payment.RefundedAmount = refundAmount;
                    appointment.DepositStatus = DepositStatus.Refunded;
                }
                else
                {
                    // Late cancellation, 0% refund configured — deposit is forfeited outright,
                    // no Stripe call: the studio keeps the already-authorized/captured amount.
                    appointment.DepositStatus = DepositStatus.Forfeited;
                }

                payment.UpdatedAt = DateTime.UtcNow;
            }
```

**Target.** Keep the `Paid` branches' code unchanged; only the 0% branch's comment is rewritten, as
shown. Route a **held** payment (`Status == Captured`) to a new private method first:

```csharp
                int refundPercent = isClient
                    ? await ResolveClientRefundPercentAsync(appointment, ct)
                    : 100;

                if (payment.Status == PaymentStatus.Captured)
                {
                    await SettleHeldDepositAsync(payment, appointment, refundPercent, ct);
                }
                else if (refundPercent >= 100)
                {
                    // … existing Paid → full refund branch, unchanged …
                }
                else if (refundPercent > 0)
                {
                    // … existing Paid → partial refund branch, unchanged …
                }
                else
                {
                    // Late cancellation, 0% refund — the deposit was already captured (Paid), so the
                    // studio keeps it with no provider call.
                    appointment.DepositStatus = DepositStatus.Forfeited;
                }

                payment.UpdatedAt = DateTime.UtcNow;
```

```csharp
    /// <summary>
    /// A held deposit (PaymentStatus.Captured = authorised, never captured) has nothing to refund:
    /// releasing the hold IS the refund, and keeping part of it means capturing only that part
    /// (POK: one capture per order, capped at the authorised amount — E6; T5b verifies that the
    /// remainder is released). Amounts are based on payment.Amount, the authorised amount. The
    /// refund share is rounded first, exactly like the Paid branch, so both paths round the same way.
    /// </summary>
    private async Task SettleHeldDepositAsync(
        Domain.Entities.Payment payment, Domain.Entities.Appointment appointment, int refundPercent, CancellationToken ct)
    {
        string providerReferenceId = payment.ProviderReferenceId!;
        decimal refund = refundPercent >= 100
            ? payment.Amount
            : CurrencyCatalog.Round(payment.Amount * Math.Max(refundPercent, 0) / 100m, payment.Currency);
        decimal keep = payment.Amount - refund;

        if (keep <= 0m)
        {
            // Full refund: release the hold.
            try
            {
                await paymentProvider.CancelAsync(payment.StudioId, providerReferenceId, ct);
            }
            catch (ServiceUnavailableException)
            {
                // POK answered, but refused the release. If it reports the hold already gone (lapsed,
                // cancelled, never paid or not found), there is nothing to release, so the
                // cancellation goes ahead. If the hold is still there or was already charged (e.g.
                // captured in the POK dashboard), rethrow: the cancellation is refused and retried,
                // never marked released on a guess. A failed status read also propagates.
                PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
                    payment.StudioId, providerReferenceId, ct);
                if (status is PaymentProviderStatus.Authorized or PaymentProviderStatus.Captured
                    or PaymentProviderStatus.Refunded)
                    throw;
                logger.LogWarning(
                    "POK refused to release the hold for payment {PaymentId} in studio {StudioId}, but reports it gone ({ProviderStatus}); cancelling anyway.",
                    payment.Id, payment.StudioId, status);
            }
            // Any other failure (studio not connected, POK unreachable or timed out) propagates:
            // 422/500, the cancellation is refused and the caller retries. Today's refund path for
            // an already-charged deposit behaves the same way.
            payment.Status = PaymentStatus.Refunded;
            payment.RefundedAmount = payment.Amount;
            appointment.DepositStatus = DepositStatus.Refunded;
            return;
        }

        if (refund <= 0m)
        {
            // Forfeit (0%): the studio keeps the whole deposit, which for a held deposit means
            // capturing it — the old code made no provider call, so the hold lapsed and nothing was
            // collected. If the capture fails for ANY provider reason (POK error, order gone, POK
            // unreachable or timed out, studio not connected), never block the cancellation: the
            // deposit stays on hold ("Capture deposit" still works), and B2's reconciliation promotes
            // it if POK did capture, or marks it Failed if the hold is gone. Only the caller's own
            // cancellation propagates. The try holds nothing but the provider call and two
            // property sets, so the broad catch hides nothing else.
            try
            {
                await paymentProvider.CaptureAsync(payment.StudioId, providerReferenceId, ct);
                payment.Status = PaymentStatus.Paid;
                payment.PaidAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Never pass ex: NotFoundException's message carries the full POK order id (§7, E3).
                logger.LogWarning(
                    "Forfeit capture failed ({ErrorType}) for payment {PaymentId} in studio {StudioId}; the deposit stays on hold for the owner to capture.",
                    ex.GetType().Name, payment.Id, payment.StudioId);
            }
            appointment.DepositStatus = DepositStatus.Forfeited;
            return;
        }

        // Partial: capture the share the studio keeps; POK releases the rest. A failure propagates
        // (503, or 422/500 when the studio isn't connected or POK is unreachable), so the
        // cancellation is refused and the client retries — the outcome Phi confirmed at C0 (D2).
        // Never fall back to a full capture: that would charge the client more than the policy
        // allows. If the hold is already gone, the client stays blocked until B2 marks the payment
        // Failed; if Phase 7 shows a lapsed hold reads as Pending, revisit D2.
        await paymentProvider.CaptureAsync(payment.StudioId, providerReferenceId, keep, payment.Currency, ct);
        payment.Status = PaymentStatus.Refunded;           // partial "refund" = the released remainder
        payment.PaidAt = DateTime.UtcNow;                  // revenue counts the kept share (PaidAt != null)
        payment.RefundedAmount = refund;
        appointment.DepositStatus = DepositStatus.Refunded;
    }
```

**`CaptureDepositCommand.cs` keeps a forfeit visible.** When POK refuses the forfeit capture, the
owner is pointed at "Capture deposit". Today that handler sets `DepositStatus = Paid`
unconditionally (lines 48–52), which would erase the forfeit. Give it the same guard as the job's
`MarkPaid`:

```csharp
        if (appointment is not null)
        {
            // A late-cancel forfeit whose capture POK refused at cancellation time (B3) stays
            // Forfeited when the owner captures it here — the same rule as PaymentReconciliationJob.MarkPaid.
            if (appointment.DepositStatus != DepositStatus.Forfeited)
                appointment.DepositStatus = DepositStatus.Paid;
            appointment.UpdatedAt = DateTime.UtcNow;
        }
```

**Reminder jobs move after the save (folds in §17, C-16).** B3 makes a refused cancellation (503)
an expected outcome, so the handler must not delete anything before it knows the cancellation will
stick. Move `jobs.CancelAppointmentJobs(appointment.ReminderJobId48h, appointment.ReminderJobId24h);`
from line 75 (and its comment on line 74) to just after `await db.SaveChangesAsync(ct);` (line 149).
- Nothing between those lines changes the two job ids.
- The waitlist claim (line 87) only mutates tracked entities, which the same save persists.
- No existing test asserts when the jobs are cancelled.

**Logger.** The handler gains `ILogger<CancelAppointmentHandler> logger` as its last constructor
parameter, with `using Microsoft.Extensions.Logging;`. Other Application handlers already take
loggers, e.g. `RegisterStudioCommand`. Update every construction site with
`NullLogger<CancelAppointmentHandler>.Instance`:
- `CancelAppointmentHandlerTests.cs` `CreateSut` (lines 30–31);
- `AppointmentHandlerIntegrationTests.cs` lines 143, 157, 175, 197 and 223;
- `WaitlistHandlerIntegrationTests.cs` line 129.

None of those three test files imports `Microsoft.Extensions.Logging.Abstractions` today. Add
`using Microsoft.Extensions.Logging.Abstractions;` to each. `CancelAppointmentHandlerTests.cs` (lines
1–9) also needs `using Microsoft.EntityFrameworkCore;` for `AsNoTracking()` and
`using NSubstitute.ExceptionExtensions;` for `ThrowsAsync`. Apart from the SDK's implicit usings
(`System`, `System.Linq`, `System.Threading.Tasks` and the like), the test project's only global
using is `Xunit`.

**If Phi named another outcome for the 1–99% failure case at C0,** implement that instead. **Never**
fall back to capturing the full deposit.

`PokPaymentProvider.CancelAsync` sends `cancellationReason = "Hold expired or superseded"`, which is
inaccurate for an appointment cancellation. Leave the wording; it's cosmetic. Do not widen the
interface to fix it.

**Tests** (`tests/Pena_e_Arte.UnitTests/Appointments/CancelAppointmentHandlerTests.cs`):
- **Rename and rewrite** `Handle_CapturedCardPayment_RefundsViaStripe` (~line 133) to
  `Handle_HeldCardPayment_StudioCancel_ReleasesHoldInsteadOfRefund`:
  - `CancelAsync` received once, `RefundAsync` never received;
  - `Status == Refunded`, `RefundedAmount == Amount`, `DepositStatus == Refunded`.
- **Add:**
  - `Handle_HeldCardPayment_ClientLateCancelZeroPercent_CapturesFullDeposit`: full `CaptureAsync`
    received, `Status == Paid`, `PaidAt` set, `DepositStatus == Forfeited`.
  - `Handle_HeldCardPayment_ClientLateCancelZeroPercent_CaptureFails_StillCancels`: a `[Theory]`
    over `TheoryData<Exception>` holding `new ServiceUnavailableException("POK request failed (409).")`,
    `new NotFoundException("PokOrder", "order-x")`, `new PaymentProviderNotConnectedException("Reconnect POK.")`,
    `new HttpRequestException()` and `new TaskCanceledException()` (a client timeout; the test's
    own token is never cancelled). `CaptureAsync` throws each one (`.ThrowsAsync(ex)`, from
    `NSubstitute.ExceptionExtensions`). Expect:
    - the appointment is `Cancelled`;
    - the payment is still `Captured`;
    - `DepositStatus == Forfeited`;
    - no exception reaches the caller.
  - `Handle_HeldCardPayment_StudioCancel_ReleaseRefusedButHoldGone_StillCancels`: `CancelAsync`
    throws `ServiceUnavailableException`, then `GetStatusAsync` returns `Canceled` (a second case
    returns `null`). Expect the appointment `Cancelled`, the payment `Refunded` with
    `RefundedAmount == Amount`, and `DepositStatus == Refunded`.
  - `Handle_HeldCardPayment_StudioCancel_ReleaseRefusedHoldStillThere_Throws`: `CancelAsync` throws
    `ServiceUnavailableException`, then `GetStatusAsync` returns `Captured` (a second case returns
    `Authorized`). Expect the exception to propagate and nothing saved: read the appointment back
    with `AsNoTracking()`, and it is not `Cancelled`.
  - `Handle_HeldCardPayment_ClientLateCancelFiftyPercent_CapturesHalf`: Amount 50 EUR →
    `CaptureAsync(studioId, ref, 25m, "EUR", …)`, `Status == Refunded`, `RefundedAmount == 25`,
    `PaidAt` set.
  - `Handle_HeldCardPayment_ClientLateCancelFiftyPercent_CaptureFails_Throws`: `CaptureAsync`
    throws `ServiceUnavailableException`. Expect:
    - the exception propagates;
    - nothing is saved. Read the appointment back with `AsNoTracking()`: the tracked entity was
      already mutated in memory, but the store must still show it as not `Cancelled`;
    - the reminders survive: `_jobs.DidNotReceiveWithAnyArgs().CancelAppointmentJobs(default, default);`.
  - `Handle_ValidCancel_CancelsReminderJobsAfterSave`: a plain `Pending` appointment with no
    payment, seeded with `ReminderJobId48h = "job-48"` and `ReminderJobId24h = "job-24"`.
    `SeedAppointment` sets neither today. It has two overloads: `(status)` at lines 365–366, which
    delegates, and `(status, date)` at lines 368–385. Add `string? reminderJobId48h = null,
    string? reminderJobId24h = null` to the two-argument overload, and seed with
    `SeedAppointment(AppointmentStatus.Pending, DateTime.UtcNow.AddDays(1), "job-48", "job-24")`.
    Then prove the order, not just the call:
    ```csharp
    _jobs.When(j => j.CancelAppointmentJobs("job-48", "job-24")).Do(_ =>
        _db.Appointments.AsNoTracking().Single(a => a.Id == id).Status
            .Should().Be(AppointmentStatus.Cancelled));   // already saved when the jobs are cancelled
    // … Handle …
    _jobs.Received(1).CancelAppointmentJobs("job-48", "job-24");
    ```
  - `Handle_HeldCardPayment_PartialSplitRoundsLikeThePaidBranch`: Amount 10.25 EUR, late-cancel
    refund 50%. The refund is `CurrencyCatalog.Round(5.125, EUR)` = 5.13 (`AwayFromZero`), so expect
    `CaptureAsync(studioId, ref, 5.12m, "EUR", …)` and `RefundedAmount == 5.13m`. That catches a
    missing or misplaced rounding step. 1234 ALL at 33% wouldn't: it is 407.22 exactly.
- **Keep** every existing `Paid`-payment test unchanged and green.
- Add to `PokPaymentProviderTests`:
  - `CaptureAsync_PartialAmount_SendsWholeUnitAmount` (`"amount":617` for 617 ALL);
  - `CaptureAsync_Partial409WithSameCapturedAmount_Succeeds`: the `/capture` call returns 409 and
    the order GET returns `"capturedAmount":617`. Expect no exception;
  - `CaptureAsync_Partial409WithDifferentCapturedAmount_Throws`: 409, then `"capturedAmount":1234`.
    Expect `ServiceUnavailableException`;
  - `CaptureAsync_Partial409WithoutCapturedAmount_Throws`: 409, then an order with no
    `capturedAmount`. Expect `ServiceUnavailableException`.

  Return a 409 from the fake handler with `new HttpResponseMessage(HttpStatusCode.Conflict)`.
- Add `NullPaymentProviderTests` coverage for the new overload.
- `CaptureDepositHandlerTests`: add `Handle_ForfeitedDeposit_StaysForfeitedAfterCapture`. Seed the
  appointment with `DepositStatus.Forfeited` and a `Captured` card payment. Expect the payment
  `Paid` with `PaidAt` set, and `DepositStatus == Forfeited`. The existing
  `Handle_AuthorizedPayment_UpdatesAppointmentDepositStatusToPaid` (line 66) stays green.
- Integration (`tests/Pena_e_Arte.IntegrationTests/Application/AppointmentHandlerIntegrationTests.cs`):
  add one held-deposit studio-cancel case asserting the persisted state.

**Help sync** (wording: §16, Phase 8.3/8.4):
- **Clients:**
  - `helpContent.ts` `client-cancel-booking` gets the B3 tip: "If your deposit was only authorised
    (not yet charged): cancelling with enough notice releases the hold, so you're never charged;
    cancelling late means the studio charges only the share its cancellation policy keeps, and the
    rest of the hold is released. Your bank may take a few days to show a release."
  - Don't write "you're never charged" without the late-cancel clause. A late cancellation does
    capture the kept share (T5b expects `capturedAmount` 617).
  - `faq-client-cancel-refund` gets the same sentence appended.
- **Owners:**
  - `owner-deposit-rule-create` gets the B3 tip from Phase 8.3. Its "0% (the default) forfeits the deposit
    entirely" now means the card is charged.
  - The `owner-payments` B3 tip.
- **Manual:**
  - `client-book`: the "Cancellation policy" callout (line 1002) says cancelling with enough notice
    "refunds your deposit in full". Append the B3 client tip to it (Phase 8.4);
  - `ref-deposit-statuses` (Refunded row, per Phase 8.4);
  - `owner-create-deposit-rule` (line 2544, next to the "Refund if cancelled late" text at line
    2571).
- Tours: none.

**Benchmark note.** Stripe cancels an uncaptured PaymentIntent to release funds; it doesn't refund
it. Its partial capture automatically releases the remainder (§20). Forfeiting a deposit without
capturing it collects nothing, which falls below every competitor's no-show/late-cancel protection
(§20).

**Commit:** `fix(payments): cancelling an appointment releases or partly captures a held deposit instead of refunding uncaptured money`

### B4 — Never resume an expired POK order; payment links get an expiry too (Finding 5)

**`CreateDepositPaymentCommand.cs`.** Line 30 currently reads
`private const int HoldDurationMinutes = 1440;`. Make it `internal const int HoldDurationMinutes = 1440;`
so `CreatePaymentIntentCommand` can reuse it.

Resume branch, current lines 95–99:
```csharp
            switch (status)
            {
                case PaymentProviderStatus.Pending:
                    // Still awaiting the client — resume with the same hold
                    return (existing.Id, existing.ClientToken, PaymentStatus.Pending);
```

Target:
```csharp
            // Legacy rows (owner payment links before B4) have no HoldExpiresAt: POK applied its 24 h
            // default (E4) from the last mint, which is UpdatedAt (the link's retry branch sets it;
            // a new row's UpdatedAt defaults to its creation time). Not named holdExpiresAt: line 144
            // declares that local in the enclosing scope (CS0136).
            DateTime orderExpiresAt = existing.HoldExpiresAt ?? existing.UpdatedAt.AddMinutes(HoldDurationMinutes);

            switch (status)
            {
                case PaymentProviderStatus.Pending when orderExpiresAt > DateTime.UtcNow.AddMinutes(10):
                    // Still awaiting the client and comfortably inside POK's order window — resume.
                    return (existing.Id, existing.ClientToken, PaymentStatus.Pending);

                case PaymentProviderStatus.Pending:
                    // Unpaid but at (or within 10 minutes of) its POK expiry. POK's public order read,
                    // which the checkout widget loads, returns 410 "Order is expired" after expiresAt,
                    // so release it and fall through to mint a fresh hold.
                    await paymentProvider.CancelAsync(existing.StudioId, existing.ProviderReferenceId, ct);
                    break;
```
Execution then continues to the existing `convertible` check. A card payment that is still
`Pending` is convertible, so a fresh hold is minted in place. Re-minting a little early is harmless:
the branch only fires on an order POK still reports unpaid.

**Shared path.** This resume logic lives in `ResolveOrCreateHoldAsync`, defined at line 72 of
`CreateDepositPaymentCommand.cs`. It is called at line 36 of that file, and also at line 46 of
`PayDepositWithSavedCardCommand.cs`. So B4 changes the saved-card path's behaviour as well. Test
it there too (below), but don't edit that handler's file (§6).

**`CreatePaymentIntentCommand.cs`.** Lines 51–52 currently read:
```csharp
        (string intentId, string clientToken) = await paymentProvider.CreatePaymentHoldAsync(
            new PaymentHoldRequest(tenant.StudioId, paymentId, amount, currency), ct);
```

Target:
```csharp
        (string intentId, string clientToken) = await paymentProvider.CreatePaymentHoldAsync(
            new PaymentHoldRequest(tenant.StudioId, paymentId, amount, currency,
                HoldDurationMinutes: CreateDepositPaymentHandler.HoldDurationMinutes), ct);
        DateTime holdExpiresAt = DateTime.UtcNow.AddMinutes(CreateDepositPaymentHandler.HoldDurationMinutes);
```
Set `HoldExpiresAt = holdExpiresAt` in both the new-payment initialiser (lines 57–70) and the
retry branch (lines 76–84).

**Tests:**
- **`CreateDepositPaymentHandlerTests`:** its `SeedPayment` helper (line 286) has no
  `HoldExpiresAt` parameter. Add `DateTime? holdExpiresAt = null` and `DateTime? updatedAt = null`
  (nothing in the app or `FakeDbContext` overwrites a seeded `UpdatedAt`). The existing resume test
  (line 94) keeps passing: its row has no `HoldExpiresAt`, and `UpdatedAt` defaults to now
  (`TenantEntity`), so the derived expiry is 24 h away. Then add:
  - `Handle_LegacyLinkWithoutExpiry_OlderThan24h_CancelsAndMintsFreshHold`: seed no
    `HoldExpiresAt` and `UpdatedAt` = now − 25 h, provider `Pending`. Expect `CancelAsync` received
    once and a new `CreatePaymentHoldAsync`.
  - `Handle_PendingHoldNearExpiry_CancelsAndMintsFreshHold`: seed `HoldExpiresAt` = now + 5 min.
    Expect `CancelAsync` received once, a new `CreatePaymentHoldAsync`, the same `Payment.Id`, a
    new `ProviderReferenceId`.
  - `Handle_PendingHoldWellInsideWindow_Resumes`: seed `HoldExpiresAt` = now + 12 h. Expect no
    `CancelAsync` and the same token (unchanged behaviour).
- **`PayDepositWithSavedCardHandlerTests`:** add
  `Handle_PendingHoldNearExpiry_CancelsAndMintsFreshHoldThenSetsUpThreeDs`.
- **`CreatePaymentIntentHandlerTests`:** add
  `Handle_SetsHoldExpiresAtAndPassesHoldDurationToProvider`.
- **Integration (`PaymentHandlerIntegrationTests.cs`):** it already builds
  `CreatePaymentIntentHandler` (line 530). Add `CreatePaymentIntent_PersistsHoldExpiresAt`,
  asserting the stored `HoldExpiresAt` is about now + 24 h.

**Help sync.** This is a gated path (`Application/Payments`), so the PR must touch Help. The
wording is in §16, Phase 8.3/8.4.
- **Clients:** `client-deposit-pay` gets a tip: "If you come back to pay after a day, a fresh card
  form is prepared automatically — just pay again from the same page." The manual's
  `client-deposit` section gets the same tip.
- **Owners:** `owner-payment-create` gets the tip: "A card checkout link stays payable for 24 hours.
  If it isn't paid by then, it shows Failed after the overnight check and you can create a new one
  for the same appointment." Add the same line to the manual's `owner-create-payment` section.
  - This is true: `CreatePaymentIntentCommand` reuses a `Failed` row for a new link (its retry
    branch).
- Tours: none.

**Benchmark note.** Checkout sessions elsewhere (Stripe Checkout `expires_at`, Square payment links)
expire and get recreated; a payer is never shown a dead form.

**Commit:** `fix(payments): never resume an expired POK order; owner payment links carry the same 24 h hold expiry`

### B5 — Owners see the held state as "On hold" (Finding 7)

- **New file** `frontend/src/features/payments/paymentStatusLabels.ts`:
  ```ts
  import { PaymentStatus } from "./payment.types";

  /** Owner-facing labels. The API's "Captured" means authorised-and-held, NOT charged — POK's own
   * merchant dashboard calls this state "On hold", so owners see the same words in both places. */
  export const PAYMENT_STATUS_LABELS: Record<PaymentStatus, string> = {
    [PaymentStatus.Pending]: "Pending",
    [PaymentStatus.CashPending]: "Cash Pending",
    [PaymentStatus.Captured]: "On hold",
    [PaymentStatus.Paid]: "Paid",
    [PaymentStatus.Refunded]: "Refunded",
    [PaymentStatus.Failed]: "Failed",
  };
  ```
- **`PaymentListPage.tsx`:** delete the local `STATUS_LABELS` (lines 25–32) and import
  `PAYMENT_STATUS_LABELS` wherever `STATUS_LABELS` was used, filter pills included.
- **`PaymentDetailPage.tsx`:** `StatusBadge` (lines 38–52) renders `PAYMENT_STATUS_LABELS[status]`
  instead of the raw `{status}`. Type its prop as `PaymentStatus`, not `string`, and don't cast to
  `any`. Side effect: the detail page's cash badge changes from "CashPending" to "Cash Pending",
  matching the list page. No test asserts the old text. Say so in the commit body.
- **Leave every other "Captured" alone.** Verified at `d8b743b`, the only owner-visible renderings
  of a payment status are:
  - `PaymentListPage.tsx`, where `STATUS_LABELS` is used at lines 46 (badge), 118 (empty-state
    text) and 172 (filter pills);
  - `PaymentDetailPage.tsx` line 49 (`{status}`).

  Every other `"Captured"` / `PaymentStatus.Captured` in `frontend/src` compares API values or is a
  comment. Do not change those:
  - `PaymentMethodSelector.tsx` lines 50, 150, 239, 285;
  - `DepositCheckoutPage.tsx` line 42;
  - `PaymentDetailPage.tsx` line 250 (the "Capture deposit" condition) and line 42 (a style-map
    key);
  - `MyBookingsSection.tsx` lines 87, 93, 195;
  - `payment.types.ts` lines 4 and 120;
  - `PaymentListPage.tsx` line 37, a style-map key.
- **Empty state.** With the shared labels, line 118 would read "No On hold payments found.". Change
  it to `` `No payments with status "${PAYMENT_STATUS_LABELS[statusFilter]}".` ``.
- **Tests** (`frontend/src/features/payments/__tests__/`):
  - `PaymentDetailPage.test.tsx` line 169 (`getByText("Captured")`) becomes `"On hold"`. The test
    at line 210 ("owner + Captured + Card → 'Capture deposit' button visible") gains an assertion
    that "On hold" shows next to the button.
  - `PaymentListPage.test.tsx` has no "Captured" assertion today. Add a case where a `Captured`
    payment's badge and filter pill both read "On hold". Pills render only when the loaded data
    has more than one status (line 155), so seed at least two.
  - The new empty-state text is hard to reach: pills come only from loaded statuses (lines 97–101),
    and a search message takes precedence (lines 116–118). Select the "On hold" pill, then make
    the mocked list query return data without the `Captured` payment and trigger a refetch. Assert
    `No payments with status "On hold".` If that proves brittle with the RTK Query mock, export the
    message as a small pure helper, test that, and say so in the PR.
  - Run `pnpm lint && pnpm build && pnpm test --coverage` in `frontend/`.

**Help sync** (gated path — required; wording: §16, Phase 8.3/8.4, which then checks these edits
rather than adding them again):
- **Manual `ref-deposit-statuses`:** the Captured row becomes Phase 8.4 item 4's text: "**On hold**
  (`Captured`) — card authorised, amount reserved, not yet charged. *Capture deposit* charges it."
  In the same "Payment record status" table, the `CashPending` row's label (line 3948) becomes
  "Cash Pending", matching the badges.
- **Manual `owner-payment-detail` and `owner-payments` mock-ups:** replace the "Captured" pill text
  (lines 2861 and 2832) with "On hold".
- **`helpContent.ts` `owner-payments`:** add the status tip from Phase 8.3, so this commit carries
  the Help change itself.
- Tours: no step targets the pill.

**Benchmark note.** POK's dashboard says "On hold"; Stripe's Dashboard says "Uncaptured"; Square's
API calls the state `APPROVED` (delayed capture, §20). "Captured" next to a "Capture deposit" button
matches none of them.

**Commit:** `feat(payments): label held card deposits "On hold" for owners (was "Captured")`

### B6 — Every capture path records `PaidAt`, so revenue reports include the deposit (Finding 14)

Copy `CaptureDepositCommand`'s bookkeeping (lines 42–52), which is already correct.

**`CompleteAppointmentCommand.cs`.** Lines 46–51 currently read:
```csharp
        if (payment is not null && !string.IsNullOrEmpty(payment.ProviderReferenceId))
        {
            await paymentProvider.CaptureAsync(payment.StudioId, payment.ProviderReferenceId, ct);
            payment.Status = PaymentStatus.Paid;
            payment.UpdatedAt = DateTime.UtcNow;
        }
```

Target:
```csharp
        if (payment is not null && !string.IsNullOrEmpty(payment.ProviderReferenceId))
        {
            await paymentProvider.CaptureAsync(payment.StudioId, payment.ProviderReferenceId, ct);
            DateTime now = DateTime.UtcNow;
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = now;                       // revenue reports filter on PaidAt != null
            payment.UpdatedAt = now;
            appointment.DepositStatus = DepositStatus.Paid;
        }
```

`appointment.UpdatedAt` is already set at line 37.

**`MarkNoShowCommand.cs`.** Lines 46–48 (the card branch) gain `payment.PaidAt = DateTime.UtcNow;`
after `payment.Status = PaymentStatus.Paid;`. Leave the rest alone:
- `DepositStatus` stays `Forfeited` (line 32, intended).
- The cash branch (lines 50–55) stays as it is. It marks a declared-but-never-received cash deposit
  `Paid` without `PaidAt`, which keeps money that was never collected out of revenue. That's odd,
  but it's a cash-flow question outside this gate. Note it in the backlog doc under C-12.

**Tests:**
- **`CompleteAppointmentHandlerTests`:** add
  `Handle_HeldCardDeposit_CapturesAndSetsPaidAtAndDepositStatusPaid`. Seed a `Captured` card
  payment with a `ProviderReferenceId`. Expect:
  - `CaptureAsync` received once;
  - `Status == Paid`, `PaidAt` set;
  - the appointment's `DepositStatus == Paid`.
- **`MarkNoShowCommand`:** has no unit tests today. Create
  `tests/Pena_e_Arte.UnitTests/Appointments/MarkNoShowHandlerTests.cs`, following
  `CompleteAppointmentHandlerTests`' setup. The handler takes `IAppDbContext`, `ICurrentTenant`,
  `IRealtimeNotifier` and `IPaymentProvider`. Add:
  - `Handle_HeldCardDeposit_CapturesAndSetsPaidAt`: `DepositStatus == Forfeited`;
  - `Handle_CancelledAppointment_Throws`.
- **Integration:** none needed. Neither handler's query changes.

**Help sync.** Revenue starts including these deposits, and two existing sentences describe the
counting rule wrongly once B6 (and B3) ship. The revenue queries count every `Paid` or `Refunded`
payment with `PaidAt` set, at `Amount − RefundedAmount` (`GetRevenueSummaryQuery.cs` line 41,
`PaymentExtensions.RetainedAmount`). So a captured no-show deposit (`Paid`, `DepositStatus`
`Forfeited`) counts, and so does the kept share of a partial refund.
- **`helpContent.ts` `owner-reports`** (line 1470) says "Only fully paid deposits (card captured or
  cash confirmed) count toward these figures — pending or refunded ones don't."
- **Manual `owner-reports`** (`index.html` line 3415) says "Only fully paid deposits count (card
  captured or cash confirmed) — pending, refunded, or forfeited ones don't."
- Replace both sentences with: "Revenue counts deposits the studio actually collected: card
  deposits once charged, and cash deposits once confirmed. After a partial refund, only the part
  you kept counts. Pending deposits, card deposits that are only authorised (not yet charged), and
  fully refunded deposits don't."
- `artist-earnings` (line 1494) already says "the same rule the studio's own revenue report uses".
  It stays true; no change.
- This is not a gated path (`Application/Appointments`), so CI won't force the Help change. Make it
  anyway (rule 7), in the B6 commit. If Phi declines B6, leave both sentences alone and list them in
  the backlog doc.

**Benchmark note.** This is a correctness fix, not a benchmark gap. A revenue report that leaves out
the most common way a deposit is collected — completing the appointment — is simply wrong, in any
product.

**Commit:** `fix(payments): completing an appointment or marking a no-show records PaidAt (and DepositStatus) like Capture deposit does`

**After Phase 2:** run the full backend suite and the frontend gates.
```bash
dotnet build "Pena e Arte.slnx" --configuration Release
dotnet test tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj
dotnet test tests/Pena_e_Arte.IntegrationTests/Pena_e_Arte.IntegrationTests.csproj
cd frontend && pnpm lint && pnpm build && pnpm test --coverage
```
Integration tests need MySQL on `127.0.0.1:3306` (CONTRIBUTING.md). Phi's local MySQL may be on
3307 (E13). If the integration tests can't reach it, say so and let CI run them. Never repoint the
test configuration.

---

## 11. Phase 3 — Gate environment and test data → checkpoint C1

**3.1 — Bring up the stack (D1 = A).** Build from the branch, so every Phase 1–2 change is inside
the images.

```bash
docker compose up -d --build
docker compose ps
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8080/health
```

The health check must return 200, and the `vault` container must be running.

If D4 gave a public callback URL, keep the override outside the repo
(`~/pok-gate-2026-10-07/docker-compose.gate.yml`):

```yaml
services:
  api:
    environment:
      Pok__WebhookCallbackBaseUrl: "https://test.tattooos.co"
```

Then start with:
```bash
docker compose -f docker-compose.yml -f ~/pok-gate-2026-10-07/docker-compose.gate.yml up -d --build api
```

Alternatively, Phi sets `POK_WEBHOOK_CALLBACK_BASE_URL` in `.env`. Either way, check that the tunnel
reaches the API before the run. The command below must not print a 5xx or a connection error; the
endpoint returns 200 and enqueues a reconciliation run, which is harmless:

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST https://test.tattooos.co/api/v1/webhooks/pok
```

**Dev-mode Vault keeps keys in memory only.** If the `vault` container restarts, every POK call
fails with 422 `PAYMENT_PROVIDER_NOT_CONNECTED` ("connection incomplete or unreadable"). If that
happens, Phi reconnects through the POK card in Studio Profile. Do not restart `vault` mid-run.

**3.2 — Test data. Phi does all UI steps; you give click-by-click instructions and check every step
read-only in the database.** Use obviously fake data. Never use a real person's name, phone, NIPT or
email.

| Item | Spec |
|---|---|
| **Studio S1, "Gate ALL"** | Register a fresh studio, country Albania. Check that its Currency card shows **ALL** and is unlocked. A fresh studio matters: seeded studios may already have payments, which locks the currency. |
| **Studio S2, "Gate EUR"** | Only if D3 = yes. Register a fresh studio; before any payment, set Currency to **EUR**. |
| **POK connection** (both studios) | Phi opens Studio Profile → "Card deposits — POK" and types the Merchant ID, Key ID and Key Secret of his staging key (E7, E8). If he no longer has the Key Secret, he creates a new key in POK (E-payments → API Keys), copies all three values from the one-time dialog, and uses those. Both studios may use the same POK merchant for this gate. |
| **S1 service "Gate deposit ALL"** | Deposit **1234**. |
| **S2 service "Gate deposit EUR"** | Deposit **12.34**. |
| **S1 deposit rule "Gate late-cancel 50"** | Active. Cancellation notice window **720 h**, "Refund if cancelled late" **50%**. Its own amount can be anything: the service's deposit replaces it (`CreateAppointmentCommand`). It is needed for T5b, so any client self-cancel within the next 30 days counts as late. |
| **Client account U1** | A fresh client registered through S1's public booking page with test details. Join S2 as well if D3 = yes. |

Verify every item read-only (Appendix B):
- the `studios` rows have `Currency = 'ALL'` / `'EUR'` and `PokMerchantId IS NOT NULL`. Never print
  the id; print `PokMerchantId IS NOT NULL` instead;
- one `StudioCredentialRefs` row per studio with `Provider = 'Pok'`;
- each studio's POK card shows "Connected" with a Merchant ID. Phi reads it; you don't copy it
  anywhere;
- U1's booking flow on S1 offers a **Card** tab. The capabilities response behind that tab is
  `cardPaymentsAvailable: true`, `pokEnvironment: "staging"`, `currency: "ALL"`. If the tab is
  missing, check `GetPaymentCapabilitiesQuery`'s three reasons in order: `provider_disabled`,
  `provider_unsupported_currency`, `provider_not_connected`.

**C1 — Stop until Phi confirms all of the above.**

**Help sync for Phase 3:** none. Test data isn't a product change. Do record any friction Phi hits
while connecting (unclear field, no hint where the Merchant ID lives, etc.). That is Phase 8 input
for `owner-connect-pok`.

---

## 12. Phase 4 — The ALL run (attended) → checkpoint C2

**How each test works.**
1. **Give Phi one step at a time.** It comes from `checklist.md`.
2. **Phi does the step** and tells you when it's done.
3. **You collect the evidence** (Appendix B), all of it read-only:
   - the `payments` and `appointments` rows;
   - the `POK order …` log lines from Phase 1;
   - the public POK read (`curl`, within 24 h of the order's creation, piped through the filter
     that drops the `merchant` object);
   - what Phi sees in the POK dashboard and on the checkout page.
4. **Record a verdict:** PASS, FAIL or OBSERVED.
5. **On any FAIL, stop** and go to Phase 6. Don't keep running tests on top of a broken mapping.

**Tests on the amount.** In ALL every amount is a whole number: 1234 to hold, 234 to refund, 617
as half of 1234. A 100× error in either direction is impossible to miss. Never compare the POK
account balance (E9).

**Run order:**
1. **T9 first.** It starts the 25 h aging clock.
   - **1b. T9b (optional, recommended).** A second held deposit made exactly like T9, then left on
     hold. Phase 7 captures it at ≥ 8 days, but only if T9's capture succeeds. If T9b isn't created
     today, it can't be created later.
2. **T1 → T2 → T3**, all on one payment.
3. **T4.**
4. **T5a.**
5. **T5b.**
6. **T6.**
7. **T8** (optional).

T7 (webhook) is checked during all of these. Give every booking a different time slot, within the
next 30 days and at least 3 days from now. That keeps the "stale" pass and T5b's 720 h window
predictable.

| # | Purpose | Phi does | Expected (PASS) | If it differs |
|---|---|---|---|---|
| **T9** | Aging probe, setup | As U1, book "Gate deposit ALL" on S1; Card tab; pay with `4111…`. Note the time. | Payment `Captured` (held), `HoldExpiresAt` ≈ now + 24 h; the flag log shows the held shape; POK dashboard badge "On hold". | Same as T1. |
| **T1** | Amount + authorise | As U1, book "Gate deposit ALL" on S1; in the Card tab confirm the form shows **1,234.00 ALL**; pay with `4111 1111 1111 1111`, `12/26`, `111`. | POK `amount` = **1234**, `finalAmount` = 1234, `currencyCode` = `ALL`, no conversion fields. DB: `Amount = 1234.0000`, `Currency = 'ALL'`, **`Status = 'Captured'` (held)**, `PaidAt` NULL, appointment `DepositStatus = 'Pending'`. Flag log property `"Mapped":"Authorized"`. Phi: dashboard "On hold". Record the exact held flag set — it **fills E1's missing row**. | Amount off → Phase 6, ToPokAmount table. The card form shows an error before payment, and the log has a `POK request failed` line with `"StatusCode":"BadRequest"` (or `"UnprocessableContent"` / `"UnprocessableEntity"`, .NET's two names for 422) → Phase 6, request-shape row (explicit `null`s, E4). `Status = 'Paid'` or `"Mapped":"Captured"` → Finding 1 confirmed → Phase 6, MapStatus. Write down whether the **old** mapping would have been wrong: was `isCompleted` true in the held shape? |
| **T2** | Full capture | As S1's owner, open T1's payment → **Capture deposit** (the badge says "On hold" if B5 shipped). | POK: `isCaptured` true, `capturedAmount` = **1234**, `canBeCaptured` false. DB: `Status = 'Paid'`, `PaidAt` set, appointment `DepositStatus = 'Paid'`. Dashboard "Paid". | `capturedAmount` ≠ 1234 → STOP (the full capture forwards POK's own `finalAmount`, so a mismatch means POK's read and write disagree) → Phase 6. |
| **T3** | Partial refund (amount unit, second write path) | Owner → T1's payment → **Refund** → amount **234** → **Confirm refund**. | POK dashboard "Partially refunded"; the Transactions list shows a refund of **234.00 ALL**; `refundableAmount` 1000 if visible. Public read: record `isRefunded` (E2 predicts **false** after a partial refund) and `capturedAmount`. DB: `Status = 'Refunded'`, `RefundedAmount = 234.0000`. | A refund of 2.34 or 23,400 → Phase 6, ToPokAmount. |
| **T4** | Release an unpaid hold | As U1, book again on S1 → open the Card tab (this creates the POK order) → **don't pay** → switch to the Cash tab → declare cash. **Before Phi switches tabs, read the full order id from the DB** (`ProviderReferenceId`), because the cash switch clears it. | Flag log at the switch: `"Mapped":"Pending"`. Public read afterwards: `isCanceled` true. DB: `Status = 'CashPending'`, `Method = 'Cash'`, `ProviderReferenceId` NULL. | `isCanceled` false → the cancel didn't take. Record POK's response code from the log (`POK request failed …`). |
| **T5a** | Studio cancels a held deposit | As U1, book on S1 and pay (`4111…`) → as owner, open that appointment → **Cancel appointment**. | **With B3:** POK `isCanceled` true (dashboard "Canceled"; the hold is released). DB: payment `Refunded`, `RefundedAmount = 1234.0000`, appointment `Cancelled` + `DepositStatus = 'Refunded'`. **Without B3 (observation):** the app calls Refund on the uncaptured order. Record POK's answer: a 503 to the owner plus `POK request failed … : <code>` in the log (appointment **not** cancelled, but its reminder jobs already deleted — Finding 4), or a 200. If 200, ask Phi what the dashboard shows. Either way Finding 4 is confirmed and B3 becomes a go-live blocker. | — |
| **T5b** | Client late-cancels a held deposit (50%) | **Only with B3.** As U1, book on S1 and pay → as U1, My Bookings → **Cancel appointment** → confirm. | POK: `isCaptured` true, `capturedAmount` = **617**, the rest released. DB: payment `Refunded`, `RefundedAmount = 617.0000`, `DepositStatus = 'Refunded'`. This is the **third** amount-unit proof (the partial-capture write path). | 6.17 or 61,700 → Phase 6. |
| **T6** | Authorised but never confirmed by the app (B1) | As U1, book on S1 → open the Card tab → **don't pay in the app**. You read the full order id from the DB and give Phi `https://pay-staging.pokpay.io/sdk-orders/<orderId>`. Phi pays **there**, on POK's hosted page. Phi must not go back to the app's card tab: the resume path would heal it and hide the test. | Before reconciliation: DB still `Pending`. Then trigger reconciliation yourself: `curl -s -X POST http://localhost:8080/api/v1/webhooks/pok` (that is exactly what POK's webhook does). **With B1:** log `healed to held`, DB `Status = 'Captured'`. **Without B1 (observation):** it stays `Pending`, and the hold would be cancelled at the first run after `HoldExpiresAt` — record that. | — |
| **T7** | Webhook (only with a D4 URL) | Nothing extra. Watch during T1–T6. | API request log, within seconds of POK events: a compact-JSON line from `UseSerilogRequestLogging` with `"RequestMethod":"POST"`, `"RequestPath":"/api/v1/webhooks/pok"` and `"StatusCode":200` (the template `HTTP {RequestMethod} {RequestPath} responded {StatusCode} …` is in `@mt`, not rendered). It is followed by a `PaymentReconciliationJob` execution (Hangfire dashboard at `http://localhost:8081/hangfire`; Phi logs in, since its password is in `.env`). Record **which** events produced a call: order created? authorised? captured? refunded? | No call at all → check the order was created after the callback URL was set (`webhookUrl` is fixed at creation). Then record "webhook not received" and add it to the POK questions (C-6). |
| **T8** | Decline (optional) | As U1, book on S1 → Card tab → `4000 0000 0000 1091` → abandon or fail the 3DS challenge. | The widget shows an error; DB stays `Pending`; no other state changes. The dashboard shows a failed attempt at `3DS_VALIDATION`. | Any state change → record it as a bug. |

**Help-sync evidence to collect during the run.** These feed Phase 8, so note them:
- the exact wording on POK's checkout ("Pay 1,234.00 ALL");
- what the client sees after paying;
- what the owner's list and detail pages show at each step;
- anything that confused Phi.

**C2 — Stop.** Show Phi the filled evidence table and agree the verdicts with him.

---

## 13. Phase 5 — The EUR run (attended, only if D3 = yes) → checkpoint C3

Prerequisites:
- Phi has created a **EUR account** on the POK staging merchant;
- S2 is connected (checkpoint C1).

| # | Phi does | Expected (PASS) |
|---|---|---|
| **T1E** | As U1, book "Gate deposit EUR" on S2; check the form shows **12.34 EUR**; pay with `4111…`. | POK `amount` **12.34**, `currencyCode` **`EUR`**. `originalCurrencyCode` is absent, or `EUR` with `appliedExchangeRate` 1. DB `Amount = 12.3400`, `Currency = 'EUR'`, `Status = 'Captured'` (held). |
| **T2E** | Owner → Capture deposit. | POK `capturedAmount` **12.34**; DB `Paid`. |
| **T3E** | Owner → Refund **2.34**. | Dashboard: refund of **2.34 EUR**; DB `Refunded`, `RefundedAmount = 2.3400`. |

**Outcomes for T1E:**
- **(a) The hold can't be created.** The owner or client sees 503, and the log shows
  `POK request failed for studio … : <code>`. POK rejects EUR for this merchant. Check the EUR account
  exists, then retry once. If it still fails, record "EUR pending (POK rejects EUR: <code>)". EUR card
  deposits stay off.
- **(b) The order is created, but POK converted it.** `currencyCode` ≠ `EUR`, or `originalCurrencyCode`
  = `EUR` with a real `appliedExchangeRate`. **STOP the EUR run.** The client would be charged in ALL,
  which breaks "the client pays exactly the amount shown" (§4, item 5). Refunds sent as EUR amounts
  would also be misread. Record it, release that order, add it to C-6 for POK in writing, and keep
  EUR card deposits disabled. To release it:
  - **still unpaid:** the owner cancels the appointment, and the next reconciliation run cancels the
    order if B1 shipped; otherwise it lapses at its 24 h expiry;
  - **already paid (held):** with B3 shipped, the owner cancels the appointment, which releases the
    hold; without B3, Phi cancels it in POK's dashboard. Reconciliation never releases a hold on a
    live appointment.
- **(c) A clean EUR order.** Continue to T2E and T3E.

**Legal reminder.** A pass here proves the mechanics only. Charging Albanian consumers in EUR waits
for the Legal sign-off.

**Help sync for Phase 5.** Record what EUR looks like on POK's checkout and on the client's
confirmation. If POK shows a different currency from the app, that's a Help *and* product finding:
put it in the report.

**C3 — Stop.** Agree the EUR verdict with Phi.

---

## 14. Phase 6 — Fix loop (only if a check failed)

You may change **only** `ToPokAmount` and `MapStatus` without asking: those are the task's own
authority. Anything else that fails gets recorded and reported to Phi; don't fix it on the fly.

**Before every rebuild in this phase** (each of the three fixes below ends in one):
1. Save the API log first, because the rebuild recreates the container and drops its log history:
   `docker compose logs api > ~/pok-gate-2026-10-07/api-before-rebuild-<n>.log`.
2. Rebuild with exactly the same `docker compose` command as Phase 3.1, including the
   `docker-compose.gate.yml` override if D4 gave a URL. A plain `up --build` would drop the webhook
   URL for every later order.

**`ToPokAmount` decision table** (current code, line 180:
`private static decimal ToPokAmount(decimal amount, string currency) => CurrencyCatalog.Round(amount, currency);`)

| Observed across T1, T3, T5b (and T1E, T3E) | Meaning | Action |
|---|---|---|
| POK records exactly what was sent | Whole units ✔ | No change. Update the class doc comment (item 1 verified) in Phase 8. |
| **Every** write is off by exactly 10^(minor units), the same way, e.g. 1234 ALL → 12.34; 12.34 EUR → 0.12 or rejected | POK wants minor units | **Show Phi first**, because this contradicts E5 (POK reads ALL back in whole units). With his OK, change it to `CurrencyCatalog.Round(amount, currency) * MinorUnitFactor(currency)`, with `private static decimal MinorUnitFactor(string currency) { decimal factor = 1m; for (int i = 0; i < CurrencyCatalog.MinorUnits(currency); i++) factor *= 10m; return factor; }` (decimal only: §7 forbids `double`). Check whether any read path must convert back (today none: `CaptureAsync` forwards POK's own `finalAmount`; re-check T2). Update `PokPaymentProviderTests` bodies (`"amount":150` → `15000`, `"refundAmount":22.78` → `2278`, the partial-capture test). Rebuild, then re-run T1–T3 and T5b. |
| Off by a different factor, only for one currency, or create and capture disagree | POK isn't symmetric, or handles currencies differently | **STOP. Change nothing.** Collect the sanitised requests and responses, and draft the written question to POK (C-6). Card deposits stay disabled. |

**Request shape: POK rejects the order before any amount is checked** (T1 can't create a hold:
`POK request failed` with `"StatusCode":"BadRequest"`, or `"UnprocessableContent"` /
`"UnprocessableEntity"`, which both name 422). E4's explicit `null`s are the likely cause:
- `"webhookUrl": null` whenever no D4 URL is set;
- `"expiresAfterMinutes": null` on owner payment links before B4 (not sent by any gate test).

This is outside the task's own authority, so **show Phi first**. With his OK, add
`[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` to the three nullable properties of
`CreateOrderRequestBody`: `ExpiresAfterMinutes`, `MerchantCustomReference` and `WebhookUrl`
(`PokPaymentProvider.cs` lines 189–191 at `d8b743b`; Phase 1 shifts them).
`System.Text.Json.Serialization` is already imported for `JsonPropertyName`. Then:
- add a `PokPaymentProviderTests` case asserting the create body contains no `"webhookUrl"` when the
  callback base URL is empty;
- rebuild;
- re-run T1.

If the error persists with the nulls gone, STOP and record POK's response for C-6.

**`MapStatus`.** If T1's held flags match neither shape A nor shape B, or a later state maps wrongly:
1. Change `MapStatus` to follow POK's own precedence (E2), using the fields you actually saw.
2. Add the observed shape as a fixture labelled "observed on staging 2026-10-DD".
3. Save the API log and rebuild, as above.
4. Repeat the failed test on a **new** booking.

Commit each fix on its own: `fix(payments): <what>, observed on POK staging <date>`.

**Help sync:** a pure mapping or unit fix has no visible surface beyond what Phase 8 documents.
State that explicitly in the commit body.

---

## 15. Phase 7 — Day-2 aging probe (attended, ≥ 25 h after T9's authorisation) → checkpoint C4

**Goal.** Find out whether a held deposit can still be captured after its POK order's `expiresAt`
(24 h). The whole "hold at booking, capture at the appointment" model depends on the answer (C-4).

0. **Check the stack survived the night.**
   - If the stack was stopped or the laptop slept at 02:00 UTC, the job ran late: Hangfire 1.8
     fires a missed recurring run once when the server comes back. If the stack is still down, it
     hasn't run at all.
   - If the `vault` container restarted, dev-mode Vault lost the POK keys. Every POK call then
     fails: API requests return 422 `PAYMENT_PROVIDER_NOT_CONNECTED`, and the job logs a per-payment
     error instead of a flag line. Phi must reconnect S1 (Phase 3.2) first.
   - Then trigger reconciliation once with `curl -s -X POST http://localhost:8080/api/v1/webhooks/pok`,
     so a fresh post-expiry flag line is logged for T9.
1. **Before Phi acts, read T9's state** (Appendix B):
   - **DB:** payment status. If B2 shipped and POK reported the hold gone, it may already be
     `Failed`.
   - **Logs:** the reconciliation run's `POK order …` line for T9's order, from the 02:00 run or the
     one you triggered. That line is the post-expiry flag set — the **real lapsed-hold shape, or proof
     the hold survived**. The public read now returns 410 (E3), so the app's log line is your only
     flag source.
2. **If T9 still shows as held**, Phi opens it as owner and clicks **Capture deposit**.
3. **Record the outcome:**

| Outcome | What it means | Follow-up |
|---|---|---|
| **Capture succeeds:** POK `capturedAmount` 1234 (dashboard "Paid"); DB `Paid`. | POK holds survive the order's 24 h expiry. | Next, the card network's ~7-day window applies (§20). If T9b was created on Day 1 (§12, run order 1b), Phi captures it at ≥ 8 days. Record the result as an addendum. |
| **Capture fails:** owner sees 503 (`POK request failed` with the `StatusCode` property), or 404 if the order is gone. | Holds die with the order. Record the code and the flag line. | **C-4 becomes a go-live blocker:** the current model cannot work for appointments more than 24 h away. `CompleteAppointmentCommand` and `MarkNoShowCommand` make the same full capture, so completing or marking a no-show on any appointment more than 24 h after booking also fails with a 503, until B2 marks the payment `Failed`. Add that to C-4. Check `MapStatus`'s provisional `{ IsCompleted: true } => PaymentProviderStatus.Failed` arm (§9.2 item 3) against the observed lapsed shape, and add the shape as a fixture (Phase 6 rules). |
| **Payment already `Failed`** (B2 acted at 02:00). | Same as "capture fails". | Same as above. The log line shows what POK reported. |

4. **Help sync for Phase 7:**
   - The answer decides the `owner-payments` "how long a hold lasts" tip and the
     charge-timing copy (Finding 11; Phase 8.3).
   - If holds die at 24 h, **don't** document a capture window in Help. The flow is go-live-blocked
     anyway. Record it in the backlog doc instead.

**C4 — Stop.** Agree the aging verdict with Phi.

---

## 16. Phase 8 — Runbook, Help and docs (autonomous) → checkpoint C5

### 8.1 — Runbook result entry

Append a new subsection to the end of
`docs/payments/runbook-studio-currency-migration-2026-09-27.md` (after line 71, the last line of
§3). Keep the existing checklist text as it is.

```markdown
### §3.1 result — POK staging release gate, run 2026-10-DD (Day 2: 2026-10-DD)

**Environment:** <local docker-compose stack | staging> at commit `<sha>` (branch
`fix/pok-staging-release-gate-2026-10-07`), POK staging (`https://api-staging.pokpay.io`),
<dev-mode Vault | staging Vault>. Operator: Phi (keys, test cards, POK dashboard). Observer: Claude Code
(API logs, read-only DB). Prompt: `docs/claude/overnight-prompt-pok-staging-release-gate-2026-10-07.md`.

**Verdict:** GATE <PASSED | FAILED at Tn> for ALL; EUR <PASSED | PENDING (reason) | FAILED>.
**Go-live: NOT approved by this gate** — remaining blockers in
`docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md`.

| Test | App sent / did | POK recorded | POK flags (confirmed / canBeCaptured / captured / completed / canceled / refunded) | App state after | Result |
|---|---|---|---|---|---|
| T1 hold | 1234 ALL | amount 1234 ALL | … | Captured (held) | PASS |
| … | | | | | |

**Held-state shape observed:** `{…}` — the pre-gate `MapStatus` <would | would not> have mapped it to
Captured (marking the deposit Paid without a capture).
**Amount unit:** whole units confirmed on create, full capture, partial capture and partial refund
<for ALL and EUR>. `ToPokAmount` <unchanged | changed in `<sha>` because …>.
**Webhook:** <received for events … | not verified: reason>.
**Aging probe:** capture at +<N> h after the POK order expired: <succeeded | failed with <code>>.
<T9b at +8 days: pending | result>.
**Fixes made in this run:** <commit list with one line each>.
**Evidence hygiene:** order references are 8-character prefixes; no keys, merchant id, emails or
card data appear in this repo. Full evidence is in Phi's local gate folder.
```

### 8.2 — Mark the 2026-10-02 sync prompt's §6.4 as superseded

Check whether that prompt has already run: is `FlowB_NeverReferencesPokTypes_UntilAmendmentDsContingencyIsLifted`
in `PaymentArchitectureTests.cs`?

- **Not run (as of 2026-10-07):** insert one line directly under its `### 6.4 Runbook
  cross-reference` heading. If that heading reads differently on Phi's disk, use the heading of its
  §6.4.
  `> **Superseded 2026-10-DD — skip this sub-step.** The staging gate ran; see the runbook's "§3.1 result" entry. Items 6.1–6.3 still apply.`
  Change nothing else in that file.
- **Already run, and its paragraph is in the runbook:** replace that paragraph with one line
  pointing to the result entry.
- **If D6 = no,** the sync prompt is still untracked. Make the edit on disk only, don't `git add`
  it, and list it in the final report as an uncommitted local change.

### 8.3 — `frontend/src/features/help/helpContent.ts` (exact edits)

The strings below are the **only** wording for each tip. The B-item commits in Phase 2 add the
strings their own item owns, copied from here. This phase then checks that each one is present
exactly once and adds the rest. Line numbers are as of `d8b743b`, so Phase 2 shifts them; find
articles by `id`.

**`owner-connect-pok`** (lines 1143–1168) — this reflects E8:
- Replace `steps` with:
  ```ts
    steps: [
      "In your POK merchant dashboard, open E-payments → API Keys.",
      "Create an API key (enter a Tag and a Description, then confirm). POK then shows your Merchant ID, Key ID and Key Secret once — copy all three before you close that window.",
      "In TattooOS, go to Studio Profile and find the \"Card deposits — POK\" section.",
      "Paste your Merchant ID, Key ID and Key Secret, then click \"Connect\".",
    ],
  ```
- Add these tips:
  ```ts
      "Lost your Key Secret? POK only shows it once. Create a new API key in POK, reconnect here with the new values, then delete the old key in POK.",
      "TattooOS doesn't test your keys when you connect. If one value is wrong, card payments fail at checkout — so copy all three values from the same API key.",
  ```
- Extend `keywords` with `"api key"`, `"e-payments"` and `"lost key secret"`, plus any matching terms
  from Phase 0.5's zero-result searches.

**`owner-payments`** (lines 1169–1183). Make its final `tips` array exactly this list, keeping only
the lines whose behaviour the gate verified or whose fix shipped. Never duplicate a line a B-item
commit already added:
```ts
    tips: [
      // B5 shipped (otherwise write "Captured (on hold)" instead of "On hold"):
      "Card deposit statuses: Pending — waiting for the client to enter their card. On hold — the client's card is authorised and the amount is reserved, but not charged yet. Paid — charged. Refunded — money returned or released (all or part). Failed — the card payment didn't go through or the hold expired; the client can pay again.",
      "To charge a deposit that's on hold, open the payment and click \"Capture deposit\". Completing the appointment, or marking it a no-show, captures it automatically.",
      // B3 shipped:
      "If a client cancels late while their deposit is on hold, TattooOS charges only the part your cancellation policy keeps and releases the rest. If you cancel the appointment, the hold is released in full.",
      "To refund a card payment, open a Paid payment, click Refund, enter an amount for a partial refund (or leave it blank for the full amount) and click Confirm refund. Each payment can be refunded once.",
      // B1 shipped:
      "If a client's card was authorised but they closed the page before it confirmed, the payment updates by itself, at the latest overnight.",
      // B2 shipped:
      "If POK reports that a held deposit's authorisation expired or was cancelled, the payment shows Failed and the client can pay again.",
      // Phase 7 showed holds survive at least N days:
      "Capture a deposit that's on hold within N days of the client paying — after that the card authorisation lapses and the client has to pay again.",
    ],
```
- Extend `keywords` with `"capture"`, `"on hold"`, `"refund"`, `"authorised"` and `"held deposit"`.
- **Don't** say POK notifies TattooOS "within moments". No deployed environment sends a
  `webhookUrl` (E11), and this text ships to production on merge (E14). The webhook sentence
  belongs in the C-2 change.

**`owner-deposit-rule-create`** (around line 1073), if B3 shipped. Its step 6 (line 1085) says
"0% (the default) forfeits the deposit entirely". Add the tip: "If a client cancels late while
their card deposit is only on hold, TattooOS charges the part you keep (all of it at 0%) and
releases the rest."

**`owner-payment-create`** (around line 1185), if B4 shipped. Add the tip: "A card checkout link
stays payable for 24 hours. If it isn't paid by then, it shows Failed after the overnight check and
you can create a new one for the same appointment."

**`client-deposit-pay`** (lines 339–358):
- If B4 shipped, add the tip: "If you come back to pay after a day, a fresh card form is prepared
  automatically — just pay again from the same page."
- Once Phase 7 has confirmed holds survive, change the first tip to: "Your card isn't charged
  straight away — it's authorised, and the studio charges it when your appointment is completed or
  marked as a no-show (or earlier, if they capture it before then). If you cancel late, the
  studio's cancellation policy decides how much is charged. Payment is by card, or by cash
  arranged with the studio."
- If holds die at 24 h, leave that tip alone and list it in the backlog doc (C-4).

**`client-cancel-booking`** (lines 78–95), if B3 shipped. Its step at line 87 already warns that
"cancelling too close to the appointment may forfeit some or all of it". Add the tip: "If your
deposit was only authorised (not yet charged): cancelling with enough notice releases the hold, so
you're never charged; cancelling late means the studio charges only the share its cancellation
policy keeps, and the rest of the hold is released. Your bank may take a few days to show a
release."

**`faq-client-cancel-refund`** (around line 2011), if B3 shipped — append the same sentence to
`answer`.

**`faq-deposit-statuses`** (around line 2004) — fix the garbled sentence. "Forfeited means the client
kept it forfeited per your studio's cancellation terms." becomes "Forfeited means the studio kept the
deposit under its cancellation terms (for example, a late cancellation or a no-show)."

**`owner-reports`** (line 1470), if B6 shipped. The B6 commit replaced its revenue-rule sentence
(§10, B6). Check that the new sentence is there exactly once.

### 8.3b — Client charge-timing strings in the app (Finding 11)

Do this only once Phase 7 has confirmed that holds survive. If holds die at 24 h, leave the strings
alone and list them in the backlog doc under C-4, since C-4's decision rewrites them anyway.

Replace these six strings: five in `frontend/src/features/payments/components/` and one in
`frontend/src/features/appointments/components/MyBookingsSection.tsx`. The line numbers are as of
`d8b743b`. "Usually" is deliberate: the owner can click "Capture deposit" before the appointment.

| File, line | Today | Becomes |
|---|---|---|
| `PaymentMethodSelector.tsx` 84 | "Your card is authorised now and charged when the studio confirms your session." | "Your card is authorised now and charged by the studio, usually at your appointment. The studio's cancellation policy applies if you cancel late or miss it." |
| `PaymentMethodSelector.tsx` 160 | "Your card is authorised and will be charged when the studio confirms your session." | "Your card is authorised and will be charged by the studio, usually at your appointment. The studio's cancellation policy applies if you cancel late or miss it." |
| `DepositCheckoutPage.tsx` 60 and 132 | "Your card has been authorised. The studio will capture the deposit before your appointment." | "Your card has been authorised. The studio charges the deposit, usually at your appointment; its cancellation policy applies if you cancel late or miss it." |
| `DepositCheckoutPage.tsx` 83 | "Your card will not be charged until the studio confirms your appointment." | "Your card is authorised now and charged by the studio, usually at your appointment." |
| `MyBookingsSection.tsx` 199 | "Deposit authorised — charged when the studio confirms" | "Deposit authorised — charged by the studio, usually at your appointment" |

How to check it:
- **Existing tests:** one asserts the old copy. `DepositCheckoutPage.test.tsx` line 194,
  `getByText(/studio will capture/i)`, is in the `?status=complete` success-state test (lines
  182–196). Change it to match the new sentence, e.g. `/charges the deposit, usually at your
  appointment/i`. The `/deposit authorised/i` heading assertions stay as they are. Nothing else
  asserts the replaced strings: `PaymentMethodSelector.test.tsx` and `frontend/e2e` don't.
  `MyBookingsSection` is tested in `frontend/src/features/appointments/__tests__/BookPage.test.tsx`
  (`describe("MyBookingsSection")`, line 900), but no case there renders a `Captured` payment.
- **New tests:** assert the new sentence once in `PaymentMethodSelector.test.tsx`, and add one case
  to the `MyBookingsSection` block that renders a `Captured` card payment and asserts the new
  "Deposit authorised — charged by the studio, usually at your appointment" text.

This is the structural fix: the copy now matches what the code does. Tone polish belongs to
Marketing's UX-copy work, so flag it in the PR rather than doing it here. This change is in a gated
path (`frontend/src/features/payments`), which the same PR's Help changes satisfy.

### 8.4 — `frontend/public/user-manual/index.html` (the served copy)

1. **New section `<section id="owner-connect-pok" data-role="owner">`.** Insert it directly after the
   `owner-create-payment` section (it closes just after line 2908). Copy the markup pattern of the
   neighbouring owner sections: role-access table, steps list, tip/warn callouts, crosslinks. The
   content mirrors Phase 8.3's `owner-connect-pok`, plus the existing two warnings (production vs sandbox
   keys; Albania-registered businesses only).
   - This fixes the **dead link** `href="#owner-connect-pok"` at line 3072. No such section exists
     today.
   - Add a nav entry under Payments (after line 459):
     `<li><a href="#owner-connect-pok" data-nav-link>Connect POK</a></li>`.
2. **`owner-payments` (line 2821):** add the status, capture and refund tips from Phase 8.3 as one
   `callout-tip`. If B5 shipped, its "Captured" mock-up pill (line 2832) becomes "On hold"; if B1
   shipped, add its tip.
3. **`owner-payment-detail` (line 2850):**
   - if B5 shipped, the mock-up pill "Captured" (line 2861) becomes "On hold";
   - if B2 shipped, add its tip;
   - keep the warning "refunds are irreversible".
4. **`ref-deposit-statuses` (line 3933):**
   - Payment-record table: `Captured` → "**On hold** (`Captured`) — card authorised, amount reserved,
     not yet charged. *Capture deposit* charges it." Use the B5 label, or keep "Captured" if B5
     didn't ship.
   - `CashPending` → "Cash Pending" (line 3948), if B5 shipped. B5's commit makes this change; only
     check it here.
   - `Refunded` → "Fully or partially refunded. For a deposit that was only on hold, a full refund
     releases the hold (the client is never charged); a partial refund charges only the share the
     studio keeps and releases the rest." (only if B3 shipped)
5. **`client-deposit` (line 1368):**
   - the B4 tip;
   - the charge-timing sentence at line 1372, made to agree with Phase 8.3 once Phase 7 confirms it.
     Today's text ("Your card isn't charged until the studio confirms the appointment") doesn't
     match the code (Finding 11);
   - with it, the wireframe text at lines 1379–1380 ("Authorising 50.00. Not charged" / "until the
     studio confirms."). Make it "Authorising 50.00. Charged" / "usually at your appointment.",
     matching Phase 8.3b.
6. **`owner-create-deposit-rule` (line 2544), if B3 shipped:** the Phase 8.3 `owner-deposit-rule-create`
   tip, next to the "Refund if cancelled late" text (line 2571).
7. **`owner-create-payment` (line 2879), if B4 shipped:** the Phase 8.3 `owner-payment-create` tip.
8. **`client-book` (line 955), if B3 shipped:** its "Cancellation policy" callout (line 1002) says
   cancelling with enough notice "refunds your deposit in full". Append the Phase 8.3
   `client-cancel-booking` B3 tip to that callout.
9. **`owner-reports` (line 3411), if B6 shipped:** the revenue-rule sentence (line 3415), replaced as
   in B6's Help sync. The B6 commit makes this change; here, only check that it is there once.

### 8.5 — Onboarding tours: no new step in this run (stated, not skipped)

- `ownerTour.ts`'s "Your studio profile" step (lines 56–60) already names "your POK account for card
  deposits".
- A dedicated step pointing at the POK card (it needs `data-tour="owner-pok-card"` on
  `PokSettingsCard`'s `<Card>` and a step with `route: "/studios/me"`, following the hours and
  currency card steps) belongs in the **go-live change** (C-13). Prompting every new owner to connect
  live POK keys before Phi approves go-live would contradict §4, item 7.
- The client and artist tours have no payment steps that changed.

### 8.6 — `docs/claude/architecture.md`

- **Decisions Log:** add a row "POK staging release gate (2026-10-DD)". Include:
  - the verdicts;
  - the held-state shape;
  - whether the old mapping was wrong;
  - the commits for Phase 1 and B1–B6;
  - the evidence location (runbook §3.1 result);
  - a pointer to the go-live blockers doc.
- **AllowAnonymous table, `POST /api/v1/webhooks/pok` row (line 1239):** the phrase "re-fetches
  every in-flight payment's real status from POK directly" is inaccurate today (Finding 2). Change it
  according to what shipped:
  - **if B1 shipped:** "…which asks POK for the real status of every pending and held card payment
    before changing anything: it heals authorised or paid ones on live appointments, releases an
    authorised hold on a cancelled appointment (a held one only if B2 shipped, and never a forfeited
    deposit, which stays on hold for the owner to capture), and cancels an unpaid order only once it
    has expired, gone stale or its appointment was cancelled";
  - **if not:** say exactly what the job does and point to Finding 2 in the backlog doc.
- **Feature Module Map:** in row 05 "Payments & Session Splits" (line 290), note "POK sandbox-verified
  2026-10-DD (runbook §3.1)".
- **Do not edit** the "POK Flow-B guard test shipped" row (E15). It becomes true when the 2026-10-02
  sync prompt runs. Just flag it in your report.

### 8.7 — `docs/claude/frontend.md`, "Payment UI Pattern" (lines 282–319)

Replace lines 282–319 with the following. Keep the `---` separator on line 320. The current text
describes Stripe Elements for Flow A, which hasn't been true since POK became the provider. Before
writing, re-check the two facts the new text relies on:
- no file under `frontend/src` reads `VITE_STRIPE_PUBLISHABLE_KEY`;
- `VITE_CONTACT_EMAIL` is still read.

```bash
grep -rn 'VITE_STRIPE_PUBLISHABLE_KEY\|VITE_CONTACT_EMAIL' frontend/src
```

The replacement text (the outer fence has four backticks because the text contains a code block):

````markdown
## Payment UI Pattern

Client-facing deposit payments (Flow A) use `PaymentMethodSelector`
(`features/payments/components/PaymentMethodSelector.tsx`) — tabs for **Card** (labelled "New
card" when the client has saved cards), **Saved card** (only then) and **Cash**, plus the
gift-card redemption field. No PayPal, no wallet providers. Do not duplicate payment UI in other
components.

**Card tab:** POK's `GuestCheckoutForm` from `@nebula-ltd/pok-payments-js/react`, mounted with
the POK order id (the payment's `clientToken`) and `options.env` taken from
`GET /api/v1/payments/capabilities` → `pokEnvironment` — never guessed from the build mode. The
widget's `onSuccess` is UX only (ADR-0001): the page re-asks the API (`createDepositPayment` resume
/ `confirmCardPayment`), which re-reads the order from POK before anything is shown as authorised.
Deposits are authorised first (POK `autoCapture: false`; shown to owners as "On hold" if B5
shipped, otherwise "Captured") and charged on Capture deposit, Complete or No-show (and, if B3
shipped, partly on a late cancellation). The standalone link page `/pay/:paymentId` is
`DepositCheckoutPage`.

**Saved-card tab:** `AddCardForm`/tokenization — known broken, see
`docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md` (C-9). Do not extend it until that is fixed.

**Cash tab:** informational panel; on confirm calls `declareCashDeposit` → `Payment { Status = CashPending }`.
If a card hold exists and is still unpaid, the API releases it at POK first.

**Environment variables (never hardcoded in source):**

```
VITE_CONTACT_EMAIL            — Platform contact email (SuspensionBanner, SubscribePage, BillingPage, CurrencySettingsCard)
VITE_STRIPE_PUBLISHABLE_KEY   — still passed as a build arg (docker-compose.yml, frontend/Dockerfile, cd.yml; placeholder
                                in frontend/.env.example) but read by no
                                frontend code: Flow B redirects to server-created Stripe Checkout and billing-portal
                                URLs. Vestigial; Flow A has no publishable key (POK's widget needs only the order id).
```

Both go in `.env.local` (gitignored). A `.env.example` with placeholder values is committed.
Do NOT add `VITE_PAYPAL_CLIENT_ID` — PayPal is not used.

**`CashDepositConfirmButton`** (`features/payments/components/CashDepositConfirmButton.tsx`)
is the owner/artist-facing button shown in the dashboard for `CashPending` payments.
Calls `confirmCashDeposit` mutation on approval.
````

The B5 parenthesis is written for the case where you don't yet know whether B5 shipped. Once you
do, keep only the sentence that's true.

### 8.8 — `docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md` (new)

One file listing every §17 item with **this run's results filled in**. For example, C-4's status
after the aging probe, C-6's questions with the gate's evidence attached, and which of B1–B6 shipped
and which remain backlog. For each item give: owner (Phi, Finance, Legal or POK), blocking or not,
and the next action. The runbook entry links here.

### 8.9 — `PokPaymentProvider` class doc comment

Rewrite the "Two things verified only against docs" block (lines 18–29) to record what is now
verified and when:
- the amount unit for ALL/EUR on create, capture, partial capture and refund;
- the held-state shape.

Point to the runbook result entry. Keep the warning that the hold window (C-4) and EUR (if pending)
are still open.

### C5 — Stop

Show Phi:
- the runbook entry;
- the Help and manual diffs;
- the backlog doc;
- the full test results.

Then open the PR (§22). Don't merge it. Phi reviews and merges. Merging deploys production and
staging (E14). That's safe: production still points at POK staging (E11, C-2), so production POK
keys fail at POK's login and no real card can be charged. Per Phi's task note (2026-10-07),
production also has no studios or payments yet.

---

## 17. Do not build blind — backlog specs (not built in this run)

Each item is specified enough to become its own prompt. None of them is built here. Phase 8.8
copies them into `docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md` with this run's results.

**C-1 — Staging Vault** (go-live: no; staging parity: yes).
- **Why:** deployed staging can never run a POK card flow without it (E12).
- **Shape of the work:**
  - remove `k8s/overlays/staging/vault-exclude-patch.yaml`'s three deletes;
  - give staging its own `tattooos-vault` StatefulSet and PVC (sized down, like the other staging
    resources);
  - init and unseal it by hand, following `docs/infra/vault-self-hosted-runbook.md`;
  - create a **scoped, non-root** token with a policy limited to `secret/data/studios/*`;
  - add new GitHub secrets `STAGING_VAULT_TOKEN` (and an address if it isn't the in-cluster DNS
    name);
  - in `cd.yml`, add `Vault__Address`/`Vault__Token` to the staging secret step, and rewrite the
    comment that currently explains why they are absent.
- **Open questions (Phi):**
  - cluster capacity (`kubectl describe nodes`);
  - who unseals staging after restarts;
  - whether staging Vault should auto-unseal first (it inherits ADR-0002's gap, C-8).

**C-2 — POK host and webhook per environment** (go-live: **yes, this is the switch**).
- **Staging** (harmless, can ship any time): add to `k8s/overlays/staging/api-config-patch.yaml`:
  - `Pok__BaseUrl: "https://api-staging.pokpay.io"`
  - `Pok__WebhookCallbackBaseUrl: "https://staging.tattooos.co"` (nginx proxies `/api/`).
- **Production** (merge only when Phi approves go-live): add to
  `k8s/overlays/production/api-config-patch.yaml`:
  - `Pok__BaseUrl: "https://api.pokpay.io"`
  - `Pok__WebhookCallbackBaseUrl: "https://app.tattooos.co"` (`App__BaseUrl` in
    `k8s/base/api-configmap.yaml`).
- **Why it matters:** until then, production calls POK staging (E11), which happens to block real
  card deposits today. Once it ships, any studio that connects production keys can take real card
  deposits. So pair it with C-11 and C-13.

**C-3 — Gift-card and package card purchases** (Finding 6) — **a product decision for Phi**.
- **Option (a):** hide or disable card purchase for gift cards and packages until it's built. Help's
  `client-gift-cards` step 2 then says card purchase isn't available yet.
- **Option (b):** build the completion path:
  - a `/gift-cards/pay/:id` page (and one for packages) with `GuestCheckoutForm`;
  - add `bool AutoCapture` to `PaymentHoldRequest` (default false), sent as `autoCapture: true` for
    these immediate sales. That's the category standard, since a gift card is a sale, not a deposit;
  - reconciliation then activates on `Captured`;
  - an email or link step, if "check your email" is kept.
- **Tests:** the activation path, plus no activation while `Authorized`.
- **Help:** `client-gift-cards`, `client-packages`, `owner-gift-cards`, `owner-packages`.

**C-4 — When a deposit is charged** (go-live: **yes, if Phase 7 shows holds don't last until the
appointment**). This is a product, Finance and Legal decision for Phi.
- **What the evidence says:**
  - a card-not-present authorisation lasts about 7 days: Stripe, and Square's delayed capture,
    which cancels an online authorisation after 7 days by default (§20);
  - a POK order lasts 24 h (E3/E4), and whether its hold survives that is Phase 7's answer;
  - the category standard is to **charge the deposit at booking**: Square, GlossGenius, Vagaro and
    Fresha all do this (§20). Refund handling follows each business's settings; GlossGenius
    deposits, for one, are non-refundable;
  - the current code authorises at booking and captures on Complete or No-show, possibly weeks
    later;
  - if holds die with the order, Complete and No-show fail with a 503 on any appointment more than
    24 h after booking, until B2 marks the payment `Failed` (Phase 7 records this). Then the deposit
    is simply lost.
- **Options:**
  - **(a) Charge at booking:** deposits use `autoCapture: true`, refunds follow `DepositRule`, the
    capture button disappears for deposits;
  - **(b) Keep the hold, but capture before it lapses:** a job captures N hours before the
    window closes, like Stripe's `automatic_delayed` capture (a private preview at Stripe);
  - **(c) Hybrid:** hold for appointments within the window, charge at booking otherwise.
- **Also for the decision:** the six in-app charge-timing strings and the Help copy (Finding 11,
  Phase 8.3b) get rewritten to whichever option is chosen.
- **Open questions:**
  - POK's refund fees (Finance);
  - consumer-terms wording for charging a deposit before the service (Legal);
  - Help and copy rewrite (Finding 11).

**C-5 — Webhook fan-out** (Finding 8; not a go-live blocker, but do it before real volume).
- Keep the handler body-blind; the architecture test must stay green.
- Debounce it: `SET pok:recon:debounce 1 NX EX 30` in Redis, and enqueue the job only when the key
  was set (fail-open on Redis errors, like `SlotLocker`).
- Give anonymous callers their own IP-partitioned rate-limit policy, instead of the shared
  `"unknown"` bucket of `billing`.
- Optionally reconcile only the payment whose `merchantCustomReference` (= `PaymentId`) appears in
  the body. That is safe even unsigned, because state is still re-read from POK — but it is a design
  change to ADR-0001's "never read the body", so it needs an ADR amendment first.
- **Close B1's residual race.** The job can mark a row `Failed` after B4 re-minted a fresh order on
  it (§10, B1). Fix it with a concurrency token on `Payment` (e.g. `IsConcurrencyToken()` on
  `ProviderReferenceId`), plus the model-snapshot migration EF requires, or with
  `[DisableConcurrentExecution]` plus a short-lived Redis lock shared with
  `ResolveOrCreateHoldAsync`. A conditional `ExecuteUpdateAsync` would also work, but needs tests
  on a relational provider, because EF InMemory can't run it.
- **Stop one failed save from spoiling the run** (§10, B1). Give the job a way to detach a payment
  whose save failed (for example, expose `ChangeTracker` or an `Entry(...).State` setter on
  `IAppDbContext`), or use a fresh context per payment through `IAppDbContextFactory`.

**C-6 — Questions to POK, in writing.** These extend Amendment D's list, with the gate's evidence
attached:
1. How long can an authorised (`autoCapture: false`) order be captured after its `expiresAt`?
   (Phase 7.)
2. What does `refund` do on an authorised, uncaptured order? (T5a without B3.)
3. EUR on a merchant without a EUR account: is the order rejected or converted? (T1E.)
4. Which events send webhooks? Is there a retry policy? Will webhooks be signed?
5. Card fees and settlement: why does the staging Lek balance (116) differ from gross receipts
   (160)? (E9.)
6. Is "POK credit" (wallet) payment on `autoCapture: false` orders supported, and does it behave
   like a card hold?
7. Can an order take several partial refunds?
8. Will `isConfirmed`, `isCaptured`, `capturedAmount`, `refundableAmount`, `status` and `can` be
   documented and kept stable?
9. **Privacy:** the unauthenticated `GET /sdk-orders/{id}` returns the merchant's contact details.
   Is that intended?

**C-7 — Check POK keys at connect time** (Finding 10).
- Before saving, `ConnectPokAccountCommand` calls `POST {BaseUrl}/auth/sdk/login` with the submitted
  keys.
- A non-200 → 422 "POK rejected these keys — copy all three values from the same API key." Nothing
  is saved and nothing is logged beyond status code + studio id.
- Optional: a `GET /merchants/{merchantId}/…` check that the Merchant ID belongs to those keys.
- This touches `Application/Payments`, so Help must change: `owner-connect-pok` drops the "TattooOS
  doesn't test your keys" tip.

**C-8 — Vault resilience before go-live** (Finding 9, ADR-0002).
- Resolve ADR-0002's manual-unseal gap: auto-unseal or a documented on-call runbook with an alert on
  `vault status` sealed.
- In `PokAuthClient`, tell "secrets store unavailable" (503 plus an ops alert, message "Card payments
  are temporarily unavailable") apart from "secret missing" (422 reconnect). This needs
  `ISecretsProvider` to surface the difference; VaultSharp exposes the HTTP status (503 sealed vs
  404).
- Map `ConnectPokAccountCommand`'s Vault failure to 503 with an actionable message instead of a
  bare 500 (E12).

**C-9 — Saved-card tokenisation** (Finding 13). A separate prompt. Source: the Project doc
`claude/pok-api-dashboard-reference-2026-10-03.md`, Parts 2 and 5.1 (the endpoint is
`/credit-debit-cards/tokenize-guest-card`, three required `csFlexCard` fields the SDK's
`AddCardForm` never provides, and a response DTO mismatch).

**C-10 — Cosmetic labels.** "Stripe PI" → "POK order" in `PaymentDetailPage.tsx` (line 347, test
line 184) and `PaymentInvoiceService.cs` (line 149).

**C-11 — Production first-transaction protocol** (go-live: **yes**). Do this after C-2 (production
config), C-8 and the gate:
1. One real studio owner connects **production** keys.
2. Check that `pokEnvironment` reads `"production"` in the capabilities response.
3. Make one small real card deposit through the client flow (e.g. 100 ALL) with a real card.
4. Capture it, refund it, and check both in POK's production dashboard.
5. Only then enable more studios.

This mirrors the common practice of a live-mode test charge before launch.

**C-12 — Fees and settlement reconciliation** (Finance).
- Reporting uses gross. Whether POK settles net of fees is unknown: order `7373e46c` showed zero
  commission with `coversCommissionFees: true`. Decide whether revenue reports show POK fees once
  C-6 item 5 is answered.
- **Also for Finance:** `MarkNoShowCommand`'s cash branch marks a declared-but-never-received cash
  deposit `Paid` (without `PaidAt`). Decide whether a no-show's unpaid cash deposit should be
  recorded as owed, written off, or left out (B6 leaves it alone).

**C-13 — A kill switch and the go-live owner tour step.**
- **Kill switch:** a config flag `Payments:CardDepositsEnabled`, `false` in production until Phi
  approves go-live. `GetPaymentCapabilitiesQuery` returns `provider_disabled` and `CardCurrencyGuard`
  refuses when it's off.
  - Without it, today's only safeguard is that production points at POK staging (E11), plus Phi's
    own note (2026-10-07) that production has no studios or payments yet.
  - A flag like this is the usual way to stage a payments rollout. That's engineering judgement,
    not a cited benchmark.
- **Tour step:** ship the dedicated "Connect POK" step (Phase 8.5) in the same change that turns the
  flag on.

**C-14 — Several partial refunds per payment.** POK may allow several (C-6, item 7); the app allows
one, because `RefundPaymentCommand` requires `Status == Paid`. Stripe and Square allow several.

**C-15 — Tell the owner when a held deposit is lost.** B2 only logs. Add an owner notification
(email + realtime) so the studio can ask the client to pay again. The same goes for B3's forfeit
capture that POK refused: the deposit stays on hold, and the owner should be told to capture it.

**C-16 — Cancellation side effects run before the provider call** (Finding 4).
`CancelAppointmentCommand` deletes the reminder jobs (line 75) before it talks to POK, and saves
only at line 149. When the provider call throws, the appointment stays active but has lost its
reminders.
- **Fix:** move the job deletion after `SaveChangesAsync`.
- **No change needed for the waitlist claim** (line 87). `ClaimNextMatchAsync` only mutates
  tracked entities, which that same save persists.
- **Status:** folded into B3, because B3 makes a refused cancellation an expected outcome. It stays
  here only if Phi declines B3.

**C-17 — Stop framework URI logging for the POK client** (go-live: **yes**; a one-line fix).
- **Why:** `services.AddHttpClient("Pok", …)` (`InfrastructureServiceExtensions.cs` line 191) keeps
  `IHttpClientFactory`'s default logging handlers. Serilog's default level is `Information`, and
  `appsettings.json` (lines 107–113) overrides only `Microsoft.AspNetCore` and EF's
  `Database.Command`. So every POK call logs "Start processing HTTP request {HttpMethod} {Uri}" and
  "Sending HTTP request {HttpMethod} {Uri}".
  - Since .NET 9 those lines redact only the query string. The path
    `/merchants/{merchantId}/sdk-orders/{orderId}/…` keeps the merchant id and the full order id.
  - Anyone holding a full order id can read the order, merchant contact details included, for 24 h
    (E3).
  - As of 2026-10-07 no deployed environment has made a POK call: production has no studios
    (Phi's note) and staging can't connect (E12). The first real payment would put both values
    into production's Loki.
- **Fix (pick one):**
  - call `.RemoveAllLoggers()` on the `AddHttpClient("Pok", …)` builder;
  - or add the Serilog override `"System.Net.Http.HttpClient.Pok": "Warning"` under
    `Serilog:MinimumLevel:Override` in `appsettings.json`.

  Keep the app's own `POK order {OrderRef}` and `POK request failed` lines.
- **Verify:** make one POK call locally, then grep the API log for `sdk-orders/`. Expect no match.
- **Help:** none (no user-visible surface). Say so in the commit body.

**Legal (separate track).** Whether Albanian studios may charge consumer deposits in EUR at all.
This gate doesn't decide it, and the EUR run doesn't imply it.

---

## 18. Test requirements (consolidated)

**Unit** (`tests/Pena_e_Arte.UnitTests`):
- **`PokPaymentProviderTests`**
  - the new real-shape theory, `GetStatusAsync_MapsRealPokFlagShapesToNormalizedStatus` (§9.3);
  - `GetStatusAsync_HeldOrderWithIsCompleted_IsNotReportedAsCaptured`;
  - `GetStatusAsync_NullUndocumentedFields_DoNotThrow`;
  - `CreateSut` with a logger;
  - `CaptureAsync_PartialAmount_SendsWholeUnitAmount` (B3);
  - `CaptureAsync_Partial409WithSameCapturedAmount_Succeeds`,
    `CaptureAsync_Partial409WithDifferentCapturedAmount_Throws` and
    `CaptureAsync_Partial409WithoutCapturedAmount_Throws` (B3);
  - the no-`webhookUrl` body test, only if Phase 6's request-shape fix was applied;
  - the existing tests stay green, updated only if Phase 6 changes `ToPokAmount`.
- **`PaymentReconciliationJobTests`**
  - every B1/B2 case in §10, including the two cancelled-appointment held-pass cases;
  - the helper changes (`SeedPayment` seeds a real appointment and takes an optional studio id;
    `SeedAppointment` takes a `DepositStatus` and an `AppointmentStatus`).
- **`CancelAppointmentHandlerTests`** — the B3 cases: the renamed studio-cancel release test, the 0%
  capture test and its failure theory, the 50% capture test and its failure test, the two
  release-refused cases, the reminder-order test and the rounding case. `CreateSut` passes a
  `NullLogger`; the file gains the three usings B3 lists. The `Paid` cases stay unchanged.
- **`CaptureDepositHandlerTests`** — `Handle_ForfeitedDeposit_StaysForfeitedAfterCapture` (B3).
- **`CreateDepositPaymentHandlerTests`, `PayDepositWithSavedCardHandlerTests`,
  `CreatePaymentIntentHandlerTests`** — the B4 cases.
- **`CompleteAppointmentHandlerTests`** and the new **`MarkNoShowHandlerTests`** — the B6 cases.
- **`NullPaymentProviderTests`** — the new overload throws.
- **`PaymentArchitectureTests`** — the three existing tests stay green and untouched.

**Integration** (`tests/Pena_e_Arte.IntegrationTests`):
- one held-deposit studio-cancel case (B3), asserting the persisted payment and appointment state.
  Every `CancelAppointmentHandler` construction gains a `NullLogger`:
  `AppointmentHandlerIntegrationTests` lines 143, 157, 175, 197 and 223, and
  `WaitlistHandlerIntegrationTests` line 129;
- if B4 shipped, `CreatePaymentIntent_PersistsHoldExpiresAt` in `PaymentHandlerIntegrationTests`;
- all existing `PaymentHandlerIntegrationTests`, `CashPaymentIntegrationTests`,
  `AppointmentHandlerIntegrationTests` and `WaitlistHandlerIntegrationTests` cases stay green.

**Frontend** (`frontend/src/features/payments/__tests__/`, plus one appointments test):
- **B5:** `PaymentListPage` and `PaymentDetailPage` show "On hold" for `Captured`, and the new
  empty-state text.
- **Phase 8.3b, if applied:** update the line-194 assertion in `DepositCheckoutPage.test.tsx` to the
  new sentence, assert the new sentence once in `PaymentMethodSelector.test.tsx`, and add one
  `Captured` case to `describe("MyBookingsSection")` in
  `frontend/src/features/appointments/__tests__/BookPage.test.tsx`.
- Keep the existing loading, error and empty-state tests green.
```bash
pnpm lint && pnpm build && pnpm test --coverage
```

**Live (this gate):**
- T1–T9 for ALL, and T1E–T3E for EUR if D3 = yes, with the evidence recorded (Appendix A).
- The flag sets observed in T1 and Phase 7 become committed fixtures (§9.3, Phase 6).

**Gates before the PR:**
```bash
dotnet format "Pena e Arte.slnx" --verify-no-changes
dotnet build "Pena e Arte.slnx" --configuration Release
dotnet test tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj
dotnet test tests/Pena_e_Arte.IntegrationTests/Pena_e_Arte.IntegrationTests.csproj   # or note CI-only, see §10
cd frontend && pnpm lint && pnpm build && pnpm test --coverage
```

---

## 19. Help-sync obligations (consolidated)

| Phase / item | `helpContent.ts` | Manual (`frontend/public/user-manual/index.html`) | Tours |
|---|---|---|---|
| 0, 3 | — (no product change) | — | — |
| 1 (`MapStatus`) | No text change. Phase 8 documents the verified held/capture behaviour. | — | — |
| B1 | `owner-payments`: self-healing tip (no webhook promise) | `owner-payments` tip | none (no new element) |
| B2 | `owner-payments`: lost-hold tip | `owner-payment-detail` tip | none |
| B3 | `client-cancel-booking` tip (with the late-cancel clause); `faq-client-cancel-refund`; `owner-deposit-rule-create` tip; `owner-payments` B3 tip | `client-book` "Cancellation policy" callout (line 1002), `ref-deposit-statuses` (Refunded row), `owner-create-deposit-rule` | none |
| B4 | `client-deposit-pay`: fresh-form tip; `owner-payment-create`: 24 h link tip | `client-deposit`, `owner-create-payment` | none |
| B5 | `owner-payments`: statuses tip with "On hold" | `ref-deposit-statuses`, `owner-payments` and `owner-payment-detail` mock-ups | none (no step targets the pill) |
| B6 | `owner-reports`: the revenue-rule sentence (line 1470) replaced; `artist-earnings` checked, no change | `owner-reports` revenue-rule sentence (line 3415) replaced | none |
| 4, 5 | — (evidence only; record the checkout and confirmation wording for Phase 8) | — | — |
| 6 | none — a mapping, unit or request-shape fix (state it in the commit body) | — | — |
| 7 | the hold-window tip and charge-timing copy, **only if** the window is known | `client-deposit` | none |
| 8 | `owner-connect-pok` (E8 steps + 2 tips); `owner-payments` (the final tips list); `faq-deposit-statuses` fix; Phase 8.3b in-app strings (if holds survive) | new `#owner-connect-pok` section + nav entry (fixes the dead link at line 3072); `owner-payments` callout-tip (statuses, capture, refund); `client-deposit` charge-timing sentence and wireframe (lines 1372, 1379–1380) | **No new step in this run.** It ships with go-live (Phase 8.5, C-13). |

`docs/user-manual.html` is the stale, unserved copy, so don't edit it. Before editing, confirm that
`frontend/public/user-manual/index.html` is still the one served:
- `frontend/vite.config.ts` sets no `publicDir`, so Vite copies `public/` into `dist/`;
- the frontend Dockerfile copies `dist/` into nginx;
- `.dockerignore` excludes `docs/`;
- CI's `help-sync` gate (`ci.yml`, about line 412) names `frontend/public/user-manual/index.html`.

---

## 20. Industry-standard benchmark notes (with sources)

**Hold windows and capture semantics.**
- Stripe gives card-not-present, customer-initiated authorisations **7 days** for Visa and
  Mastercard (Visa merchant-initiated: about 5 days).
- An authorisation that isn't captured in time is released, and the payment becomes `canceled`.
- A partial capture automatically releases the remainder, and most payments allow only one capture.
- Source: Stripe, "Place a hold on a payment method",
  https://docs.stripe.com/payments/capture-later (canonical path now
  `/payments/place-a-hold-on-a-payment-method`). Its `automatic_delayed` capture is a private
  preview.
- Square's delayed capture gives the same window: 7 days for online (card-not-present) payments,
  36 hours in person. After that, Square cancels the payment by default. An authorised,
  not-yet-completed payment has status `APPROVED`.
  https://developer.squareup.com/docs/payments-api/take-payments/card-payments/delayed-capture
- **Applied here:** B3 (release vs partial capture), Phase 7 and C-4.

**Deposits in the vertical.** The category charges deposits **at booking**:
- Square Appointments: "Deposits are collected when a client books online".
  https://squareup.com/help/gb/en/article/8096-deposits-on-square-appointments
- GlossGenius: deposits are charged at the time of booking, and are non-refundable.
  https://glossgenius.com/blog/does-glossgenius-let-you-take-deposits-yes
- Vagaro: a partial deposit is charged at booking; its "hold with a credit card" option stores the
  card without authorising it. https://www.vagaro.com/learn/deposits-for-hair-appointment-service
- Fresha: clients confirm by paying a deposit, and the business decides when it becomes
  non-refundable.
  https://www.fresha.com/help-center/knowledge-base/payments/613-payments-policies-overview
- Tattoo-specific: Porter restricts the calendar to customers who have paid a deposit.
  https://getporter.io/tattoo-scheduling-software

TattooOS's "authorise at booking, capture at the appointment" is a **divergence**. It isn't wrong in
itself, but it is only viable within the authorisation window. Phase 7 provides the evidence, and
C-4 is the decision. Label it a divergence wherever it is documented.

**Webhooks and reconciliation.**
- Stripe's webhook guidance: events can arrive more than once and out of order; return 2xx quickly
  and process asynchronously; retrieve objects from the API rather than relying on one event; verify
  signatures. https://docs.stripe.com/webhooks
- POK sends unsigned webhooks, so ADR-0001's "trigger, then re-read" is the right adaptation.
- What fell short was the reconciliation behind it (Finding 2, B1). The debounce (C-5) fits the
  "process asynchronously, don't amplify" guidance.

**Amount units.**
- Stripe expects amounts in the currency's **minor unit**, and has documented per-currency exceptions
  (e.g. ISK and UGX are sent in two-decimal form for backward compatibility).
  https://docs.stripe.com/currencies
- POK's API represents amounts in **major units** (E5) — another divergence across providers. The
  only safe practice is what this gate does: verify per provider and per currency, with
  distinctive amounts, on every write path (create, capture, partial capture, refund).

**Status labels.** Stripe's Dashboard labels the held state "Uncaptured" (capture-later page above);
POK's dashboard labels it "On hold" (E2); Square's API calls it `APPROVED` (delayed-capture page
above). "Captured" next to a "Capture deposit" button matches none of them (B5).

**Rollout controls** (engineering judgement, not a cited benchmark). A staged go-live usually means
two things: one live-mode test transaction before launch (C-11), and a kill switch or feature flag
(C-13). Today the app has neither. Only the accidental production misconfiguration (E11) stands
between a connected studio and real money.

**Rule-6 summary for the PR.**
- **Brings us to the standard:** B1–B5 and Phase 1. B6 is a plain correctness fix.
- **Flagged, not built:** C-4, C-5, C-7, C-13 and C-14.
- **Explicit divergences:** POK's major-unit amounts (provider fact); the authorise-then-capture
  deposit model (pending C-4).

---

## 21. Final self-check (run before declaring done)

- [ ] Phi answered D1–D6 at C0; the answers are in the evidence log and summarised in the PR.
- [ ] No file in §6's "do not touch" list changed. Check with `git diff --name-only main...HEAD`
      against §6.
- [ ] No migration was generated. No `appsettings*.json`, `docker-compose.yml`, `k8s/**` or
      `.github/**` change.
- [ ] `dotnet format` is clean, the Release build is clean, and unit tests are green. Integration
      tests are green locally, or explicitly left to CI with the reason. The frontend lint, build and
      tests are green.
- [ ] `PaymentArchitectureTests` is untouched and green.
- [ ] No app-authored log line contains a raw POK body, a key, a merchant id, a full order id, an
      email, a phone number or card data. **Known and out of scope:** `IHttpClientFactory`'s own
      `System.Net.Http.HttpClient.Pok.*` lines carry the merchant id and full order ids in the URI
      path (§7, C-17). Never paste or attach those lines.
      - Grep the whole run's API log as below. Every compact-JSON line contains `@t`/`@mt`, so a
        bare `@` matches everything. A body logged as a property would be escaped, hence
        `merchant\\?"`. Expected: no output.
        ```bash
        docker compose logs api --since 48h | grep -E '[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+\.[A-Za-z.]{2,}|keySecret|coversCommissionFees|merchant\\?":'
        ```
      - `docker compose logs` reads only the current container, and every `up --build` recreates
        it. Run the same grep over the logs you saved before each rebuild (§14), or over the stack's
        Loki: Grafana at `http://localhost:3000` (Phi logs in; the password is in `.env`), Explore,
        `{container="pena_e_arte_api"}`, last 48 h. The local Loki keeps 48 h.
- [ ] `PokPaymentProvider`'s class doc comment (lines 18–29) records what was verified, when, and
      what is still open (Phase 8.9).
- [ ] The post-T1 fixture update is committed (§9.3), or the evidence log says why T1 didn't
      produce a held shape.
- [ ] Every test T1–T9 (and T1E–T3E if D3 = yes) has a verdict: PASS, FAIL or OBSERVED, with a
      reason. No blanks.
- [ ] Every observed POK flag shape (held, captured, partially refunded, canceled, lapsed) is either a
      committed fixture or explicitly noted as not observed.
- [ ] The runbook §3.1 result entry exists, is dated, uses 8-character order prefixes only, and says
      **"Go-live: NOT approved by this gate"**.
- [ ] The Help surfaces in §19 are updated for every shipped item, the dead `#owner-connect-pok`
      link is fixed, and the tours verdict is stated.
- [ ] `architecture.md`: the Decisions Log row is added, the AllowAnonymous row is corrected, and
      the Module Map is noted. `frontend.md`'s Payment UI Pattern is rewritten.
- [ ] `docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md` exists, with this run's results.
- [ ] The 2026-10-02 sync prompt's §6.4 is marked superseded, or its runbook paragraph replaced
      (Phase 8.2). If D6 = no, the edit is on disk only and the report says so.
- [ ] Phase 8.3b is done (holds survive) or listed under C-4 in the backlog doc (holds die).
- [ ] No secret was ever pasted in chat. If one was, the key was rotated before continuing.
- [ ] Day-2 (Phase 7) is either done, or the PR and runbook entry say "aging probe pending" with the
      date it's due. Don't merge the "hold window" Help tip until it's done.

---

## 22. Final deliverables

**Branch:** `fix/pok-staging-release-gate-2026-10-07`. Commits in order:
1. the checkpoint;
2. the D6 docs commit, if approved;
3. Phase 1;
4. each approved B-item;
5. Phase 6 fixes, if any (each committed when its failure is seen, in Phase 4, 5 or 7);
6. the post-T1 fixture update (§9.3);
7. the Phase 8.3b in-app strings, if applied;
8. the Phase 8 docs.

**Files expected to change:**
- **Code:**
  - `Pena_e_Arte.Infrastructure/Services/PokPaymentProvider.cs`
  - `Pena_e_Arte.Infrastructure/Services/NullPaymentProvider.cs` (B3)
  - `Pena_e_Arte.Infrastructure/Jobs/PaymentReconciliationJob.cs` (B1/B2)
  - `Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs` (B3)
  - `Pena_e_Arte.Application/Appointments/Commands/CancelAppointmentCommand.cs` (B3)
  - `Pena_e_Arte.Application/Payments/Commands/CreateDepositPaymentCommand.cs` and
    `CreatePaymentIntentCommand.cs` (B4)
  - `Pena_e_Arte.Application/Appointments/Commands/CompleteAppointmentCommand.cs` and
    `MarkNoShowCommand.cs` (B6)
  - `frontend/src/features/payments/paymentStatusLabels.ts` (new), `PaymentListPage.tsx` and
    `PaymentDetailPage.tsx` (B5)
  - `frontend/src/features/payments/components/PaymentMethodSelector.tsx`, `DepositCheckoutPage.tsx`
    and `frontend/src/features/appointments/components/MyBookingsSection.tsx` (Phase 8.3b, if
    applied)
  - `Pena_e_Arte.Application/Payments/Commands/CaptureDepositCommand.cs` (B3's forfeit guard)
  - the tests for all of the above, including the new
    `tests/Pena_e_Arte.UnitTests/Appointments/MarkNoShowHandlerTests.cs` and the `NullLogger`
    updates in `AppointmentHandlerIntegrationTests.cs` and `WaitlistHandlerIntegrationTests.cs`
- **Help:** `frontend/src/features/help/helpContent.ts` and `frontend/public/user-manual/index.html`.
- **Docs:**
  - `docs/payments/runbook-studio-currency-migration-2026-09-27.md`
  - `docs/claude/architecture.md`
  - `docs/claude/frontend.md`
  - `docs/claude/overnight-prompt-pok-final-decision-sync-2026-10-02.md` (one line, Phase 8.2;
    committed only if D6 = yes)
  - `docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md` (new)
  - the D6 docs, if approved

**Final docs commit message:**
```
docs(payments): record the POK staging release gate — amounts, status mapping, webhook, aging probe

Runbook §3.1 result entry (sanitized evidence, verdicts, fixes, go-live still NOT approved);
Help: owner-connect-pok (Merchant ID/Key ID/Key Secret shown once at key creation), owner-payments
(statuses, capture, refund), client Help and manual copy aligned to verified behaviour; manual
gains the missing #owner-connect-pok section; architecture.md Decisions Log + corrected webhook
row; frontend.md Payment UI Pattern now describes POK; go-live blockers spec added. Tours: no new
step until go-live.
```

Adjust that body to what actually shipped. For example, if Phase 8.3b was deferred to C-4, say
"client Help copy unchanged pending C-4" instead.

**PR title:** `POK staging release gate: verified amounts and status mapping, money-path fixes, go-live blockers`

**PR body must contain:**
- a verdict table (per test);
- Phi's C0 answers;
- the list of shipped B-items and declined ones;
- the Help-sync verdict for every phase, including "no tour change, because…";
- the Rule-6 summary (§20);
- the go-live blockers that remain (link to the backlog doc);
- the sentence: **"Merging this does not enable card deposits for any studio."**

End the PR body with the attribution lines your harness requires.

**Final report to Phi (in chat):**
- the verdicts;
- the held-state shape, and whether the old mapping would have lost money;
- the aging-probe result or its due date;
- the files changed;
- any uncommitted local change (e.g. the Phase 8.2 edit when D6 = no);
- the E15 drift ("guard test shipped" row) that is still open;
- the next three go-live actions in order (typically C-4 decision → C-8 → C-2 production + C-11).

---

## Appendix A — Gate evidence log (keep in `~/pok-gate-2026-10-07/evidence.md`, not in the repo)

```markdown
# POK staging release gate — evidence (2026-10-DD)
Commit: <sha>   Env: <local|staging>   POK host: https://api-staging.pokpay.io
C0 answers: D1=… D2=… D3=… D4=… D5=… D6=…

## T1 — hold (ALL 1234)
Time (UTC): …   Appointment: …   Payment: …   POK order (full, local only): …
Checkout showed: "Pay 1,234.00 ALL"
POK public read: amount=… finalAmount=… currencyCode=… isConfirmed=… isCompleted=… isCaptured=… canBeCaptured=… isCanceled=… isRefunded=… refundableAmount=… status=… can=…
App flag log (JSON properties of the "POK order {OrderRef}…" line): IsConfirmed=… CanBeCaptured=… IsCaptured=… IsCompleted=… IsCanceled=… IsRefunded=… CapturedAmount=… PokStatus=… Mapped=…
DB: payments.Status=… Amount=… Currency=… HoldExpiresAt=… PaidAt=…; appointments.DepositStatus=…
Dashboard (Phi): badge=…   Verdict: PASS/FAIL/OBSERVED — reason
Old MapStatus would have returned: …
## T2 … T9, T1E–T3E (same layout)
## Phase 7 — aging probe (+N h)
Reconciliation flag line for T9 (02:00 run or triggered): …   Capture attempt result: …   Verdict: …
## T9b (optional) — capture at +8 days: …
```

## Appendix B — Read-only commands

**Local (D1 = A).** Never print the `merchant` object, keys or emails.

**Reading the API log.** The console sink writes compact JSON (`CompactJsonFormatter`):
- the message template is in `@mt` and every value is a top-level property, e.g. `"Mapped":"Authorized"`,
  `"RequestPath":"/api/v1/webhooks/pok"`;
- the `StatusCode` of a `POK request failed` line is the enum name (`BadRequest`, `Conflict`, …), not the
  number.

Look at one line's format before you match on anything (§9.2 item 4). The greps below match the
template text, so they work either way.

```bash
# Gate studios and their ids (studio ids are not secrets; never print PokMerchantId itself)
docker compose exec -T mysql mysql -uroot -proot pena_e_arte_dev -e "
SELECT Id, Name, Currency FROM studios WHERE Name LIKE 'Gate %';"

# Latest payments for a studio (order reference truncated in anything you copy into the repo)
docker compose exec -T mysql mysql -uroot -proot pena_e_arte_dev -e "
SELECT Id, AppointmentId, Status, Method, Amount, Currency, LEFT(ProviderReferenceId,8) AS OrderRef,
       HoldExpiresAt, PaidAt, RefundedAmount, UpdatedAt
FROM payments WHERE StudioId = '<studioId>' ORDER BY CreatedAt DESC LIMIT 10;"

# Full order id for one payment (terminal only — needed for curl / POK hosted page; never commit it)
docker compose exec -T mysql mysql -uroot -proot pena_e_arte_dev -N -e "
SELECT ProviderReferenceId FROM payments WHERE Id = '<paymentId>';"

# Appointment deposit state
docker compose exec -T mysql mysql -uroot -proot pena_e_arte_dev -e "
SELECT Id, Status, DepositAmount, DepositStatus FROM appointments WHERE Id = '<appointmentId>';"

# Studio connection state (no ids printed)
docker compose exec -T mysql mysql -uroot -proot pena_e_arte_dev -e "
SELECT s.Name, s.Currency, s.PokMerchantId IS NOT NULL AS HasMerchantId,
       (SELECT COUNT(*) FROM StudioCredentialRefs c WHERE c.StudioId = s.Id AND c.Provider = 'Pok') AS PokRefs
FROM studios s WHERE s.Name LIKE 'Gate %';"

# POK public read — ONLY within 24 h of order creation (E3); prints whitelisted fields only
ORDER=<full order id>
curl -s "https://api-staging.pokpay.io/sdk-orders/$ORDER" | python3 -c '
import json,sys
d=json.load(sys.stdin); o=(d.get("data") or {}).get("sdkOrder") or {}
keys=["amount","finalAmount","capturedAmount","refundableAmount","currencyCode","originalAmount",
      "originalCurrencyCode","appliedExchangeRate","autoCapture","isConfirmed","isCompleted","isCaptured",
      "canBeCaptured","isCanceled","isRefunded","status","can","expiresAt"]
print({"statusCode": d.get("statusCode"), "error": d.get("error"), "message": d.get("message"),
       **{k:o.get(k) for k in keys}})'
# A 410 body has no statusCode: it prints error="Gone", message="Order is expired" (E3).
# E1's undocumented fields (isConfirmed, refundableAmount, status, can) were read from the
# merchant-scoped endpoint. If this public read prints None for them, use the app's flag log line.

# App's own view of POK (flag log, Phase 1) + webhook arrivals + provider errors
docker compose logs api --since 15m | grep -E 'POK order|webhooks/pok|POK request failed|healed to held|lost its provider hold|PaymentReconciliationJob'

# Trigger reconciliation now (exactly what POK's webhook does)
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://localhost:8080/api/v1/webhooks/pok
```

`MYSQL_HOST_PORT` only matters for host-side clients. `docker compose exec` goes through the
container, so the 3306/3307 difference doesn't apply.

**Deployed staging (only if D1 = B).**

```bash
kubectl -n tattooos-staging logs deploy/tattooos-api --since=15m | grep -E 'POK order|webhooks/pok|POK request failed'
```

For the database, use whatever read-only access Phi has already set up. Never print connection
strings or secrets.

## Appendix C — Observed POK flag shapes (sanitised; the source for §9.3's fixtures)

```json
{"label":"created, unpaid, read before expiry (86d07767)","autoCapture":false,"isConfirmed":false,"canBeCaptured":false,"isCaptured":false,"isCompleted":false,"isCanceled":false,"isRefunded":false,"capturedAmount":0,"status":"created"}
{"label":"unpaid, 3DS failed, expired (2b8d3d2d)","autoCapture":true,"isConfirmed":false,"canBeCaptured":false,"isCaptured":false,"isCompleted":false,"isCanceled":false,"isRefunded":false,"capturedAmount":0,"status":"expired"}
{"label":"paid, autoCapture (ae4506ec)","autoCapture":true,"isConfirmed":true,"canBeCaptured":false,"isCaptured":true,"isCompleted":true,"isCanceled":false,"isRefunded":false,"capturedAmount":100,"status":"paid"}
{"label":"manual capture 60/100 (7373e46c)","autoCapture":false,"isConfirmed":true,"canBeCaptured":false,"isCaptured":true,"isCompleted":true,"isCanceled":false,"isRefunded":false,"capturedAmount":60,"status":"paid"}
{"label":"HELD — fill from T1","autoCapture":false,"isConfirmed":"?","canBeCaptured":"?","isCaptured":"?","isCompleted":"?","isCanceled":"?","isRefunded":"?","capturedAmount":"?","status":"?"}
{"label":"LAPSED HOLD — fill from Phase 7","…":"…"}
```
