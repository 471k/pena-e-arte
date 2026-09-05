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

## Files

- `load-tests/staging-baseline.js` — the k6 script itself, reusable for future runs.
- This document — update it (or add a dated successor) after any re-run, so results stay
  comparable over time.
