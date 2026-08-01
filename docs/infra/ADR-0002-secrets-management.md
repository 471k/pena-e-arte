# ADR-0002 — Secrets management

**Status:** Accepted · **Date:** 31 July 2026 (production backend resolved 1 Aug 2026) · **Decider:** Phi
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
   auto-unsealed, data lost on restart) — the same local tier as `mysql`/`redis`.
4. **Production backend (resolved 1 Aug 2026): HCP Vault** — HashiCorp's managed/hosted Vault,
   **not** a self-hosted Raft cluster and **not** Infisical/Doppler. Rationale: CLAUDE.md rule 4
   already names Vault; self-hosting Raft/unsealing/HA/backup is disproportionate ops burden for a
   solo founder; HCP Vault gives the same policy/dynamic-secrets model through the same `VaultSharp`
   client already integrated, with HashiCorp running storage/HA/unsealing. **No code change** —
   only `Vault:Address`/`Vault:Token` config differs at deploy time, and no production infra exists
   yet, so this decision only records the target.
5. Per-tenant provider credentials are modelled as a **pointer only**: `StudioCredentialRef`
   (`StudioId`, `Provider`, `SecretPath`) — a Vault path/key, with **no value column**. No real
   credential is issued or stored in this session; this is scaffolding for ADR-0001 Article
   4(g).

## Alternatives considered (for the production backend)

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **HCP Vault (HashiCorp-managed)** | The CLAUDE.md-named backend, same policy/dynamic-secrets model + the same `VaultSharp` client already integrated; HashiCorp runs storage/HA/unsealing/backup — none of the self-host ops burden | Paid managed service; secrets held with HashiCorp (still your Vault, their infra) | **CHOSEN (1 Aug 2026).** Zero code change from the local integration — only config differs at deploy. |
| **Self-hosted Vault (Raft cluster)** | Full control; no third-party holds secrets | Unsealing, HA/Raft, upgrades, backup — disproportionate ops for a solo founder | Rejected — the ops burden HCP Vault removes. |
| **Infisical** | Lower ops than self-hosted Vault; good DX | Not the CLAUDE.md-named backend; a different client/abstraction | Rejected in favour of staying on Vault (HCP gives managed ops without leaving Vault). |
| **Doppler** | Fully managed; excellent rotation tooling | Different model/vendor; not Vault | Rejected — same reason as Infisical. |
| **Keep env vars only** | Zero new moving parts | Cannot do per-tenant, rotatable, non-DB-resident credentials; fails ADR-0001's Article 4(g) posture | Rejected — the exact gap this ADR closes. |

Because everything goes through `ISecretsProvider`, even this resolved choice stays cheap to
revisit: swapping backend would be **one new implementation class** plus a DI line, no call-site
changes — but HCP Vault needs no new implementation at all (it is the same `VaultSecretsProvider`,
pointed at an HCP address).

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
