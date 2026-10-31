# Alerting runbook

**Owner:** Phi · **Related:** `k8s/observability/grafana-alerting-configmap.yaml`,
`docs/infra/secrets-rotation-runbook.md`

Before 2026-09-05 nothing paged anyone — no `AlertRule`/Alertmanager config existed anywhere in
`k8s/`, and a production outage would only surface when a person happened to look at Grafana or a
customer complained. This is the first real alerting for this cluster.

## What exists

Six Grafana Alerting rules, provisioned as code via
`k8s/observability/grafana-alerting-configmap.yaml` (same file-provisioning mechanism
`grafana-configmap.yaml` already uses for dashboards/datasources — mounted at
`/etc/grafana/provisioning/alerting/alerting.yaml`), evaluated every 1 minute, all routed to one
contact point.

| Alert | Source | Condition | Why this threshold |
|---|---|---|---|
| API pod not ready | Prometheus (`up{service="tattooos-api"}`, the same series `api-overview.json`'s "Scrape target up" panel already renders) | `< 1` for 5 minutes | A normal rolling deploy replaces a pod in well under 2 minutes end to end (kubelet's default readiness/liveness periods plus rollout time); 5 continuous minutes unreachable is a real outage, not deploy noise. |
| API 5xx error rate spike | Prometheus, the same percent formula as the dashboard's "Error rate (5xx as % of total)" panel | `> 5%` for 5 minutes | Comfortably above the background noise of a handful of isolated/retried requests at low traffic, low enough to catch a real regression before it compounds. First-cut threshold — tune once real production traffic gives a baseline. |
| Hangfire job failure rate | Loki, `sum(count_over_time({namespace="tattooos"} \|= "HangfireJobFailed" [15m]))` (instant query → reduce → threshold) | `>= 1` in 15 minutes | The log line is emitted only when a job lands in its final `Failed` state, i.e. after Hangfire's automatic retries (10 attempts) are exhausted — so a single line already means a job is dead, not a transient blip. The threshold was `>= 3` until 2026-10-03, which could never fire for one broken daily/monthly job (that is how `industry-report`'s R2 upload failure on 2026-10-01 went unnoticed). This alert only became possible on 2026-09-05: see "Why a new log line was needed" below. |
| Vault sealed or unreachable | Loki, `sum(count_over_time({namespace="tattooos"} \|~ "VaultHealthCheckFailed\|Secrets store unavailable" [15m]))` (instant query → reduce → threshold) | `>= 1` in 15 minutes, severity critical | Added 2026-10-15 (go-live blocker C-8). Production Vault is single-node and manually unsealed (ADR-0002), so it stays sealed after any restart and every POK card payment returns 503 until someone unseals it. `VaultHealthCheckJob` probes `/v1/sys/health` every 5 minutes and logs `VaultHealthCheckFailed` on anything but healthy (since 2026-10-21 also when the app's own token is rejected, can't be renewed, or lacks permissions on the studios' POK keys — it renews the token itself before it runs out), so the alert fires within ~6 minutes even with no payment traffic; real payments that hit a dead Vault log `Secrets store unavailable`. What to do: `docs/infra/vault-self-hosted-runbook.md`, "When the 'Vault sealed or unreachable' alert fires". |
| Database queries timing out | Loki, `sum by (environment) (count_over_time({namespace=~"tattooos\|tattooos-staging", container=~"api\|migrate"} \|~ "Lock wait timeout exceeded\|Command Timeout expired" [3h]))` | `>= 1` in 3 hours, per environment, severity critical | Added 2026-10-29. Production **and** staging, one alert per environment (the email names it). An orphaned prepared XA transaction held locks on staging's `hangfire_Set` from 15 to 29 Oct and nothing alerted; its only trace was the queries it blocked, which MySqlConnector ends with "Command Timeout expired" before MySQL's 50 s lock wait does (18-54 a day on staging, zero on either environment before 15 Oct and after the rollback). The 3-hour window is there because those came only ~1-2 an hour: a short window would fire and resolve over and over. |
| Deploy failed at the database migration | Loki, `` sum by (environment) (count_over_time({namespace=~"tattooos\|tattooos-staging", container="migrate"} \|= `"@l":"Fatal"` [1h])) `` | `>= 1` in 1 hour, per environment, severity critical | Added 2026-10-29. The migration Job logs "Application startup failed" at Fatal when it can't migrate or seed; CD then stops before the API rollout, so the environment quietly stays on its old version (staging, 26-29 Oct: 22 such lines, no alert). Only the `migrate` container: the API also logged Fatal lines at shutdown (Redis UNSUBSCRIBE timeouts during rollouts, until 28 Oct). |

**Receiver:** Grafana's built-in `email` contact point, sent through Resend's SMTP relay
(`smtp.resend.com`, username `resend`, password = the same `RESEND_API_KEY` the app already uses
for transactional email — reused rather than a new credential, since it's the same Resend
account either way). Wired via `GF_SMTP_*` env vars on the Grafana Deployment, sourced from a new
`tattooos-grafana-smtp` Secret (populated by `cd.yml`, same GitHub secret, materialized into
the `monitoring` namespace since K8s Secrets are namespace-scoped and Grafana doesn't live in
`tattooos`).

**Recipient:** `ops@tattooos.co` — a dedicated Google Group under the project's Workspace
domain, created 2026-10-01 and confirmed to receive external mail (tested from a non-Workspace
address). Replaces `phisoftwaresolutions@gmail.com`, which both this contact point and
`k8s/base/cluster-issuer.yaml`'s ACME contact used as a temporary stand-in since 2026-09-03/05
while no dedicated ops inbox existed. Both recipients were swapped together in the same change,
per this entry's own prior note that they're the same open question.

**Change log:** 2026-10-01 — recipient and ACME contact moved from `phisoftwaresolutions@gmail.com`
to `ops@tattooos.co`. The external UptimeRobot monitor's alert contact is configured in its own
dashboard, not in this repo; see `uptime-and-status-page.md`.

**Reload on config change:** Grafana only reads its provisioned files (alert rules and contact points,
datasources, dashboards) at startup. The `deploy` job's "Restart Grafana only if its provisioned config
changed" step hashes the four `tattooos-grafana-*` ConfigMaps into a `checksum/grafana-config` pod-template
annotation, so Grafana restarts on its own exactly when one of them changes and is left alone otherwise.
The Deployment is `Recreate`, so expect about a minute of Grafana downtime on such a deploy. Before this
step existed (2026-10-01), a changed recipient needed a manual `kubectl rollout restart`.

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

1. **API pod not ready** — `kubectl get pods -n tattooos`. If a pod is `CrashLoopBackOff`,
   `kubectl logs` it and check whether the last deploy is the cause (`kubectl rollout history
   deployment/tattooos-api -n tattooos`); `kubectl rollout undo` if so. If pods look
   healthy but the alert still fired, check Prometheus itself
   (`kubectl get pods -n monitoring`) — the scrape target, not the app, may be down.
2. **API 5xx error rate spike** — open the "TattooOS API Overview" Grafana dashboard, filter
   by `http_route` to find which endpoint is failing, then check recent logs in Loki
   (`{namespace="tattooos", container="tattooos-api"}`) for the actual exception. Roll
   back the last deploy if the timing lines up.
3. **Hangfire job failure rate** — query Loki for `{namespace="tattooos"} |= "HangfireJobFailed"`
   to see which job type and exception is recurring, then check
   `https://app.tattooos.co/hangfire` (Basic Auth via `HANGFIRE_DASHBOARD_USERNAME`/
   `HANGFIRE_DASHBOARD_PASSWORD`) for the specific failed job instances and their retry history.
4. **Database queries timing out** — look for a long-open transaction and its locks, read-only
   (`information_schema.INNODB_TRX`, `performance_schema.data_locks`) from a throwaway `mysql:8.0`
   pod in the affected namespace that reads `ConnectionStrings__Default` from `tattooos-api-secrets`
   and runs `SET SESSION TRANSACTION READ ONLY` first. A transaction with `trx_mysql_thread_id = 0` that never
   ends is an orphaned prepared XA transaction: `doadmin` cannot see or roll it back (no
   `XA_RECOVER_ADMIN` on DigitalOcean managed MySQL), so open a DigitalOcean support ticket asking
   them to run `XA RECOVER` / `XA ROLLBACK` for it, and ask for no restart or failover (production
   and staging share the cluster). 2026-10-29: DigitalOcean did exactly that within ~4 hours,
   ticket #12950916. Hangfire no longer uses XA since PR #316, so a new one should not happen.
   With no such transaction, look in Loki for which query timed out (a slow query, not a lock).
5. **Deploy failed at the database migration** — `kubectl logs job/tattooos-migrate -n <namespace>`
   for the exception, and the failed CD run in GitHub Actions. Fix the cause, then re-run the
   failed CD jobs (`gh run rerun <run id> --failed`). If the exception is a command timeout, see 4.

## Manual verification

**Verified end-to-end live on 2026-09-05, with Phi's explicit go-ahead for real downtime.**

First deploy surfaced a real bug: `api-pod-not-ready` and `api-5xx-error-rate` both sat in
`health=error` ("invalid format of evaluation results ... looks like time series data, only
reduced data can be alerted on") — the single-stage "classic condition" query model doesn't
reduce a Prometheus range query correctly in this Grafana version. Fixed in a follow-up PR by
switching to the standard three-stage reduce→threshold expression pipeline; confirmed via
Grafana's own `/api/prometheus/grafana/api/v1/rules` that all three rules report `health=ok`
before running the live test.

**Live test:** scaled `tattooos-api` to 0 replicas at 11:41:14 AM, confirmed via
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

**2026-10-16: the two Loki rules had the same bug, hidden.** They kept the single-stage form
because it "worked" for Loki. It didn't: `count_over_time` returns one series per log stream (one
per API pod), so as soon as matching lines existed the rule evaluated to `health=error` ("frame
cannot uniquely be identified by its labels: has duplicate results with labels {}") and fired
only because `execErrState: Alerting`. With no matching lines it showed `health=ok`, which is why
nobody noticed. Found by the real C-8 test: production Vault was sealed after a node reboot
(~13 Oct), the alert emails did arrive at ops@, but each one carried `grafana_state_reason:
Error`. Both rules now use `sum(...)` as an instant query plus reduce → threshold, the same
pipeline as the Prometheus rules; checked first with Grafana's `/api/v1/eval` against live Loki
(sealed Vault → `C = 1`, no matching lines → no data → OK). After any change to these rules,
check `/api/prometheus/grafana/api/v1/rules` for `health=ok` **while the condition is true**,
not only while it is quiet.

**2026-10-29: the two new rules were checked the same way before merging.** `/api/v1/eval` against
live Loki: with their real windows both return no data (OK; the staging lock was gone). With the
window widened to 14 days, so it covers the lock, each returns one series, `environment="staging"`,
and `C = 1` (470 timed-out queries, 22 failed migrations); production returns nothing. Their email
summaries use `{{ $labels.environment }}`. The first version wrote it `$$labels` on the assumption that
file provisioning expands `$name` as an environment variable; it doesn't for these alert annotations (Grafana
13.1.1 kept `$$labels` literally, which would have broken the email text), so a plain `$` is right here.
