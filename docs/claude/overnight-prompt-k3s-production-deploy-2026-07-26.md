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

1. **Hetzner Cloud VPS, K3s installed with its default Traefik ingress controller.** Provider
   and ingress controller are now decided (were open in an earlier draft of this prompt —
   resolved 2026-07-26, see §2.7/§2.8; the ingress-controller choice was **revised same-day**
   from the community `ingress-nginx` project originally spec'd here, once it turned out that
   project was archived 2026-03-24 — no further releases, bugfixes, or security patches — and
   was caught before anything beyond K3s itself was installed): **Hetzner**, not AWS —
   cheapest for this scale, and Hetzner has no first-party managed-MySQL product to
   conveniently co-locate with anyway (confirmed against Hetzner's own current site nav — no
   "Managed Databases" product listed), so same-provider DB convenience was never on the table
   either way.
   - Create the server in the Hetzner Cloud Console: Ubuntu 24.04 LTS image (or newer — 26.04
     LTS was available and used in practice, which is fine, more current, longer support
     window), the **Regular Performance** (shared vCPU) tier at the 2 vCPU / 4 GB RAM size
     (Hetzner's own "best price-performance" tier per their current site copy — their old
     `CX22`-style SKU names may have changed, match by spec, not by a possibly-stale name;
     `CPX22` was the real current match in practice). Location: **Nuremberg or Falkenstein**
     (Germany) — closest of Hetzner's regions to Albania, and pairs well with DigitalOcean's
     Frankfurt region for the database (§0.2).
   - Add your SSH public key at creation time instead of a password.
   - Add a Hetzner Cloud Firewall: allow `22/tcp` (restricted to your own IP), `80/tcp` and
     `443/tcp` (world-open — this is the public ingress; `80` specifically so Traefik/whatever
     serves it can redirect plain HTTP to HTTPS rather than refusing the connection), and
     `6443/tcp` restricted to your own IP only (the K3s API server — never expose this
     world-wide). Leave outbound unrestricted — this box's dependency set (GHCR, DigitalOcean,
     Cloudflare, Let's Encrypt, Stripe, Resend, Twilio, apt mirrors) doesn't publish stable IP
     ranges suitable for tight egress rules; an accepted-risk call, named rather than silently
     skipped.
   - Install K3s with **Traefik left enabled** (the default — do not pass
     `--disable traefik`; an earlier draft of this step disabled it in favor of installing
     `ingress-nginx` separately, reversed for the reason above):
     ```bash
     curl -sfL https://get.k3s.io | sh -
     ```
     Verify with `kubectl get pods -n kube-system` — expect `traefik-*` and `svclb-traefik-*`
     pods reaching `Running` within a minute or two. K3s's built-in ServiceLB automatically
     binds Traefik to the node's 80/443 — unlike the bare-metal `ingress-nginx` install this
     replaces, **no manual `hostNetwork`/`hostPort` patch step is needed**, one genuine
     simplification from this change.
   - Copy `/etc/rancher/k3s/k3s.yaml` off the box (it's the kubeconfig; rewrite its `server:`
     field from `127.0.0.1` to the box's public IP) — you'll need its contents as a GitHub
     Actions secret in Phase 9. Confirm the real Pod CIDR now, don't assume Flannel's default:
     `kubectl cluster-info dump | grep -m1 cluster-cidr` — needed for §8.10's
     `ForwardedHeaders:TrustedProxyCidr` value.
2. **DigitalOcean Managed MySQL, engine version 8.4.** Provider resolved 2026-07-26 (see
   §2.7) — real MySQL protocol (not Vitess), and DigitalOcean now defaults new clusters to
   MySQL 8.4 (confirmed current as of this prompt — 8.0 clusters are on a forced-upgrade path
   to 8.4 starting Oct 2026 per DigitalOcean's own migration notice), an exact version match
   with `mysql:8.4` in `docker-compose.yml`.
   - Create the cluster in the DigitalOcean console, **Frankfurt (FRA1)** region (closest to
     the Hetzner Germany regions), MySQL 8.4.
   - Under the cluster's **Trusted Sources**, restrict inbound connections to the Hetzner
     VPS's public IP only — a managed database reachable from the entire internet is not an
     acceptable default.
   - DigitalOcean enforces TLS on managed MySQL connections. The connection string this
     prompt's §6.5/§8.8 use must add `SslMode=Required` (or `VerifyCA` with DigitalOcean's
     provided CA certificate bundled into the container image/secret) — this is a real
     difference from today's local `DB_CONNECTION_STRING`, which has no SSL parameters at
     all, and is called out explicitly in Phase 8 below so it isn't silently dropped.
   - Create a database named `pena_e_arte_prod` inside the cluster (DigitalOcean provisions a
     default one; add this one explicitly rather than reusing the default, for a name that
     matches this project's existing naming convention). Confirm `utf8mb4`/
     `utf8mb4_unicode_ci` as the connection-level charset/collation (DigitalOcean's server
     default may differ from the local container's — EF Core migrations run with the app's
     own connection settings, not the server default, so this is a should-confirm, not a hard
     blocker).
   - Copy the resulting connection details into the `.NET` connection-string shape (not
     DigitalOcean's own `mysql://` URI shape): `Server=<host>;Port=25060;
     Database=pena_e_arte_prod;User=<user>;Password=<password>;SslMode=Required;
     AllowPublicKeyRetrieval=true;`
3. **A Cloudflare API token** scoped to `Zone:DNS:Edit` for the `tattooos.co` zone only (not
   the Global API Key) — needed for cert-manager's DNS-01 solver. Cloudflare dashboard → My
   Profile → API Tokens → Create Token → "Edit zone DNS" template, scoped to `tattooos.co`.
4. **A DNS record in Cloudflare**: `A` record, name `app`, value = the Hetzner box's public
   IP. Leaving Cloudflare's orange-cloud proxy **on** is fine and recommended (DNS-01
   validates via a TXT record, not by reaching the server on port 80/443, so proxying doesn't
   interfere with issuance) — but if it's on, set Cloudflare's **SSL/TLS mode to "Full
   (strict)"** (SSL/TLS → Overview in the Cloudflare dashboard), not "Flexible": strict mode
   validates Cloudflare's edge-to-origin connection against the real Let's Encrypt cert
   cert-manager issues on the box; Flexible would silently accept an unencrypted or
   self-signed origin connection instead, undermining the whole point of Phase 6.
5. **A GHCR PAT or confirmation that `GITHUB_TOKEN`'s default `packages: write` permission is
   enabled** for `471k/pena-e-arte` (Settings → Actions → General → Workflow permissions).
6. Once 1–5 exist, add these **GitHub Actions repo secrets** (Settings → Secrets and
   variables → Actions): `KUBE_CONFIG` (base64 of the rewritten kubeconfig from step 1),
   `PROD_DB_CONNECTION_STRING` (the DigitalOcean connection string from step 2, with
   `SslMode=Required`), `CLOUDFLARE_API_TOKEN`. The other secrets the API already needs
   (`JWT_SECRET_KEY`, `STRIPE_SECRET_KEY`, etc. — full list in §6.5) get their production
   values added as GitHub secrets too, reusing the exact names already in `.env.example` with
   a `PROD_` prefix where they don't already have one, so Phase 9's workflow can reference
   them. **Never paste any of these secret values into a chat/prompt/doc anywhere, including
   back to this consultation project** — add them directly in the GitHub Settings UI.

### 0.1 — Phase 0 progress log (updated live as steps complete, not written up front)

**Step 1 (Hetzner/K3s) — done, 2026-07-27:**
- Project: `pena-e-arte-prod`
- Server: `pena-e-arte-k3s` — CPX22 (2 vCPU/4GB/80GB), Falkenstein, Ubuntu 26.04 LTS, backups on
- Public IPv4: `49.13.66.15` (the value for the Cloudflare `app` DNS record in step 4)
- Public IPv6: `2a01:4f8:c17:29e8::1`
- Firewall: `pena-e-arte-k3s-fw` — `22`/`6443` restricted to the operator's IP, `80`/`443`/ICMP
  open to anyone
- SSH key pair: private key at `C:\Users\User\.ssh\hetzner-pena-e-arte` on the operator's
  machine (passphrase-protected — file contents/passphrase never recorded anywhere, including
  here), public key attached to the Hetzner server
- K3s: `v1.36.2+k3s1`, running with its **default Traefik** ingress controller (not
  `ingress-nginx` — see §2.8's corrected resolution; this was reinstalled twice: once to
  remove an earlier `--disable traefik` flag, once more after the cluster's admin kubeconfig
  was accidentally exposed in a chat session and needed fresh, never-exposed credentials)
- kubeconfig: `C:\Users\User\.kube\hetzner-prod.yaml` on the operator's machine, `server:`
  field rewritten to `https://49.13.66.15:6443` — **file contents never recorded in any doc or
  chat**, only its local path
- Pod CIDR: confirmed `10.42.0.0/16` (K3s stock default, empirically confirmed via
  `kubectl get node pena-e-arte-k3s -o jsonpath='{.spec.podCIDR}'` returning `10.42.0.0/24`)
  — this is the real value for `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR`, referenced from Phase
  10 rather than re-derived there

**Step 2 (DigitalOcean managed MySQL) — done, 2026-07-27:**
- DigitalOcean project: `pena-e-arte-prod` (kept separate from the operator's other
  DigitalOcean projects — `Bite Right Demo`, `Klinika dentare`, `Phi Software S...`)
- Cluster: `pena-e-arte-prod-db` — MySQL 8.4, Standard Edition, Basic/Regular, 1 vCPU/1GB RAM,
  10GiB autoscaling storage, single primary node (no standby/HA), Frankfurt (FRA1), $15.15/mo
- Network Access: trusted sources restricted to `49.13.66.15/32` ("Hetzner K3s box") only —
  no other inbound allowed
- Database created: `pena_e_arte_prod` (the auto-created `defaultdb` was left in place, unused)
- Connection host: `pena-e-arte-prod-db-do-user-30836506-0.j.db.ondigitalocean.com`, port
  `25060`, user `doadmin`, `sslmode=REQUIRED` — confirms §0 step 2's `SslMode=Required`
  requirement was correctly anticipated
- **Password never recorded in this doc or in chat** — the operator holds the assembled
  `PROD_DB_CONNECTION_STRING` value locally, to be added directly as a GitHub Actions secret
  in step 6

**Step 3 (Cloudflare API token) — done, 2026-07-27:**
- Token name: `pena-e-arte-dns01`
- Permissions: `Zone → DNS → Edit`
- Zone Resources: `Include → Specific zone → tattooos.co` only (not All zones)
- Client IP Address Filtering: `Is in → 49.13.66.15` — restricted to the Hetzner box only, an
  extra layer beyond the zone scoping
- TTL: no expiration (accepted tradeoff for a long-lived automation credential)
- **Token value never recorded in this doc or in chat** — held locally, to be added directly
  as the `CLOUDFLARE_API_TOKEN` GitHub Actions secret in step 6

**Step 4 (Cloudflare DNS record + SSL mode) — done, 2026-07-27.**
- DNS record: `A` — `app` → `49.13.66.15` (Hetzner server public IP from step 1), Proxied
  (orange cloud, so Cloudflare terminates client TLS and fronts the origin — required for
  Full (strict) mode below)
- SSL/TLS encryption mode: changed from `Full` to `Full (strict)` — Cloudflare now validates
  the origin certificate rather than accepting any/self-signed cert. This means Phase 6's
  cert-manager + Let's Encrypt setup is not optional/deferrable: until a trusted cert is live
  on the Traefik Ingress, Cloudflare will fail the edge-to-origin TLS handshake and the origin
  will be unreachable through the proxy. Confirmed via dashboard: "Automatic mode disabled",
  mode active.
- No other DNS records touched; the 3 pre-existing records on the zone were left as-is.

**Steps 5–6 (GHCR permissions + GitHub Actions secrets) — not started.** Secrets this step
will need once steps 1–4 are complete: `KUBE_CONFIG` (base64 of the kubeconfig file above),
`PROD_DB_CONNECTION_STRING` (from step 2, with `SslMode=Required`), `CLOUDFLARE_API_TOKEN`
(from step 3), plus production values for `JWT_SECRET_KEY`, the Stripe keys/webhook secrets,
R2 credentials, `RESEND_API_KEY`, `HANGFIRE_DASHBOARD_USERNAME`/`PASSWORD`,
`PROD_GRAFANA_ADMIN_USER`/`PASSWORD`, and `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR` (already known:
`10.42.0.0/16`).

**When Phase 0 is done, confirm here that it's actually complete** (kubectl can reach the
box, the DigitalOcean cluster is up and its Trusted Sources are locked to the Hetzner IP, the
Cloudflare token and DNS record exist, GitHub secrets are populated) before handing this file
to a Claude Code session to run Phases 1–10 unattended.

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

1. **Cluster is not provisioned yet as of this prompt's original draft; provisioning target is
   now decided.** This prompt's Phases 1–10 write manifests, workflows, and docs against a
   cluster that Phase 0 makes real — it does not create the cluster itself. Phase 0 must still
   actually be executed (a human task) before Phases 1–10 run.
2. **Managed MySQL, not self-hosted.** No MySQL `StatefulSet`/PVC gets written. The API's
   `ConnectionStrings__Default` in production points at the managed instance via a K8s Secret
   populated from the `PROD_DB_CONNECTION_STRING` GitHub secret — **now with `SslMode=Required`
   in that connection string** (§0.2), since DigitalOcean enforces TLS on managed connections
   and today's local `DB_CONNECTION_STRING` has no SSL parameters to copy from.
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
7. **VPS provider: Hetzner** (resolved 2026-07-26, was §3.2 in the original draft — see that
   section below, now marked resolved rather than deleted, for the reasoning trail). Cheapest
   option at this scale; also, checked directly against Hetzner's current site, they have no
   first-party managed-database product, so "same provider as the DB" was never actually an
   available convenience to weigh against AWS either way.
8. **Ingress controller: Traefik, K3s's own default — revised same-day, see below**
   (resolved 2026-07-26, closes what was previously an open item in Phase 6 of this prompt).
   The first resolution of this item picked `ingress-nginx` (disabling K3s's built-in Traefik
   to install it separately) specifically to match `CLAUDE.md`'s documented "Nginx" stack
   line. That was reversed the same day, mid-Phase-0-execution, once it turned out the
   community `kubernetes/ingress-nginx` project was archived 2026-03-24 — no further releases,
   bugfixes, or security patches, ever, going forward. Shipping a new production cluster on an
   already-end-of-life ingress controller was worse than deviating from `CLAUDE.md`'s literal
   wording, so this now uses K3s's built-in Traefik (actively maintained by Traefik Labs,
   purpose-built for K3s, zero extra install step) instead — **`CLAUDE.md`'s infra stack table
   should be updated to say Traefik, not Nginx, for the ingress layer; flagged here, not done
   silently, and not yet applied since this consultation project can edit `docs/claude/` but
   `CLAUDE.md` itself lives at the repo root — whoever runs Phases 1–10 should make that edit
   too.** `nginx.conf.template`'s own reverse-proxy role (frontend Pod → API Service) is
   unaffected either way; only the layer in front of it (cluster ingress → frontend Pod)
   changed. See §0 step 1 for the exact install sequence and Phase 6 for the resulting
   Ingress-annotation differences.
9. **Managed MySQL provider: DigitalOcean, engine 8.4** (resolved 2026-07-26, was §3.1 in the
   original draft, now marked resolved below). DigitalOcean now defaults new clusters to
   MySQL 8.4 — an exact version match with `mysql:8.4` — and uses the real MySQL protocol, not
   Vitess, minimizing migration/FK-behavior risk. TLS is mandatory on the connection; see
   item 2 above.

---

## 3. Decisions to flag — do NOT guess, do NOT build blind

These are named explicitly, per this project's own precedent, rather than silently decided or
silently skipped. **Do not implement a specific choice for any of these; implement the
provider-agnostic version and leave the named gap.**

### 3.1 — Managed MySQL provider — **RESOLVED 2026-07-26: DigitalOcean**

Originally an open money decision. Three real current-market candidates were priced and
compared (kept here for the reasoning trail):

| Provider | Entry price (2026) | Notes |
|---|---|---|
| **DigitalOcean Managed MySQL — chosen** | ~$15/mo | Standard MySQL protocol (not Vitess-sharded) — closest behavioral match to today's `mysql:8.4` container and Pomelo/EF Core, and now defaults new clusters to MySQL 8.4 (exact version match). |
| PlanetScale (Vitess-backed MySQL, PS-10 tier) — not chosen | ~$39/mo for HA | PlanetScale's free/Hobby tier is gone (removed April 2024) and their newer low-cost tiers lean Postgres-first; the MySQL-compatible tier is Vitess underneath, which has known foreign-key-constraint and some `ALTER TABLE` behavioral differences from real MySQL. |
| AWS RDS for MySQL — not chosen | Variable, generally the most expensive at every tier of the three | Would only have made sense paired with an AWS VPS, which also wasn't chosen (§3.2). |

Concrete provisioning steps (region, trusted-sources firewall, TLS requirement) are in §0
step 2. **This session should not create the account itself** — Phase 0 step 2 requires Phi
to have already created the instance before this session starts; if it wasn't done, stop
here rather than substituting a self-hosted `StatefulSet` "to keep moving" — that would
silently reverse decision §2.2.

### 3.2 — VPS/host provider — **RESOLVED 2026-07-26: Hetzner**

Originally an open money decision; `CLAUDE.md` listed "Hetzner/AWS" without picking one.
Hetzner was chosen: meaningfully cheaper than equivalent AWS EC2 for a single-node K3s box at
this app's current scale (why `CLAUDE.md` lists it first), and — checked directly against
Hetzner's current site as part of resolving this — Hetzner has no first-party managed-MySQL
product, so there was never a "same provider as the DB" convenience to weigh against AWS
either way; the database (§3.1) lives on a different provider regardless of which VPS host was
picked. Concrete provisioning steps (region, tier, firewall, K3s install with Traefik
disabled) are in §0 step 1.

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
  **zero** application-code packages; the one new dependency is `cert-manager`, a cluster-level
  Kubernetes add-on, not an app package. Traefik itself isn't a new dependency — it's K3s's
  own built-in default, installed automatically.
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

**Ingress controller: Traefik — resolved 2026-07-26, revised same-day (see §2.8 for the full
reasoning trail).** An earlier resolution of this item disabled K3s's built-in Traefik in
favor of installing the community `ingress-nginx` project, to match `CLAUDE.md`'s "Nginx"
stack line. That was reversed within the same day, mid-Phase-0-execution, once it turned out
`kubernetes/ingress-nginx` was archived 2026-03-24 (no further releases/patches ever). Phase 0
now installs K3s with Traefik left at its default (no `--disable traefik` flag). If, when this
phase actually runs, `kubectl get pods -n kube-system` does **not** show `traefik-*`/
`svclb-traefik-*` pods `Running`, **stop and flag it back to Phi rather than silently
installing a different ingress controller** — it means Phase 0 wasn't executed exactly as
specified in §0 step 1, not that this phase should improvise around it. `CLAUDE.md`'s infra
stack table still says "Nginx" for ingress as of this writing — flagged as needing an update
(see §2.8), not yet made since it's outside this consultation project's `docs/claude/`-only
write scope.

Install `cert-manager` (`kubectl apply -f
https://github.com/cert-manager/cert-manager/releases/latest/download/cert-manager.yaml` —
pin the exact current stable version tag from
[cert-manager's releases page](https://github.com/cert-manager/cert-manager/releases) rather
than trusting `latest` to still point at the same thing it does today; record the tag actually
used in the Decisions Log entry this phase's own §13 requires).

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
`Ingress` — note the annotations are different from the original `ingress-nginx`-targeted
draft (removed: `nginx.ingress.kubernetes.io/*`, which Traefik doesn't read at all — a
leftover `nginx.ingress.kubernetes.io/*` annotation is silently ignored by Traefik rather than
erroring, so this would have failed quietly, not loudly, if left in):
```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: pena-e-arte
  annotations:
    cert-manager.io/cluster-issuer: letsencrypt-prod-dns01
spec:
  ingressClassName: traefik
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
`cert-manager.io/cluster-issuer` is controller-agnostic (cert-manager reads it regardless of
which ingress controller is in front), so that annotation carries over unchanged.
`ingressClassName: traefik` is added explicitly rather than relying on whatever K3s's default
`IngressClass` resolves to.

**WebSocket/long-connection timeout — flagged, not asserted.** Traefik detects and proxies
WebSocket upgrades (what `/hubs/*` SignalR connections need) automatically, unlike `nginx`,
which needs the explicit `Upgrade`/`Connection` header passthrough `nginx.conf.template`
already does at the frontend-Pod hop — that part is unaffected by this change either way.
What's *not* carried over cleanly is nginx's simple per-Ingress `proxy-read-timeout`/
`proxy-send-timeout` annotations: Traefik has no direct equivalent annotation on a plain
`networking.k8s.io/v1 Ingress` object. Its timeout knobs live either in the (cluster-wide)
static entrypoint config (`transport.respondingTimeouts`, part of the Traefik HelmChart values
K3s manages) or a `Middleware` CRD referenced via
`traefik.ingress.kubernetes.io/router.middlewares`. **Do not guess which one at spec-writing
time** — during Phase 6's actual execution, open a long-lived SignalR connection through the
real Ingress and confirm empirically whether Traefik's default timeouts already exceed what
`/hubs/*` needs (plausible, since Traefik's defaults are often quite long/unset by default)
before adding any extra config; only add the static-config or Middleware fix if a real
connection drop is observed, and record whichever it turns out to be in the Decisions Log
entry this phase's own §13 requires.

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
  (Grafana's own OAuth, or a Traefik `BasicAuth` `Middleware` CRD attached via
  `traefik.ingress.kubernetes.io/router.middlewares`) — named as a follow-up, not assumed.

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
`client → (Cloudflare, if proxied) → Traefik Pod → frontend nginx Pod (proxies /api/) →
API Pod`. Both Traefik and the frontend's own nginx container run as Pods inside the
cluster, so both hops' source IPs fall inside the cluster's Pod CIDR — K3s's default (Flannel)
Pod CIDR is `10.42.0.0/16` **only if the Phase-0 install didn't override it**; confirm via
`kubectl cluster-info dump | grep -m1 cluster-cidr` or `/etc/rancher/k3s/config.yaml` on the
real box before hardcoding this. Set `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR` to that confirmed
value. (This part of the fix is unaffected by the Traefik-vs-ingress-nginx change in §2.8 —
the topology is still two in-cluster proxy hops regardless of which one is first.)

**That alone is not sufficient.** `ForwardedHeadersOptionsBuilder.cs` (§6.4) constructs
`ForwardedHeadersOptions` without setting `ForwardLimit`, which defaults to **1** — meaning
ASP.NET Core only strips *one* trusted hop off the `X-Forwarded-For` chain even if two are
present and both match `KnownNetworks`. Traefik, like nginx, appends to an existing
`X-Forwarded-For` header rather than replacing it, so the header arriving at Kestrel looks
like `<real-client-ip>, <traefik-pod-ip>` — but with `ForwardLimit: 1`, the middleware only
processes the right-most entry and `HttpContext.Connection.RemoteIpAddress` ends up as the
*Traefik pod's* IP, not the real client's, defeating the exact per-client rate-limiting this
config was added for in today's security pass. **Confirm Traefik's append behavior
empirically against the real Ingress during Phase 6/10 execution** (don't just assume it
matches nginx's `$proxy_add_x_forwarded_for` semantics exactly) before relying on it. **Fix,
exact diff:**
```csharp
ForwardedHeadersOptions options = new()
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 2,   // two trusted proxy hops in the K3s topology: Traefik, then the
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
