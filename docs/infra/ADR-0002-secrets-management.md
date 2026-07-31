# ADR-0002 — Secrets management

**Status:** Accepted (default for this session) · **Date:** 31 July 2026 · **Decider:** Phi
**Context docs:** `CLAUDE.md` rule 4, `docs/payments/ADR-0001-payment-providers.md` (Article 4(g)
per-tenant secrets posture), `docs/payments/implementation-readiness-status-2026-07-31.md` §1
**Related:** `docs/infra/secrets-rotation-runbook.md`, EPIC-0001 Phase 5

---

## Context

Application secrets (JWT signing key, Stripe keys, Cloudflare R2 keys, Resend, Twilio,
Instagram) are today read from environment variables / `appsettings` sections. That is
twelve-factor step one, and fine for a solo-founder dev setup, but it does not scale to the
posture ADR-0001 requires: **per-tenant** provider credentials (POK, easyPos) that must never
sit in the application database and must be rotatable independently per studio.

CLAUDE.md rule 4 names Vault explicitly ("All secrets via environment variables or Vault").
There is no production infrastructure yet — no K3s cluster, no server (per
`implementation-readiness-status-2026-07-31.md` §1) — so this decision is about the *mechanism*
and the *local default*, not standing up a production secrets cluster.

## Decision

1. Introduce a provider-neutral **`ISecretsProvider`** abstraction (`Domain/Interfaces`) with a
   single `GetSecretAsync(key, ct)` that **fails closed** — it throws if a secret is
   unresolvable and never returns null for a caller to proceed on with no credential.
2. The default backend is **HashiCorp Vault**, per CLAUDE.md rule 4, via the `VaultSharp` .NET
   client (`VaultSecretsProvider`, KV v2).
3. Locally, Vault runs in **dev mode** as a new `docker-compose.yml` service (in-memory,
   auto-unsealed, data lost on restart) — the same local tier as `mysql`/`redis`. This is
   **deliberately not** the production posture; standing up a Raft-backed production Vault is
   out of scope for this session and belongs to the K3s deploy work once real infra exists.
4. Per-tenant provider credentials are modelled as a **pointer only**: `StudioCredentialRef`
   (`StudioId`, `Provider`, `SecretPath`) — a Vault path/key, with **no value column**. No real
   credential is issued or stored in this session; this is scaffolding for ADR-0001 Article
   4(g).

## Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **Vault (dev mode local, Raft in prod)** | Named non-negotiable in CLAUDE.md rule 4; industry standard; strong per-path policy/leasing; dev mode is zero-setup locally | Highest ops burden in production (unseal, upgrades, HA/Raft, backup) for a solo founder | **Chosen as the default.** `ISecretsProvider` makes the prod backend a later, cheap decision. |
| **Infisical** | Much lower ops burden (managed or single-container self-host); good DX; per-env/per-folder secrets; open source | Younger ecosystem; another vendor/service to run; not the CLAUDE.md-named backend | **Documented as the recommended lower-ops production alternative** — see below. |
| **Doppler** | Lowest ops burden (fully managed); excellent DX and rotation tooling | SaaS dependency + per-seat cost; secrets leave your infra | **Documented alternative** for a founder who prefers fully-managed over self-hosted. |
| **Keep env vars only** | Zero new moving parts | Cannot do per-tenant, rotatable, non-DB-resident credentials; fails ADR-0001's Article 4(g) posture | Rejected — the exact gap this ADR closes. |

**Recommendation for the founder to decide before a real production deploy:** if the ops burden
of running production Vault (unseal/HA/backup) is unattractive for a solo founder, swap the
backend to **Infisical** (self-hosted single container, lower burden) or **Doppler** (managed).
Because everything goes through `ISecretsProvider`, that swap is **one new implementation class**
(e.g. `InfisicalSecretsProvider`) plus a DI line — not a rewrite, and no call site changes.

## Consequences

- `VaultSecretsProvider` is registered always but is inert until `Vault:Address` is configured;
  construction does not connect, and a call fails closed if Vault is unreachable/empty.
- A pre-commit gitleaks hook (`.githooks/pre-commit`) is added as the one scanning layer neither
  CI gitleaks nor GitHub push protection can provide (both only see a commit after it exists).
- Secrets currently in `.env`/config must still be **rotated** by the founder (this session
  cannot touch live values) — see `secrets-rotation-runbook.md`.
- The `docker-compose.yml` Twilio/Instagram env gap was fixed at the same time (both were live
  integrations running with permanently empty credentials in any composed deployment).

## Not in scope

Production Vault cluster, K3s manifests, cloud secrets-manager account creation, and issuing any
real POK/easyPos/Polar credential. Those are separate, later work.
