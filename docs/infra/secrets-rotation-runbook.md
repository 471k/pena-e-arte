# Secrets rotation runbook

**Owner:** Phi · **Related:** `docs/infra/ADR-0002-secrets-management.md`, EPIC-0001 Phase 5

**First real drill: proposed 2026-09-19.** This runbook has existed but never been exercised
end-to-end. Proposed first drill: rotate a low-stakes secret (Resend API key or the Hangfire
dashboard credentials — either has an easy, safe rollback and no user-facing blast radius if
something goes wrong) live, with Phi present, following the General procedure below exactly as
written to see whether it actually holds up in practice. Not scheduled as an automated reminder
per Phi's 2026-09-05 decision — noted here as the next real step instead.

Rotating a secret = issue a new value at the provider, update it wherever the app reads it,
redeploy/restart, then revoke the old value. **This is a founder action** — an automated session
cannot rotate live values (no access to the real `.env` or the external accounts). Do these in
order and never commit a real value (the pre-commit gitleaks hook + CI gitleaks + GitHub push
protection all guard against it, but do not rely on them as your only line).

## Where each secret is read

- **Local / container:** the `api` service `environment:` block in `docker-compose.yml`, fed
  from `.env` (never committed; see `.env.example` for the variable names).
- **Production (once it exists):** the secrets backend behind `ISecretsProvider` (Vault by
  default — see ADR-0002), *not* `.env`.

## General procedure

1. Generate a new value at the provider (see per-secret notes below).
2. Add the new value alongside the old where possible (dual-run) so there is no downtime.
3. Update `.env` (local) and/or write the new value to the secrets backend (prod).
4. Restart/redeploy the API so it re-reads config.
5. Verify the integration works with the new value.
6. **Revoke/delete the old value** at the provider.
7. Record the rotation date somewhere durable (not in git).

## Per-secret notes

| Secret | Env var(s) | Where to rotate | Notes |
|---|---|---|---|
| JWT signing key | `JWT_SECRET_KEY` | Self-generated (`openssl rand -base64 48`) | Min 32 bytes (startup guard enforces). Rotating invalidates all live JWTs → users must re-login; do it in a low-traffic window. Keep issuer/audience stable. |
| Stripe secret key | `STRIPE_SECRET_KEY` | Stripe Dashboard → Developers → API keys → Roll | Roll the secret key; update webhook signing secrets if rolled too. Flow B billing only. |
| Stripe publishable key | `STRIPE_PUBLISHABLE_KEY` | Stripe Dashboard | Not secret, but rotate together for consistency. |
| Stripe webhook secrets | `STRIPE_WEBHOOK_SECRET_BILLING`, `STRIPE_WEBHOOK_SECRET_CONNECT` | Stripe Dashboard → Webhooks → each endpoint → Roll signing secret | Dual-run supported (Stripe keeps both briefly). |
| Cloudflare R2 access key | `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY` | Cloudflare Dashboard → R2 → Manage API tokens | Create a new token, deploy, then delete the old token. `R2_ACCOUNT_ID`/`R2_BUCKET_NAME`/`R2_PUBLIC_URL`/`R2_BACKUP_BUCKET_NAME` are not secret — but if the token is re-scoped to specific buckets rather than account-wide, make sure the new token still covers the backup bucket too. If it doesn't, `R2ExportJob` will fail loudly (a total-failure run throws deliberately, see `R2ExportService`), which the Hangfire-failure-rate alert in `docs/infra/alerting-runbook.md` catches — not silent. |
| Resend API key | `RESEND_API_KEY` | Resend Dashboard → API Keys | Create new, deploy, revoke old. |
| Twilio auth token | `TWILIO_AUTH_TOKEN` | Twilio Console → Account → Auth tokens (promote secondary) | Twilio supports a primary/secondary token so you can rotate with zero downtime. `TWILIO_ACCOUNT_SID`/`TWILIO_FROM_NUMBER` are identifiers, not secrets. |
| Instagram app secret | `INSTAGRAM_APP_SECRET` | Meta App Dashboard → Settings → Basic → Reset | Resetting invalidates existing OAuth exchanges; re-connect flows afterwards. |
| Instagram token-encryption key | `INSTAGRAM_TOKEN_ENCRYPTION_KEY` | Self-generated | Rotating this re-keys stored Instagram tokens — plan a re-encryption/reconnect step; do not rotate casually. |
| Vault token | `VAULT_TOKEN` (prod) | Vault (revoke + issue new AppRole/token) | Local dev-mode uses the root dev token (`VAULT_DEV_ROOT_TOKEN`), never used in production. |
| Hangfire dashboard creds | `HANGFIRE_DASHBOARD_USERNAME`, `HANGFIRE_DASHBOARD_PASSWORD` | Self-generated | Basic-auth for `/hangfire`; rotate like any password. |
| Grafana admin password | `PROD_GRAFANA_ADMIN_USER`, `PROD_GRAFANA_ADMIN_PASSWORD` | Self-generated | **Corrected 2026-09-05** — this row previously said `GRAFANA_ADMIN_PASSWORD` / "local observability stack only," which went stale once the production `monitoring` namespace shipped (Phase 7); these are the real production Grafana admin credentials. |
| Grafana alert-email SMTP password | reuses `RESEND_API_KEY` | Resend Dashboard → API Keys | **Added 2026-09-05** — Grafana's alerting email contact point authenticates via Resend's SMTP relay using the same key as the app's transactional email (materialized into a second K8s Secret, `pena-e-arte-grafana-smtp`, since Secrets don't cross namespaces). Rotating `RESEND_API_KEY` per the row below also rotates this — no separate step needed, but redeploy Grafana (`kubectl rollout restart deployment/pena-e-arte-grafana -n monitoring`) too, since it doesn't watch the Secret for changes. |
| Production DB connection string | `PROD_DB_CONNECTION_STRING` | DigitalOcean → `pena-e-arte-prod-db` → reset user password, or rotate via Users & Databases | Contains the DB password inline (MySQL connection-string format) — treat rotation of this the same as a password rotation. |
| Staging DB connection string | `STAGING_DB_CONNECTION_STRING` | Same DigitalOcean cluster, staging's own user/database | Same handling as the production connection string above. |
| Cloudflare API token (DNS-01 solver) | `CLOUDFLARE_API_TOKEN` | Cloudflare Dashboard → API Tokens | Used by `cert-manager`'s `letsencrypt-prod-dns01` ClusterIssuer, lives in the `cert-manager` namespace (not `pena-e-arte`) — see `k8s/base/cluster-issuer.yaml`. Rotating does not require reissuing existing certs. |
| Staging R2 access key | `STAGING_R2_ACCESS_KEY_ID`, `STAGING_R2_SECRET_ACCESS_KEY` | Cloudflare Dashboard → R2 → Manage API tokens | Same handling as the production R2 row above, separate bucket/token. |
| Staging Stripe keys | `STAGING_STRIPE_SECRET_KEY`, `STAGING_STRIPE_PUBLISHABLE_KEY`, `STAGING_STRIPE_WEBHOOK_SECRET_BILLING` | Stripe Dashboard (test-mode keys) | Same handling as the production Stripe rows above. |
| Social-verification credentials | `FACEBOOK_APP_ID`/`FACEBOOK_APP_SECRET`/`FACEBOOK_REDIRECT_URI`, `X_CLIENT_ID`/`X_CLIENT_SECRET`/`X_BEARER_TOKEN`/`X_REDIRECT_URI`, `YOUTUBE_CLIENT_ID`/`YOUTUBE_CLIENT_SECRET`/`YOUTUBE_API_KEY`/`YOUTUBE_REDIRECT_URI`, `TIKTOK_CLIENT_KEY`/`TIKTOK_CLIENT_SECRET`/`TIKTOK_REDIRECT_URI` | Each platform's own developer console | All config-gated — the app degrades gracefully if any of these are unset. Not previously listed in this runbook at all (drift found 2026-09-05). |
| Social OAuth state signing key | `SOCIAL_STATE_SIGNING_KEY` | Self-generated | Signs the OAuth `state` parameter across all social-connect flows above; rotating invalidates any in-flight OAuth redirect (users just retry). Not previously listed (drift found 2026-09-05). |
| Google / Apple sign-in client IDs | `VITE_GOOGLE_CLIENT_ID`, `VITE_APPLE_CLIENT_ID` | Google Cloud Console / Apple Developer | Public client identifiers, not secret, but listed here since they're part of the same `pena-e-arte-api-secrets` bundle and breakage here looks identical to a secret-rotation incident. Not previously listed (drift found 2026-09-05). |
| Forwarded-headers trusted proxy CIDR | `FORWARDED_HEADERS_TRUSTED_PROXY_CIDR` | Self-known value (`10.42.0.0/16`, the K3s pod CIDR) | Not a secret, but a wrong value here silently breaks client-IP resolution (GeoIP, rate limiting) across the whole app — listed for that reason. Not previously listed (drift found 2026-09-05). |
| Vault address | `VAULT_ADDR` | Self-known value (in-cluster Service DNS) | Not a secret, kept alongside `VAULT_TOKEN` above for consistency — same reasoning as the CIDR row. Not previously listed (drift found 2026-09-05). |
| R2 backup bucket name | `R2_BACKUP_BUCKET_NAME` | Cloudflare Dashboard → R2 → bucket name (not a credential itself) | **Added 2026-09-05** alongside `R2ExportJob` — production-only, deliberately unset on staging. Not secret, but the bucket must actually exist (BLOCKING-MANUAL — see `docs/infra/backup-dr-runbook.md`) before this is set to a real value, or the job just no-ops (harmless, logged as a warning, not an error). |

## After any rotation

- Confirm no real value landed in git: `git log -p -S '<fragment>'` should return nothing, and
  the pre-commit hook would have blocked a staged secret anyway (`.githooks/pre-commit`).
- If a secret was ever exposed, treat it as compromised: rotate immediately and audit access logs
  at the provider.
