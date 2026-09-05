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

**Verified end-to-end live on 2026-09-05, with Phi's explicit go-ahead for real downtime.**

First deploy surfaced a real bug: `api-pod-not-ready` and `api-5xx-error-rate` both sat in
`health=error` ("invalid format of evaluation results ... looks like time series data, only
reduced data can be alerted on") — the single-stage "classic condition" query model doesn't
reduce a Prometheus range query correctly in this Grafana version. Fixed in a follow-up PR by
switching to the standard three-stage reduce→threshold expression pipeline; confirmed via
Grafana's own `/api/prometheus/grafana/api/v1/rules` that all three rules report `health=ok`
before running the live test.

**Live test:** scaled `pena-e-arte-api` to 0 replicas at 11:41:14 AM, confirmed via
`/api/prometheus/grafana/api/v1/rules` polling that the alert transitioned
`inactive → pending → firing` at exactly the 5-minute mark (11:47:12 AM, matching the rule's
`for: 5m`), confirmed the fired alert in Alertmanager's `/api/alertmanager/grafana/api/v2/alerts`
routed to the `ops-email` receiver, then restored the API to 2 replicas immediately (total
downtime ~7 minutes, `/health/live` confirmed `200` again afterward). **The email itself
arrived** — `notifications@tattooos.co` → `phisoftwaresolutions@gmail.com`, subject
`[FIRING:1] API pod not ready ...`, delivered 33 seconds after the alert fired, correct summary
and label values — confirming the full chain (Prometheus → Grafana alert rule → Alertmanager →
Resend SMTP relay → inbox) actually works, not just that the YAML looks right. The `[RESOLVED]`
notification also arrived once the API came back healthy, confirming the full fire → recover →
resolve → notify lifecycle, not just the initial fire.
