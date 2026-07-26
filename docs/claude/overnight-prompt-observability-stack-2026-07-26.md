# Overnight Master Prompt — Stand Up the Observability Stack (Prometheus + Loki + Tempo + Grafana, Local First)

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact
> files, exact current code, exact target config, exact verification steps. Read the whole
> file before touching anything — later phases depend on decisions made in §2 and §3.

**Date logged:** 2026-07-26
**Requested by:** Phi
**Origin:** Continuation of an interrupted consultation session. App-side instrumentation
(Serilog structured logging + OpenTelemetry tracing/metrics, tenant/user/request
correlation, OTLP exporter config) was already verified wired and correct — see
`docs/claude/architecture.md`'s Decisions Log entry "Structured-log correlation fields"
(undated in the log itself — `RequestIdMiddleware` predates the earliest overnight prompt
this project has records of; its exact origin session isn't recoverable and isn't needed
here) — but there is **no backend for any of it to talk to**: no Grafana,
Prometheus, Loki, or Tempo in `docker-compose.yml`, and no CD pipeline/deployed environment
to observe in the first place (`docs/claude/overnight-prompt-ci-pipeline-2026-07-26.md`
explicitly scoped deployment out of tonight's CI work as a separate follow-up). This prompt
closes the local half of that gap: wire up the collector stack CLAUDE.md's own tech-stack
table already names (`Grafana · Prometheus · Loki · Tempo`) in `docker-compose.yml` so a
developer running `docker compose up` locally can actually see the traces, metrics, and logs
the app is already emitting. **Production/K3s rollout of this stack is explicitly out of
scope tonight** — see §3 "do not build blind" — for the same reason CD itself is out of
scope: there is no deployed environment yet to ship it to.

**Mode:** Fully autonomous. No user present. Run until every phase exits clean.
**Run with:** `claude --dangerously-skip-permissions`
**Before starting:**
```
git add -A && git commit -m "chore: pre-observability-stack checkpoint"
git checkout -b feat/observability-stack-2026-07-26
```

---

## 1. Read First

1. `CLAUDE.md` — non-negotiable rules, especially #3 (never log PII), #4 (secrets never in
   source), #5 (structured logs only), #6 (industry-standard bar), #7 (Help-sync — see §8,
   though the verdict here is "no change needed," stated explicitly, not silently skipped).
2. `docker-compose.yml` — ground truth for existing services, port map, env-var override
   pattern (`${VAR:-default}` and `${VAR:?error message}`), and the "container-parity
   services" comment block above `api`/`frontend`.
3. `.env.example` — ground truth for how new required secrets get documented (see
   `HANGFIRE_DASHBOARD_USERNAME`/`PASSWORD`'s `:?` guard pattern — mirror it for Grafana).
4. `Pena_e_Arte.API/Program.cs` (lines 26–43, 104–106, ~132) — Serilog bootstrap,
   `AddApiOpenTelemetry` call site, `UseSerilogRequestLogging`, `app.MapPrometheusScrapingEndpoint()`.
5. `Pena_e_Arte.API/Extensions/OpenTelemetryExtensions.cs` — full current OTel wiring
   (quoted verbatim in §2 below).
6. `Pena_e_Arte.API/Middleware/RequestIdMiddleware.cs` and
   `Pena_e_Arte.API/Middleware/RequestLoggingEnrichment.cs` — existing `request_id`/`user_id`/
   `tenant_id` correlation (quoted verbatim in §2).
7. `Pena_e_Arte.API/appsettings.json` and `appsettings.Development.json` — current
   `OpenTelemetry:OtlpEndpoint` and `Serilog:WriteTo` (Console + `CompactJsonFormatter`) config.
8. `Pena_e_Arte.API/Extensions/HangfireDashboardAuthFilter.cs` — the existing pattern for
   gating an ops dashboard behind `issuer`-role auth; not directly reusable for Grafana (a
   separate process with its own auth), but mirror its *spirit* (no default credentials,
   no anonymous access) when setting Grafana's admin password.
9. `docs/claude/architecture.md` Decisions Log — read the full "Structured-log correlation
   fields" entry before writing anything; it already documents exactly what exists and
   what's missing. Do not re-derive this from scratch.

---

## 2. Working Context — Confirmed Facts (verified against live source, not assumed)

- **Metrics and traces are already fully wired for local consumption** —
  `OpenTelemetryExtensions.AddApiOpenTelemetry` (verbatim):

  ```csharp
  public static IServiceCollection AddApiOpenTelemetry(
      this IServiceCollection services,
      IConfiguration configuration)
  {
      string? otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];

      services.AddOpenTelemetry()
          .ConfigureResource(r => r.AddService("Pena_e_Arte.API"))
          .WithTracing(tracing =>
          {
              tracing.AddAspNetCoreInstrumentation();
              if (!string.IsNullOrEmpty(otlpEndpoint))
                  tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
          })
          .WithMetrics(metrics =>
          {
              metrics.AddAspNetCoreInstrumentation();
              metrics.AddPrometheusExporter();
              if (!string.IsNullOrEmpty(otlpEndpoint))
                  metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
          });

      return services;
  }
  ```

  `Program.cs` already calls `app.MapPrometheusScrapingEndpoint()` (default path `/metrics`,
  no auth — same visibility level as `/health`, acceptable for a metrics endpoint per
  standard Prometheus practice, not PII-bearing).
- **`OpenTelemetry:OtlpEndpoint`** is `""` in `appsettings.json` (prod default — OTLP export
  disabled until a real endpoint is configured) and `"http://localhost:4317"` in
  `appsettings.Development.json`. Nothing listens on `4317` today — this prompt makes that
  true.
- **Logs are Console-only** — `Serilog:WriteTo` is `Console` with
  `Serilog.Formatting.Compact.CompactJsonFormatter` (one JSON object per line, already
  machine-parseable — no reformatting needed for a log backend to consume it).
- **`request_id` correlation exists but is NOT confirmed to be the same value as the
  OpenTelemetry trace ID.** Current code, verbatim:

  ```csharp
  // Pena_e_Arte.API/Middleware/RequestIdMiddleware.cs
  public class RequestIdMiddleware(RequestDelegate next)
  {
      public async Task InvokeAsync(HttpContext context)
      {
          using (LogContext.PushProperty("request_id", context.TraceIdentifier))
              await next(context);
      }
  }
  ```

  ```csharp
  // Pena_e_Arte.API/Middleware/RequestLoggingEnrichment.cs
  public static class RequestLoggingEnrichment
  {
      public static void Enrich(IDiagnosticContext diagnosticContext, HttpContext httpContext)
      {
          diagnosticContext.Set("request_id", httpContext.TraceIdentifier);

          if (httpContext.User.Identity?.IsAuthenticated != true) return;

          string? userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
          string? tenantId = httpContext.User.FindFirst("tenant_id")?.Value;
          if (userId is not null) diagnosticContext.Set("user_id", userId);
          if (tenantId is not null) diagnosticContext.Set("tenant_id", tenantId);
      }
  }
  ```

  Both push/set **`context.TraceIdentifier`** (ASP.NET Core's own internal per-request
  identifier) as `request_id`. **`context.TraceIdentifier` is not
  guaranteed to equal `System.Diagnostics.Activity.Current?.TraceId` (the W3C/OTel trace ID
  that Tempo will actually store).** This matters: the entire value proposition of an LGTM
  stack over three disconnected tools is jumping from a log line straight to its trace in
  one click, and that only works if the ID in the log line is the *same* ID Tempo indexes
  traces by. Verify this empirically in Phase 2 before assuming either way — do not guess.
- **Docker-compose has no `networks:` block** — all current services (`mysql`, `redis`,
  `minio`, `api`, `frontend`) share Compose's implicit default network and resolve each other
  by service name. New services join the same default network with no extra config.
- **Ports already in use:** `3306` (mysql), `6379` (redis), `9000`/`9001` (minio), `8080`
  (api), `8081` (frontend). New ports this prompt adds — `3000` (Grafana), `9090`
  (Prometheus), `3100` (Loki), `3200`/`4317`/`4318` (Tempo — query API / OTLP gRPC / OTLP
  HTTP), `12345` (Alloy UI) — do not collide with any of these.
- **`Promtail` is EOL as of March 2, 2026** (Grafana Labs deprecated it in favor of Grafana
  Alloy on 2025-02-13; commercial support ended, no further updates). Do not use Promtail —
  any tutorial or training-data memory suggesting a `promtail` service in `docker-compose.yml`
  is describing a dead tool as of today's date. **Grafana Alloy is the current, supported log
  shipper** and is what this prompt specifies in Phase 5.
- **The API and frontend containers can run in two different local topologies**, and the
  stack must work for both without the developer having to choose one permanently:
  1. Fully containerized (`docker compose up --build api frontend`) — Prometheus reaches the
     API at the Compose service name `api:8080`.
  2. Fast local dev (`dotnet run` / `pnpm dev` against the infra containers only) — the API
     process runs on the host, not in a container, so Prometheus (itself containerized) must
     reach it via `host.docker.internal:8080`, which requires an `extra_hosts` entry on Linux
     (native on Docker Desktop for Mac/Windows).
  Phase 4 scrapes both targets — a target that's down just shows `up == 0` in Prometheus,
  which is harmless, and this avoids forcing a topology decision that isn't this prompt's to
  make.

---

## 3. Decisions already made vs. decisions to flag

### 3.1 Already decided — implement as specified, do not re-litigate

1. **Collector stack: Grafana + Prometheus + Loki + Tempo.** This isn't an open question —
   `CLAUDE.md`'s own tech-stack table already names exactly these four tools under `Infra`.
   Tonight's job is wiring, not re-picking a stack.
2. **Log shipping: Grafana Alloy, not Promtail, and not a new Serilog sink NuGet package.**
   Alloy tails Docker container stdout directly via the Docker socket
   (`discovery.docker` + `loki.source.docker`) and ships to Loki. This means **zero new
   backend NuGet packages** are needed for logs — Serilog keeps writing `CompactJsonFormatter`
   JSON to Console exactly as it does today; Alloy reads it from Docker's log API. This is the
   preferred path over adding e.g. `Serilog.Sinks.Grafana.Loki` per CLAUDE.md's spirit of not
   adding packages when a config-only path exists.
3. **Loki label design: do NOT put `request_id`, `user_id`, or `tenant_id` in Loki stream
   labels.** Loki's index is built from label combinations — high-cardinality values as
   labels (a new one per request) cause severe ingester memory pressure and slow queries as
   the dataset grows (documented Loki anti-pattern, not a matter of opinion). Labels stay
   low-cardinality: `container`, `service_name`, `compose_project`. `request_id`/`user_id`/
   `tenant_id` remain in the JSON log body and are queried at query time via LogQL's `| json`
   parser (e.g. `{service_name="pena-e-arte-api"} | json | request_id="..."`).
4. **Local `docker-compose.yml` only. No K3s manifests, no remote-write, no managed Grafana
   Cloud.** Mirrors `overnight-prompt-ci-pipeline-2026-07-26.md`'s own scope cut for CD — there
   is no deployed environment for a production observability stack to observe yet. Building
   K3s DaemonSet/StatefulSet manifests for a cluster that doesn't have CD wired to it yet
   would be speculative infrastructure with no way to verify it actually works.
5. **Grafana gets a real admin password via a required env var, no default, mirroring the
   existing `HANGFIRE_DASHBOARD_USERNAME`/`PASSWORD` pattern exactly.** Anonymous access
   stays off (Grafana's default when `GF_AUTH_ANONYMOUS_ENABLED` is unset).
6. **Datasources and dashboards are file-provisioned** (`grafana/provisioning/datasources/*.yaml`,
   `grafana/provisioning/dashboards/*.yaml` + one starter JSON dashboard), not clicked together
   in the UI — so a fresh `docker compose up` reproduces the exact same Grafana state for every
   developer, matching the "reproducible local environment" spirit of the rest of
   `docker-compose.yml`.

### 3.2 Flag, verify empirically, do not assume

1. **Whether `context.TraceIdentifier` already equals the OTel `Activity.Current.TraceId`
   hex string in this specific ASP.NET Core 10 + `AddAspNetCoreInstrumentation()` setup.**
   This determines whether Phase 2 is a no-op verification or a real code change:
   - **If they already match** (test per Phase 2's steps): no code change needed — just
     confirm it explicitly in the final summary and in the `architecture.md` Decisions Log
     entry, so nobody "fixes" a non-bug later.
   - **If they don't match**: add a `trace_id`/`span_id` `LogContext.PushProperty` call
     sourced from `System.Diagnostics.Activity.Current?.TraceId.ToHexString()` /
     `.SpanId.ToHexString()` inside `RequestIdMiddleware` (same file, same pattern, zero new
     packages — the `System.Diagnostics.Activity` API is already available via the ASP.NET
     Core / OpenTelemetry SDK references already in the project). Do **not** reach for
     `Serilog.Enrichers.Span` (a real, maintained package that does the same thing) without
     flagging it as a new-dependency decision first — the manual one-line version is
     preferred here since it's zero-dependency and the codebase already leans toward "no new
     package if a few lines of code do the same job" (see how `RequestIdMiddleware` itself is
     hand-rolled rather than a middleware package).
2. **Whether `OpenTelemetry.Instrumentation.EntityFrameworkCore` and
   `OpenTelemetry.Instrumentation.Http` should be added** to get DB-query spans and outbound
   HTTP spans (Stripe/Resend/Twilio calls) inside each trace, not just the inbound
   ASP.NET Core span. This is genuinely valuable — right now a slow request shows up in
   Tempo as one opaque span with no visibility into whether the time went to MySQL, Redis, or
   an external API call, which is exactly the kind of gap that makes an N+1 query or a slow
   third-party call invisible until someone greps logs by hand. **Do not add these packages
   tonight without flagging it** — confirm current package stability/version before adding
   (these have historically shipped as prerelease/beta in the `open-telemetry-dotnet-contrib`
   repo) and note the decision explicitly in the final summary either way. This is the single
   biggest "do not build blind" item in this prompt — see §3.3.

### 3.3 Do not build blind — backlog, not built tonight

- **Production/K3s rollout of this stack** — needs persistent-volume sizing decisions for
  Prometheus/Loki/Tempo retention, an ingress + auth story for Grafana in a real cluster,
  and Alloy running as a DaemonSet (not a single container reading one host's Docker socket)
  once there's more than one node. All of this is blocked on the CD pipeline itself not
  existing yet (`overnight-prompt-ci-pipeline-2026-07-26.md`). Do not start this tonight.
- **Retention windows and their storage-cost trade-off** — tonight's config uses short local
  retention (see Phase 3/4 configs) suitable for a dev laptop, not a considered cost decision
  for a production deployment. That's a real business decision (how many days of traces/logs
  justify the storage spend) that belongs to whoever owns infra cost once there's a
  production target to size against.
- **Alerting / on-call routing** (Grafana Alerting → Slack/PagerDuty/email) — no on-call tool
  has been chosen for this product yet. Do not wire alert rules to a destination that doesn't
  exist; a dashboard with data is tonight's deliverable, paging is a separate decision.
- **A public status page** — `docs/claude/architecture.md`'s own "Industry-Standard Benchmark
  Set" lists "status pages" under the general B2B SaaS platform-admin benchmark for the
  `issuer` role. Once this stack is live and a real deployed environment exists, an
  uptime-based public status page (Grafana's own public dashboards, or a dedicated tool) would
  be the natural next step to match that benchmark — flagged here as a fast-follow, not built
  tonight; it depends on production deployment existing first.
- **`OpenTelemetry.Instrumentation.EntityFrameworkCore`/`.Http`** — see §3.2 item 2.

---

## 4. Scope boundary — do not touch

- Any file under `Pena_e_Arte.Domain/`, `Pena_e_Arte.Contracts/`, `frontend/src/**` — this is
  a backend-infra/config change with exactly one narrow, optional exception (§3.2 item 1's
  possible one-line addition to `RequestIdMiddleware.cs`, and nowhere else in `Pena_e_Arte.API`
  beyond what Phase 2 specifies).
- `Pena_e_Arte.API/Extensions/HangfireDashboardAuthFilter.cs` and the Hangfire dashboard
  wiring in `Program.cs` — referenced for pattern only, not modified.
- Any Stripe/Resend/Twilio/R2 configuration — unrelated to this change.
- `.github/workflows/*` — CI is a separate, already-completed initiative
  (`overnight-prompt-ci-pipeline-2026-07-26.md`); do not add observability steps to CI
  tonight unless explicitly asked in a future pass.
- Do not add any Kubernetes/K3s manifest files — see §3.3.
- Do not change `Serilog:MinimumLevel` or add new `Serilog:WriteTo` sinks beyond what's
  already there (Console/`CompactJsonFormatter` stays exactly as-is — Alloy reads it from
  Docker's log API, it does not need a second sink).

---

## 5. Constraints (restated — apply exactly as CLAUDE.md and prior prompts do)

- No new npm/NuGet packages without flagging it as a prerequisite decision first (see §3.2
  for the two live candidates tonight — resolve both explicitly one way or the other before
  calling this done).
- No `useEffect` for data fetching — not applicable to this backend/infra-only change, stated
  for completeness.
- TypeScript strict / no `any` — not applicable (no frontend files touched tonight).
- Explicit C# types, no unclear `var` — applies to the possible `RequestIdMiddleware.cs`
  change in §3.2 item 1.
- No business logic in endpoints — not applicable, no new endpoints added.
- Tenant isolation via EF Core global query filters — not applicable, no new entities/queries.
- Every endpoint has `.RequireAuthorization()` with the correct policy — not applicable, no
  new API endpoints added (Grafana/Prometheus/Loki/Tempo are separate processes with their
  own access model, not ASP.NET Core endpoints).
- **Never log PII.** Doubly relevant tonight: verify in Phase 6 that no dashboard panel,
  Loki query, or Grafana Explore view surfaces a raw log line containing anything beyond
  `request_id`/`user_id`/`tenant_id` — the existing enrichment already excludes names/emails/
  phone numbers/card data (per `RequestLoggingEnrichment.cs`'s own doc comment), this phase
  just needs to confirm the collector layer doesn't introduce a new leak path (e.g. an OTel
  span attribute accidentally carrying a request body).
- Structured logs only — unchanged, Serilog stays as the only logging path.
- Tests ship with every change — see §7 for what "tests" means for infra config (there is no
  Application-layer business logic added tonight; verification is empirical, per §7).

---

## 6. Phase 1 — `docker-compose.yml`: five new services

Add after the existing `minio` service and before the "Container-parity services" comment
block. All five join the existing default Compose network automatically (no `networks:`
block exists today — do not add one).

```yaml
  prometheus:
    image: prom/prometheus:v3.x.x   # pin to the current stable tag — check
                                     # https://hub.docker.com/r/prom/prometheus/tags
                                     # at execution time, don't guess a version
    container_name: pena_e_arte_prometheus
    restart: unless-stopped
    volumes:
      - ./docker/observability/prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prometheus_data:/prometheus
    extra_hosts:
      - "host.docker.internal:host-gateway"   # Linux needs this; no-op on Docker Desktop
    ports:
      - "9090:9090"

  loki:
    image: grafana/loki:3.x.x   # pin to current stable — check
                                # https://hub.docker.com/r/grafana/loki/tags
    container_name: pena_e_arte_loki
    restart: unless-stopped
    volumes:
      - ./docker/observability/loki-config.yml:/etc/loki/local-config.yaml:ro
      - loki_data:/loki
    ports:
      - "3100:3100"

  tempo:
    image: grafana/tempo:2.x.x   # pin to current stable — check
                                  # https://hub.docker.com/r/grafana/tempo/tags
    container_name: pena_e_arte_tempo
    restart: unless-stopped
    command: [ "-config.file=/etc/tempo/tempo.yaml" ]
    volumes:
      - ./docker/observability/tempo.yaml:/etc/tempo/tempo.yaml:ro
      - tempo_data:/var/tempo
    ports:
      - "3200:3200"   # Tempo query API (used by Grafana's Tempo datasource)
      - "4317:4317"   # OTLP gRPC — matches OpenTelemetry:OtlpEndpoint
      - "4318:4318"   # OTLP HTTP

  alloy:
    image: grafana/alloy:1.x.x   # pin to current stable — check
                                  # https://hub.docker.com/r/grafana/alloy/tags
    container_name: pena_e_arte_alloy
    restart: unless-stopped
    command:
      - run
      - /etc/alloy/config.alloy
      - --server.http.listen-addr=0.0.0.0:12345
    volumes:
      - ./docker/observability/config.alloy:/etc/alloy/config.alloy:ro
      - /var/run/docker.sock:/var/run/docker.sock:ro
    ports:
      - "12345:12345"   # Alloy UI — http://localhost:12345/graph, dev-only visibility tool
    depends_on:
      - loki

  grafana:
    image: grafana/grafana:11.x.x   # pin to current stable — check
                                     # https://hub.docker.com/r/grafana/grafana/tags
    container_name: pena_e_arte_grafana
    restart: unless-stopped
    environment:
      GF_SECURITY_ADMIN_USER: ${GRAFANA_ADMIN_USER:-admin}
      GF_SECURITY_ADMIN_PASSWORD: ${GRAFANA_ADMIN_PASSWORD:?Set GRAFANA_ADMIN_PASSWORD in .env — do not use admin/admin}
      GF_AUTH_ANONYMOUS_ENABLED: "false"
      GF_USERS_ALLOW_SIGN_UP: "false"
    volumes:
      - ./docker/observability/grafana/provisioning:/etc/grafana/provisioning:ro
      - grafana_data:/var/lib/grafana
    ports:
      - "3000:3000"
    depends_on:
      - prometheus
      - loki
      - tempo
```

Add to the `volumes:` block at the bottom of the file: `prometheus_data:`, `loki_data:`,
`tempo_data:`, `grafana_data:`.

**Image tags:** every tag above is intentionally a placeholder (`v3.x.x`, `3.x.x`, etc.) —
run `docker manifest inspect` or check the Docker Hub tags page for each image at execution
time and pin an exact current stable version (not `:latest` — this repo pins every other
image in `docker-compose.yml` to an exact version, e.g. `mysql:8.4`, `redis:7-alpine`; match
that convention). Record the exact tags chosen in the final summary and the
`architecture.md` Decisions Log entry (§16).

Add the API's OTLP endpoint override for container-parity mode to the existing `api` service's
`environment:` block (it currently has no `OpenTelemetry__OtlpEndpoint` override, so it falls
back to `appsettings.Development.json`'s `http://localhost:4317`, which does not resolve to
the `tempo` container from inside the `api` container):

```yaml
      OpenTelemetry__OtlpEndpoint: ${OTEL_EXPORTER_OTLP_ENDPOINT:-http://tempo:4317}
```

Add to `.env.example` (mirroring the existing Hangfire comment style):

```
# Grafana admin login — generate a real value, do not use admin/admin even locally.
# e.g. `openssl rand -base64 24`
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=

# Only needed if the API runs containerized (docker compose up --build api). When running
# via `dotnet run` on the host, appsettings.Development.json's localhost:4317 already works
# since Tempo's OTLP port is published to the host.
OTEL_EXPORTER_OTLP_ENDPOINT=http://tempo:4317
```

---

## 7. Phase 2 — Verify (and if needed, fix) trace/log correlation

1. Run the stack (`docker compose up -d tempo loki prometheus grafana alloy` plus the API via
   either topology) and make one real authenticated API request.
2. Find that request's log line in Loki (via Grafana Explore or `logcli`) and read its
   `request_id` field.
3. Find the corresponding trace in Tempo for the same request (search by time range + HTTP
   route) and read its trace ID.
4. **Compare the two values.**
   - **Equal:** no code change needed. State this explicitly in the final summary and add a
     one-line Decisions Log note confirming it was verified, not assumed.
   - **Not equal:** implement the fix specified in §3.2 item 1 — add `trace_id`/`span_id`
     `LogContext.PushProperty` calls in `RequestIdMiddleware.cs` sourced from
     `System.Diagnostics.Activity.Current`, guarding for `Activity.Current` being `null`
     (possible if OTel sampling drops the request — do not throw in that case, just skip the
     enrichment for that request). Re-run steps 1–3 against the fixed code to confirm the IDs
     now match before moving on.

---

## 8. Phase 3 — `docker/observability/prometheus.yml`

```yaml
global:
  scrape_interval: 15s
  evaluation_interval: 15s

scrape_configs:
  - job_name: "pena-e-arte-api-container"
    metrics_path: /metrics
    static_configs:
      - targets: ["api:8080"]
        labels:
          service: "pena-e-arte-api"
          topology: "containerized"

  - job_name: "pena-e-arte-api-host"
    metrics_path: /metrics
    static_configs:
      - targets: ["host.docker.internal:8080"]
        labels:
          service: "pena-e-arte-api"
          topology: "host-dotnet-run"
```

Both jobs are intentionally present — see §2's topology note. Whichever one is actually
running will show `up == 1`; the other shows `up == 0` and is harmless noise, not an error.

---

## 9. Phase 4 — `docker/observability/tempo.yaml`

```yaml
server:
  http_listen_port: 3200

distributor:
  receivers:
    otlp:
      protocols:
        grpc:
          endpoint: 0.0.0.0:4317
        http:
          endpoint: 0.0.0.0:4318

ingester:
  max_block_duration: 5m

compactor:
  compaction:
    block_retention: 48h   # dev-laptop-appropriate retention — see §3.3 on production retention

storage:
  trace:
    backend: local
    local:
      path: /var/tempo/traces
    wal:
      path: /var/tempo/wal
```

---

## 10. Phase 5 — `docker/observability/loki-config.yml` and `docker/observability/config.alloy`

`loki-config.yml` — minimal single-process local config (filesystem storage, no S3/GCS, no
clustering — matches this repo's "local dev, not production" scope for this pass):

```yaml
auth_enabled: false

server:
  http_listen_port: 3100

common:
  path_prefix: /loki
  storage:
    filesystem:
      chunks_directory: /loki/chunks
      rules_directory: /loki/rules
  replication_factor: 1
  ring:
    kvstore:
      store: inmemory

schema_config:
  configs:
    - from: 2026-01-01
      store: tsdb
      object_store: filesystem
      schema: v13
      index:
        prefix: index_
        period: 24h

limits_config:
  retention_period: 48h   # dev-laptop-appropriate — see §3.3 on production retention
```

`config.alloy` — discovers running containers via the Docker socket and ships their logs to
Loki, tagged with low-cardinality labels only (per §3.1 decision 3 — no `request_id`/`user_id`/
`tenant_id` as labels):

```river
discovery.docker "containers" {
  host = "unix:///var/run/docker.sock"
}

discovery.relabel "containers" {
  targets = discovery.docker.containers.targets

  rule {
    source_labels = ["__meta_docker_container_name"]
    regex         = "/(.*)"
    target_label  = "container"
  }
}

loki.source.docker "default" {
  host       = "unix:///var/run/docker.sock"
  targets    = discovery.relabel.containers.output
  forward_to = [loki.write.default.receiver]
}

loki.write "default" {
  endpoint {
    url = "http://loki:3100/loki/api/v1/push"
  }
}
```

Verify the exact `river`-syntax component names (`discovery.docker`, `loki.source.docker`,
`loki.write`) against the installed Alloy version's own reference docs at execution time —
Alloy's config language has changed component names between minor versions; don't ship this
verbatim without confirming it against the pinned image tag's actual docs.

---

## 11. Phase 6 — Grafana provisioning

`docker/observability/grafana/provisioning/datasources/datasources.yaml`:

```yaml
apiVersion: 1

datasources:
  - name: Prometheus
    type: prometheus
    access: proxy
    url: http://prometheus:9090
    isDefault: true

  - name: Loki
    type: loki
    uid: loki-uid
    access: proxy
    url: http://loki:3100
    jsonData:
      derivedFields:
        - datasourceUid: tempo-uid
          matcherRegex: '"request_id":"([a-f0-9-]+)"'
          name: TraceID
          url: '$${__value.raw}'
          # Only works once Phase 2 confirms request_id == the OTel trace ID (or the fix
          # from §3.2 item 1 has been applied so it does). If Phase 2 concluded the two
          # never match, point this at the trace_id field name added by that fix instead —
          # do not leave a derived field pointing at a value that isn't a real trace ID.

  - name: Tempo
    type: tempo
    uid: tempo-uid
    access: proxy
    url: http://tempo:3200
    jsonData:
      tracesToLogsV2:
        datasourceUid: loki-uid
        filterByTraceID: true
```

`docker/observability/grafana/provisioning/dashboards/dashboards.yaml`:

```yaml
apiVersion: 1

providers:
  - name: "Pena e Arte"
    orgId: 1
    folder: "Pena e Arte"
    type: file
    options:
      path: /etc/grafana/provisioning/dashboards/json
    updateIntervalSeconds: 30
```

Ship one starter dashboard,
`docker/observability/grafana/provisioning/dashboards/json/api-overview.json`, covering the
RED method (Rate, Errors, Duration) from the metrics already exported by
`AddAspNetCoreInstrumentation()`'s built-in `http.server.request.duration` histogram:
request rate by route, error rate (5xx as % of total), p50/p95/p99 latency by route. Build
this against the real metric names `AddPrometheusExporter()` actually produces (query
`http://localhost:9090/api/v1/label/__name__/values` once the stack is running to confirm
exact names before hardcoding a panel query — OTel's semantic-convention metric names have
changed across SDK versions, don't guess).

---

## 12. Phase 7 — Test requirements

There is no new Application-layer business logic tonight, so this phase's "tests" are
empirical verification steps, run and recorded, not unit tests:

1. `docker compose up -d prometheus loki tempo alloy grafana` — all five containers reach a
   healthy/running state (add `healthcheck:` blocks mirroring the existing `minio`/`api`
   pattern in `docker-compose.yml` for at least `prometheus` (`/-/healthy`) and `tempo`
   (`/ready`) — do not skip this just because it's infra, `minio` and `api` both have one).
2. `curl http://localhost:8080/metrics` returns HTTP 200 with Prometheus-format output.
3. Prometheus target page (`http://localhost:9090/targets`) shows at least one of the two
   `pena-e-arte-api-*` jobs as `UP`.
4. Make a real API request; confirm within ~15s it's queryable in Grafana Explore against
   both the Loki datasource (log line present, `request_id`/`user_id`/`tenant_id` visible in
   the JSON body, absent from any label) and the Tempo datasource (trace present with an
   `AddAspNetCoreInstrumentation` root span).
5. Phase 2's trace/log correlation check (§7) — recorded pass/fail with the actual IDs
   compared.
6. Open the provisioned `api-overview` dashboard in Grafana and confirm all panels render
   data (not "No data") after a handful of real requests.
7. Confirm `docker compose down -v && docker compose up -d` reproduces the same Grafana
   datasources/dashboard from a clean volume — proves the provisioning is actually
   file-driven, not dependent on manual UI state from a previous run.

---

## 13. Help-sync obligation (CLAUDE.md rule #7)

**Verdict: no Help Menu / user manual / onboarding-tour update needed.** This is
infrastructure with zero user-visible surface — no `client`/`artist`/`owner`/`issuer` sees a
new screen, button, field, or workflow change as a result of this prompt. Grafana/Prometheus/
Loki/Tempo are developer/operator tooling, not part of the product. Stating this explicitly
per CLAUDE.md rule #7's requirement that "no Help change needed" be a stated judgment, not a
silent omission — confirmed by checking `frontend/src/features/help/**` and
`frontend/public/user-manual/index.html` for any existing "status"/"uptime"/"system health"
content (none exists) before concluding this.

---

## 14. Industry-standard benchmark note (CLAUDE.md rule #6)

This change does not map onto the vertical booking-SaaS benchmark set (Vagaro/Fresha/
Boulevard/Mindbody/Zenoti/GlossGenius/etc.) at all — none of those products' *end users*
(client/artist/owner) ever see an observability stack; it's invisible platform
infrastructure. The relevant benchmark is the **general engineering practice standard** for
any production SaaS backend: structured logs + metrics + traces with cross-correlation is
table stakes for operating a multi-tenant system at any real scale, and
`docs/claude/architecture.md`'s own benchmark set already lists "status pages" and "audit
logs" under the general B2B SaaS platform-admin standard for the `issuer` role — this
prompt is the prerequisite infrastructure for eventually meeting that standard (see §3.3's
status-page backlog item), not the standard itself. Flagging this divergence explicitly per
the project's own "say so rather than silently presenting as benchmark-driven" convention.

---

## 15. Final self-check / verification checklist

Do not consider this done until all of the following are true:

- [ ] All five new services (`prometheus`, `loki`, `tempo`, `alloy`, `grafana`) start clean
      via `docker compose up -d` with no crash-loop.
- [ ] `docker compose config` validates the full file with no YAML errors.
- [ ] Every new image tag is an exact pinned version (no `:latest`), confirmed against the
      actual current stable release at execution time, not guessed.
- [ ] Phase 2's trace/log correlation check ran and its outcome (match / fixed / still
      mismatched with reason) is recorded in the final summary.
- [ ] §12's seven verification steps all passed, each with what was actually observed
      (not "should work").
- [ ] No PII appears in any Loki-queryable log line, Grafana panel, or Tempo span attribute
      beyond `request_id`/`user_id`/`tenant_id` (spot-check a handful of real spans/logs, not
      just the code that theoretically prevents it).
- [ ] `GRAFANA_ADMIN_PASSWORD` has no default and the compose file fails closed
      (`:?` guard) if unset, exactly like `HANGFIRE_DASHBOARD_USERNAME`/`PASSWORD`.
- [ ] Nothing under §4's "do not touch" list was modified except the one narrowly-scoped
      `RequestIdMiddleware.cs` change, and only if Phase 2 determined it was actually needed.
- [ ] `.env.example` updated with the three new variables and comments.
- [ ] §3.2's two open decisions (trace/log ID match, EF Core/HTTP client instrumentation
      packages) are both explicitly resolved one way or the other in the final summary — not
      silently left ambiguous.
- [ ] `docs/claude/architecture.md` Decisions Log entry added (exact text below).

---

## 16. Final deliverable spec

**Files created:**
- `docker/observability/prometheus.yml`
- `docker/observability/loki-config.yml`
- `docker/observability/tempo.yaml`
- `docker/observability/config.alloy`
- `docker/observability/grafana/provisioning/datasources/datasources.yaml`
- `docker/observability/grafana/provisioning/dashboards/dashboards.yaml`
- `docker/observability/grafana/provisioning/dashboards/json/api-overview.json`

**Files modified:**
- `docker-compose.yml` (five new services, new volumes, `OpenTelemetry__OtlpEndpoint` env
  var on `api`)
- `.env.example` (three new variables)
- `Pena_e_Arte.API/Middleware/RequestIdMiddleware.cs` — only if Phase 2 determined the
  trace-ID fix was needed
- `docs/claude/architecture.md` — new Decisions Log entry:

  > **Local observability stack (Grafana + Prometheus + Loki + Tempo)** — 2026-07-26. Added
  > to `docker-compose.yml` as five new services (`prometheus`, `loki`, `tempo`, `alloy`,
  > `grafana`), config under `docker/observability/`. Log shipping uses Grafana Alloy
  > (`loki.source.docker` over the Docker socket) — **not Promtail**, which reached EOL
  > 2026-03-02 — so Serilog's existing Console/`CompactJsonFormatter` output needed zero
  > code changes to become Loki-queryable. Prometheus scrapes the API's existing
  > `/metrics` endpoint (already exposed via `MapPrometheusScrapingEndpoint()`, unchanged).
  > Tempo receives traces via the existing `OpenTelemetry:OtlpEndpoint` config, now pointed
  > at `tempo:4317` in container-parity mode via a new `OpenTelemetry__OtlpEndpoint`
  > compose override (host-mode `dotnet run` still uses `appsettings.Development.json`'s
  > `localhost:4317`, which now resolves since Tempo's OTLP port is published to the host).
  > Loki labels are deliberately low-cardinality only (`container`, no `request_id`/
  > `user_id`/`tenant_id` as labels — those stay query-time `| json` fields) to avoid the
  > standard Loki high-cardinality-label ingester-pressure anti-pattern. [Fill in: whether
  > `context.TraceIdentifier` already matched the OTel trace ID, or whether
  > `RequestIdMiddleware.cs` needed the `trace_id`/`span_id` enrichment fix, and the exact
  > pinned image tags used.] Production/K3s rollout, alerting/on-call routing, retention-cost
  > tuning, and a public status page are explicitly out of scope — tracked as follow-ups,
  > blocked on the CD pipeline (`overnight-prompt-ci-pipeline-2026-07-26.md`) landing first.

**Commit message:**
```
feat: local observability stack (Grafana, Prometheus, Loki, Tempo via Alloy)
```
