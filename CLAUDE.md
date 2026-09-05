# CLAUDE.md — Tattoo Studio SaaS

> Primary reference for Claude Code. Read this first, then load the relevant
> file from `docs/claude/` for the layer you are working on.

---

## What This Project Is

Multi-tenant SaaS for tattoo studios. Each studio is a tenant. Four roles:
`client` `artist` `owner` `issuer` (platform admin, cross-tenant).

**Core features:** appointment booking + deposits, digital consent forms,
design approval workflow, client profiles + body map, payments + session
splits, automated notifications.

---

## Detailed Instructions

| Working on | Read this file |
|---|---|
| Backend / API | `docs/claude/backend.md` |
| Frontend / React | `docs/claude/frontend.md` |
| Database / EF Core | `docs/claude/database.md` |
| Architecture patterns | `docs/claude/architecture.md` |
| Coding conventions | `docs/claude/conventions.md` |

---

## Tech Stack (Quick Reference)

```
Frontend      React 19 · Vite · TypeScript · React Router v7
              Redux Toolkit · RTK Query · Tailwind · shadcn/ui
              @microsoft/signalr · React Hook Form

Backend       ASP.NET Core 10 · C# · Minimal API
              MediatR · FluentValidation · ASP.NET Core Identity
              JWT · Policy-based RBAC · SignalR
              Serilog · OpenTelemetry

Data          MySQL 8.4 · EF Core 10 (Pomelo) · Redis

Services      Stripe.net · Resend · Twilio · Hangfire · Cloudflare R2

Infra         Docker · K3s · Traefik · GitHub Actions
              Cloudflare · Hetzner/AWS
              Grafana · Prometheus · Loki · Tempo
```

---

## Project Structure

```
/
├── CLAUDE.md
├── docs/
│   └── claude/                       ← per-layer instruction files
│       ├── backend.md
│       ├── frontend.md
│       ├── database.md
│       ├── architecture.md
│       ├── conventions.md
│       └── self-promotion-prompts.md ← feature prompts (SP-01 through SP-08)
├── Pena_e_Arte.API/                  ← ASP.NET Core entry point
├── Pena_e_Arte.Application/          ← MediatR handlers, DTOs, validators
├── Pena_e_Arte.Domain/               ← Entities, enums, interfaces
├── Pena_e_Arte.Infrastructure/       ← EF Core, external services, SignalR
├── Pena_e_Arte.Contracts/            ← Shared request/response models
├── frontend/                         ← React + Vite app
│   └── src/
│       ├── app/                      ← store, router
│       ├── features/                 ← feature slices (appointments, clients…)
│       ├── shared/                   ← reusable components, hooks, utils
│       └── layouts/                  ← role-based layout components
└── tests/
    ├── Pena_e_Arte.UnitTests/
    └── Pena_e_Arte.IntegrationTests/
```

---

## Non-Negotiable Rules (Apply Everywhere)

1. **Tenant isolation is mandatory.** Every DB query touching tenant data must
   go through EF Core global query filters. Never query without tenant scope
   unless the role is `issuer`.

2. **RBAC at the endpoint.** Every endpoint must have `.RequireAuthorization()`
   with the correct policy. No unprotected endpoints except `/auth` and `/health`.

3. **Never log PII.** Logs must include `tenant_id`, `user_id`, `request_id`.
   Never include names, emails, phone numbers, or card data.

4. **Secrets never in source.** All secrets via environment variables or Vault.
   No hardcoded connection strings, API keys, or tokens anywhere.

5. **Structured logs only.** Use Serilog. No `Console.WriteLine`, no
   `console.log` in production paths.

6. **Match current industry standards — for every tenant/role.** Every feature or
   change, backend and frontend alike, must reflect the current standard for this
   product category: vertical booking/scheduling SaaS (Vagaro, Fresha, Boulevard,
   Mindbody, Zenoti, GlossGenius-tier UX and architecture) plus general B2B SaaS
   platform-admin standards for the issuer role (org/tenant management, billing,
   audit logs, support tooling). This applies to backend architecture/structure/
   conventions and to frontend UI/UX equally, and must hold for every tenant this
   touches — client, artist, owner, and issuer — not just the role the feature was
   built for. See `docs/claude/architecture.md`'s "Industry-Standard Benchmark" note
   for the concrete comparison set and how to verify against it.

7. **Keep Help in sync — every time.** Every feature added or changed must update,
   in the same change: the in-app Help Menu content
   (`frontend/src/features/help/helpContent.ts`), the standalone user manual
   (`frontend/public/user-manual/index.html`), and any onboarding-tour step it
   affects (`frontend/src/features/help/tours/*.ts`). A feature is not done until
   Help describes it correctly. See `docs/claude/architecture.md`'s "In-App Help
   Menu" section for why these three surfaces exist and must stay aligned.

---

## Common Commands

```bash
# Backend
dotnet build
dotnet test
dotnet ef migrations add <Name> --project Pena_e_Arte.Infrastructure
dotnet ef database update --project Pena_e_Arte.Infrastructure
dotnet run --project Pena_e_Arte.API

# Frontend
pnpm install
pnpm dev
pnpm build
pnpm test
pnpm lint

# Docker
docker compose up -d
docker compose down
```

---

## What Claude Code Should Never Do

- Add a new ORM or data access library (EF Core is the only one)
- Bypass global query filters without explicit `issuer` role check
- Store state in-memory that should be in Redis (sessions, slots, rate limits)
- Create a REST endpoint without a corresponding FluentValidation validator
- Skip writing a test for business logic in the Application layer
- Use `var` for non-obvious types in C# — be explicit
- Use `any` in TypeScript — always type explicitly
- Ship a user-facing feature/change without updating `helpContent.ts`, the
  standalone manual, and any affected onboarding-tour step in the same change
- Introduce a backend pattern or a frontend UI/UX pattern that falls behind the
  current standard for this SaaS category without flagging the gap explicitly
  (silently shipping a substandard pattern is worse than flagging it and moving on)
