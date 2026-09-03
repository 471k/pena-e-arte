# Production-readiness gap audit — input for a master overnight prompt

> **Purpose of this file:** not an overnight prompt itself — a verified, deduplicated,
> dependency-ordered gap list to hand to a Claude Code session (main **Pena e Artë - Engineering**
> project) so it can write the actual master overnight prompt(s) from it. Every item below was
> checked against the live repo, the live GitHub repo/Actions config, and the live Hetzner
> cluster on 2026-09-03 — not assumed from an older audit. §0 records what an earlier pasted
> audit got wrong (already fixed, or never was broken) so the next session doesn't re-litigate
> settled work. Everything past §0 is a real, currently-open gap, grouped into tiers in the order
> they should be tackled — later tiers depend on earlier ones landing first, except where a tier
> is explicitly marked parallelizable.

**Verified as of:** 2026-09-03, this session · **Verification method:** direct repo reads/greps,
`gh secret list`, `gh api repos/471k/pena-e-arte/actions/permissions/workflow`,
`gh pr checks`/`gh run view --log`, and live `kubectl` against `~/.kube/hetzner-prod.yaml`.

---

## §0 — Corrections to an earlier (stale) audit — do NOT rebuild these

A prior gap list circulated some claims that no longer hold. Re-verified line by line:

| Claim | Verified status | Evidence |
|---|---|---|
| "`/privacy`/`/terms` are dead links" | **False — fixed.** Real routes exist. | `frontend/src/app/router.tsx:129-132`: `/privacy`, `/terms`, `/refund-policy`, `/contact` all routed; comment notes the old `CatchAllRedirect` bounce was the bug this fixed. |
| "No public ToS/Privacy/Refund/Contact pages" | **False — fixed.** Same as above, EPIC-0001 Phases 1-2 (PENA-100/101/102), landed 2026-07-31. | `docs/engineering/EPIC-0001-pre-implementation-hardening.md`: "Execution status — 31 July 2026 (COMPLETE: all 7 phases landed)". |
| "No `Currency` column on `Payment`" | **False — fixed.** | `Pena_e_Arte.Domain/Entities/Payment.cs:23`: `public string Currency { get; set; } = "ALL";` |
| "Retention/deletion: zero, no TTL, no right-to-erasure job" | **False — fixed.** Real two-stage job. | `Pena_e_Arte.Infrastructure/Jobs/RetentionPurgeJob.cs` (soft-delete + hard-purge + anonymize-on-erasure, GDPR Art. 5(1)(e)/Art. 17), wired as a Hangfire recurring job in `Pena_e_Arte.API/Program.cs:108`. |
| "`useSignalR`/`useSupportHub` tunnel bug" | **False — already fixed.** | `frontend/src/shared/hooks/useSignalR.ts:19`: connects directly to `localhost:5078` when `import.meta.env.DEV`, bypassing the unreliable Vite WS proxy; `DEV` is `false` in a real prod build so this path is inert there. |
| "Brand mismatch: TattooOS vs. Pena e Artë" | **Resolved by design, not a gap.** Deliberate: brand stays "TattooOS" in the UI, legal entity disclosed site-wide. | `SiteFooter.tsx` + `frontend/src/shared/constants/legalEntity.ts`, EPIC-0001 Phase 1. Still worth a PSP/KYC reviewer's attention at review time, but it's not an unaddressed engineering gap. |
| "CI/CD is a dead end — `ci.yml` never pushes, no `cd.yml`" | **False — `cd.yml` exists** with `build-and-push`/`deploy` jobs (K3s prod prompt Phase 9), **plus** this session added `build-and-push-frontend-staging`/`deploy-staging`. | `.github/workflows/cd.yml`. Real gap now is narrower: it has never been *run against a live cluster* — see Tier 1. |
| "GHCR push permission unconfirmed" | **Confirmed enabled.** | `gh api repos/471k/pena-e-arte/actions/permissions/workflow` → `"default_workflow_permissions":"write"`. |
| "DigitalOcean Managed MySQL status unconfirmed" | **Confirmed done**, 2026-07-27. | `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md` §0.1: cluster `pena-e-arte-prod-db`, Frankfurt, MySQL 8.4, trusted-sources locked to the Hetzner IP. |
| "Cloudflare API token / DNS / SSL mode unconfirmed" | **Confirmed done**, 2026-07-27. | Same §0.1: token `pena-e-arte-dns01` (DNS:Edit, zone-scoped, IP-filtered), `app` A record, Full (strict). |
| "ACME email still `CHANGE-ME@tattooos.co`" | **Already fixed** (PR #82, `b64f5c7`), and re-confirmed today. | `k8s/base/cluster-issuer.yaml`: `email: phisoftwaresolutions@gmail.com` (temporary personal address, tracked for a future `ops@` swap — see Tier 5). |
| "Staging environment doesn't exist" | **Built this session** (PR #83) — manifests + CD jobs + shared observability + frontend noindex/banner. **Not yet merged, not yet applied to the cluster.** | `k8s/overlays/staging/`, `.github/workflows/cd.yml`, `docs/infra/staging-environment.md`. See Tier 2. |
| "~30 GitHub secrets missing" | **Overstated.** 21 of the required-for-production set now exist (R2, Resend, Twilio, Hangfire, Grafana admin, `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR`, `SOCIAL_STATE_SIGNING_KEY` were all added today). Real remaining gap is narrower — see Tier 1/2. | `gh secret list`, checked live today. |

---

## Tier 1 — Get production actually live on the cluster (blocks everything below)

Nothing past this tier can be verified end-to-end until this lands. Confirmed today via live
`kubectl`: the cluster has **only stock K3s system pods** — no `cert-manager` CRDs, no
`pena-e-arte`/`monitoring`/`pena-e-arte-staging` namespaces, nothing deployed. This is the
single highest-priority tier.

### 1.1 — Populate the remaining production-blocking GitHub secrets

**BLOCKING-MANUAL** (Phi only — real external credentials, add directly via GitHub Settings,
never through a chat/prompt/doc):

| Secret | Status | Blocks |
|---|---|---|
| `VAULT_TOKEN` | Missing — only exists after 1.3 below runs | App can start without it (Vault is unused today), but ADR-0002's "Vault is the secrets backend" isn't real until this is set |
| `VITE_GOOGLE_CLIENT_ID` | Missing | Frontend build (`cd.yml`), Google sign-in on any deployed environment |
| `VITE_APPLE_CLIENT_ID` | Missing | Frontend build, Apple sign-in on any deployed environment |

**Deliberately NOT required to unblock a first deploy** (app degrades gracefully / feature is
gated off without it): `STRIPE_*` (see the callout below — this is a business decision, not a
missing-secret task), `INSTAGRAM_*`, `TIKTOK_*`/`FACEBOOK_*`/`X_*`/`YOUTUBE_*`.

> **Stripe callout — don't treat this as "just add the secret."** Per project memory, Stripe is
> unavailable at the country level for this platform's Albania-registered legal entity — this
> blocks **any** live Stripe merchant account, not just Connect. `STRIPE_SECRET_KEY`/
> `STRIPE_PUBLISHABLE_KEY`/`STRIPE_WEBHOOK_SECRET_BILLING`/`STRIPE_WEBHOOK_SECRET_CONNECT` are
> all still absent from GitHub Secrets, and populating them with **live** keys isn't currently
> possible. `Flow A` (client deposits) already moved to `NullPaymentProvider` (fails closed) — a
> real, standalone frontend bug is still open there too, see Tier 4. `Flow B` (subscription
> billing) still calls Stripe.net directly and needs the same provider rethink. **This is an
> external/business decision (see Tier 7), not something an overnight session can resolve** —
> the master prompt should either leave Flow B's production Stripe secrets unset (app already
> handles Stripe being unconfigured, matching the Cash-only-deploy fix from PR #81) or wait on
> the POK/alternative-provider decision before wiring real keys.

### 1.2 — Install cert-manager on the cluster

Cluster-level one-time bootstrap, not part of `cd.yml`'s own steps (per the K3s deploy prompt's
own design). `kubectl apply -f https://github.com/cert-manager/cert-manager/releases/download/<pinned-tag>/cert-manager.yaml`
— pin the exact current stable tag, don't trust `latest`. Verify `cert-manager`/`cainjector`/
`webhook` pods reach `Running` before anything referencing `ClusterIssuer`/`Certificate` objects
is applied.

### 1.3 — Run the production `kubectl apply` for the first time

> **Update, same day:** while preparing to merge PR #83, found and fixed a real ordering bug in
> `cd.yml` that would have made *this exact first deploy* fail immediately — both `deploy` and
> `deploy-staging` tried to `kubectl create secret ... -n <namespace>` before that namespace
> existed on a fresh cluster (namespace creation only happened later, inside "Apply manifests").
> Fixed via an idempotent "Ensure namespaces exist" step added to both jobs, right after writing
> the kubeconfig. No longer a blocker for this step.

Once 1.1/1.2 are done: merge or manually trigger `cd.yml`'s `deploy` job (needs a successful
`ci.yml` run on `main`, or `workflow_dispatch`). Confirm:
- `kubectl get pods -n pena-e-arte` → API/frontend/Redis/Vault all `Running`.
- `kubectl get pods -n monitoring` → Prometheus/Loki/Tempo/Alloy/Grafana `Running`.
- `curl -I https://app.tattooos.co` returns a real Let's Encrypt cert (not self-signed) —
  `openssl s_client -connect app.tattooos.co:443 -servername app.tattooos.co </dev/null 2>/dev/null | openssl x509 -noout -issuer`.
- `kubectl rollout status deployment/pena-e-arte-api -n pena-e-arte` / same for frontend, both
  succeed.

### 1.4 — Vault init/unseal runbook (BLOCKING-MANUAL, Phi only, one time ever)

`docs/infra/vault-self-hosted-runbook.md` is already written and has never been executed. Must
run **after** 1.3 (the `vault-0` pod needs to exist and be `Running`/sealed first). Produces the
`VAULT_TOKEN` GitHub secret from 1.1. No Claude Code session should ever run
`vault operator init`/`unseal`/`login` or see their output — this is explicit in the runbook
itself.

### 1.5 — Same-cluster capacity check (gates Tier 2, not Tier 1)

Once 1.1–1.4 are done and production is `Running`:
```bash
kubectl describe nodes | grep -A 5 "Allocated resources"
kubectl top nodes
```
Needs ≥ ~700Mi allocatable memory headroom remaining (Hetzner CPX22, 2 vCPU/4GB) before Tier 2
can apply the staging overlay for real. If it fails, the priced fallback (a second small
Hetzner CPX11, ~€4-5/mo, its own standalone K3s install) is written up in
`docs/infra/staging-environment.md` — don't silently shrink staging's already-halved resource
requests further to force a fit.

---

## Tier 2 — Staging environment (parallel-safe to write, blocked on Tier 1 to apply)

Manifests, CD jobs, shared-observability wiring, and frontend noindex/banner are **already
built and committed** (PR #83, branch `feat/staging-environment`) — this tier is about landing
and actually deploying that work, not writing it again.

### 2.1 — Merge PR #83

CI status as of this check: `Non-negotiable-rules guardrails`, `Docker images build`, `Help
stays in sync`, `Analyze (javascript-typescript)` all passing; `Backend — build/format/test`,
`Analyze (csharp)`, `Frontend — lint/typecheck/build/test/e2e` were still running at last
check — confirm all green before merging. (A gitleaks false-positive on a slash-separated
phrase in the source prompt doc was found and fixed via history rewrite this session — resolved,
not a recurring risk unless similar prose patterns get added elsewhere.)

### 2.2 — Populate staging's BLOCKING-MANUAL prerequisites (Phi only)

All absent from `gh secret list` today — full detail in `docs/infra/staging-environment.md` §5:

| Item | Produces |
|---|---|
| Cloudflare DNS `A` record `staging` → Hetzner box IP (proxied) | — |
| DigitalOcean: `pena_e_arte_staging` DB + scoped user on the *existing* `pena-e-arte-prod-db` cluster | `STAGING_DB_CONNECTION_STRING` |
| Cloudflare R2: new `pena-e-arte-staging` bucket + scoped token | `STAGING_R2_ACCOUNT_ID`/`STAGING_R2_ACCESS_KEY_ID`/`STAGING_R2_SECRET_ACCESS_KEY`/`STAGING_R2_BUCKET_NAME`/`STAGING_R2_PUBLIC_URL` |
| Stripe test-mode webhook, `/api/v1/webhooks/stripe/billing` only (not `/connect` — orphaned Flow-A-Connect-era route) | `STAGING_STRIPE_WEBHOOK_SECRET_BILLING`; reuse local dev's test-mode key pair (not regenerated) for `STAGING_STRIPE_SECRET_KEY`/`STAGING_STRIPE_PUBLISHABLE_KEY` |
| Google/Apple OAuth: add `https://staging.tattooos.co` as an extra authorized origin/redirect on the **existing** clients | uses the same `VITE_GOOGLE_CLIENT_ID`/`VITE_APPLE_CLIENT_ID` from Tier 1.1 |

### 2.3 — Apply and verify staging for real

Run 1.5's capacity check (already gated in Tier 1), then let `deploy-staging` run (auto on next
push to `main`, or `workflow_dispatch` with `redeploy_staging_only`). Full manual-verification
checklist (pods `Running`, real TLS cert for `staging.tattooos.co` specifically, Stripe Elements
using the test key, `X-Robots-Tag: noindex`, a real Stripe test-webhook delivery logged in
Loki/Grafana, an open SignalR connection, production untouched throughout) is in
`docs/infra/staging-environment.md` §"What this session did NOT verify."

---

## Tier 3 — Code-level fixes, independent of infra (parallelizable with Tiers 1-2)

These don't need a live cluster — they're pure application-code changes and can be built,
tested, and merged while Tiers 1-2 proceed.

### 3.1 — Deposit checkout Stripe/backend mismatch (confirmed real, standalone bug)

`DepositCheckoutPage.tsx`/`PaymentMethodSelector.tsx` still call Stripe Elements directly
(`stripe.confirmPayment`) against a backend (`IPaymentProvider` → `NullPaymentProvider`) that
fails closed by design. A real deposit attempt renders the Stripe form, then fails server-side,
on any environment. **Needs a UI fix reflecting the actual (no live provider yet) backend
state** — e.g., disable/hide the deposit-payment step, or show a clear "card payments
temporarily unavailable" state, rather than presenting a form that can never succeed. Not
blocked on the POK/provider decision (Tier 7) — this is about not shipping a broken UI in the
meantime, independent of which provider eventually lands.

### 3.2 — Intake-form consent

`frontend/src/features/forms/components/SubmitIntakeFormPage.tsx` collects free-text medical/
tattoo-history data (`formData: z.string().min(10)`) with **zero** consent UI. The versioned-
consent system (`ConsentTemplate`/`ConsentForm`, EPIC-0001 Phase 3) only has two kinds today —
`AppointmentConsent` and `CrossTenantProfileSharing` (`ConsentTemplateKind` enum) — neither
covers this intake-form submission specifically. Real Law 124/2024 (Albania) / GDPR Art. 9
exposure until this has its own consent checkbox, tied to a real `ConsentTemplate`/
`ConsentForm` record the same way appointment booking already does it.

### 3.3 — Refund and cash-confirmation audit logging

`RefundPaymentCommand`/`ConfirmCashDepositCommand` don't implement `IAuditableCommand` — only
`UpdateSessionSplitsCommand` does, in the whole `Payments` command set (confirmed via grep). Any
refund or cash-payment confirmation happens with zero audit trail today. Small, mechanical fix
(the pattern already exists to copy), real compliance/dispute-resolution gap until it's done.

### 3.4 — Frontend test for the deposit-flow fix (3.1)

Whatever 3.1 lands as (disabled state, message, etc.) needs a small component test, matching
this codebase's existing convention (see `StagingBanner.test.tsx` from this session for the
current bar: a handful of focused render-state assertions, not exhaustive coverage).

---

## Tier 4 — Operational hardening (mostly blocked on Tier 1, some parallel)

### 4.1 — Alerting/on-call routing

Nothing pages anyone today. Grafana Alerting rules + a real receiver (email/Slack-equivalent)
don't exist. Explicitly named out-of-scope by both prior K3s prompts — real follow-up, not an
oversight. Needs Tier 1's Grafana instance live first.

### 4.2 — Public status page

None exists. Same "named, not built" status as 4.1.

### 4.3 — External uptime monitoring

Nothing outside the cluster itself watches for an outage — if the Hetzner box goes down, nobody
finds out except by noticing the app is unreachable. A free/cheap external check (UptimeRobot-
class service or equivalent) pointed at `https://app.tattooos.co/health/live` is the minimal
version of this.

### 4.4 — Backup/DR runbook

No documented runbook exists (`docs/infra/` has no backup/DR doc). DigitalOcean's managed MySQL
has automated backups by default, but restore has never been *tested*. The R2 bucket has no
documented backup/versioning policy. Needs: (a) confirm DigitalOcean's actual retention window
and do one real restore-to-a-scratch-instance test, (b) decide + document an R2
backup/versioning policy, (c) write the actual runbook (what do you do if the Hetzner box dies).

### 4.5 — Secrets rotation runbook — never exercised

`docs/infra/secrets-rotation-runbook.md` exists but has never actually been run end-to-end.
Operational task, not a code change — schedule a real rotation drill once Tier 1 is live.

### 4.6 — ACME contact email — real ops inbox

`k8s/base/cluster-issuer.yaml`'s `email` is Phi's personal Gmail, tracked as a known temporary
value (see the file's own comment). Swap for a dedicated `ops@tattooos.co` once that inbox
exists — doesn't block issuance, purely a "who gets Let's Encrypt's renewal-failure emails"
correctness fix. Low priority, easy to forget — listed here so it doesn't.

### 4.7 — GeoIP data path

`k8s/base/api-configmap.yaml`'s `GeoIp:*` keys are deliberately left unset — no K8s volume/PVC +
population mechanism exists yet for the GeoLite2 `.mmdb` files `docker-compose.yml` mounts
locally. Feature stays gracefully degraded in production until this is addressed. Low priority.

---

## Tier 5 — Testing rigor (parallelizable, no infra dependency)

### 5.1 — Load/performance testing

None exists anywhere in the repo. First real traffic spike would be the first time actual
behavior under load is observed. Needs: pick a tool (k6/Artillery-class), define a baseline
scenario (booking flow, portfolio browse), run it against staging (Tier 2) once live — not
against production.

### 5.2 — Accessibility audit

None on record. Needs an actual pass (axe-core in CI, or a manual audit) — not currently gated
anywhere in `ci.yml`.

### 5.3 — Cross-device manual QA process

Mobile bugs so far have been found ad hoc (per the Mobile UI/UX baseline work), not via a
repeatable check. Needs a lightweight, repeatable checklist (device/breakpoint matrix), not
necessarily automation.

### 5.4 — E2E suite staleness

Has silently broken before (tests desynced from unrelated form changes, only caught because CI
happened to still run them). Worth a periodic manual review pass of `e2e/` against the current
UI, not a one-time fix.

---

## Tier 6 — External/human-only steps (independent of all engineering tiers)

No code, no infra — these block real payments/legal operation regardless of how complete the
engineering work above is:

1. Entity/VAT decision for the payment-provider question underneath Tier 1's Stripe callout.
2. POK/Polar/easyPos (or equivalent) account setup + KYC, given Stripe's Albania block.
3. Business bank account for settlement.
4. Studio Services Agreement + DPA drafting (platform ↔ studio-tenant contract terms).
5. DPIA/DPO threshold check (Albania Law 124/2024 + GDPR) — determine whether this platform's
   data processing volume/sensitivity (health data via intake forms, per Tier 3.2) crosses the
   threshold requiring a formal DPIA or a designated DPO.
6. Final lawyer-reviewed Privacy Policy/Terms of Service copy — `HAS_FINAL_LEGAL_COPY` is
   still `false`; both pages currently render a `[LAWYER REVIEW REQUIRED]` banner
   (confirmed still open per `docs/engineering/EPIC-0001-pre-implementation-hardening.md`
   item 4 — the one item out of that epic's 8 follow-ups NOT marked resolved).

---

## Tier 7 — Local dev environment (not production-blocking, but blocks local verification)

Docker Desktop is still broken on the operator's machine — confirmed today, `docker ps` fails
with `failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine`. Blocks:
Redis locally (live-presence testing), `docker compose up` container-parity testing (this
project's own stated pre-deploy check — `pnpm build`/`tsc -b` has caught real bugs `vitest run`
missed before, per project memory), and any local Vault-dev-mode testing. Needs a Docker Desktop
reinstall — outside any Claude Code session's reach, flagged here so it isn't lost.

---

## Suggested master-prompt structure

Given the above, the master overnight prompt(s) probably split cleanly along tier boundaries,
matching this project's existing one-prompt-per-cohesive-unit-of-work convention
(`overnight-prompt-k3s-production-deploy-2026-07-26.md`, `-cd-k8s-vault-2026-09-03.md`,
`-staging-environment-2026-09-03.md` are the precedent):

- **Prompt A — "First production deploy"**: Tier 1 in full. The BLOCKING-MANUAL items (1.1,
  1.4) must be marked exactly that, same standard the existing three prompts already hold
  themselves to — a session should stop and report rather than improvise around a missing
  prerequisite.
- **Prompt B — "Staging goes live"**: Tier 2, explicitly sequenced *after* Prompt A's own
  BLOCKING-MANUAL items are confirmed done (mirrors how this session's own staging work was
  written to depend on production already being live).
- **Prompt C — "Compliance + payment-flow correctness"**: Tier 3 (3.1-3.4) — fully
  autonomous, no external dependency, can run any time, ideally before or alongside Prompt A.
- **Prompt D — "Operational hardening"**: Tier 4 — mostly BLOCKING-MANUAL-gated on Prompt A;
  4.6/4.7 are small enough to fold into Prompt A directly instead if preferred.
- **Prompt E — "Testing rigor"**: Tier 5 — fully autonomous, no external dependency.
- Tier 6 and Tier 7 are not overnight-prompt material at all — they're Phi's own follow-ups,
  listed here only so the master prompt doesn't accidentally try to build around them.
