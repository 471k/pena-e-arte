# Overnight Prompt — Staging Goes Live (Tier 2)

> Feed this file directly to Claude Code (main **Pena e Artë - Engineering** project, full repo
> write access) as the task prompt, **alongside** `docs/infra/staging-environment.md` (already
> written, describes the mechanism in full). Read it before writing anything. **This file does
> not redesign staging — it lands and verifies what's already built.** This prompt must run
> **strictly after** `docs/claude/overnight-prompt-production-deploy-first-2026-09-03.md`'s own
> BLOCKING-MANUAL items are confirmed done — staging shares the production DigitalOcean MySQL
> cluster and the same Hetzner box, so production must already be live and stable before this
> session touches the cluster. **Mode: fully autonomous** for merge/CI/deploy mechanics, gated
> by the BLOCKING-MANUAL items in §2 below (all Phi-only real external credentials).

**Date logged:** 2026-09-03
**Requested by:** Phi
**Origin:** Engineering-consultation gap audit. Staging's manifests, CD jobs
(`build-and-push-frontend-staging`/`deploy-staging` in `cd.yml`), shared-observability wiring,
and the frontend `noindex`/banner treatment were all built and committed this session.

**Update, found after this file was first written (same day): PR #83 is merged.** Merging it
auto-triggered `cd.yml` — `build-and-push`/`build-and-push-frontend-staging` both succeeded
(real images now in GHCR), but `deploy` (production) failed at its very first `kubectl` call
with `dial tcp 49.13.66.15:6443: i/o timeout` — GitHub-hosted Actions runners cannot reach the
K3s API server through the Hetzner firewall's IP restriction (full detail and options in
`overnight-prompt-production-deploy-first-2026-09-03.md`'s own same-day update).
`deploy-staging` correctly self-gated to `skipped` rather than running against a failed
production deploy — confirmed working as designed, not a separate bug. **This means
`deploy-staging` cannot succeed either until that connectivity problem is resolved, regardless
of how complete this file's own §1-§3 gates are** — read the production prompt's update before
assuming this one's Phase (landing `deploy-staging`) can proceed.

**Precondition check before starting anything:**
```bash
git status                                        # must be clean
kubectl get pods -n pena-e-arte                   # production must be Running — if not,
                                                   # stop, this prompt is not unblocked yet
gh pr checks 83 --repo 471k/pena-e-arte           # confirm current CI status, don't trust
                                                   # this doc's stale snapshot
```
If production isn't `Running` in the `pena-e-arte` namespace, **stop immediately** and report
that Tier 1 (the production-deploy prompt) hasn't landed yet — do not attempt to apply the
staging overlay against a cluster where production itself isn't confirmed live, even though the
manifests are technically independent; the capacity check in §3 below is only meaningful once
production's real resource usage is known.

---

## 1. Merge PR #83 — already done, confirm rather than repeat

**PR #83 merged 2026-09-03T20:45:34Z** (merge commit `2eff4c96`), all 8 required checks green
at merge time. Two real things were found and fixed on the branch before it merged, on record
so this session doesn't re-derive them: a gitleaks false-positive on a slash-separated phrase in
the source prompt doc (fixed via a clean history rewrite — the branch was unmerged and
single-author, so this was safe), and a real `cd.yml` ordering bug where both `deploy` and
`deploy-staging` tried to create Secrets in a namespace before that namespace existed on a fresh
cluster (fixed with an idempotent "Ensure namespaces exist" step in both jobs). **This session's
only job for §1 is to confirm the merge is real**, not repeat it:
```bash
gh pr view 83 --repo 471k/pena-e-arte --json state,mergedAt,mergeCommit
git log --oneline main -5   # confirm the merge commit is actually on main
```
If this disagrees with what's stated above (PR reopened, reverted, etc.), stop and say so —
don't assume this doc is still accurate.

---

## 2. Populate staging's BLOCKING-MANUAL prerequisites (Phi only)

Full detail already written in `docs/infra/staging-environment.md` §"Prerequisites still
outstanding" — this table is a checklist, not a redefinition. **Never paste real values into
this session's output** — confirm presence only (`gh secret list`), same rule as every other
prompt in this series.

| Item | Produces | Notes |
|---|---|---|
| Cloudflare DNS `A` record `staging` → Hetzner box IP (proxied) | — | Same zone/token already used for `app` (Tier 1's `pena-e-arte-dns01` token). |
| DigitalOcean: `pena_e_arte_staging` DB + scoped user on the **existing** `pena-e-arte-prod-db` cluster | `STAGING_DB_CONNECTION_STRING` | Not a new DB cluster — a new database + user on the one from Tier 1. Confirm `trusted-sources` on that cluster already permits the Hetzner box's IP (it should, from Tier 1) rather than re-locking it. |
| Cloudflare R2: new `pena-e-arte-staging` bucket + scoped token | `STAGING_R2_ACCOUNT_ID` / `STAGING_R2_ACCESS_KEY_ID` / `STAGING_R2_SECRET_ACCESS_KEY` / `STAGING_R2_BUCKET_NAME` / `STAGING_R2_PUBLIC_URL` | A separate bucket from production's — staging file uploads must never land in the production bucket. |
| Stripe test-mode webhook, **`/api/v1/webhooks/stripe/billing` only** | `STAGING_STRIPE_WEBHOOK_SECRET_BILLING` | Do **not** also wire `/connect` — that route is orphaned from the Flow-A-Connect era per the source audit's Stripe callout; wiring a webhook to a dead route just adds a permanently-failing delivery to monitor. Reuse local dev's existing test-mode key pair for `STAGING_STRIPE_SECRET_KEY`/`STAGING_STRIPE_PUBLISHABLE_KEY` — do not generate a new test-mode pair. |
| Google/Apple OAuth: add `https://staging.tattooos.co` as an extra authorized origin/redirect on the **existing** clients | — | Reuses `VITE_GOOGLE_CLIENT_ID`/`VITE_APPLE_CLIENT_ID` from the production prompt's Tier 1.1 — do not create separate staging OAuth clients. |

If any of these are still missing when you reach §4, do everything in §3 that doesn't need
them (the capacity check is independent), then stop and report exactly which are missing —
same standard as every prior prompt in this series.

---

## 3. Same-cluster capacity check

The production-deploy prompt's own §6 already runs this once production is live and records
the numbers. Re-run it now, immediately before applying staging, since the two prompts may run
hours or days apart and production's real memory footprint under actual (however light) traffic
could differ from the number recorded right after first deploy:

```bash
kubectl describe nodes | grep -A 5 "Allocated resources"
kubectl top nodes
```

Needs ≥~700Mi allocatable memory headroom remaining (Hetzner CPX22, 2 vCPU/4GB) — staging's
resource requests in the overlay are already halved from production's, per
`docs/infra/staging-environment.md`. **If headroom is short, do not shrink staging's requests
further to force a fit** — that risks staging pods getting OOM-killed under any real load,
defeating the point of having a staging environment at all. Instead, stop and report the actual
numbers against `docs/infra/staging-environment.md`'s documented fallback (a second small
Hetzner CPX11, ~€4–5/mo, its own standalone K3s install) so Phi can decide whether to provision
it — this prompt does not provision new infrastructure on its own initiative.

---

## 4. Apply and verify staging for real

Once §1 (merged) and §2 (secrets present) and §3 (capacity confirmed) all clear:

1. `deploy-staging` runs automatically on the next push to `main` (which the §1 merge itself
   triggers) — or manually via `gh workflow run cd.yml -f redeploy_staging_only=true` if that
   `workflow_dispatch` input exists (confirm the exact input name in `cd.yml` rather than
   guessing). Watch it: `gh run watch`.
2. Run the full manual-verification checklist from `docs/infra/staging-environment.md`'s
   "What this session did NOT verify" section — this prompt's job is specifically to close
   that list out, not to re-describe it:
   - `kubectl get pods -n pena-e-arte-staging` → api/frontend/Redis all `Running`.
   - A real TLS cert for `staging.tattooos.co` **specifically** (not a wildcard reused from
     production, unless that's actually how `cluster-issuer.yaml` is scoped — confirm which):
     ```bash
     openssl s_client -connect staging.tattooos.co:443 -servername staging.tattooos.co </dev/null 2>/dev/null \
       | openssl x509 -noout -issuer -subject
     ```
   - `curl -sI https://staging.tattooos.co | grep -i x-robots-tag` → confirms `noindex` is
     actually served, not just present in the source.
   - Load `https://staging.tattooos.co` in a browser (or headless via Playwright), confirm the
     staging banner renders.
   - Stripe Elements on a staging deposit-flow page uses the **test** publishable key, not
     production's live key (or, if 3.1 of the compliance/payment-correctness prompt hasn't
     landed yet, confirm at least that it's the test key, separately from whether the flow
     itself is functional).
   - Trigger one real Stripe test-mode webhook delivery (Stripe CLI or the Dashboard's "send
     test webhook" against the staging endpoint) and confirm it's logged in Loki/Grafana under
     the shared observability stack — this is the concrete proof the webhook secret and routing
     are both correct, not just configured.
   - Open a SignalR connection against staging (e.g. sign in on the staging frontend and watch
     the browser's WS connection, or `read_network_requests` if driving it via browser
     automation) and confirm it connects — staging shares the same Redis-backed backplane
     pattern as production; confirm it isn't accidentally cross-wired to production's Redis.
   - **Throughout all of the above, re-confirm production is untouched**: `kubectl get pods -n
     pena-e-arte` still shows the same healthy state as before this prompt started.
3. Record which of these checks passed/failed in the final summary — don't claim "staging is
   live" without having actually run each one.

---

## 5. Explicitly out of scope

- Provisioning a second Hetzner box, even if §3's capacity check comes back short — report and
  stop, per §3.
- Any change to the staging manifests, CD jobs, or overlay structure beyond what PR #83 already
  contains — if something in PR #83 turns out to be wrong during this prompt's verification
  pass, fix the specific bug and say so, but don't redesign the mechanism.
- Load/performance testing against staging — that's Tier 5's job (a later prompt), and should
  only run after this prompt confirms staging is actually stable.
- Wiring `/api/v1/webhooks/stripe/connect` for staging — deliberately excluded, see §2.

---

## 6. Final self-check

- [ ] Production was confirmed `Running` before this session touched anything (§0's
      precondition check), and is confirmed still `Running` at the end.
- [ ] PR #83 was not merged until `gh pr checks 83` showed every required check green — no
      check was skipped or assumed passing from this doc's stale snapshot.
- [ ] Every secret in §2's table was confirmed present via `gh secret list` (names only, never
      values) before §4 ran; if any were missing, this session stopped and reported exactly
      which, having still completed §3's capacity check.
- [ ] §3's capacity numbers were re-run fresh, not copied from the production prompt's earlier
      recording.
- [ ] Every item in `docs/infra/staging-environment.md`'s "What this session did NOT verify"
      list was actually checked and the result (pass/fail, with evidence) is in the final
      summary — not asserted without the corresponding command's output.
- [ ] The `/connect` Stripe webhook route was not wired for staging.
- [ ] The final summary states plainly whether staging is live and verified end-to-end, or
      exactly which item is still blocking.
