# Staging environment — `staging.tattooos.co`

**Status:** manifests/CD jobs written and committed 3 Sep 2026 · **not yet applied to the real
cluster** (production itself hadn't been deployed to the live cluster as of this writing — see
"Current status" below) · **Related:**
`docs/claude/overnight-prompt-staging-environment-2026-09-03.md` (full spec),
`docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md`,
`docs/claude/overnight-prompt-cd-k8s-vault-2026-09-03.md`

---

## What this is, and why

`test.tattooos.co` — the environment used for anything beyond local `docker compose up` — is a
Cloudflare Tunnel to a laptop, not a deployed workload: it goes down whenever that laptop
sleeps, reboots, or its local Docker breaks (it did, the same day this was raised). Staging is a
real, deployed K3s workload on a stable public URL, so webhooks, SignalR, and presence tracking
can be exercised against something that behaves like production without depending on a laptop's
uptime — and so a future PSP (payment service provider) reviewer has something real to point at.

## Mechanism: same manifests as production, one Kustomize overlay

`k8s/overlays/staging/` and `k8s/overlays/production/` both patch the exact same
`k8s/base/*.yaml` files — same Deployment/Service/Ingress/Job *shapes*, differing only in
namespace, replica count, resource sizing, image tag, and the Ingress host. Not two
independently-maintained manifest sets that can silently drift apart.

| | Production (`pena-e-arte` namespace) | Staging (`pena-e-arte-staging` namespace) |
|---|---|---|
| API/frontend replicas | 2 (zero-downtime rolling update) | 1 (staging tolerates brief downtime mid-deploy) |
| Resource requests/limits | full (§Phase 2/3 of the July 26 prompt) | halved |
| Database | DigitalOcean `pena_e_arte_prod` | DigitalOcean `pena_e_arte_staging` — same cluster, second database + scoped user |
| Redis | own instance, `pena-e-arte` namespace | own instance, `pena-e-arte-staging` namespace — namespace isolation alone gives this for free |
| R2 bucket | production bucket | separate `pena-e-arte-staging` bucket + scoped token |
| Stripe | live-mode keys (Flow B billing; blocked on Albania — see the Decisions Log) | test-mode keys, reused from local dev — not newly generated |
| Observability | same shared Prometheus/Loki/Tempo/Alloy/Grafana, `environment="production"` | same shared stack, `environment="staging"` — **not** a duplicated stack |
| Vault | `pena-e-arte-vault` StatefulSet, `pena-e-arte` namespace | **none** — deliberately excluded, see below |
| JWT signing key | shared (`JWT_SECRET_KEY`) | shared — safe because `Jwt:Issuer`/`Jwt:Audience` differ (`tattoos-prod` vs. `tattoos-staging`) and the databases are separate, so a token from one environment doesn't validate in the other |

## What's deliberately shared, not duplicated

- **Observability.** One Prometheus/Loki/Tempo/Alloy/Grafana stack in the `monitoring`
  namespace, not a second stack in a `monitoring-staging` namespace. Staging is an additional
  scrape target (`k8s/observability/prometheus-configmap.yaml`, job
  `pena-e-arte-api-staging`) and its pods are picked up automatically by Alloy's existing
  cluster-wide (node-filtered, not namespace-filtered) pod discovery. Both carry an
  `environment` label (`production`/`staging`) added at the Prometheus static-config and Alloy
  relabel stage — this is what lets `api-overview.json`'s one dashboard filter between the two
  via a template variable (default `production`) instead of needing a forked copy.
- **Vault.** `k8s/base/kustomization.yaml` includes the Vault StatefulSet/Service/ConfigMap
  (needed by `overlays/production`) — `overlays/staging`'s `resources: [../../base]` would
  otherwise pull those in too, standing up a *second* single-node Vault + PVC in the staging
  namespace. Nothing in this staging plan calls for that (no code path calls
  `ISecretsProvider` yet, same as production today), so
  `k8s/overlays/staging/vault-exclude-patch.yaml` deletes those three resources via
  `$patch: delete`. If a per-tenant-credentials feature ever needs a real staging Vault, that's
  a deliberate follow-up decision.
- **Cluster/node, Redis *mechanism* (not data), Hangfire/Grafana admin credentials, the
  `letsencrypt-prod-dns01` `ClusterIssuer`** (cluster-scoped — any namespace's Ingress can
  reference it; the name is a real misnomer once it backs a staging cert too, flagged rather
  than renamed since renaming would touch a live production Ingress annotation for a cosmetic
  reason).

## A real Kustomize gotcha found while building this

Kustomize's `namespace:` transformer doesn't just leave `Namespace`-kind objects alone (as
their cluster scope would suggest) — it rewrites their own `.metadata.name` to the target
namespace too. Naively including all three `Namespace` objects from `k8s/base/namespace.yaml`
in the staging overlay renamed the `pena-e-arte` object to `pena-e-arte-staging`, colliding
with the real `pena-e-arte-staging` object already in the list (`namespace transformation
produces ID conflict`). Production's overlay never hits this because it sets no `namespace:`
transformer at all (base resources already hardcode `namespace: pena-e-arte` inline).
`k8s/overlays/staging/namespace-exclude-patch.yaml` deletes the `pena-e-arte`/`monitoring`
`Namespace` objects from what staging applies — `deploy-staging`'s own `needs: [deploy]` gate
means those two already exist (created by production's own deploy) by the time staging ever
runs, so this isn't a real gap, just avoided double-application.

Confirmed empirically: `kubectl kustomize k8s/overlays/staging` renders cleanly (validated
against the real cluster's `kubectl`/Kustomize v5.8.1) with 1 replica each for API/frontend,
halved resource requests/limits, zero `StatefulSet` objects (Vault excluded), and an Ingress
correctly pointed at `staging.tattooos.co`.

## The frontend build-twice problem

The API reads config at runtime — one image works in both namespaces, just pointed at
different Secrets/ConfigMaps. The frontend does not: `VITE_STRIPE_PUBLISHABLE_KEY`,
`VITE_GOOGLE_CLIENT_ID`, `VITE_APPLE_CLIENT_ID`, and `VITE_PUBLIC_URL` are Vite build-time
values baked into the static JS bundle. Reusing production's frontend image on staging would
silently serve production's live Stripe key and public URL to `staging.tattooos.co` visitors.
`.github/workflows/cd.yml`'s `build-and-push-frontend-staging` job builds a second,
distinctly-tagged image (`ghcr.io/471k/pena-e-arte-frontend:staging-<sha>`) with staging's own
build-args — same class of fix already applied for the `ci.yml`-placeholder version of this
identical bug (see the Sept 3 CD prompt).

## Deploy trigger

`deploy-staging` runs `needs: [deploy, build-and-push-frontend-staging]`, gated so it proceeds
when production's own deploy either **succeeded** (normal push-to-`main` flow) or was
**skipped** (manual `workflow_dispatch` — staging-only redeploy, e.g. after an infra-only
change to `k8s/overlays/staging/`) but never when production's deploy **failed**. Its own
`concurrency: group: cd-staging` is separate from production's `cd-production` group, so a
staging redeploy never queues behind or gets cancelled by a production deploy, and a hung/
failing staging deploy never blocks a subsequent production one.

## noindex + banner

Staging's frontend image sets `IS_STAGING="noindex, nofollow"` (empty on production's default,
`frontend/Dockerfile`'s `ENV IS_STAGING=""`); `frontend/nginx.conf.template` emits
`X-Robots-Tag: ${IS_STAGING}` on every response — nginx's documented behavior is to omit an
`add_header` entirely when its value is an empty string, so no `if`/`map` conditional logic was
needed. A small `StagingBanner` component
(`frontend/src/shared/components/StagingBanner.tsx`) renders a dismissable top banner when
`import.meta.env.VITE_PUBLIC_URL` contains `staging.`.

## Object naming — a real discrepancy from the original spec, resolved one way

The original staging spec's prose named the CD-populated Secret
`pena-e-arte-staging-api-secrets` in a few places, while its own §2.8 design decision says
object *names* stay identical to production's (`pena-e-arte-api-secrets`,
`pena-e-arte-api-config`) with namespace isolation alone doing the disambiguation — no
`namePrefix`. These two are incompatible: the base Deployments' `envFrom` references are the
unprefixed names, so a `-staging-`-prefixed Secret would silently fail to resolve.
**Resolved in favor of the unprefixed names** (matching §2.8's own design and this overlay's
already-validated `kubectl kustomize` output) — `deploy-staging` creates
`pena-e-arte-api-secrets`/reads `pena-e-arte-api-config`, both inside the
`pena-e-arte-staging` namespace.

## Same-cluster capacity gate — not yet run

The Hetzner box is a CPX22 (2 vCPU/4GB RAM). This overlay must not be applied for real until
production (the July 26 prompt's Phases 1–10 plus the Sept 3 prompt's in-cluster Vault) is
actually deployed and `Running`, and:

```bash
kubectl describe nodes | grep -A 5 "Allocated resources"
kubectl top nodes
```

show at least ~700Mi of allocatable memory headroom remaining. **As of this writing (3 Sep
2026), production has not been deployed to the live cluster** — confirmed empirically:
`kubectl get pods -A` shows only `kube-system` pods, no `cert-manager` CRDs are installed, and
no `pena-e-arte`/`monitoring`/`pena-e-arte-staging` namespaces exist. This capacity check has
therefore **not been run**, and the staging overlay has **not** been applied to the real
cluster. If the check ever shows less than ~700Mi headroom, the named fallback is a second,
small Hetzner CPX11 (~€4–5/mo) running its own fully standalone single-node K3s install — not
a multi-node join of the existing cluster (`local-path-provisioner`'s PVCs bind to the node
they were created on, making a multi-node join real added complexity for Redis/Vault/
observability PVCs).

## Prerequisites still outstanding (BLOCKING-MANUAL, Phi only)

None of this is created by any Claude Code session — see
`docs/claude/overnight-prompt-staging-environment-2026-09-03.md` §5 for the full detail:

1. Cloudflare DNS `A` record `staging` → the Hetzner box's public IP (proxied).
2. DigitalOcean: `pena_e_arte_staging` database + scoped user on the existing
   `pena-e-arte-prod-db` cluster → `STAGING_DB_CONNECTION_STRING` GitHub secret.
3. Cloudflare R2: a second bucket `pena-e-arte-staging` + scoped token →
   `STAGING_R2_*` GitHub secrets.
4. Stripe test-mode webhook endpoint, `/api/v1/webhooks/stripe/billing` only (not `/connect` —
   that route is orphaned Flow-A-Connect-era code, nothing can trigger it anymore) →
   `STAGING_STRIPE_WEBHOOK_SECRET_BILLING`. The test-mode key pair itself is reused from local
   dev, not regenerated → `STAGING_STRIPE_SECRET_KEY`/`STAGING_STRIPE_PUBLISHABLE_KEY`.
5. Google/Apple OAuth: add `https://staging.tattooos.co` as an additional authorized origin/
   redirect URI on the **existing** OAuth clients (no new clients).
6. cert-manager installed on the cluster, Vault initialized/unsealed, and the remaining
   production GitHub secrets populated — all outstanding per
   `docs/claude/overnight-prompt-cd-k8s-vault-2026-09-03.md` §1/§5, and all prerequisites for
   `deploy-staging` regardless of staging's own items above, since `deploy-staging` needs
   `deploy` to have run.

## What this session did NOT verify (say so plainly, don't claim otherwise)

The manual-verification steps from the original spec's §8 (pods `Running`, a real
`staging.tattooos.co` TLS cert, the Stripe Elements iframe using the right test key, a real
Stripe test-mode webhook delivery, an open SignalR connection, production untouched
throughout) all require the real cluster with production already live — none of them ran.
This session committed manifests, CD jobs, and frontend changes; it did not deploy anything.
