# Overnight Prompt — Real Staging Environment (staging.tattooos.co, in-cluster)

> Feed this file directly to Claude Code (running in the main **Pena e Artë - Engineering**
> project, with full repo write access). **Read
> `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md` and
> `docs/claude/overnight-prompt-cd-k8s-vault-2026-09-03.md` in full first** — this file builds
> `k8s/overlays/staging/` on top of the `k8s/base/` those two already produced. **As of this
> writing (2026-09-03, re-verified same day) that work is merged to `main`**: `feat/cd-secrets-
> inline-vault` merged via PR #80 (`65c66a9`), followed by two same-day fixes also on `main` —
> PR #81 (`8bfb6ab`, makes the Stripe health check optional so Cash-only deploys don't block K8s
> readiness) and PR #82 (`b64f5c7`, replaces the ACME email placeholder in `cluster-issuer.yaml`
> with Phi's real address) — see §1 for the exact, currently-true state. **None of it has been
> applied to the real cluster yet** — merged-to-`main` is a code-readiness milestone, not a
> deployment. Confirm `k8s/base/` exists on `main` and matches §1's inventory before starting
> this file's Phase 1; if it doesn't (this session is on a different/stale checkout), stop and
> flag rather than rebuilding any of it from scratch — that work is either already done, or a
> real prerequisite this file assumes and does not repeat. **Mode: fully autonomous**, except
> items marked **BLOCKING-MANUAL** below — those require Phi personally and must already be
> complete before the phase that depends on them runs. If a BLOCKING-MANUAL prerequisite is
> missing, stop and say so rather than improvising around it — same standard the other two
> prompts already hold themselves to.

**Date logged:** 2026-09-03
**Requested by:** Phi
**Origin:** Phi flagged that `test.tattooos.co` — the environment currently used for anything
beyond local `docker compose up` — is a Cloudflare Tunnel to Phi's own laptop, not a deployed
workload: it goes down whenever the laptop sleeps, reboots, or Docker breaks locally (it broke
the same day this was raised). A real staging environment needs to be an actual deployed
workload on a stable public URL, specifically so webhooks, SignalR, and presence tracking can
be exercised against something that behaves like production without depending on a laptop's
uptime — and, per Phi's own framing, so a future PSP (payment service provider) reviewer has
something real to point at during whichever provider's live-mode approval process ends up
applying. (This file's own §1 flags a same-day finding that Stripe specifically may not be the
provider that review ends up running against, given the platform's Albania registration — see
§1 for the full reasoning; the webhook/SignalR/presence testing goal below stands regardless of
which provider is eventually live.)

**Checkpoint before starting:**
```bash
git status                     # must be clean before starting
git log --oneline -1           # confirm what's actually checked out — expect b64f5c7 or later, on main
ls k8s/base/                   # confirm the production manifests this file depends on exist
```
`k8s/base/` now lives on `main` (PR #80 merged it there; PRs #81/#82 landed cleanly on top of
it the same day). Branch from `main` directly:
```bash
git checkout main && git pull
git checkout -b feat/staging-environment
git commit --allow-empty -m "checkpoint: before staging-environment work"
```
If `git log --oneline -1` does not show `b64f5c7` or a later commit on `main` — i.e. this
session is looking at a stale or different checkout than the one this file was written
against — stop and flag rather than branching from the wrong base.

---

## 1. Current state as of today (2026-09-03) — read before writing anything

This is not a "add staging next to a working production deployment" prompt either —
**production is code-complete but not yet live.** Confirmed against the live repo today:
`k8s/base/` and `k8s/observability/` exist in full (API/frontend/Redis Deployments+Services,
Ingress, `ClusterIssuer`, migration Job, the self-hosted Vault `StatefulSet` from the Sept 3
prompt, and the translated Prometheus/Loki/Tempo/Alloy/Grafana stack), `.github/workflows/
cd.yml` exists (`build-and-push` + `deploy` jobs, triggered via `workflow_run` once `ci.yml`
passes on `main`), and `k8s/overlays/production/kustomization.yaml` exists — all merged to
`main` via PR #80 (`65c66a9`), re-verified today (2026-09-03) at `main`@`b64f5c7`. **None of it
has been applied to the real cluster yet — merged-to-`main` is a code-readiness milestone, not
a deployment.** Two follow-up fixes landed on `main` the same day, both re-confirmed by this
audit: `k8s/base/cluster-issuer.yaml`'s ACME contact email is **no longer** the
`CHANGE-ME@tattooos.co` placeholder — PR #82 (`b64f5c7`) set it to Phi's real address
(`phisoftwaresolutions@gmail.com`, tracked as temporary pending a dedicated `ops@tattooos.co`
inbox) — resolved for staging's certificate exactly as it is for production's, since both reuse
the one `ClusterIssuer` (§3.2); and PR #81 (`8bfb6ab`) made the `StripeHealthCheck` registration
conditional on `Stripe:SecretKey` being present (previously it was tagged `"ready"`
unconditionally, so an unconfigured Stripe key would have failed `/health/ready` and blocked
K8s pod readiness — and the whole `cd.yml` rollout — for a Cash-only deploy). Still genuinely
open, unchanged by either fix: no `kubectl apply` has run against the live cluster,
cert-manager isn't installed on the box, Vault hasn't been initialized/unsealed, and most of
the GitHub Actions secrets `cd.yml` needs still don't exist. Phase 0's manual, human
prerequisites are separately confirmed done (Hetzner box, K3s+Traefik, DigitalOcean managed
MySQL, Cloudflare API token + `app.tattooos.co` DNS record + Full(strict) SSL) per the July 26
prompt's §0.1 log.

**New finding from this same audit, directly relevant to §5/§8 below — flagged, not resolved
here.** PR #81's commit message states Stripe isn't currently available for the platform's own
Albania-registered legal entity (`LEGAL_ENTITY_NAME` "Pena e Artë", per the Decisions Log's
legal-entity-disclosure row), and that Phi is evaluating an alternative provider ("POK") for
client-facing card payments. Tracing this through the code: `IPaymentProvider`'s only concrete
implementation is `NullPaymentProvider`, which fails closed on every call — client deposits
(Flow A) don't touch Stripe at all today; only Cash does. Flow B (`IStripeBillingService` —
platform subscription billing) still calls Stripe.net directly and is what this file's
Stripe-webhook testing plan actually exercises. `BillingEndpoints.cs` maps two webhook routes:
`/api/v1/webhooks/stripe/billing` (live — dispatches Flow B subscription commands) and
`/api/v1/webhooks/stripe/connect` (dispatches `MarkPaymentAuthorizedCommand`/
`ConfirmPaymentCommand`/`MarkPaymentFailedCommand` — **Flow A commands, from before the POK
pivot**). Since Flow A no longer creates Stripe `PaymentIntent`s at all, nothing will ever fire
a real Stripe event at `/connect` again — it reads as orphaned code left over from the
pre-PENA-106 Connect-based architecture (see also `payment-fallback-prompt.md`, superseded, and
`payment-simplified-prompt.md`, itself now stale post-PENA-106), not a live integration point.
Separately, the frontend (`DepositCheckoutPage.tsx`, `PaymentMethodSelector.tsx`) still imports
`@stripe/react-stripe-js` and calls `stripe.confirmPayment` directly for the client deposit
flow — it was not updated when the backend moved Flow A to `IPaymentProvider`/
`NullPaymentProvider`, so a real deposit attempt against any environment (production or
staging, once actually live) will render the Stripe Elements form and then fail server-side.
**Phi confirmed (2026-09-03) this is a real, standalone bug — not staging-specific, and
deliberately not patched as part of this audit; it needs its own fix/ticket once the POK (or
other) provider decision lands, not a silent mid-audit patch.** It means **§5 item 4 and §8's
manual-verification steps below are narrowed accordingly** to stop treating a full end-to-end
deposit as something staging should be expected to prove.

**Phi also confirmed (2026-09-03) the open question this section originally flagged:** Stripe's
Albania restriction is country-level, not Connect-specific — it blocks any live Stripe merchant
account for this entity, full stop. So Flow B (subscription billing) will need the same
provider rethink Flow A already got, before this platform could ever actually go live with
Stripe billing directly. Test-mode working locally (the `STRIPE_SECRET_KEY` pair already in
`.env.example`/local dev) doesn't indicate anything about live-mode eligibility — that earlier
reasoning doesn't hold up as reassurance and shouldn't be read that way. Nothing in this file
changes as a result: staging only ever needed test-mode Stripe for Flow B, which is unaffected;
this is recorded here so a future session doesn't have to re-derive it, and so nobody assumes
Flow B is Stripe's long-term home just because it hasn't been migrated yet.

This file's staging overlay builds directly on the real `k8s/base/` files listed above, not a
hypothetical structure this session still has to invent. **This is the literal mechanism that
satisfies "same manifests as prod"**: `k8s/overlays/staging/` and `k8s/overlays/production/`
both patch the exact same `k8s/base/*.yaml` files — same Deployment/Service/Ingress/Job
*shapes*, differing only in namespace, replica count, resource sizing, image tag, and the
Ingress host — not two independently-maintained manifest sets that can silently drift apart.
**Sequencing**: this file's phases (the overlay itself, the `cd.yml` additions, the Phase 6
frontend changes) can be written and committed now, on the same branch, without waiting on a
live cluster. But §3.1's capacity check — and therefore actually *applying* the staging
overlay for real — can't happen until production has been deployed to the live cluster at
least once (Phi has completed the remaining Phase 0/§5 BLOCKING-MANUAL items from the other two
prompts: the ACME email fix, the missing GitHub secrets, cert-manager install, Vault init). If
this session can only get as far as committing staging's manifests/CD-job additions without a
live cluster to verify against, **say so plainly in the final summary** rather than claiming
§8's manual-verification steps passed.

---

## 2. Decisions already made — implement as specified, do not re-litigate

### 2.1 — Same K3s cluster, new namespace, not a second Hetzner box (default; see §3.1 for the capacity gate that can override this)

The user's own framing offered two options: the same cluster, or a second small Hetzner box.
Resolved here as **same cluster, new namespace `pena-e-arte-staging`** — cheaper (zero new VPS
cost), and it's what makes the Kustomize-overlay approach in §2.8 work cleanly: one cluster, one
`k8s/base/`, two overlays. A second box would mean either joining it as a second K3s node
(real complexity: `local-path-provisioner`'s PVCs bind to the node they were created on, so
Redis/Vault/observability PVCs would need explicit node affinity to avoid a pod being scheduled
onto the wrong node for its own volume — not a one-line change) or running a fully separate
single-node K3s install (its own Traefik, cert-manager, ClusterIssuer — essentially cloning
Phase 0 items 1/3/4 and Phase 6 a second time). Neither is warranted unless the capacity check
in §3.1 says the existing box can't hold both environments — that check is a real gate, not a
formality, and this decision is written to be reversed cleanly if it fails.

### 2.2 — Staging database: second database on the *existing* DigitalOcean cluster, not a second managed cluster

`pena-e-arte-prod-db` (the cluster provisioned per the July 26 prompt's §0.1) gets a second
database, `pena_e_arte_staging`, with its own scoped DB user (not `doadmin`, and not reusing
prod's application user) limited to that one database. This is cheaper than a second ~$15/mo
DigitalOcean cluster and still real MySQL 8.4/Pomelo-compatible — the same DB engine and version
prod uses, satisfying the "behaves like production" requirement precisely. **Named tradeoff,
not silently accepted:** this means staging's queries share the same cluster's compute/IO/
connection-pool ceiling as prod's. Acceptable given staging's stated purpose here is behavioral
testing (webhooks, SignalR, presence, a PSP reviewer clicking through flows) — not load testing.
If load/performance testing against staging is ever wanted, revisit this (a second cluster, or
at minimum DigitalOcean connection-pool limits reviewed) before running it — named again in §13.

### 2.3 — No second observability stack — extend the existing one

The July 26 prompt's Phase 7 stands up Prometheus/Loki/Tempo/Alloy/Grafana once, in a
`monitoring` namespace, scraping `pena-e-arte`. This prompt does not duplicate that stack into
a second `monitoring-staging` namespace (real resource cost on an already resource-constrained
single box — see §3.1) — instead, §6 Phase 5 below adds `pena-e-arte-staging` as additional
scrape/log targets to the *same* Prometheus/Loki/Alloy, with an `environment` label
(`production`/`staging`) added at the Alloy/Prometheus relabeling stage so Grafana dashboards
can filter by it. One shared observability stack, environment-labeled — not two.

### 2.4 — The frontend image must be built twice: a real, non-obvious problem, not a formality

**Flagging this precisely because it's easy to miss just by copying the production overlay
pattern.** The API reads its configuration at runtime (`IConfiguration`/environment variables
via the `pena-e-arte-api-config`/`-secrets` objects) — one API image works unmodified in both
namespaces, just pointed at different Secrets/ConfigMaps. **The frontend does not work the same
way.** Per the Sept 3 prompt's §4, `VITE_STRIPE_PUBLISHABLE_KEY`, `VITE_GOOGLE_CLIENT_ID`,
`VITE_APPLE_CLIENT_ID`, and `VITE_PUBLIC_URL` are Vite build-time values, baked into the static
JS bundle by `docker build --build-arg ...` — Vite has no runtime-config mechanism here (unlike
`nginx.conf.template`'s `BACKEND_HOST`/`BACKEND_PORT`, which really are resolved at container
*start*, not build, via `envsubst`). A staging Deployment running the exact same frontend image
tag as production would silently serve **production's** Stripe publishable key, OAuth client
IDs, and public URL to anyone hitting `staging.tattooos.co` — the frontend would *look* like it
was pointed at staging (same Ingress, same DNS) while its Stripe Elements / Google / Apple SDK
calls silently target production's live-mode configuration. This is exactly the kind of
build-succeeds-rollout-goes-green-fails-silently-at-runtime gap the Sept 3 prompt's own §4 fix
was written to catch for the CI-placeholder version of this same mistake — the equivalent bug
here is worse, since it'd point staging's frontend at *live* Stripe. **Fix specified in §6 Phase
4**: `cd.yml` gets a second `build-and-push-frontend-staging` job producing a distinctly-tagged
image (`ghcr.io/471k/pena-e-arte-frontend:staging-${{ github.sha }}`) with staging's own
build-args (`VITE_PUBLIC_URL=https://staging.tattooos.co`, staging's Stripe *test-mode*
publishable key, the same `VITE_GOOGLE_CLIENT_ID`/`VITE_APPLE_CLIENT_ID` as production per §5
item 5's reasoning). The API image needs no staging-specific build — `k8s/overlays/staging`
reuses the exact same `ghcr.io/471k/pena-e-arte-api:${{ github.sha }}` tag production uses.

### 2.5 — Deploy trigger: staging deploys on every push to `main`, right after production, as an independent job

No `develop`/`staging` git branch exists in this repo (confirmed: `ci.yml` triggers only on
`main`) and there's no reason to invent one for a solo-dev project — that would be new process
overhead with no second engineer to gate against. `cd.yml`'s staging deploy job runs `needs:
[deploy]` (the existing production deploy job from the July 26 prompt's Phase 9/Sept 3 prompt's
amendments) so staging always lands *after* production's migration Job and rollout succeed, but
`continue-on-error` semantics are **not** shared — a staging deploy failure must not fail the
overall workflow red in a way that looks like a production incident, and must never trigger a
production rollback. §6 Phase 4 specifies this as a genuinely separate job, not a step appended
to the existing one. **Also add** a `workflow_dispatch` input (`redeploy_staging_only: boolean`)
so staging can be redeployed on demand — e.g. after an infra-only change to
`k8s/overlays/staging/` — without needing a throwaway commit to `main` to trigger it.

### 2.6 — Redis: fully separate instance per namespace, zero new mechanism

`k8s/base/redis-deployment.yaml`/`redis-service.yaml`/`redis-pvc.yaml`, patched by the staging
overlay the same way the frontend/API Deployments are — this already gives staging its own
Redis (sessions, rate-limit buckets, SignalR presence state) with zero code changes and zero new
manifests, purely because Kubernetes namespaces already isolate same-named resources from each
other. No cross-namespace Redis sharing, no new Redis instance-selection logic anywhere.

### 2.7 — Resend/Twilio left unset on staging by default

Per the Sept 3 prompt's §5 table, both are "required for a real launch... not startup-blocking"
— the app already handles them being unset via the same config-gated-inactive pattern used for
the social-OAuth integrations (confirmed in `.env.example`'s own comments). Leaving
`RESEND_API_KEY`/`TWILIO_*` empty on staging means booking-confirmation emails/SMS silently
don't send there — an explicit, named tradeoff, not an oversight: the alternative (wiring real
Resend/Twilio credentials into staging) risks real emails/SMS firing against whatever test data
gets seeded or entered by a PSP reviewer clicking through the app, which is a worse default.
Flagged as a real gap in §9 (email/SMS-dependent flows aren't end-to-end verifiable on staging
today) rather than silently accepted as fine forever.

### 2.8 — Mechanism: `k8s/overlays/staging/`, a sibling of `overlays/production/`, one replica each, halved resource requests

```
k8s/overlays/staging/
  kustomization.yaml       # namespace: pena-e-arte-staging, image tags set by CD (§6 Phase 4),
                            # replica-count patches (1, not 2 — see below), resource-request
                            # patches, Ingress host patch (staging.tattooos.co)
  ingress-patch.yaml        # strategic-merge or JSON6902 patch overriding base/ingress.yaml's
                            # host/tls/secretName for staging
```
**1 replica for API and frontend, not 2.** Production's Phase 2/3 spec 2 replicas specifically
for the `maxUnavailable: 0` zero-downtime rolling-update guarantee a real user-facing checkout
flow needs — staging has no such requirement (it's fine if it's briefly unavailable mid-deploy),
and running 1 replica each roughly halves staging's resource footprint, directly helping the
§3.1 capacity question. Resource requests also halved from production's Phase 2 defaults
(`requests: { cpu: 50m, memory: 128Mi }`, `limits: { cpu: 250m, memory: 256Mi }` for the API;
proportionally smaller for the frontend/Redis) — still enough for real behavioral testing, not
sized for load. This is a Kustomize `patchesStrategicMerge`/`patches` job, not new YAML files
duplicating the base Deployments — the whole point of reusing `k8s/base/` is that a staging-only
tweak is a small patch, not a forked copy that can drift from prod's shape over time.


---

## 3. Decisions to flag — do NOT guess, do NOT build blind

### 3.1 — Same-cluster capacity: a real gate, checked empirically, not assumed

**Do not apply the staging overlay until this check is run against the real cluster, after
production (Phases 1–10 of the July 26 prompt, plus the Sept 3 prompt's in-cluster Vault) is
already deployed and `Running`.** Run:
```bash
kubectl describe nodes | grep -A 5 "Allocated resources"
kubectl top nodes    # requires metrics-server; K3s ships it by default — confirm it's there
```
The box is a Hetzner CPX22 (2 vCPU / 4 GB RAM — per the July 26 prompt's §0.1). By the time
production's Phase 2/3/5/7 Deployments, the Sept 3 prompt's Vault StatefulSet, K3s's own
system pods (Traefik, CoreDNS, metrics-server, local-path-provisioner), and cert-manager are all
running, real headroom may already be thin — this prompt does not assume a specific number
because guessing it would be exactly the kind of "don't guess, confirm empirically" mistake this
project's own conventions exist to catch (see, for direct precedent, the July 26 prompt's Phase
6 WebSocket-timeout handling). **If allocatable memory headroom after production is up is less
than roughly 700Mi** (a conservative floor: staging's halved-per-§2.8 API+frontend+Redis
requests come to roughly 250–350Mi, leaving room for burst/limits headroom and K3s's own
overhead) **stop here and flag it back to Phi** rather than silently applying an overlay that
will leave pods `Pending` — do not silently shrink requests further below §2.8's already-halved
values to force a fit, and do not silently provision a second Hetzner box on your own initiative
either (that's a real recurring-cost decision, same class as §3.1/§3.2 of the July 26 prompt).
If a second box does turn out to be needed, the shape is: a second Hetzner **CPX11** (cheapest
tier, ~€4–5/mo), its own fully standalone single-node K3s install (Traefik, cert-manager,
`ClusterIssuer`, `staging.tattooos.co` DNS record pointed at *this* box's IP instead), running
`k8s/overlays/staging` against that cluster's own kubeconfig — a second, small clone of Phase 0
items 1/3/4/6 plus Phase 6, not a multi-node join of the existing cluster (see §2.1 for why a
multi-node join is worse). Write this fallback path up in the final summary as a concrete,
priced option rather than leaving Phi to re-derive it if the capacity check fails.

### 3.2 — Reusing `letsencrypt-prod-dns01` for staging's certificate too

The July 26 prompt's `ClusterIssuer` is named `letsencrypt-prod-dns01` — a `ClusterIssuer` is
cluster-scoped (not namespace-scoped), so any namespace's `Ingress` can reference it, including
one in `pena-e-arte-staging`. **Recommended: reuse it as-is**, cert-manager will happily issue a
second, independent certificate for `staging.tattooos.co` under the same issuer — no new
`ClusterIssuer`, no new Cloudflare API token scope needed (the existing `cloudflare-api-token`
Secret's DNS-01 permissions already cover the whole `tattooos.co` zone). The name says "prod,"
which is a real misnomer once it's backing a staging cert too — **flagged, not renamed here**:
renaming it would mean re-pointing production's already-issued (by the time this prompt runs)
Ingress annotation at a renamed object, a real change to a live production resource for a purely
cosmetic reason, which this prompt's own scope boundary (§4) says not to touch. If Phi wants a
rename for clarity, that's a small, low-risk follow-up — named, not done here.

### 3.3 — `noindex` + a visible "staging" banner

Not asked for explicitly, but worth naming: per this project's own CLAUDE.md rule #6 (match
current industry standards for this SaaS category), Vagaro/Fresha/Boulevard-tier staging/sandbox
environments are consistently kept out of search-engine indexes and visually distinguished from
production, specifically so nobody (a QA pass, a PSP reviewer, Phi at 2am) mistakes a staging
screenshot for a production one. **Recommended, small, and named as a real addition, not
assumed pre-existing**: an `X-Robots-Tag: noindex, nofollow` response header added at the
frontend's nginx layer only for the staging image build (a small `nginx.conf.template`
conditional, or a separate staging-only nginx snippet baked into the staging-tagged frontend
image per §2.4's already-required second build) and a persistent, dismissable banner
("STAGING — test data, not production") rendered by the frontend when `VITE_PUBLIC_URL` matches
the staging host. §6 Phase 6 specs the concrete, minimal version of this. **Flag if Phi would
rather skip it** — it's a small addition, not a blocking one, and nothing else in this prompt
depends on it landing.

### 3.4 — Staging is fully public, same as the literal ask — but named as a real tradeoff

As spec'd, `staging.tattooos.co` is reachable by anyone, no `BasicAuth`/IP-allowlist in front of
it — this is deliberate and matches the stated purpose precisely (Stripe's webhook servers need
to reach it directly for real webhook-delivery testing, and "a PSP reviewer" needs a URL they
can just open). The real cost of that: without §3.3's `noindex`, search engines could index a
staging URL serving fabricated/test data; and anyone who finds the URL can create accounts,
book fake appointments, and exercise the app against staging's test-mode Stripe keys — low risk
today (test-mode Stripe transactions move no real money, staging has its own isolated DB per
§2.2), but worth Phi's explicit sign-off rather than a silent default. **Recommended: leave it
fully public, ship §3.3's `noindex`, revisit a Traefik `BasicAuth` `Middleware` gate later if
Phi ever wants to restrict discovery** (webhook paths would need to stay open even then, which
is a small added complexity — named, not built preemptively for a need that doesn't exist yet).

---

## 4. Explicit scope boundary — do not touch

- **No changes to `Pena_e_Arte.Domain/`, `Pena_e_Arte.Contracts/`, or any endpoint/handler
  file.** This is infra-only, same boundary as the July 26 prompt's §4 — the one addition this
  prompt makes beyond pure infra is §3.3's frontend-only `noindex`/banner (a `nginx.conf`
  header + a presentational React conditional), which touches no backend code.
- **No changes to `k8s/base/*`'s actual resource *shapes*** — this prompt patches via
  `k8s/overlays/staging/`, it does not fork or duplicate the base Deployment/Service/Ingress/
  Job YAML. If a change is needed to accommodate staging that isn't expressible as an overlay
  patch, stop and flag it rather than quietly forking `base/` — that would defeat the entire
  "same manifests as prod" point of this design.
- **Do not touch `k8s/overlays/production/` or the existing production `deploy` job in
  `cd.yml`.** Staging is additive: a new overlay directory, a new job (or two — build + deploy)
  in `cd.yml`, gated with `needs: [deploy]` so it never runs before or in place of production's
  own deploy, and structured so its failure can't fail or roll back production's job (§2.5).
- **Do not touch `docker-compose.yml` or local dev** — this prompt is production/staging-cluster
  only, exactly like the two prompts it depends on.
- **Do not run any of §5's BLOCKING-MANUAL steps yourself** — Stripe dashboard webhook
  registration, DigitalOcean database/user creation, R2 bucket + token creation, and OAuth
  redirect-URI additions all require Phi's account access and must already be done, with the
  resulting values added directly as GitHub Actions secrets, before the corresponding phase
  below runs.

---

## 5. Prerequisites — Phi does this before staging phases run (**BLOCKING-MANUAL**)

None of this belongs in `k8s/` YAML or `cd.yml` written by this session — it produces the
real-world resources the manifests then reference by name/secret, same pattern as the July 26
prompt's own Phase 0.

1. **Cloudflare DNS**: add an `A` record, name `staging`, value = the same Hetzner box's public
   IP already recorded in the July 26 prompt's §0.1 (`49.13.66.15` as of that log — confirm it
   hasn't changed). Proxied (orange cloud), same as the existing `app` record — the zone's SSL
   mode is already `Full (strict)` (a zone-wide setting, not per-record), so no additional
   Cloudflare change is needed beyond the one new DNS record.
2. **DigitalOcean**: inside the *existing* `pena-e-arte-prod-db` cluster (do not create a new
   cluster — §2.2), add a new database `pena_e_arte_staging` and a new database user scoped to
   it only (not `doadmin`, not prod's application user). Assemble the connection string in the
   same shape as prod's (`Server=<same host>;Port=25060;Database=pena_e_arte_staging;
   User=<new staging user>;Password=<...>;SslMode=Required;AllowPublicKeyRetrieval=true;`) — same
   host/port as production, different `Database=`/`User=`/`Password=`. Add as the
   `STAGING_DB_CONNECTION_STRING` GitHub Actions secret.
3. **Cloudflare R2**: create a second bucket, `pena-e-arte-staging`, and a scoped R2 API token
   limited to it (mirrors the isolation reasoning in §2.2 — staging's test uploads, consent-form
   PDFs, and portfolio images should not land in production's real bucket). Add
   `STAGING_R2_ACCOUNT_ID`/`STAGING_R2_ACCESS_KEY_ID`/`STAGING_R2_SECRET_ACCESS_KEY`/
   `STAGING_R2_BUCKET_NAME`/`STAGING_R2_PUBLIC_URL` as GitHub Actions secrets.
4. **Stripe test-mode webhook endpoint — `/billing` only** (revised by this audit's §1 finding):
   in the Stripe Dashboard, **test mode**, register one new webhook endpoint:
   `https://staging.tattooos.co/api/v1/webhooks/stripe/billing` (exact route path confirmed
   against `Pena_e_Arte.API/Endpoints/BillingEndpoints.cs` — `app.MapGroup("/api/v1/webhooks/
   stripe")`, `MapPost("/billing", ...)`, `AllowAnonymous()` — correct, Stripe can't authenticate
   as an app user). **Do not also register `/connect`**: per §1, that route only ever dispatched
   Flow A commands from the pre-POK, Stripe-Connect-based architecture, and Flow A no longer
   creates Stripe `PaymentIntent`s at all (`NullPaymentProvider` fails closed) — so no real
   Stripe event will ever be sent there, on staging or production. Registering it anyway would
   just be dead configuration pointed at dead code; if Phi wants that endpoint formally
   deprecated/removed, that's separate follow-up work, out of scope here. The one `/billing`
   endpoint yields its own `whsec_...` signing secret — add as
   `STAGING_STRIPE_WEBHOOK_SECRET_BILLING` (no `_CONNECT` counterpart needed).
   **The Stripe secret/publishable key pair itself does not need to be newly generated** — a
   test-mode key pair isn't tied to a single deployment or webhook endpoint, so staging can reuse
   the exact same `sk_test_.../pk_test_...` pair local dev already uses (per `.env.example`);
   add those same values as `STAGING_STRIPE_SECRET_KEY`/`STAGING_STRIPE_PUBLISHABLE_KEY` GitHub
   secrets (a straight copy of whatever's already in local `.env`, not a new Stripe-dashboard
   step) — only the one webhook secret is genuinely new, because Stripe signs webhook payloads
   per registered endpoint URL. (The publishable key is still needed even though Flow A's
   deposit flow doesn't complete server-side right now — see §1 — because the frontend build
   still needs a valid `VITE_STRIPE_PUBLISHABLE_KEY` to render at all without a build-time
   config error.)
5. **Google/Apple OAuth**: add `https://staging.tattooos.co` as an additional authorized
   JavaScript origin (Google) / redirect URI (Google and Apple, matching whatever exact
   callback path the existing production client already registers) on the **existing** OAuth
   client(s) — do not provision new OAuth clients for staging. `VITE_GOOGLE_CLIENT_ID`/
   `VITE_APPLE_CLIENT_ID` stay the same values already used for production's frontend build; only
   the authorized-origins list on each client's console changes. Confirm this is done before
   Phase 4 runs, or Google/Apple sign-in on staging will fail with a redirect-URI-mismatch error
   at the OAuth provider, not at this app.
6. Once 1–5 exist, add the complete staging secrets set (§5 below) via the GitHub Settings UI.
   **Never paste any of these values into a chat, prompt, or doc anywhere, including back to
   this consultation project** — same standing rule as both prompts this one depends on.


---

## 6. Complete GitHub Actions secrets checklist for staging (new secrets only — anything not listed here is already covered by production's existing secrets and is not duplicated)

| GitHub secret name | Source | Required for |
|---|---|---|
| `STAGING_DB_CONNECTION_STRING` | §5 item 2 | app startup (staging namespace) |
| `STAGING_R2_ACCOUNT_ID` / `STAGING_R2_ACCESS_KEY_ID` / `STAGING_R2_SECRET_ACCESS_KEY` / `STAGING_R2_BUCKET_NAME` / `STAGING_R2_PUBLIC_URL` | §5 item 3 | file storage (staging namespace) |
| `STAGING_STRIPE_SECRET_KEY` / `STAGING_STRIPE_PUBLISHABLE_KEY` | §5 item 4 — same test-mode pair as local dev, copied not regenerated | Stripe test-mode flows on staging |
| `STAGING_STRIPE_WEBHOOK_SECRET_BILLING` | §5 item 4 — genuinely new, tied to the one new webhook endpoint URL (`/connect` deliberately not registered — see §1/§5 item 4) | Stripe webhook delivery to staging (Flow B only) |
| `VITE_PUBLIC_URL` for the staging build | not a secret — literal `https://staging.tattooos.co`, passed directly in the `cd.yml` staging build-args, not sourced from a GitHub secret | frontend build (staging image, §2.4) |

**Reused unchanged from production's existing/pending secret set** — not duplicated, not
re-added: `KUBE_CONFIG` (same cluster), `JWT_SECRET_KEY` (same signing key is fine — staging
and production both validate their own independently-issued tokens; a shared secret doesn't
let staging tokens authenticate against production or vice versa, since `Jwt:Issuer`/
`Jwt:Audience` plus the separate databases already scope sessions per environment — **flagged
as a judgment call, not silently assumed**: if Phi wants a distinct staging JWT signing key for
defense-in-depth, that's a one-line addition, not built here since nothing about this app's auth
model actually requires it), `CLOUDFLARE_API_TOKEN` (same zone, same DNS-01 solver), `VAULT_ADDR`/
`VAULT_TOKEN` (nothing calls `ISecretsProvider` in a live path yet per the Sept 3 prompt — no
staging-specific Vault wiring needed until that changes), `HANGFIRE_DASHBOARD_USERNAME`/
`PASSWORD` (reused — Hangfire's dashboard isn't publicly exposed either way, see Phase 7 note
below), `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR` (same cluster, same Pod CIDR, same two-hop
Traefik→frontend-nginx topology applies identically inside the staging namespace), and
`PROD_GRAFANA_ADMIN_USER`/`PASSWORD` (one shared Grafana per §2.3, no separate staging login).
**Deliberately left unset on staging, per §2.7**: `RESEND_API_KEY`, `TWILIO_*`.

---

## 7. Phase-by-phase spec

### Phase 1 — `k8s/overlays/staging/` scaffold

```
k8s/overlays/staging/
  kustomization.yaml
  ingress-patch.yaml
  replica-patch.yaml         # or inlined into kustomization.yaml's patches: list — either is
                              # fine, match whatever style overlays/production/ ends up using
                              # once the July 26 prompt actually writes it
  resources-patch.yaml       # halved CPU/memory requests/limits, §2.8
```
`kustomization.yaml` sets: `namespace: pena-e-arte-staging` (Kustomize's `namespace:` transformer
rewrites every base resource's metadata into the new namespace — this is what makes "same
manifests, different environment" mechanical rather than copy-pasted), a `namePrefix` is **not**
needed (namespace isolation alone is sufficient — object *names* like `pena-e-arte-api` can stay
identical across namespaces, they're only ever addressed within their own namespace or via a
namespace-qualified DNS name), `images:` overrides pinning the staging frontend tag
(`ghcr.io/471k/pena-e-arte-frontend:staging-<sha>`, set by CD the same way
`overlays/production/kustomization.yaml` gets its tags set — via `kustomize edit set image` as a
CD step, never hand-edited) while the API image reuses the exact same tag CD just deployed to
production (§2.4). References the two patch files above via `patches:`.

### Phase 2 — Namespace + Secrets/ConfigMap objects

`k8s/base/namespace.yaml` already only defines `pena-e-arte` and `monitoring` (per the July 26
prompt's Phase 1) — add `pena-e-arte-staging` there too (one more `Namespace` object in the same
file, or a small addition via the overlay's own resources — either is fine; adding it to
`base/namespace.yaml` directly is simpler since Kustomize's namespace transformer needs the
target namespace to actually exist and this keeps all three namespace declarations in one place
rather than scattered across overlays). `pena-e-arte-staging-api-secrets` /
`pena-e-arte-staging-api-config` get created by CD (§7 Phase 4) the same
`kubectl create secret generic ... --dry-run=client -o yaml | kubectl apply -f -` idempotent
pattern Phase 8 of the July 26 prompt already establishes for production — not committed to
`k8s/` with real values, same rule, no exception for staging just because its Stripe keys are
test-mode.

### Phase 3 — Ingress + certificate

New `Ingress` object (via `ingress-patch.yaml`, patching `base/ingress.yaml`'s host/tls/
secretName — do not hand-write a second full Ingress from scratch, patch the base one so a
future change to, say, an added path rule on the base Ingress automatically carries into both
overlays): `host: staging.tattooos.co`, `tls.hosts: ["staging.tattooos.co"]`,
`tls.secretName: pena-e-arte-staging-tls` (a distinct Secret name from production's
`pena-e-arte-tls` — two independent certificates, same issuer per §3.2), same
`cert-manager.io/cluster-issuer: letsencrypt-prod-dns01` annotation and `ingressClassName:
traefik` as production. Backend service reference stays `pena-e-arte-frontend` at port `8080` —
correct as-is, since Kustomize's namespace transformer already scopes that Service name to
`pena-e-arte-staging` for this overlay; no rename needed.

### Phase 4 — `cd.yml` additions: staging build + deploy jobs

Add to the existing `cd.yml` (already committed per §1 — created by the July 26 prompt's
Phase 9, corrected by the Sept 3 prompt's §4). **Mirror its real, already-written secret-
population step exactly** (the `Populate pena-e-arte-api-secrets` step currently lists roughly
35 `--from-literal` keys covering every app secret from JWT through the Vault address/token) —
the staging equivalent needs the same complete key list, not a shortened one, substituting only
the genuinely environment-specific values (DB connection string, R2 credentials, Stripe secret/
publishable/webhook keys) with their `STAGING_`-prefixed sources per §6, and reusing production's
own secret values for everything else (JWT, Hangfire, Vault, the social/OAuth block, per §6's
"reused unchanged" list) exactly as that step already does today:

1. **`build-and-push-frontend-staging`** (needs: `build-and-push` or runs independently — either
   is fine since it builds a *different* image tag; running it in parallel with the existing
   `build-and-push` job is fine and slightly faster). `docker/build-push-action@v7`, same
   Dockerfile (`frontend/Dockerfile`), `push: true`, tag
   `ghcr.io/471k/pena-e-arte-frontend:staging-${{ github.sha }}` only (no `:latest`-equivalent
   floating tag for staging — always deploy by exact sha, avoids any ambiguity about which build
   staging is running). Build-args:
   ```yaml
   build-args: |
     VITE_STRIPE_PUBLISHABLE_KEY=${{ secrets.STAGING_STRIPE_PUBLISHABLE_KEY }}
     VITE_CONTACT_EMAIL=support@tattooos.co
     VITE_GOOGLE_CLIENT_ID=${{ secrets.VITE_GOOGLE_CLIENT_ID }}
     VITE_APPLE_CLIENT_ID=${{ secrets.VITE_APPLE_CLIENT_ID }}
     VITE_PUBLIC_URL=https://staging.tattooos.co
   ```
   Reuses the same `VITE_GOOGLE_CLIENT_ID`/`VITE_APPLE_CLIENT_ID` secrets production's build
   already uses (per §5 item 5 — same OAuth client, staging origin added as an extra authorized
   redirect). Grep the diff after writing this job to confirm `pk_test_placeholder` (the
   `ci.yml`-only value the Sept 3 prompt's §4 fixed for production) does not appear here either.
2. **`deploy-staging`** (`needs: [deploy, build-and-push-frontend-staging]` — waits for both
   production's own deploy job *and* this job's frontier image to exist; runs on every push to
   `main`, or when `github.event.inputs.redeploy_staging_only == 'true'` per §2.5's added
   `workflow_dispatch` input). Steps: write `KUBE_CONFIG` to a temp kubeconfig (reuse — same
   cluster), `kubectl create secret generic pena-e-arte-staging-api-secrets -n
   pena-e-arte-staging --from-literal=... --dry-run=client -o yaml | kubectl apply -f -` (DB
   connection string, R2 creds, Stripe test keys/webhook secrets, and the JWT, Hangfire, and
   Grafana credentials reused from production's own secret values per §6's "reused unchanged"
   list), `kustomize edit
   set image` inside `k8s/overlays/staging` to pin the API tag (same sha as production) and the
   frontend tag (`staging-${{ github.sha }}`), delete-and-recreate the staging migration Job
   (same `batch/v1` shape as `base/migration-job.yaml`, pointed at
   `STAGING_DB_CONNECTION_STRING` via the namespace-scoped Secret) and `kubectl wait --for=
   condition=complete` on it, `kubectl apply -k k8s/overlays/staging`, then `kubectl rollout
   status deployment/pena-e-arte-api -n pena-e-arte-staging --timeout=180s` and the same for
   frontend. **This job's failure must not fail the overall workflow run in a way that blocks
   or rolls back the already-completed production deploy** — run it as a separate job (not a
   step appended to `deploy`) so a red `deploy-staging` job is visually distinct in the Actions
   UI from a red `deploy` job, and confirm in the final summary that a deliberately-broken
   staging deploy (e.g., a bad secret value) doesn't touch production's `Deployment` objects at
   all — verify this empirically against the real cluster, don't just reason about it from the
   YAML.

### Phase 5 — Observability: staging as additional scrape/log targets on the existing stack

Extend the July 26 prompt's Phase 7 `prometheus-configmap.yaml` scrape config with a second job
targeting `pena-e-arte-api.pena-e-arte-staging.svc.cluster.local:8080` (a `job_name:
pena-e-arte-api-staging`, mirroring the existing prod job's shape), and add an `environment:
"production"` / `environment: "staging"` static label to each job respectively via
`relabel_configs`/`static_configs` `labels:` — this is the one mechanism that lets a single
Grafana dashboard filter by environment instead of needing two dashboards. Alloy's log-shipping
config picks up staging's pod logs automatically (it discovers pods cluster-wide via the
containerd socket, not per-namespace, per the July 26 prompt's Phase 7 note on
`loki.source.containerd`) — add the same `environment` label at Alloy's relabeling stage so Loki
queries can filter the same way Prometheus's can. No changes to Tempo (traces already carry
whichever `OTEL_SERVICE_NAME`/resource attributes the API sets — confirm empirically once
staging is live that a staging-originated trace is distinguishable in Tempo, e.g. via
`resource.service.name` or a namespace-derived attribute; if it isn't, that's a real gap to flag,
not silently left ambiguous). Grafana's existing `api-overview.json` RED dashboard gets a new
`environment` template variable (`production`/`staging`, default `production` so nothing about
today's default view changes) rather than a second, forked dashboard.

### Phase 6 — `noindex` + staging banner (§3.4)

Frontend: an `X-Robots-Tag: noindex, nofollow` response header, added in the staging-only nginx
config baked into the `staging-`tagged frontend image (a conditional in
`frontend/nginx.conf.template`, gated on a new `IS_STAGING` env var the staging Deployment sets
via `envFrom`/`env:` — matching the existing `BACKEND_HOST`/`BACKEND_PORT` `envsubst` mechanism,
**not** a second Dockerfile/nginx.conf file to maintain) — production's Deployment simply
doesn't set `IS_STAGING`, so the header is absent there by default, zero behavior change to
production's own nginx config. A small, persistent, dismissable banner component
("STAGING — test data only, not connected to production") rendered when
`import.meta.env.VITE_PUBLIC_URL` (baked in at build time per §2.4) includes `staging.` — a
presentational-only React addition, no new state management, no backend involvement.

### Phase 7 — Docs

- New `docs/infra/staging-environment.md` — what staging is, its URL, what's isolated
  (database, R2 bucket, Stripe test-mode keys/webhooks) vs. shared (K3s cluster/node, Redis
  *mechanism* though not instance, observability stack, Vault, JWT signing key, Hangfire/
  Grafana creds), the §3.1 capacity-check commands, and the §5 secrets list — written so a
  future Phi-or-successor doesn't have to re-derive any of this from `k8s/` YAML archaeology.
- `docs/claude/architecture.md` Decisions Log — new entry, following this project's own
  established convention (see the entries this prompt cites throughout for the concrete phrasing
  standard: what shipped, what got flagged instead of built, real values used for anything left
  as a placeholder here — the actual DB user name, the actual `STAGING_*` secret values are
  never recorded, but confirmation that they exist should be).


---

## 8. Test requirements

- **Unit/integration:** none new required — this prompt is infra manifests, `cd.yml` jobs, and
  the small Phase 6 frontend `noindex`/banner addition. If Phase 6's `IS_STAGING`-conditional
  header logic is added as actual TypeScript/nginx-template logic (not just a static per-image
  config difference), add one small frontend test asserting the banner renders when
  `VITE_PUBLIC_URL` contains `staging.` and does not render otherwise — cheap, matches this
  codebase's existing component-test conventions, not skipped just because the rest of this
  prompt is infra-only.
- **Manual verification** (part of this prompt's own "done," same standard as the July 26 and
  Sept 3 prompts hold themselves to):
  1. `kubectl get pods -n pena-e-arte-staging` shows the API, frontend, and Redis pods
     `1/1 Running` (1 replica each, per §2.8), and `kubectl get pods -n pena-e-arte` (production)
     is unaffected — still whatever state it was in before this session's Phase 4 ran.
  2. `curl -I https://staging.tattooos.co` returns a valid Let's Encrypt certificate for
     `staging.tattooos.co` specifically (not a wildcard/prod cert reused by accident) — confirm
     via `openssl s_client -connect staging.tattooos.co:443 -servername staging.tattooos.co
     </dev/null 2>/dev/null | openssl x509 -noout -subject -issuer`.
  3. Open the deployed staging frontend in a browser, devtools open: confirm the Stripe
     Elements iframe on the deposit page loads and is using the `pk_test_...` key from
     `STAGING_STRIPE_PUBLISHABLE_KEY` (visible in Network/Sources — this is the concrete check
     that §2.4's fix actually took effect, the same style of check the Sept 3 prompt's own §7
     step 4 specifies for production), and confirm the response headers include
     `X-Robots-Tag: noindex, nofollow` (Phase 6). **Do not treat a successfully-submitted
     deposit as part of this check** — per §1's finding, the deposit flow fails server-side
     against `NullPaymentProvider` regardless of environment; this step only confirms the
     frontend build picked up staging's own key, not that a deposit can complete end to end.
  4. Send a real Stripe test-mode event to the one `/billing` webhook endpoint (Stripe
     Dashboard's "Send test webhook" button against the endpoint registered in §5 item 4, or the
     Stripe CLI's `stripe trigger` against test mode pointed at the real staging URL rather than
     local forwarding) — confirm a `200` response and that the event is visibly processed (check
     Loki/Grafana for the corresponding log line, per Phase 5's observability extension — this is
     also the first real end-to-end proof that Phase 5's staging log/scrape targets actually
     work, not just that the YAML applied cleanly). **Do not send a test event to `/connect`** —
     per §1/§5 item 4, that endpoint is not registered for staging and no longer has a live code
     path behind it.
  5. Open a real SignalR connection against `wss://staging.tattooos.co/hubs/...` (any hub — e.g.
     `NotificationHub`) from a browser session signed into a staging-seeded test account, confirm
     the connection upgrades successfully through the same Traefik→frontend-nginx two-hop path
     production uses, and stays open — this is the direct test of the "presence tracking... on
     something that behaves like production" purpose stated in this prompt's own Origin section.
  6. Confirm production is untouched throughout: `kubectl get deployment -n pena-e-arte -o
     jsonpath='{.items[*].spec.template.spec.containers[*].image}'` shows the same image tags
     before and after this session's staging work, and `https://app.tattooos.co` keeps serving
     normally the whole time.

---

## 9. Help-menu sync

**No Help Menu / user-manual / onboarding-tour changes.** Staging is not a surface any of the
app's four roles (`client`, `artist`, `owner`, `issuer`) interact with as part of using the
product — it's an internal/operator environment, same reasoning the July 26 prompt's §9 already
applies to the production K3s rollout itself. Per `CLAUDE.md` rule #7's stated exception for
zero-user-visible-surface changes, stated explicitly here rather than silently skipped. (Phase
6's banner is visible *within* staging, but staging itself has no real end users — it's not a
`CLAUDE.md`-rule-7-relevant "feature" surface.)

---

## 10. Industry-standard benchmark note

Same framing as the July 26 prompt's §10: this is operational infrastructure, not a booking-SaaS
UX feature, so the relevant comparison is general B2B SaaS operational practice, not the
Vagaro/Fresha/Boulevard UX benchmark `CLAUDE.md` rule #6 names for feature work. A persistent,
production-shaped staging/sandbox environment reachable on a stable URL — not a developer's
laptop — is a baseline expectation at this product's stage, doubly so once a payment processor's
review process is in scope (PSPs commonly ask to see exactly this kind of environment during
underwriting). What this prompt deliberately does **not** build, named rather than silently
dropped: an automated, anonymized production→staging data-sync/seeding pipeline (staging starts
empty or with whatever test data gets manually entered — a real gap once realistic-looking QA
data is needed, common at comparable-stage SaaS companies but a separate, larger piece of work);
CI-gated pre-production checks (e.g., an automated smoke-test suite that must pass against
staging before a deploy is considered "done," or before merges to `main` are even allowed) —
staging today is a manual-verification and PSP-facing environment, not yet a release gate; and
any access restriction beyond `noindex` (§3.4). These are real gaps, not oversights — restated
in §13 so they don't quietly fall off the backlog now that staging itself exists.

---

## 11. Final self-check / verification checklist

Before declaring this done:

- [ ] `k8s/overlays/staging/` exists and only *patches* `k8s/base/*` — no forked/duplicated
      Deployment, Service, or Ingress YAML that could drift from `overlays/production/`'s shape.
- [ ] `cd.yml`'s new `build-and-push-frontend-staging` and `deploy-staging` jobs exist, both
      gated correctly (`needs: [deploy]` or equivalent) so staging never deploys before or
      instead of production, and a staging-job failure was verified (§8 step 6) not to touch
      production's Deployments.
- [ ] No secret value committed anywhere in `k8s/**`, `cd.yml`, or any new doc — grep the diff.
- [ ] `pk_test_placeholder`/any CI-only placeholder string does not appear in the staging
      frontend build-args, same grep discipline as the Sept 3 prompt's own §9 checklist item.
- [ ] §3.1's capacity check was actually run against the real cluster (after production landed)
      before the staging overlay was applied — its result (headroom sufficient, or the fallback
      second-box path invoked instead) is recorded in the Decisions Log entry (§7 Phase 7), not
      just assumed.
- [ ] All six §8 manual-verification steps completed against the real cluster and real
      `staging.tattooos.co` URL, not just "the YAML has no syntax errors."
- [ ] `docs/claude/architecture.md` Decisions Log entry added confirming what shipped, which
      decisions in §4 got resolved which way (same-cluster vs. second box; secret-key reuse),
      and the real values used for anything this prompt left as a placeholder (DB host reuse
      confirmation, actual staging DNS record, whether the capacity check passed or triggered
      the fallback).
- [ ] `docs/infra/staging-environment.md` written per §7 Phase 7.

---

## 12. Final deliverable spec

**Files written/changed:**
- `k8s/base/namespace.yaml` — add `pena-e-arte-staging` (§7 Phase 2)
- `k8s/overlays/staging/**` (new, per §7 Phase 1)
- `.github/workflows/cd.yml` — two new jobs, additive only (§7 Phase 4)
- `frontend/nginx.conf.template` — `IS_STAGING`-conditional `X-Robots-Tag` header (§7 Phase 6)
- One small new frontend component/conditional for the staging banner (§7 Phase 6) + its test
  (§8)
- `docs/infra/staging-environment.md` (new, §7 Phase 7)
- `docs/claude/architecture.md` — new Decisions Log entry (§7 Phase 7)

**Commit message(s)** (separate commits, in this order):
1. `feat(k8s): add pena-e-arte-staging namespace and overlays/staging (patches base/, mirrors overlays/production)`
2. `feat(ci): cd.yml — build staging-tagged frontend image with staging build-args, deploy-staging job`
3. `feat(observability): scrape/log pena-e-arte-staging alongside production, environment-labeled`
4. `feat(frontend): noindex header + staging banner, gated on IS_STAGING`
5. `docs(infra): add staging-environment.md`
6. `docs(architecture): log staging-environment decision`

---

## 13. Explicitly out of scope after this prompt (do not silently build these either)

- **A second Hetzner box / multi-node K3s** — only if §3.1's capacity check fails; not built
  preemptively.
- **Automated production→staging data sync or anonymized seeding pipeline** — named in §10, real
  future work, not this prompt's.
- **CI-gated pre-production smoke tests against staging** — named in §10; staging today is a
  manual-verification and PSP-facing environment, not a release gate.
- **`BasicAuth`/IP-allowlist in front of staging** — named in §3.4 as an open question, left
  fully public for now per the literal ask (Stripe webhook delivery + a PSP reviewer both need
  unauthenticated public reachability).
- **A distinct staging JWT signing key** — named in §6 as a judgment call, not built; the shared
  key doesn't cross-authenticate staging and production sessions given the separate databases.
- **Everything the July 26 and Sept 3 prompts' own out-of-scope sections already named**
  (alerting/on-call routing, a public status page, retention-cost tuning, autoscaling,
  Vault auto-unseal/HA, DB backup/restore runbook) — unaffected and unexpanded by this prompt.
