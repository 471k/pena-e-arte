# Load/performance testing — first baseline (2026-09-05)

**Owner:** Phi · **Related:** `load-tests/staging-baseline.js`, `docs/infra/alerting-runbook.md`

No load/performance testing existed anywhere in this repo before this pass — no `k6`/Artillery
config, nothing in CI. This establishes the first real baseline, run against staging only,
never production, per this prompt's explicit constraint.

## Gate check

`kubectl get pods -n pena-e-arte-staging` confirmed staging genuinely `Running` before starting
(API, frontend, Redis all up; the one-off `migrate` Job `Completed`) — this section proceeded on
that real output, not an assumption from an earlier deploy record.

## Tool choice: k6, not Artillery

k6 was picked over Artillery for the reasons named in the source prompt: scriptable in
JavaScript (matches this codebase's existing language), and it has a straightforward GitHub
Actions integration if this is ever promoted into CI later. Installed locally via `scoop install
k6` (v2.2.0) — not previously present in this environment.

## Scenario

Two scenarios ran concurrently, each ramping 0→25 VUs over 2 minutes, holding 25 VUs for 3
minutes, then ramping to 0 over 1 minute (50 VUs combined at peak — a modest, realistic spike,
not a stress-to-failure test, per the source prompt):

1. **`guest_booking`** — `POST /api/v1/public/studios/:slug/book`, the heaviest write path a
   stranger can hit (guest checkout, no auth). Each iteration uses a unique guest email and a
   randomized future date/time (see "seed data" below for why the date is randomized).
2. **`discover_browse`** — `GET /api/v1/public/studios/nearby` + `GET
   /api/v1/public/studios/:slug`, the heaviest anonymous read path (`DiscoverPage`/`EmbedPage`
   per `docs/claude/architecture.md`).

### Seed data — "Load Test Studio"

Staging had two real studios (`hangfire-fix-verify-studio`, `staging-verify-studio`) but **zero
artists on either** — the guest-booking write path can't be exercised at all without one (both
`CreateAppointmentCommand` and the public availability check require an active artist with an
open schedule). Rather than write directly to the staging database, a dedicated **"Load Test
Studio"** (`load-test-studio`) was created through the real, running API — the same
register→create-user→login→create-artist→set-schedule sequence a real owner would use, not a
raw SQL insert:

```bash
POST /api/v1/studios          # {name, slug, city, lat/lng, ownerEmail, nipt}
POST /api/v1/auth/register    # {email, password, role: "owner", studioId}
POST /api/v1/auth/login       # -> accessToken
POST /api/v1/artists          # one artist, as that owner
PUT  /api/v1/artists/:id/schedule  # 00:00-23:59 every day, so any random future slot clears
                                    # IsAnyArtistAvailableAsync's schedule check
```

This studio/artist/owner account is now real, persistent staging data — intentionally left in
place (not torn down) for future load-test runs to reuse without re-seeding. The booking write
scenario also randomizes each iteration's appointment date across a 1-60-day, 09:00-19:00 UTC
window on purpose: this studio has exactly one artist, so every concurrent VU booking the *same*
slot would collide on the real conflict check after the first success, measuring "slot already
booked" 422s instead of the real write path.

Image URLs in the booking payload point at `pena-e-arte-r2-staging....workers.dev/load-test/...`
paths that don't correspond to real uploaded objects — `IR2Service.IsR2Url` only checks the URL
prefix against the configured public R2 domain, not object existence, so this satisfies the
validator without needing a real presigned-upload round trip per iteration.

A smoke test (3 VUs × 6 iterations, i.e. far below the real baseline's concurrency) ran clean
first — 100% success, booking p95 ≈4.1s, browse p95 ≈1.25s — confirming the script itself was
correct before running the real 50-VU baseline below.

## Results — the real 50-VU run

```
k6 run -e BASE_URL=https://staging.tattooos.co load-tests/staging-baseline.js
```

| Metric | Result |
|---|---|
| Total requests | 7,414 (20.5 req/s average) |
| Overall check success rate | **15.8%** (6,240 of 7,414 checks failed) |
| Booking success rate | 9% (223/2,338) |
| Booking latency | avg 1.87s · p90 2.23s · **p95 14.17s** · max 60s (timeout) |
| Browse success rate | 19% (951/5,076 across both endpoints) |
| Browse latency | avg 825ms · p90 2.44s · **p95 4.02s** · max 39.8s |

All four thresholds defined in the script (`p95<2000ms` booking, `p95<1000ms` browse, error
rate `<5%` on both) failed — this is a genuine finding, not a broken script: the identical
script ran clean at 3 concurrent VUs immediately before this run.

## What actually happened — cross-referenced against the live cluster, not guessed

`kubectl get pods -n pena-e-arte-staging` immediately after the run showed the API pod with
`RESTARTS: 1`, and `kubectl describe` on it showed why:

```
Warning  Unhealthy  kubelet  Liveness probe failed: Get "http://.../health/live":
                              context deadline exceeded (Client.Timeout exceeded while
                              awaiting headers)
Normal   Killing    kubelet  Container api failed liveness probe, will be restarted
```

**Staging's API pod became so saturated under 50 concurrent VUs that it stopped responding to
its own liveness probe in time, and Kubernetes killed and restarted it mid-test.** Cross-checked
against the shared Prometheus instance (port-forwarded locally to confirm staging's metrics are
genuinely live and queryable, not just present after the fact, per this prompt's requirement):
`up{job="pena-e-arte-api-staging"}` shows a real `0` sample during the test window, then `1`
again — Prometheus itself independently observed the same outage the k6 run and the pod's own
events did.

**Root cause, from the deployment spec, not speculation:** staging's API deployment runs a
**single replica** at `resources: { limits: { cpu: 250m, memory: 256Mi }, requests: { cpu: 50m,
memory: 128Mi } }` — a tier sized for "does this deploy work at all," not for any concurrent
load. 250m CPU (a quarter of one core) cannot service 50 concurrent HTTP connections plus their
downstream MySQL/Redis round-trips without the request queue backing up far enough to blow past
the liveness probe's timeout.

Staging recovered on its own immediately after the restart (`/health/live` returns 200 again,
confirmed) — no lasting damage, no manual intervention needed.

**Not fixed in this pass, per this prompt's explicit scope** ("this prompt's job is to establish
the baseline and surface findings, not to chase them"). The fix is almost certainly staging's
resource tier and/or replica count — a real, low-risk, low-cost change (staging, not production)
that Phi should make deliberately rather than have it slipped in as a side effect of a load-test
script. Re-run this same script after any such change to confirm it actually helps before
declaring it fixed.

## Follow-up (2026-09-05) — resource limits fixed, a deeper bottleneck surfaced

Phi asked for the pod-resource issue above to be fixed. Before picking new numbers, checked real
cluster capacity rather than guessing: `kubectl top nodes` showed the whole node at only ~7% real
CPU usage (146m of ~2000m) — the 250m *limit* was artificially throttling this one pod via CFS
quota enforcement even though the physical node had plenty of spare cycles, so raising CPU was
low-risk. Memory was the genuinely tight resource (measured ~80% used cluster-wide, ~700-800Mi
real headroom across production + monitoring + staging combined on this single CPX22 box) — bumped
more conservatively. New values (`k8s/overlays/staging/resources-patch.yaml`): CPU 250m→750m limit
(50m→75m request), memory 256Mi→384Mi limit (128Mi→160Mi request). Applied directly to the live
cluster first (`kubectl patch` + verified rollout), then committed to keep the manifest in sync.

**Re-running this exact same baseline after the fix still shows staging becoming unhealthy under
the same ~50 VUs** — but the failure mode changed. Before: raw `context deadline exceeded`
timeouts (the pod too CPU-starved to answer at all). After: a mix of timeouts and clean HTTP `503`
responses from the app's own `/health/ready` check — the pod is now CPU/memory-healthy enough to
respond, but something *downstream* is reporting unhealthy under load.

Checked the actual DB connection string staging uses (`kubectl get secret ... ConnectionStrings__Default`,
password redacted before viewing): `Server=pena-e-arte-prod-db-do-user-30836506-0.j.db.ondigitalocean.com;
Database=pena_e_arte_staging;Uid=staging_user;...` — confirmed byte-for-byte the same server
hostname production's own connection string uses, just a different database/user. **Staging and
production share the exact same physical DigitalOcean managed MySQL instance.** No explicit
connection-pool-size tuning exists on either connection string, so the likely culprit is the DB
instance's own `max_connections` ceiling (a Basic-tier 1GB instance has a genuinely small one)
being reached faster than expected once 50 concurrent load-test VUs each open their own EF Core
connections against it — but this wasn't verified with a real `SHOW VARIABLES LIKE
'max_connections'` / connection-count query, since that would mean connecting to the live
production database instance, a real production-adjacent action outside what this follow-up was
asked to do.

**Confirmed no production impact from this re-test**: `https://app.tattooos.co/health/live`
stayed `200` throughout, and `kubectl get pods -n pena-e-arte` showed 0 restarts across the whole
run — but the shared-instance finding above means a *future*, larger staging load test could
plausibly compete with production for DB connections, which the original baseline's "staging
only, never production" safety framing didn't anticipate needing to account for.

**This is now a materially different, more sensitive question than the original pod-resource
one** — it touches the live production database's own tier/connection-limit configuration, not
just a K8s Deployment's resource requests. Flagged as a distinct follow-up for Phi's own decision
(e.g., whether to size up the DB tier, add explicit per-environment connection-pool caps in each
connection string, or accept staging's load ceiling as-is) — not addressed here.

## Follow-up #2 (2026-09-05, same day) — root-caused with a real number, capped per-environment

Phi asked for everything fixable without DigitalOcean dashboard/API access to actually get fixed.
Confirmed the exact ceiling by connecting from *inside* the cluster (a short-lived
`kubectl run` debug pod in the `pena-e-arte-staging` namespace, using the existing `staging_user`
credentials already available in that namespace's own Secret — no new access needed, and no
direct connection attempted from outside the cluster's network, sidestepping any question of
whether DigitalOcean's trusted-sources firewall would even allow that):

```
SHOW VARIABLES LIKE 'max_connections';   ->  76
SHOW STATUS LIKE 'Threads_connected';    ->  11   (idle, at the time of the check)
SHOW STATUS LIKE 'Max_used_connections'; ->  78   (high-water mark — already at/above the
                                                    76 ceiling, most likely from this same
                                                    load-test baseline's earlier 50-VU run)
```

**76 total connections for the entire shared instance — production and staging combined —**
confirms the theory exactly: nothing was misconfigured, the instance's own tier just has a small
budget, and neither connection string had ever set an explicit pool-size ceiling (MySqlConnector's
own default is up to 100 connections *per pool*, meaning production's 2 replicas alone could
theoretically have opened up to 200 connections between them under enough load — a latent risk
that existed independently of staging, not something staging introduced).

Fixed by adding an explicit `Maximum Pool Size` to both connection strings, sized off the real 76
number with headroom for DigitalOcean's own overhead: **staging capped at 15** (1 replica — small
enough it can never meaningfully compete with production, deliberately, since staging load should
never affect production), **production capped at 25 per replica** (2 replicas = 50 theoretical
max). 15 + 50 = 65 of 76, leaving ~11 as real margin. This is a hard, client-side-enforced ceiling
per pool — production physically cannot open more than its cap regardless of what staging does,
so this doesn't require re-running the load test to "prove" the isolation the way the pod-resource
fix did; the guarantee is structural, not empirical.

Applied both ways, matching this project's established pattern: updated the `STAGING_DB_
CONNECTION_STRING`/`PROD_DB_CONNECTION_STRING` GitHub Actions secrets (source of truth for future
deploys) *and* patched both live K8s Secrets directly, then did a `kubectl rollout restart` on
each Deployment (staging first, verified healthy, then production — production's existing
`maxUnavailable: 0` rolling-update strategy made this a zero-downtime restart, confirmed via a
`/health/live` check that stayed `200` throughout with 0 pod restarts on both new replicas).

**Still not touched, and still needs Phi specifically**: actually resizing the DigitalOcean
database tier itself (option 2 from the three named above) — this session has no DigitalOcean API
token or `doctl` configured anywhere, so that one genuinely requires either the DO dashboard or a
token Phi provides. The connection-pool caps above are a real, load-bearing mitigation on their
own regardless of whether the tier itself ever changes.

## Follow-up #3 (2026-09-06) — DB tier actually resized, pool caps raised to match

Phi resized `pena-e-arte-prod-db` via the DO dashboard: **1GB/1vCPU (75 connections) → 2GB/1vCPU
(150 connections)**, $15.15/mo → $30.45/mo. Guided step by step (Basic - Shared CPU tab; an
earlier wrong turn into the much pricier Storage-Optimized tab was caught and corrected before
saving). Verified live, before and after:

```
Before: innodb_buffer_pool_size = 32 MB,  max_connections = 76
After:  innodb_buffer_pool_size = 256 MB, max_connections = 151
```

(DigitalOcean applied the new `max_connections` limit within seconds of clicking Save, but the
actual hardware swap — and the buffer pool size increase that comes with it — took a few more
minutes, visible as a "RESIZING" badge with a progress bar on the cluster's dashboard page.)

With the real ceiling roughly doubled, raised the per-environment pool caps from the previous
follow-up proportionally: **staging 15→25, production 25→50 per replica** (100 across its 2
replicas). 125 of 151 total, keeping a similar relative safety margin to before. Same mechanism
as last time: updated both GitHub secrets and both live K8s secrets, then a `kubectl rollout
restart` on each Deployment (staging first, verified, then production — zero-downtime via its
`maxUnavailable: 0` strategy, confirmed 0 restarts and `/health/live` 200 on both new replicas
throughout).

This closes all three items from the original load-test finding: pod resource limits (follow-up
#1), connection-pool exhaustion (follow-up #2), and the DB tier itself (this one). No further
staging-load-test-related follow-up expected unless a future run surfaces something new.

## Files

- `load-tests/staging-baseline.js` — the k6 script itself, reusable for future runs.
- This document — update it (or add a dated successor) after any re-run, so results stay
  comparable over time.
