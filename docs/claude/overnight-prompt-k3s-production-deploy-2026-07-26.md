# Overnight Prompt — K3s Production Deployment: Manifests, Managed-DB Cutover, Ingress/TLS, In-Cluster Observability, CD Pipeline

> Feed this file directly to Claude Code (running in the main **Pena e Artë - Engineering**
> project, with full repo write access) as the task prompt. It is self-contained: exact files,
> exact current code, exact target code, exact tests, exact docs to sync. Read the whole file
> before writing anything — **Phase 0 is not this session's job — it is a human prerequisite
> that must already be done before this session starts.** Do not attempt to sign up for a
> cloud provider, create a server, or purchase a database instance; those require a human with
> a credit card and cannot be done by an unattended coding session. If any Phase-0 prerequisite
> is missing, stop and say so instead of improvising a workaround.

**Date logged:** 2026-07-26
**Requested by:** Phi
**Origin:** Engineering-consultation review of the production-deployment gap: the repo has a
complete local/container-parity story (`docker-compose.yml`, Dockerfiles for API and frontend,
a full Prometheus/Loki/Tempo/Grafana/Alloy stack added earlier today) but zero K8s manifests,
zero CD step, and no live server anywhere. `CLAUDE.md` names K3s as the target orchestrator;
nothing in the repo makes that real. `ForwardedHeaders:TrustedProxyCidr` (added in today's
security-remediation pass) has no ingress CIDR to point at yet. The observability
Decisions Log entry logged earlier today explicitly named "Production/K3s rollout" as a
follow-up "blocked on the CD pipeline landing first" — this prompt is that follow-up.
**Mode: fully autonomous for Phases 1–10 below; Phase 0 is explicitly out of this session's
reach and must be complete first (see below).**

**Checkpoint before starting (Phases 1–10 session only):**
```bash
git status                     # must be clean before starting
git checkout main && git pull
git checkout -b feat/k3s-production-deploy
git commit --allow-empty -m "checkpoint: before K3s production deploy work"
```

---

## 0. Prerequisites — Phi does this before the autonomous session starts

These require a human with account/billing access. **None of this belongs in `k8s/` YAML
written by this session** — it produces the real-world resources the manifests below then
reference by name/secret. Do not proceed past Phase 1 until these exist:

1. **A VPS with K3s installed.** `CLAUDE.md` names Hetzner/AWS; this consultation isn't
   picking one for you (see §2 "flagged" below) — but concretely, once you've picked one:
   `curl -sfL https://get.k3s.io | sh -` on a fresh Ubuntu 24.04 box provisions a
   single-node K3s cluster in ~60 seconds. Copy `/etc/rancher/k3s/k3s.yaml` off the box
   (it's the kubeconfig; rewrite its `server:` field from `127.0.0.1` to the box's public
   IP) — you'll need its contents as a GitHub Actions secret in Phase 9.
2. **A managed MySQL 8.4-compatible instance** (see §2 — provider not chosen here). Create
   the instance, create a database named `pena_e_arte_prod`, and note the resulting
   connection string in the same shape `DB_CONNECTION_STRING` already uses (see §6.5) —
   confirm the provider allows `utf8mb4`/`utf8mb4_unicode_ci` as the server default (or that
   it can be set per-connection; EF Core migrations run with the app's own connection, not
   server defaults, so this is a should-confirm, not a hard blocker).
3. **A Cloudflare API token** scoped to `Zone:DNS:Edit` for the `tattooos.co` zone only (not
   the Global API Key) — needed for cert-manager's DNS-01 solver.
4. **DNS records in Cloudflare** for whichever hostname(s) Phase 6 below targets, pointed at
   the K3s box's public IP. If Cloudflare's orange-cloud proxy is enabled on those records,
   confirm it's compatible with DNS-01 issuance (DNS-01 validates via TXT record, not HTTP
   traffic, so proxying the A/AAAA record doesn't interfere — but flag if this assumption
   doesn't hold for your zone setup).
5. **A GHCR PAT or confirmation that `GITHUB_TOKEN`'s default `packages: write` permission is
   enabled** for `471k/pena-e-arte` (Settings → Actions → General → Workflow permissions).
6. Once 1–5 exist, add these **GitHub Actions repo secrets** (Settings → Secrets and
   variables → Actions): `KUBE_CONFIG` (base64 of the rewritten kubeconfig from step 1),
   `PROD_DB_CONNECTION_STRING`, `CLOUDFLARE_API_TOKEN`. The other secrets the API already
   needs (`JWT_SECRET_KEY`, `STRIPE_SECRET_KEY`, etc. — full list in §6.5) get their
   production values added as GitHub secrets too, reusing the exact names already in
   `.env.example` with a `PROD_` prefix where they don't already have one, so Phase 9's
   workflow can reference them.

**When Phase 0 is done, hand this file to a Claude Code session with a note confirming which
managed-MySQL provider and which VPS host were actually used** (Phase 2 below needs to know
this to write accurate provisioning notes into the docs), then let it run Phases 1–10
unattended.

---

## 1. Goal

Take the images `docker-compose.yml` already builds correctly and make them run in production
under K3s: multiple replicas with restart-on-crash and rolling updates, a real ingress with
TLS, the exact same observability stack the local compose file runs (reused, not
reinvented), and a CD step so "merged to `main`" and "running in production" stop being two
different manual steps.

Applicable non-negotiable rules from `CLAUDE.md`: #2 (RBAC unaffected — no new endpoints),
#3 (never log PII — carries into K8s log aggregation, see §8.7), #4 (secrets never in
source — see §6.4/§8.8), #5 (structured logs only — unaffected, Alloy/Loki wiring already
does this), #6 (industry-standard — see §11), #7 (Help sync — see §10, N/A here).

---

## 2. Decisions already made — implement as specified, do not re-litigate

Resolved via this project's clarifying pass before this prompt was written:

1. **Cluster is not provisioned yet.** This prompt's Phases 1–10 write manifests, workflows,
   and docs against a cluster that Phase 0 makes real — it does not create the cluster itself.
2. **Managed MySQL, not self-hosted.** No MySQL `StatefulSet`/PVC gets written. The API's
   `ConnectionStrings__Default` in production points at the managed instance via a K8s Secret
   populated from the `PROD_DB_CONNECTION_STRING` GitHub secret. Provider is Phi's choice
   (§4.1) — the manifests are provider-agnostic (any MySQL 8.4-compatible endpoint + standard
   connection string works with Pomelo unchanged).
3. **Observability is self-hosted in-cluster**, reusing the exact configs already built and
   verified today under `docker/observability/` (`prometheus.yml`, `loki-config.yml`,
   `tempo.yaml`, `config.alloy`, `grafana/provisioning/`) — translated into ConfigMaps, not
   rewritten. Same pinned image tags as `docker-compose.yml`: `prom/prometheus:v3.13.1`,
   `grafana/loki:3.7.4`, `grafana/tempo:3.0.2`, `grafana/alloy:v1.18.0`, `grafana/grafana:13.1.1`.
4. **TLS via cert-manager + Let's Encrypt, DNS-01 challenge through Cloudflare** — not HTTP-01.
   Reasoning (Phi's, recorded here so it isn't re-litigated): faster issuance, supports the
   wildcard cert `*.tattooos.co` (useful since `app.tattooos.co` already exists as a
   subdomain), and doesn't require the cluster to be internet-reachable on port 80 during
   issuance. Exact hostname(s) the Ingress routes: **confirm `app.tattooos.co` is the intended
   production frontend host before merging** — this prompt writes the Ingress for
   `app.tattooos.co` as the single host (see §8.6 for why only one host is needed) but that
   exact string was inferred from context, not stated as a literal final decision, and must be
   verified against the real DNS record created in Phase 0 step 4.
5. **Kustomize, not Helm**, for the `k8s/` manifests. Reasoning: no chart repository to host,
   no templating language to learn on top of YAML, ships with `kubectl` natively (`kubectl
   apply -k`), and nothing about this deployment (single environment, no multi-tenant Helm
   values matrix) needs Helm's extra machinery. This is a lower-stakes engineering call than
   §4's items, not re-confirmed with Phi before writing — flag if this should change; a Helm
   rewrite later is mechanical (the same manifests become templates) if outgrown.
6. **Registry: GHCR** (`ghcr.io/471k/pena-e-arte-api`, `ghcr.io/471k/pena-e-arte-frontend`) —
   matches the GitHub Actions runner with zero new account, consistent with `ci.yml` already
   living in the same repo.

---

## 3. Decisions to flag — do NOT guess, do NOT build blind

These are named explicitly, per this project's own precedent, rather than silently decided or
silently skipped. **Do not implement a specific choice for any of these; implement the
provider-agnostic version and leave the named gap.**

### 3.1 — Managed MySQL provider (money decision, Phi's call)

Three real current-market candidates, priced as of this consultation (verify current pricing
before committing — rates change):

| Provider | Entry price (2026) | Notes |
|---|---|---|
| DigitalOcean Managed MySQL | ~$15/mo | Standard MySQL protocol (not Vitess-sharded) — closest behavioral match to today's `mysql:8.4` container and Pomelo/EF Core. Recommended default for this app's size and this project's "match local dev exactly" philosophy. |
| PlanetScale (Vitess-backed MySQL, PS-10 tier) | ~$39/mo for HA | PlanetScale's free/Hobby tier is gone (removed April 2024) and their newer low-cost tiers lean Postgres-first; the MySQL-compatible tier is Vitess underneath, which has known foreign-key-constraint and some `ALTER TABLE` behavioral differences from real MySQL — worth a compatibility check against this app's actual migration history before committing, given EF Core relies on real FK constraints. |
| AWS RDS for MySQL | Variable, generally the most expensive at every tier of the three | Makes sense if there's already other AWS infra to attach it to; otherwise adds AWS billing-account overhead for a solo-dev setup with no other AWS footprint today. |

**This session should not create any of these accounts.** Phase 0 step 2 already required Phi
to have picked one and created the instance before this session starts; if it wasn't done,
stop here rather than substituting a self-hosted `StatefulSet` "to keep moving" — that would
silently reverse decision §2.2.

### 3.2 — VPS/host provider (money decision, Phi's call)

`CLAUDE.md` lists "Hetzner/AWS" without picking one. Not resolved by this prompt — Phase 0
step 1 required the box to already exist. If cost is the deciding factor: Hetzner's cloud VPS
tier is meaningfully cheaper than equivalent AWS EC2 for a single-node K3s box at this app's
current scale, which is why `CLAUDE.md` lists it first — but this is a recorded observation,
not this prompt overriding Phi's actual choice.

### 3.3 — Redis: self-hosted single instance in-cluster (lower-stakes than 3.1/3.2, flagged for visibility not a blocking question)

The original scoping note bundled Redis with MySQL as "same choice, managed vs. self-hosted."
This prompt splits them: Redis here is used for sessions, rate-limiting buckets, and
short-TTL slot locks (`CLAUDE.md`'s "never store state in-memory that should be in Redis"
rule) — none of it is data you'd mourn losing on a pod restart, unlike relational data in
MySQL. §8.5 below specs a self-hosted single-replica `Deployment` + `PersistentVolumeClaim`
(RDB snapshotting, not full AOF durability) as the pragmatic default. If a managed Redis
(e.g., a low-cost hosted Redis tier) is preferred instead, that's a small swap — flag it if
so, but this prompt proceeds with self-hosted since the cost/complexity tradeoff clearly
favors it for this specific data's disposability.

### 3.4 — Resource sizing / replica counts

This prompt specs 2 replicas for the API and 2 for the frontend as the rolling-update-safe
minimum (1 replica means "restart-on-crash" but not "no downtime during a deploy or a node
hiccup"), with conservative resource requests/limits (§8.2/§8.3) based on this being a
low-traffic solo-dev SaaS today, not a sized-for-load benchmark. **Re-tune these once real
traffic exists** — this prompt does not run a load test.

---

## 4. Explicit scope boundary — do not touch

- **No changes to `Pena_e_Arte.Domain/`, `Pena_e_Arte.Contracts/`, or any endpoint/handler
  file** — this is infra-and-startup-path only. The one exception, specified exactly, is
  §8.4's `Program.cs`/`ForwardedHeadersOptionsBuilder.cs`/new `Migrations:ApplyOnStartup`
  config flag changes — nothing else in the Application/API layers changes.
- **No changes to `frontend/src/**`** — the frontend's own runtime behavior (nginx proxy
  config, build) is already container-parity-correct; only its K8s wrapper is new.
- **No changes to `docker-compose.yml` or `docker/observability/*`** — those stay exactly as
  they are for local dev; this prompt reads them as the source of truth to translate into K8s
  form, it does not modify the source.
- **Do not touch `ci.yml`** — this prompt adds a **new** `cd.yml`, not a modification to the
  existing test/build gates. `ci.yml`'s `docker-build` job (build-no-push validation) stays
  exactly as-is; `cd.yml` is additive and only triggers on `main` after `ci.yml`'s checks
  pass.
- **Do not enable `dotnet ef database update` or any migration path other than the Phase 8.4
  Job** against the production database directly from a local machine or ad hoc script —
  the whole point of §8.4 is exactly one, auditable, CI-driven migration path.

---

## 5. Constraints (restated, apply throughout)

- No new NuGet/npm packages without flagging as a prerequisite decision. This prompt adds
  **zero** application-code packages; the one new dependency is `cert-manager` and (if used)
  `ingress-nginx`, both cluster-level Kubernetes add-ons, not app packages.
- No `useEffect` for data fetching — not applicable, this prompt touches no React data-fetch
  code.
- TypeScript strict / no `any` — not applicable, no `.ts`/`.tsx` changes.
- Explicit C# types, no `var` for non-obvious types — applies to the one C# change in §8.4.
- No business logic in endpoints (MediatR + FluentValidation only) — unaffected, no endpoint
  changes.
- Tenant isolation via EF Core global query filters — unaffected; migrations still run
  through the same `AppDbContext`.
- Every endpoint has `.RequireAuthorization()` — unaffected, no new endpoints.
- Never log PII, structured logs only — carries into K8s: no new log statements are added by
  this prompt; Serilog's existing Console/JSON sink is what Alloy already scrapes, unchanged
  by moving the container from `docker compose` to a K8s Pod.
- Tests ship with every change — see §9.

---

## 6. Current state — exact files this prompt depends on (read before writing anything)

### 6.1 `docker-compose.yml` — services being translated (already quoted in full in the repo;
key shape for `api`/`frontend`/`redis` services, environment variable names, and the
`prometheus`/`loki`/`tempo`/`alloy`/`grafana` observability services, is the ground truth for
every K8s manifest below — do not invent an env var name that isn't already in this file or
`.env.example`.)

### 6.2 `Pena_e_Arte.API/Dockerfile`
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
...
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD ["curl", "-f", "http://localhost:8080/health/live"]
ENTRYPOINT ["dotnet", "Pena_e_Arte.API.dll"]
```
Runs as non-root `appuser`. Port 8080. Health endpoints already exist and are correct for
K8s probes (see 6.3) — this prompt does not add new ones.

### 6.3 `Pena_e_Arte.API/Program.cs` — health checks and startup migration (lines ~47–58, 124–135)
```csharp
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<StripeHealthCheck>("stripe", tags: ["ready"]);

WebApplication app = builder.Build();

using (IServiceScope migrationScope = app.Services.CreateScope())
{
    AppDbContext migDb = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await migDb.Database.MigrateAsync();
}
...
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
```
**This is the exact race condition flagged in §8.4**: every pod replica runs
`MigrateAsync()` at startup, unguarded. With 1 replica (today's only real deployment target)
this is harmless. With 2+ replicas rolling out simultaneously, two pods can race to apply the
same pending migration concurrently — EF Core's migration history table does not provide
cross-process locking against this by default. §8.4 fixes it.

### 6.4 `Pena_e_Arte.API/Extensions/ForwardedHeadersOptionsBuilder.cs` (full file, 55 lines) —
already quoted in this project's audit; the relevant fact for §8.10: `ForwardedHeadersOptions`
is constructed with no explicit `ForwardLimit`, so it defaults to **1** hop. §8.10 explains why
this K8s topology has **2** proxy hops in front of the API and why that default breaks it.

### 6.5 `.env.example` — the full current env-var contract (already quoted in full above in
this project's research). Every K8s Secret/ConfigMap key in §8.8 must be a 1:1 rename of one
of these (e.g. `DB_CONNECTION_STRING` → `ConnectionStrings__Default`, matching the exact
`__` double-underscore convention `docker-compose.yml`'s `api.environment` block already uses)
— do not invent new config keys.

### 6.6 `frontend/nginx.conf.template` — proxies `/api/` and `/hubs/` to
`http://${BACKEND_HOST}:${BACKEND_PORT}` (WebSocket-upgrade-aware for `/hubs/`). This is why
§8.6's Ingress only needs one host: the frontend Pod is already a same-origin reverse proxy
to the API Service, exactly like the local compose topology. Do not add a second Ingress host
for the API unless a genuine external-only consumer emerges (there is none identified today —
Stripe webhooks hit `/api/webhooks/...`, which routes through the same proxy path).

### 6.7 `.github/workflows/ci.yml`'s `docker-build` job — builds both Dockerfiles, `push: false`,
"no registry configured" per its own comment. §9 (CD workflow) is the first thing to push
anywhere.

### 6.8 `docs/claude/architecture.md` Decisions Log (2026-07-26 observability entry) — explicit
prior statement: "Production/K3s rollout, alerting/on-call routing, retention-cost tuning, and
a public status page are explicitly out of scope — tracked as follow-ups, blocked on the CD
pipeline landing first." This prompt closes the "K3s rollout" and "CD pipeline" parts of that
follow-up. **Alerting/on-call routing, retention-cost tuning, and a public status page remain
out of scope after this prompt too** — do not silently expand into them (see §13).

---

## 7. Phase-by-phase spec

### Phase 1 — `k8s/` scaffold

Create:
```
k8s/
  base/
    namespace.yaml                 # pena-e-arte, monitoring
    api-deployment.yaml
    api-service.yaml
    frontend-deployment.yaml
    frontend-service.yaml
    redis-deployment.yaml
    redis-service.yaml
    redis-pvc.yaml
    migration-job.yaml
    ingress.yaml
    cluster-issuer.yaml
    kustomization.yaml
  overlays/
    production/
      kustomization.yaml           # sets image tags (pinned by CD, see §8.9), namespace
  observability/
    namespace.yaml                 # monitoring namespace (or reuse base/namespace.yaml)
    prometheus-configmap.yaml      # from docker/observability/prometheus.yml
    prometheus-deployment.yaml
    prometheus-service.yaml
    prometheus-pvc.yaml
    loki-configmap.yaml            # from docker/observability/loki-config.yml
    loki-deployment.yaml
    loki-service.yaml
    loki-pvc.yaml
    tempo-configmap.yaml           # from docker/observability/tempo.yaml
    tempo-deployment.yaml
    tempo-service.yaml
    tempo-pvc.yaml
    alloy-configmap.yaml           # from docker/observability/config.alloy
    alloy-daemonset.yaml           # DaemonSet, not Deployment — needs the Docker/containerd
                                    # socket on every node; irrelevant for a single-node
                                    # cluster today but correct if a second node is ever added
    grafana-configmap.yaml         # from docker/observability/grafana/provisioning/
    grafana-deployment.yaml
    grafana-service.yaml
    grafana-pvc.yaml
    kustomization.yaml
```
Use `apps/v1` for Deployments/DaemonSets, `v1` for Services/ConfigMaps/Secrets/PVCs,
`networking.k8s.io/v1` for Ingress, `batch/v1` for the migration Job,
`cert-manager.io/v1` for the ClusterIssuer — all current stable, non-deprecated API groups.

### Phase 2 — API `Deployment` + `Service`

Two replicas, `RollingUpdate` strategy (`maxSurge: 1`, `maxUnavailable: 0` — never drop below
2 healthy pods during a rollout), readiness/liveness probes matching the real endpoints from
§6.3 exactly:
```yaml
livenessProbe:
  httpGet: { path: /health/live, port: 8080 }
  initialDelaySeconds: 15
  periodSeconds: 10
readinessProbe:
  httpGet: { path: /health/ready, port: 8080 }
  initialDelaySeconds: 10
  periodSeconds: 5
  failureThreshold: 3
```
`initialDelaySeconds: 15` on liveness matches the Dockerfile's own `--start-period=15s`.
Resource requests/limits (§3.4 caveat applies): `requests: { cpu: 100m, memory: 256Mi }`,
`limits: { cpu: 500m, memory: 512Mi }` — conservative starting point for current traffic, not
a sizing benchmark. Pull all config from the `pena-e-arte-api-config` ConfigMap and
`pena-e-arte-api-secrets` Secret (§8.8) via `envFrom`, not individually listed `env:` entries,
so adding a new setting later is a one-file change. `Service` is `ClusterIP`, port 8080 →
targetPort 8080, name `pena-e-arte-api`.

### Phase 3 — Frontend `Deployment` + `Service`

Same replica/rolling-update shape as Phase 2. Two env vars only, matching
`docker-compose.yml`'s `frontend.environment` block exactly:
```yaml
env:
  - name: BACKEND_HOST
    value: pena-e-arte-api       # the K8s Service DNS name from Phase 2, same-namespace short form
  - name: BACKEND_PORT
    value: "8080"
```
`nginx-unprivileged`'s built-in `envsubst`-on-templates entrypoint (already relied on by the
compose setup) resolves these into `nginx.conf.template` at container start — no new
mechanism needed. `Service` is `ClusterIP`, port 8080 → targetPort 8080, name
`pena-e-arte-frontend`. No `readinessProbe`/`livenessProbe` change needed beyond the
Dockerfile's existing `wget --spider http://localhost:8080/` shape, translated to a K8s
`httpGet` probe on `/`.

### Phase 4 — Migration `Job` + `Program.cs`/config change (the race-condition fix from §6.3)

**Exact `Program.cs` diff** — gate the existing auto-migrate block behind a new config flag,
default `true` so local `dotnet run`/`docker compose` behavior is completely unchanged:

```csharp
// Before:
using (IServiceScope migrationScope = app.Services.CreateScope())
{
    AppDbContext migDb = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await migDb.Database.MigrateAsync();
}

// After:
if (builder.Configuration.GetValue("Migrations:ApplyOnStartup", defaultValue: true))
{
    using IServiceScope migrationScope = app.Services.CreateScope();
    AppDbContext migDb = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    await migDb.Database.MigrateAsync();
}
```
Add `"Migrations": { "ApplyOnStartup": true }` to `appsettings.json` (explicit default,
matches current behavior everywhere this key isn't overridden — local dev and
`docker-compose.yml` need zero changes). In K8s production only, the API `Deployment`'s
ConfigMap sets `Migrations__ApplyOnStartup=false`.

**New `batch/v1` `Job`** (`k8s/base/migration-job.yaml`), same image as the API Deployment,
same env/secret wiring, **no** `Migrations__ApplyOnStartup` override (so it defaults to
`true` and does exactly one migration run), `restartPolicy: Never`,
`backoffLimit: 2`. CD (§8.9) runs `kubectl apply -f migration-job.yaml && kubectl wait
--for=condition=complete job/pena-e-arte-migrate --timeout=120s` **before** rolling out the
new API Deployment image — one controlled migration, then the rollout, not N pods racing.
Delete-and-recreate the Job object on each deploy (`kubectl delete job ... --ignore-not-found`
before `apply`) since Job names aren't reusable with new pod specs otherwise.

**Test to add:** `tests/Pena_e_Arte.IntegrationTests/Startup/MigrationsApplyOnStartupTests.cs`
— boots the test host twice with `Migrations:ApplyOnStartup=false` via
`WebApplicationFactory`'s config override, asserts `Database.MigrateAsync()` is not invoked
(mock/spy the scope or assert via a schema-version check), and once with the flag unset/true
asserting it is. Matches this codebase's existing NSubstitute-at-the-handler-level testing
convention — do not add a live MySQL dependency to this specific test if avoidable.

### Phase 5 — Redis `Deployment` + `Service` + `PersistentVolumeClaim`

Single replica (§3.3), `redis:7-alpine` (same tag as compose), `PersistentVolumeClaim`
(`1Gi`, default `StorageClass` — K3s ships `local-path-provisioner` by default, confirm it's
enabled on the Phase-0 box). `Service` `ClusterIP`, port 6379, name `pena-e-arte-redis`. No
password today (matches `docker-compose.yml`'s Redis, which also has none) — **flag
explicitly**: an unauthenticated Redis reachable from anything else in the same cluster
namespace is an accepted risk today because nothing else shares the namespace, but is worth
revisiting if the cluster ever hosts a second workload. Not fixed in this prompt — named, not
silently left undocumented.

### Phase 6 — `Ingress` + cert-manager

Install `cert-manager` (`kubectl apply -f
https://github.com/cert-manager/cert-manager/releases/latest/download/cert-manager.yaml` —
CD should pin an exact version tag rather than `latest` once Phase 0 confirms the target;
flag the exact pinned version as a TODO if not resolved before merging) and `ingress-nginx`
(K3s ships Traefik by default — **explicit decision needed**: either disable Traefik at K3s
install time — `curl -sfL https://get.k3s.io | sh -s - --disable traefik` — and install
ingress-nginx to match `CLAUDE.md`'s documented stack, or use Traefik's own
`IngressRoute`/cert-manager integration instead. This prompt writes standard
`networking.k8s.io/v1 Ingress` objects, which work with either controller, but the
Traefik-vs-nginx-ingress choice affects Phase 0's install command — **flag this back to Phi
if K3s was already installed with Traefik still enabled**, don't silently reconfigure a live
cluster).

`ClusterIssuer` (Cloudflare DNS-01):
```yaml
apiVersion: cert-manager.io/v1
kind: ClusterIssuer
metadata:
  name: letsencrypt-prod-dns01
spec:
  acme:
    server: https://acme-v02.api.letsencrypt.org/directory
    email: <Phi's real contact email — confirm before merging>
    privateKeySecretRef:
      name: letsencrypt-prod-dns01-key
    solvers:
      - dns01:
          cloudflare:
            apiTokenSecretRef:
              name: cloudflare-api-token
              key: api-token
```
`Ingress`:
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: pena-e-arte
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod-dns01
    nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"   # matches nginx.conf.template's
    nginx.ingress.kubernetes.io/proxy-send-timeout: "3600"   # own /hubs/ SignalR timeout
spec:
  tls:
    - hosts: ["app.tattooos.co"]     # CONFIRM against real DNS record, see §2.4
      secretName: pena-e-arte-tls
  rules:
    - host: app.tattooos.co
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: pena-e-arte-frontend
                port: { number: 8080 }
```
Cloudflare API token delivered as a `v1 Secret` (`cloudflare-api-token`), populated from the
`CLOUDFLARE_API_TOKEN` GitHub secret by CD (§8.9) — never committed.

### Phase 7 — Observability namespace

Translate each `docker/observability/*` config file into a `ConfigMap` (`kubectl create
configmap ... --from-file=...` semantics, expressed declaratively in the YAML) mounted into
the matching Deployment/DaemonSet, 1:1 with the compose service it replaces:

- `prometheus` — Deployment, 1 replica, PVC for `/prometheus` data, scrape config from the
  translated `prometheus.yml` **with one required edit**: the compose config's
  `pena-e-arte-api-container`/`pena-e-arte-api-host` dual-target setup (for the
  containerized-vs-`dotnet run` dev topologies) collapses to a single target,
  `pena-e-arte-api.pena-e-arte.svc.cluster.local:8080`, in K8s — there is no "host" topology
  in production. Flag this as an intentional simplification, not an oversight, in the
  translated file's comments.
- `loki` — Deployment, 1 replica, PVC for `/loki`.
- `tempo` — Deployment, 1 replica, PVC for `/var/tempo`, no liveness/readiness `exec` probe
  (distroless image, same reason the Docker Compose version has none — use an `httpGet` probe
  against `/ready` on port 3200 instead, which *is* possible in K8s even though a shell-based
  `HEALTHCHECK` wasn't in Docker).
- `alloy` — **DaemonSet**, not Deployment (see Phase 1 note), mounts the containerd socket
  (`/run/k3s/containerd/containerd.sock` — K3s's containerd socket path differs from Docker's
  `/var/run/docker.sock` the compose file mounts; confirm exact path against the real Phase-0
  box, K3s defaults to `/run/k3s/containerd/containerd.sock`) instead of the Docker socket,
  since K3s uses containerd, not the Docker daemon, as its container runtime — **this is a
  real config difference from the compose version's `loki.source.docker`, not a copy-paste
  translation**; Alloy's `loki.source.containerd` (or the appropriate K3s-compatible log
  source, confirm against Alloy's current docs before writing the config) replaces
  `loki.source.docker` in the K8s config's log-scraping stanza.
- `grafana` — Deployment, 1 replica, PVC for `/var/lib/grafana`, admin credentials from a
  `v1 Secret` populated by CD from `PROD_GRAFANA_ADMIN_USER`/`PROD_GRAFANA_ADMIN_PASSWORD`
  GitHub secrets (never `admin`/`admin`, matching the same guard `docker-compose.yml`'s
  `GF_SECURITY_ADMIN_PASSWORD:?...` already enforces locally). Not exposed via the public
  Ingress in this prompt — reachable via `kubectl port-forward` only, since there's no
  decided-yet need for public Grafana access and exposing an admin dashboard publicly without
  a considered auth story is exactly the kind of thing this project's rules say to flag rather
  than default into. **Flagged, not built**: if Phi wants Grafana reachable at, say,
  `grafana.tattooos.co`, that's a small additive Ingress host plus an actual auth decision
  (Grafana's own OAuth, or an additional `nginx.ingress.kubernetes.io/auth-type` basic-auth
  gate) — named as a follow-up, not assumed.

### Phase 8 — Secrets management

Plain `v1 Secret` objects, `type: Opaque`, populated at deploy time by CD (`kubectl create
secret generic ... --from-literal=... --dry-run=client -o yaml | kubectl apply -f -`, the
standard idempotent-apply pattern) from GitHub Actions secrets — **not** committed to
`k8s/` as YAML with real values, and not using a new secrets-management tool
(Sealed Secrets, External Secrets Operator, Vault) since none is already in this stack and
`CLAUDE.md` rule 4 is satisfied by "secrets via environment variables," which GitHub Actions
secrets → K8s Secrets already achieves without new infrastructure. Two Secret objects:
`pena-e-arte-api-secrets` (JWT, Stripe, R2, Resend, Hangfire, DB connection string — the
sensitive half of §6.5's env-var list) and `cloudflare-api-token` (Phase 6). Everything
non-sensitive (`Jwt__Issuer`, `Jwt__Audience`, `App__BaseUrl`, `Cors__AllowedOrigins__0`,
`Migrations__ApplyOnStartup`) goes in a plain `ConfigMap`, `pena-e-arte-api-config`, matching
`docker-compose.yml`'s own sensitive/non-sensitive split (compare which vars there have a
`:?` required-and-hidden default vs. a visible `:-` fallback).

### Phase 9 — CD workflow (`.github/workflows/cd.yml`, new file)

Triggers on `push` to `main`, **after** `ci.yml`'s checks pass (use `workflow_run` trigger
keyed on `ci.yml`'s completion with `conclusion == 'success'`, not a bare `push` trigger that
races CI). Jobs:

1. **`build-and-push`** — `docker/build-push-action@v7` for both images, `push: true`, tag
   `ghcr.io/471k/pena-e-arte-api:${{ github.sha }}` and `:latest`, same for `-frontend`, same
   build-args the existing `docker-build` CI job already uses.
2. **`deploy`** (needs `build-and-push`) — writes `KUBE_CONFIG` secret to a temp kubeconfig
   file, `kubectl apply -k k8s/overlays/production` after `overlays/production/kustomization.yaml`
   pins the freshly-built `github.sha` image tags (via `kustomize edit set image`, run as a
   step, not hand-edited), applies/recreates the migration Job (Phase 4) and waits for its
   completion **before** applying the API/frontend Deployments, then `kubectl rollout status
   deployment/pena-e-arte-api -n pena-e-arte --timeout=180s` and the same for frontend — fail
   the workflow if either rollout doesn't complete, don't silently leave a half-rolled-out
   Deployment.
3. Secrets consumed: `KUBE_CONFIG`, `PROD_DB_CONNECTION_STRING`, `JWT_SECRET_KEY` (prod
   value), `STRIPE_SECRET_KEY`/`STRIPE_PUBLISHABLE_KEY`/webhook secrets, R2 credentials,
   `RESEND_API_KEY`, `HANGFIRE_DASHBOARD_USERNAME`/`PASSWORD`, `CLOUDFLARE_API_TOKEN`,
   `PROD_GRAFANA_ADMIN_USER`/`PASSWORD`, `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR` (see §8.10 for
   its value). All referenced by name, none hardcoded, none logged (mask them in any
   `echo`/debug step).

### Phase 10 — `ForwardedHeaders:TrustedProxyCidr` real value + the two-hop `ForwardLimit` fix

This is the concrete "done" signal from the original scoping note — and there's a real
correctness bug underneath it worth fixing precisely, not just filling in a CIDR:

**The production request path has two reverse-proxy hops in front of the API**, not one:
`client → (Cloudflare, if proxied) → ingress-nginx Pod → frontend nginx Pod (proxies /api/) →
API Pod`. Both `ingress-nginx` and the frontend's own nginx container run as Pods inside the
cluster, so both hops' source IPs fall inside the cluster's Pod CIDR — K3s's default (Flannel)
Pod CIDR is `10.42.0.0/16` **only if the Phase-0 install didn't override it**; confirm via
`kubectl cluster-info dump | grep -m1 cluster-cidr` or `/etc/rancher/k3s/config.yaml` on the
real box before hardcoding this. Set `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR` to that confirmed
value.

**That alone is not sufficient.** `ForwardedHeadersOptionsBuilder.cs` (§6.4) constructs
`ForwardedHeadersOptions` without setting `ForwardLimit`, which defaults to **1** — meaning
ASP.NET Core only strips *one* trusted hop off the `X-Forwarded-For` chain even if two are
present and both match `KnownNetworks`. With nginx's `$proxy_add_x_forwarded_for` correctly
appending at each hop, the header arriving at Kestrel looks like `<real-client-ip>,
<ingress-nginx-pod-ip>` — but with `ForwardLimit: 1`, the middleware only processes the
right-most entry and `HttpContext.Connection.RemoteIpAddress` ends up as the *ingress-nginx
pod's* IP, not the real client's, defeating the exact per-client rate-limiting this config was
added for in today's security pass. **Fix, exact diff:**
```csharp
ForwardedHeadersOptions options = new()
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 2,   // two trusted proxy hops in the K3s topology: ingress-nginx, then the
                         // frontend Pod's own nginx (see docs/claude/overnight-prompt-
                         // k3s-production-deploy-2026-07-26.md §8.10 for the full chain)
};
```
**Test to add:** extend `tests/Pena_e_Arte.IntegrationTests/Middleware/ForwardedHeadersTests.cs`
(already exists per §6.4's context) with a case sending
`X-Forwarded-For: 203.0.113.7, 10.42.1.5` from a `TestServer` request whose direct connection
IP is also inside the trusted CIDR, asserting `HttpContext.Connection.RemoteIpAddress`
resolves to `203.0.113.7` (the real client), not `10.42.1.5` (the first hop) — this is the
exact regression `ForwardLimit: 1` would silently reintroduce.

---

## 8. Test requirements

- **Unit:** none new beyond what Phase 4/Phase 10 already specify (both are integration-level
  concerns — a unit test mocking `IConfiguration` for the `Migrations:ApplyOnStartup` branch
  is optional/cheap to add but the integration test is the one that actually proves it).
- **Integration:** `MigrationsApplyOnStartupTests.cs` (Phase 4), extended
  `ForwardedHeadersTests.cs` (Phase 10) — both must run in CI (`ci.yml`'s existing
  `integration` test job, no new job needed, no new external service dependency).
- **Frontend:** none — no frontend code changes in this prompt.
- **Infra verification (manual, part of Phase 9's own "done," not an automated test):**
  1. Open a draft PR touching only `k8s/**`/`cd.yml`, confirm `ci.yml` still passes unaffected.
  2. Merge to a throwaway branch pointed at the real (Phase-0) cluster once available, watch
     `cd.yml` run end to end: image push visible in GHCR, migration Job completes, both
     Deployments roll out, `kubectl get pods -n pena-e-arte` shows 2/2 Ready on each.
  3. `curl -I https://app.tattooos.co` returns a valid Let's Encrypt cert (not self-signed),
     confirm via `openssl s_client -connect app.tattooos.co:443 -servername app.tattooos.co
     </dev/null 2>/dev/null | openssl x509 -noout -issuer`.
  4. Kill one API pod (`kubectl delete pod ...`) mid-session, confirm the Service keeps
     serving from the remaining replica with zero dropped requests during the restart
     (matches the "restart-on-crash" goal from the original gap description).
  5. Open Grafana via `kubectl port-forward`, confirm the same `api-overview.json` RED
     dashboard from the local stack renders real data against production traffic — proves
     the observability-stack translation (Phase 7) actually receives telemetry, not just that
     the Pods are Running.

---

## 9. Help-menu sync

**No Help Menu / user-manual / onboarding-tour changes.** This prompt has zero user-visible
surface: no new UI, no new user-facing workflow, no change to what any role (`client`,
`artist`, `owner`, `issuer`) can see or do — the app behaves identically to a user whether it's
served from `docker compose up` or from the K3s cluster this prompt stands up. Stated
explicitly per `CLAUDE.md` rule #7's stated exception for zero-user-visible-surface changes,
not silently skipped.

---

## 10. Industry-standard benchmark note

This is infrastructure, not a booking-SaaS UX feature, so the relevant benchmark is general
B2B SaaS *operational* practice, not the Vagaro/Fresha/Boulevard-tier UX set `CLAUDE.md` rule
#6 names for feature work. What a comparable-stage SaaS is generally expected to have:
multi-replica deploys with zero-downtime rollout (this prompt: yes, Phase 2/3/9), automated
TLS (yes, Phase 6), centralized structured logging/metrics/tracing reachable without SSHing
into a box (yes, Phase 7), and a single-command/CI-driven deploy path instead of a manual
process (yes, Phase 9). What a comparable-stage SaaS is generally also expected to have that
**this prompt deliberately does not build**, per the same Decisions Log entry that scoped this
work: **alerting/on-call routing** (Prometheus/Grafana are queryable, but nothing pages anyone
on a fired alert), **a public status page**, and **tuned log/metric retention** (Loki/Tempo/
Prometheus run with their out-of-the-box retention defaults, not a cost- or
compliance-informed policy). These are real gaps, not oversights — named here again so they
don't quietly fall off the backlog now that the thing they were blocked on has landed.

---

## 11. Final self-check / verification checklist

Before declaring this done:

- [ ] `dotnet build` clean, all existing tests green, plus the two new tests from Phase 4/10.
- [ ] No drift from §4's "do not touch" list — diff review confirms only `k8s/**`,
      `.github/workflows/cd.yml`, `Program.cs`, `ForwardedHeadersOptionsBuilder.cs`,
      `appsettings.json`, and the two new test files changed.
- [ ] No secret value committed anywhere in `k8s/**` or `cd.yml` — grep the diff for anything
      that looks like a real key/token/password before committing.
- [ ] No PII in any new log statement — there are none added, confirm by diff.
- [ ] `kubectl apply -k k8s/overlays/production --dry-run=server` succeeds against the real
      cluster (validates schema/RBAC without actually applying).
- [ ] All five §9 manual infra-verification steps completed against the real Phase-0 cluster,
      not just "the YAML has no syntax errors."
- [ ] `docs/claude/architecture.md` Decisions Log entry added (§13) confirming what shipped,
      what got flagged instead of built (§3, §7 Phase 6/7 flagged items, §10's named gaps),
      and the real values used for anything this prompt left as a placeholder (domain,
      Pod CIDR, MySQL provider, VPS host).
- [ ] `ForwardedHeaders:TrustedProxyCidr` set to the *confirmed* (not assumed) Pod CIDR from
      the real cluster.

---

## 12. Final deliverable spec

**Files written/changed:**
- `k8s/**` (new, per §7 Phase 1 structure)
- `.github/workflows/cd.yml` (new)
- `Pena_e_Arte.API/Program.cs` (Phase 4 diff)
- `Pena_e_Arte.API/Extensions/ForwardedHeadersOptionsBuilder.cs` (Phase 10 diff)
- `Pena_e_Arte.API/appsettings.json` (`Migrations:ApplyOnStartup` default)
- `tests/Pena_e_Arte.IntegrationTests/Startup/MigrationsApplyOnStartupTests.cs` (new)
- `tests/Pena_e_Arte.IntegrationTests/Middleware/ForwardedHeadersTests.cs` (extended)
- `docs/claude/architecture.md` — new Decisions Log entry (this session writes it; note this
  file lives under `docs/claude/`, which the main Engineering project *can* write to since
  Help/architecture docs ship alongside the feature that needed them — unlike this
  consultation project, which is doc-only for everything)

**Commit message(s)** (separate commits, in this order):
1. `fix(api): gate startup migrations behind Migrations:ApplyOnStartup, fix ForwardLimit for two-hop K3s proxy chain`
2. `feat(k8s): add production manifests — API/frontend/redis deployments, ingress+cert-manager, migration job`
3. `feat(k8s): add in-cluster observability stack (prometheus/loki/tempo/alloy/grafana)`
4. `feat(ci): add cd.yml — build/push images to GHCR, deploy to K3s on merge to main`
5. `docs(architecture): log K3s production deployment decision`

---

## 13. Explicitly out of scope after this prompt (do not silently build these either)

- Alerting/on-call routing (Alertmanager or equivalent) — named in §10 and in today's earlier
  Decisions Log entry, still not built here.
- Public status page.
- Log/metric/trace retention tuning beyond each tool's shipped defaults.
- Autoscaling (`HorizontalPodAutoscaler`) — §3.4's replica counts are static.
- Multi-node K3s / high-availability control plane — Phase 0 provisions a single-node cluster;
  this prompt does not add a second node or etcd HA.
- Backup/restore runbook for the managed MySQL instance — assumed covered by whichever managed
  provider Phi picks (§3.1), but not verified or documented by this prompt. **Flag this as a
  near-term follow-up**: "managed" reduces operational burden, it does not eliminate the need
  to know the provider's actual backup/point-in-time-recovery story before this app is
  handling real customer data in production.
