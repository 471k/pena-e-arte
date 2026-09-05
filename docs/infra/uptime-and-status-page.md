# External uptime monitoring + public status page

**Owner:** Phi · **Related:** `docs/infra/alerting-runbook.md`

Before 2026-09-05, nothing outside the cluster watched for an outage — an internal Grafana alert
(see `alerting-runbook.md`) only fires if Prometheus/Loki themselves are still up and reachable;
a full cluster/network outage would page no one and show no public status anywhere.

## §5 — External uptime monitor

**Service:** UptimeRobot — Phi already has an account, so no new signup needed.

**Target:** `https://app.tattooos.co/health/live` — confirmed reachable externally (`HTTP 200`,
verified 2026-09-05). This is `Pena_e_Arte.API/Program.cs`'s liveness-only health check
(`app.MapHealthChecks("/health/live", ...)`), deliberately chosen over the plain `/health` or
`/health/ready` this file also registers: `/health/ready` can legitimately flip during a normal
rolling deploy (a pod that's up but not yet accepting traffic isn't an outage), which would make
an external monitor page on every deploy — `/health/live` only reflects whether the process
itself is alive.

**Monitor config to add** (BLOCKING-MANUAL — add this in the UptimeRobot dashboard):

| Field | Value |
|---|---|
| Monitor type | HTTP(s) |
| Friendly name | `Pena e Arte — production` |
| URL | `https://app.tattooos.co/health/live` |
| Monitoring interval | 5 minutes (UptimeRobot's free-tier floor; upgrade only if 1-minute granularity is worth the cost — not needed to close this gap) |
| Alert contacts | same address as `alerting-runbook.md`'s receiver (`phisoftwaresolutions@gmail.com` today) — keep these two in sync if that address ever changes |

## §4 — Public status page

**Recommendation: use UptimeRobot's own hosted public status page, not a custom-built one.**
UptimeRobot's free tier includes a hosted status page as part of the same account created for
§5 — pointing it at the monitor above is a checkbox in the same dashboard, not a second piece of
infrastructure to build, deploy, and keep in sync. A custom page (static HTML on R2/Cloudflare
Pages, or a public frontend route) would duplicate what the monitor account already tracks and
add its own uptime-of-the-status-page-itself problem. Build a custom one only if Phi wants
tattooOS branding on it specifically — nothing in the current requirements calls for that.

**Setup (BLOCKING-MANUAL, same account as §5):** in UptimeRobot, Status Pages → Add Status Page →
select the `Pena e Arte — production` monitor from §5 → publish. UptimeRobot gives this a
shareable URL on its own domain (a custom domain/CNAME is available on paid tiers if wanted
later).

## Status

Both prepared, neither created yet — Phi opted to add the monitor and status page directly in
the existing UptimeRobot dashboard rather than have this session drive it via browser
automation. Nothing else in this repo depends on either existing.
