# Alerting runbook

**Owner:** Phi · **Related:** `k8s/observability/grafana-alerting-configmap.yaml`,
`docs/infra/secrets-rotation-runbook.md`

Before 2026-09-05 nothing paged anyone — no `AlertRule`/Alertmanager config existed anywhere in
`k8s/`, and a production outage would only surface when a person happened to look at Grafana or a
customer complained. This is the first real alerting for this cluster.

## What exists

Three Grafana Alerting rules, provisioned as code via
`k8s/observability/grafana-alerting-configmap.yaml` (same file-provisioning mechanism
`grafana-configmap.yaml` already uses for dashboards/datasources — mounted at
`/etc/grafana/provisioning/alerting/alerting.yaml`), evaluated every 1 minute, all routed to one
contact point.

| Alert | Source | Condition | Why this threshold |
|---|---|---|---|
| API pod not ready | Prometheus (`up{service="pena-e-arte-api"}`, the same series `api-overview.json`'s "Scrape target up" panel already renders) | `< 1` for 5 minutes | A normal rolling deploy replaces a pod in well under 2 minutes end to end (kubelet's default readiness/liveness periods plus rollout time); 5 continuous minutes unreachable is a real outage, not deploy noise. |
| API 5xx error rate spike | Prometheus, the same percent formula as the dashboard's "Error rate (5xx as % of total)" panel | `> 5%` for 5 minutes | Comfortably above the background noise of a handful of isolated/retried requests at low traffic, low enough to catch a real regression before it compounds. First-cut threshold — tune once real production traffic gives a baseline. |
| Hangfire job failure rate | Loki, `count_over_time({namespace="pena-e-arte"} \|= "HangfireJobFailed" [15m])` | `>= 3` in 15 minutes | Hangfire auto-retries a job before it lands in `Failed`, so one failure can be a transient blip. 3+ distinct failures in 15 minutes means retries themselves are exhausting — the actual signal worth waking someone for. This alert only became possible on 2026-09-05: see "Why a new log line was needed" below. |

**Receiver:** Grafana's built-in `email` contact point, sent through Resend's SMTP relay
(`smtp.resend.com`, username `resend`, password = the same `RESEND_API_KEY` the app already uses
for transactional email — reused rather than a new credential, since it's the same Resend
account either way). Wired via `GF_SMTP_*` env vars on the Grafana Deployment, sourced from a new
`pena-e-arte-grafana-smtp` Secret (populated by `cd.yml`, same GitHub secret, materialized into
the `monitoring` namespace since K8s Secrets are namespace-scoped and Grafana doesn't live in
`pena-e-arte`).

**Recipient:** `phisoftwaresolutions@gmail.com` — the same address already used (and already
public) in `k8s/base/cluster-issuer.yaml`'s ACME contact. No dedicated ops/on-call inbox was
confirmed to exist as of 2026-09-05 (see that file's comment); this reuses the one already known
real and monitored rather than inventing a new destination. **Revisit this once a dedicated ops
inbox exists** — swap both this recipient and the ACME contact together, they're the same
open question.

## Why a new log line was needed

The Hangfire rule above depends on a structured log line that did not exist before this runbook
was written. Before 2026-09-05, only two jobs (`GuestPendingUploadCleanupJob`,
`ManualReminderJob`) had their own internal `try`/`catch` + `LogWarning` — every other job's
unhandled exception was visible only in the Hangfire dashboard itself, never in Loki, so no
Loki-based "a Hangfire job failed" alert could actually have fired on anything. A global
`IApplyStateFilter` (`Pena_e_Arte.Infrastructure/Jobs/HangfireJobFailureLogFilter.cs`) now emits
one `LogError` (job id, type, failure reason) whenever *any* job transitions to Hangfire's
`Failed` state — that's the `"HangfireJobFailed"` line the Loki query above matches.

## What a responder should do first

1. **API pod not ready** — `kubectl get pods -n pena-e-arte`. If a pod is `CrashLoopBackOff`,
   `kubectl logs` it and check whether the last deploy is the cause (`kubectl rollout history
   deployment/pena-e-arte-api -n pena-e-arte`); `kubectl rollout undo` if so. If pods look
   healthy but the alert still fired, check Prometheus itself
   (`kubectl get pods -n monitoring`) — the scrape target, not the app, may be down.
2. **API 5xx error rate spike** — open the "Pena e Arte API Overview" Grafana dashboard, filter
   by `http_route` to find which endpoint is failing, then check recent logs in Loki
   (`{namespace="pena-e-arte", container="pena-e-arte-api"}`) for the actual exception. Roll
   back the last deploy if the timing lines up.
3. **Hangfire job failure rate** — query Loki for `{namespace="pena-e-arte"} |= "HangfireJobFailed"`
   to see which job type and exception is recurring, then check
   `https://app.tattooos.co/hangfire` (Basic Auth via `HANGFIRE_DASHBOARD_USERNAME`/
   `HANGFIRE_DASHBOARD_PASSWORD`) for the specific failed job instances and their retry history.

## Manual verification

Per this runbook's own standard (and `docs/infra/secrets-rotation-runbook.md`'s "founder
action" framing for anything touching live production), triggering a real alert and confirming
delivery is a live-production action and needs the `pena-e-arte-grafana-smtp` secret to exist
first (only created by a real `cd.yml` run against this branch's changes — not yet run as of
this writing). **Status: prepared, not yet verified end-to-end** — see the session's final
summary for the exact handoff.
