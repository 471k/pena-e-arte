# Contributing to TattooOS

Primary engineering reference is [`CLAUDE.md`](./CLAUDE.md) and the per-layer docs under
[`docs/claude/`](./docs/claude/). This file covers the mechanics: local setup, the CI gates your
change must pass, and the Definition of Done.

## Local setup

```bash
# Backend
dotnet build "Pena e Arte.slnx"
dotnet test

# Frontend
cd frontend && pnpm install && pnpm dev

# Local infra (MySQL, Redis, MinIO, Vault dev-mode, observability)
docker compose up -d mysql redis            # minimum for tests
# (see .env.example for the required env vars — copy to .env and fill in)
```

Integration tests connect to MySQL on `127.0.0.1:3306` (root/root); start it before running them.

## Pre-commit hook (secret scanning) — enable once per clone

The repo ships a pre-commit hook that scans your **staged** changes for secrets with gitleaks —
the one layer neither CI gitleaks nor GitHub push protection can provide (both only see a commit
after it exists). Enable it:

```bash
git config core.hooksPath .githooks
```

The hook uses a local `gitleaks` binary if installed, otherwise the official gitleaks Docker image
(Docker is already required here). It **fails closed** — if neither is available it blocks the
commit rather than skipping the scan. See [`docs/infra/ADR-0002-secrets-management.md`](./docs/infra/ADR-0002-secrets-management.md).

## Required CI checks (`.github/workflows/ci.yml`)

Every PR to `main` must pass:

| Job | What it enforces |
|---|---|
| **Backend — build, format, test** | `dotnet format --verify-no-changes` (blocking), Release build, the **architecture fitness test** (fails if a platform-balance-ledger/payout-queue type is introduced — Law 55/2020 Art. 4(g)), and the full unit + integration suites against a real MySQL 8.4. |
| **Frontend — lint, typecheck, build, unit, e2e** | `pnpm lint`, `pnpm build` (`tsc -b` strict + vite), `pnpm test --coverage`, Playwright e2e. |
| **Docker images build (no push)** | API + frontend images build. |
| **Non-negotiable-rules guardrails** | gitleaks secret scan; no `Console.WriteLine`/`console.log` in production code; every `Map*` endpoint has `.RequireAuthorization()`/`.AllowAnonymous()`. |
| **Help stays in sync (rule 7)** | If a change touches a user-facing gated path (payments/forms/billing/studios/clients features, the matching Application slices, or the `ConsentForm`/`ConsentTemplate`/`ClientProfile`/`Payment` entities) without updating a Help surface (`helpContent.ts`, `user-manual/index.html`, or a tour), the check fails. Override with a justified `[skip-help-sync]` in the latest commit message when the change genuinely has no user-visible surface. |

Run these locally before pushing — the backend format/test and frontend lint/build gates are the
most common causes of a red PR.

## Definition of Done

A change is done when:

1. **Acceptance criteria are demonstrated** — not just "the code exists", but shown working.
2. **Tests ship with it** — business logic in the Application layer always has a test
   (`CLAUDE.md`); backend `dotnet test` and frontend `pnpm test` are green.
3. **No PII in logs** — structured Serilog only; logs carry `tenant_id`/`user_id`/`request_id`,
   never names/emails/phones/card/health text (rule 3).
4. **Help is in sync** — `helpContent.ts`, the standalone user manual, and any affected
   onboarding tour are updated in the *same* change (rule 7). State "no Help change needed"
   explicitly in the PR when that's the correct verdict.
5. **No secrets in source** — all secrets via env vars / Vault (rule 4); the pre-commit hook and
   CI gitleaks both guard this.
6. **The PR says which `CLAUDE.md` rule / industry standard it serves** (rule 6) and flags any
   pattern that falls behind the current SaaS-category standard rather than silently shipping it.
7. **A reviewer can re-run the demonstration** from the PR description.

## Conventions (quick reference — full detail in `docs/claude/`)

- Backend: MediatR + FluentValidation (a validator per command/endpoint); EF Core only; tenant
  isolation via global query filters (issuer-only `IgnoreQueryFilters` with a justification
  comment); explicit C# types, no `var` for non-obvious types.
- Frontend: RTK Query for data fetching (no `useEffect` fetching); no `any`; Tailwind + shadcn/ui.
- Don't add a new NuGet/npm package without flagging it as a prerequisite decision in the PR.
