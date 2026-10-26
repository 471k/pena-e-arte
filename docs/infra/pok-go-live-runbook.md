# POK go-live runbook: switching production to real card deposits

**Status:** written 21 Oct 2026, not run yet · **Who:** Phi decides and gives the go; Claude Code can
prepare the PRs and run the read-only checks
**Related:** `docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md` (C-2, C-11, C-13),
`k8s/overlays/production/api-config-patch.yaml`, `docs/infra/vault-self-hosted-runbook.md`

---

Today production (app.tattooos.co) talks to **POK staging** and card deposits are **off**
(`Payments__CardDepositsEnabled: "false"`), so no real money can move. Going live is three
config values in `k8s/overlays/production/api-config-patch.yaml` plus the "Connect POK" tour step
(draft PR #278). Nothing in this runbook happens without Phi's explicit go.

Go live in **two deploys**, not one:

1. **Point production at POK production**, card deposits still off. Owners can now connect their
   production POK keys (the connect check asks the POK environment the app talks to, so production
   keys are refused until this step). Clients still see cash only.
2. **Turn card deposits on** and ship the tour step. Run the first real-money tests (C-11) at once.

Splitting them means no client ever sees a card option for a studio whose saved keys belong to the
wrong POK environment, and the first studio (ours) is connected and checked before money can move.

## Before step 1: gates

All of these must be true. Tick them in the blockers doc, not here.

- [x] **C-4 decided** (22 Oct 2026): charge the deposit at booking; Legal and Finance agreed. The change that
  charges card deposits when the client pays must be live before go-live; the C-11 tests confirm it with real money.
- [ ] **Legal sign-off for EUR deposits**, only if any studio will take deposits in EUR.
- [ ] **POK's written answers**, at least on production pricing, refund timing and privacy (email
      questions Q1, Q2, Q6), or Phi decides to go without them. The hold limit (Q4) no longer
      blocks: deposits are charged at booking.
- [ ] **Production POK merchant account ready:** Phi has the production key ID, key secret and
      merchant ID for our own test studio (kept in KeePass, never in chat or the repo).
- [ ] **Vault unsealed and the app's token valid.** Claude can check both without seeing a secret:
      ```powershell
      kubectl -n tattooos exec tattooos-vault-0 -- vault status   # expect: Sealed false
      ```
      and in Loki, no `VaultHealthCheckFailed` line in the last 15 minutes. If Vault is sealed,
      Phi unseals it (`vault-self-hosted-runbook.md`); every connect, capture and refund returns 503
      while it is sealed.

## Before step 1: read-only checks on the production database

Each query needs Phi's OK at the time (read-only pod, as in the production verification playbook;
select ids and counts only, never names or emails).

```sql
-- Studios that saved POK keys on production. Those keys are POK *staging* keys (production
-- has only ever talked to POK staging), so each of these studios must reconnect after step 1.
SELECT LEFT(StudioId, 8) AS studio, CreatedAt
FROM StudioCredentialRefs
WHERE Provider = 'Pok' AND DeletedAt IS NULL;

-- Card payments still open at POK staging. After step 1 the app can no longer reach them.
SELECT Status, COUNT(*) AS n
FROM payments
WHERE Method = 'Card' AND Status IN ('Pending', 'Captured') AND DeletedAt IS NULL
GROUP BY Status;
```

- **Open card payments must be 0** before step 1. They are POK staging payments, so no real money is
  involved, but one left open stays stuck: after step 1 the nightly sync can't reach it and logs
  "Failed to reconcile …" for it every night. Settle them while the app still talks to POK staging:
  - `Captured` means held, not yet charged: the owner captures, refunds or releases each one;
  - `Pending` means an order nobody paid: the nightly sync cancels it once its hold time has passed
    (or at once if the appointment is cancelled). Wait for that, then re-run the query.
- **Note the connected studios.** Each owner must use "Reconnect / change account" on the POK card
  (Studio settings) with production keys after step 1. Until they do, nothing breaks while card
  deposits are off; after step 2 their clients would get an error at checkout. So step 2 waits
  until every listed studio has reconnected.

## Step 1: point production at POK production (card deposits still off)

1. Claude opens a PR that changes two lines in `k8s/overlays/production/api-config-patch.yaml`:
   ```yaml
   Pok__BaseUrl: "https://api.pokpay.io"
   Pok__WebhookCallbackBaseUrl: "https://app.tattooos.co"
   ```
   `Payments__CardDepositsEnabled` stays `"false"`. Phi approves; CI green; merge; CD deploys
   (CD applies the ConfigMap and restarts the API pods, so the new values are live after the deploy).
2. Check the pods have the values (not secrets, safe to print):
   ```powershell
   kubectl -n tattooos exec deploy/tattooos-api -- printenv Pok__BaseUrl Pok__WebhookCallbackBaseUrl Payments__CardDepositsEnabled
   ```
   Expect `https://api.pokpay.io`, `https://app.tattooos.co`, `false`.
3. Check the webhook route still reaches the API: `GET https://app.tattooos.co/api/v1/webhooks/pok`
   returns **405** (it only accepts POST); an unknown path under `/api/v1/webhooks/` returns 404.
4. Phi connects our test studio on app.tattooos.co with the **production** keys (Studio settings →
   Card deposits — POK). The app checks them with POK production before saving: a refusal means
   wrong keys or wrong environment. The card still says card deposits aren't switched on yet.
5. The owners of the studios listed in the pre-check reconnect with their production keys.

**Rollback for step 1:** revert the PR. No payment exists at POK production yet, so nothing is
stranded. Studios that connected production keys must reconnect again later.

## Step 2: turn card deposits on

1. Rebase draft PR #278 (the "Connect POK" tour step) on main and add the third line to it:
   ```yaml
   Payments__CardDepositsEnabled: "true"
   ```
   One PR, one deploy: the feature and its tour step arrive together. Re-read the step text
   (`frontend/src/features/help/tours/ownerTour.ts`) against what the POK card shows at that time.
2. Mark #278 ready for review, CI green, Phi's go, merge; CD deploys.
3. Check `printenv` as in step 1 (`Payments__CardDepositsEnabled` now `true`), then:
   - signed in as a client of our test studio, the booking page offers card and cash;
   - the owner tour, in a real browser at 390 px and 1280 px wide, shows "Take card deposits with
     POK" on Studio settings, and the POK card no longer says card deposits are off.
4. **Run the first real-money tests now (C-11).** The list is PT1–PT13 in the internal section of the
   POK email draft (`claude-shared/pok-gate/fee/pok-email-draft.md`, outside this repo). Use our own
   cards and about 100 ALL per test; a full refund returns POK's whole fee, so the whole list costs
   roughly 50–100 ALL. The minimum before inviting any real studio:
   - pay a 100 ALL deposit by card: it shows **Paid** at once in the app (charged at booking, C-4),
     the "Deposit received" email arrives, and POK Business shows it confirmed with POK's fee
     (2.5 % + 20 ALL on staging);
   - refund 30 ALL, then the remaining 70 ALL, from the app; both show in POK Business and on the
     card's bank statement, for the full amounts;
   - book again, pay, and cancel inside the free window: the deposit is **refunded in full** and
     POK returns its whole fee;
   - POK's webhook reached production: a payment completed on POK's own page (tab closed before
     returning) turns Paid within seconds, not at the nightly sync.
5. Watch for a day: Grafana alerts, Loki for `Failed to reconcile`, `VaultHealthCheckFailed` and
   503 responses on `/api/v1/payments/`, and the POK Business dashboard.

## Emergency: turn card deposits off after step 2

**Never point production back at POK staging after step 2.** Held deposits now exist only at POK
production; switching the host back would leave the app unable to capture, refund or release them.
Turn off only the kill switch: new card payments stop, while capture, refund and release of the
payments already taken keep working.

Fast path (minutes, no CI), then make it permanent:

```powershell
'{"data":{"Payments__CardDepositsEnabled":"false"}}' | Out-File -Encoding ascii "$env:TEMP\card-off.json"
kubectl -n tattooos patch configmap tattooos-api-config --type merge --patch-file "$env:TEMP\card-off.json"
kubectl -n tattooos rollout restart deployment/tattooos-api
kubectl -n tattooos rollout status deployment/tattooos-api --timeout=180s
```

Then open a PR setting `Payments__CardDepositsEnabled: "false"` in the production overlay. **Until it
merges, the next CD deploy turns card deposits back on** (CD applies the overlay's value).

An order already handed to a client can still be paid on POK's hosted page until it expires (24 h);
the webhook and the nightly sync record it correctly as held. Clients see "Card payments are
temporarily unavailable" and can pay in cash.
