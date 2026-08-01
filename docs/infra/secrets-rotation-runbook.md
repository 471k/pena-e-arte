# Secrets rotation runbook

**Owner:** Phi · **Related:** `docs/infra/ADR-0002-secrets-management.md`, EPIC-0001 Phase 5

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
| Cloudflare R2 access key | `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY` | Cloudflare Dashboard → R2 → Manage API tokens | Create a new token, deploy, then delete the old token. `R2_ACCOUNT_ID`/`R2_BUCKET_NAME`/`R2_PUBLIC_URL` are not secret. |
| Resend API key | `RESEND_API_KEY` | Resend Dashboard → API Keys | Create new, deploy, revoke old. |
| Twilio auth token | `TWILIO_AUTH_TOKEN` | Twilio Console → Account → Auth tokens (promote secondary) | Twilio supports a primary/secondary token so you can rotate with zero downtime. `TWILIO_ACCOUNT_SID`/`TWILIO_FROM_NUMBER` are identifiers, not secrets. |
| Instagram app secret | `INSTAGRAM_APP_SECRET` | Meta App Dashboard → Settings → Basic → Reset | Resetting invalidates existing OAuth exchanges; re-connect flows afterwards. |
| Instagram token-encryption key | `INSTAGRAM_TOKEN_ENCRYPTION_KEY` | Self-generated | Rotating this re-keys stored Instagram tokens — plan a re-encryption/reconnect step; do not rotate casually. |
| Vault token | `VAULT_TOKEN` (prod) | Vault (revoke + issue new AppRole/token) | Local dev-mode uses the root dev token (`VAULT_DEV_ROOT_TOKEN`), never used in production. |
| Hangfire dashboard creds | `HANGFIRE_DASHBOARD_USERNAME`, `HANGFIRE_DASHBOARD_PASSWORD` | Self-generated | Basic-auth for `/hangfire`; rotate like any password. |
| Grafana admin password | `GRAFANA_ADMIN_PASSWORD` | Self-generated | Local observability stack only. |

## After any rotation

- Confirm no real value landed in git: `git log -p -S '<fragment>'` should return nothing, and
  the pre-commit hook would have blocked a staged secret anyway (`.githooks/pre-commit`).
- If a secret was ever exposed, treat it as compromised: rotate immediately and audit access logs
  at the provider.
