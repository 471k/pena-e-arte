# Architecture Instructions — Patterns & Decisions

> Load this file when making structural decisions, adding new features,
> or unsure which layer owns a responsibility.

---

## Layer Responsibilities

```
Domain          Pure business logic. No EF, no HTTP, no DI.
                Entities, value objects, domain exceptions, interfaces.

Application     Orchestration. MediatR handlers, DTOs, validators.
                Depends on Domain + Infrastructure interfaces only.
                No direct EF Core or HTTP here.

Infrastructure  Implementation details. EF Core, Stripe, Twilio, Redis,
                SignalR hubs, Hangfire jobs.
                Implements interfaces defined in Domain.

API             Entry point. Minimal API endpoints, middleware, auth config.
                Calls Application via MediatR only. No business logic here.

Contracts       Request/response DTOs shared between API and (optionally)
                frontend code generation.
```

**Dependency rule:** outer layers depend on inner layers. Never the reverse.
`API → Application → Domain`. Infrastructure implements Domain interfaces.

---

## Adding a New Feature — Checklist

For every new feature (e.g. "design approval"):

```
Domain
  [ ] Entity in Domain/Entities/
  [ ] Status enum in Domain/Enums/ if applicable
  [ ] Interface in Domain/Interfaces/ if infra needed

Application
  [ ] Command + Handler for each write operation
  [ ] Query + Handler for each read operation
  [ ] FluentValidation validator per command
  [ ] Request/Response DTOs in Contracts/

Infrastructure
  [ ] EF Core configuration in Persistence/Configurations/
  [ ] Migration generated and reviewed
  [ ] External service implementation if needed

API
  [ ] Endpoint class in Endpoints/
  [ ] Routes added to Program.cs MapGroup
  [ ] Authorization policy applied to each endpoint

Frontend
  [ ] RTK Query endpoints added to feature api slice
  [ ] Components in features/<domain>/components/
  [ ] Route added to router with correct role guard

Tests
  [ ] Unit tests for domain logic
  [ ] Integration test for each command handler
  [ ] Integration test for each query handler
```

---

## Multi-Tenancy Architecture

```
Request arrives
    ↓
JWT validated (ASP.NET Core Identity)
    ↓
TenantMiddleware extracts tenant_id claim → sets ICurrentTenant
    ↓
Authorization policy checks role claim
    ↓
Endpoint → MediatR → Handler
    ↓
AppDbContext — global query filters apply tenant_id automatically
    ↓
Response
```

`ICurrentTenant` is scoped to the request. Inject it anywhere you need
the current tenant. Never pass tenant ID as a method parameter through
the application layer — always resolve from `ICurrentTenant`.

---

## Hangfire Job Conventions

Jobs are fire-and-forget for notifications. Use typed job classes.

```csharp
// Infrastructure/Jobs/AppointmentReminderJob.cs
public class AppointmentReminderJob(INotificationService notifications)
{
    public async Task SendReminder(Guid appointmentId, string type)
    {
        // fetch appointment, send via Twilio/MailKit
    }
}

// Scheduled from a handler after appointment creation:
_backgroundJobs.Schedule<AppointmentReminderJob>(
    job => job.SendReminder(appointment.Id, "48h"),
    appointment.Date.AddHours(-48));
```

---

## SignalR Event Naming Convention

```
AppointmentCreated      booking confirmed
AppointmentCancelled    booking cancelled
AppointmentUpdated      time/artist changed
DesignUploaded          artist uploaded new draft
DesignApproved          client approved design
DesignChangeRequested   client requested changes
NotificationReceived    generic in-app notification
```

Always push from Infrastructure (Hangfire jobs or command handlers).
Never push directly from an endpoint handler.

---

## Stripe Connect Flow

```
Studio onboarding:
  1. Owner triggers ConnectStudio command
  2. Infrastructure creates Stripe Connect account
  3. Stores stripe_account_id on Studio entity
  4. Returns onboarding URL — owner completes in browser

Payment at booking:
  1. Client creates appointment → CreateAppointmentCommand
  2. Handler creates PaymentIntent on studio's Connect account
  3. Deposit amount held, remainder charged at session end
  4. Webhook confirms payment → updates Appointment.DepositStatus

Payout:
  Stripe handles automatically to studio's Connect account.
  Platform fee deducted at the Stripe level — not in our code.
```

---

## Feature Module Map

Maps each product feature to its domain entities, infrastructure dependencies, and ownership layer.

| # | Feature | Domain Entities | Infrastructure | Scope |
|---|---|---|---|---|
| 01 | Appointment Booking + Deposits | `Appointment`, `DepositRule` | Stripe Connect, Hangfire | Per-tenant |
| 02 | Consultation & Consent Forms | `IntakeForm`, `ConsentForm` | Cloudflare R2 (PDF storage) | Per-tenant |
| 03 | Design Approval Workflow | `DesignRevision`, `DesignApproval` | Cloudflare R2 (images), SignalR | Per-tenant |
| 04 | Client Profile & Tattoo History | `ClientProfile`, `TattooRecord`, `BodyMap` (value object) | Cloudflare R2 (photos) | Per-tenant |
| 05 | Payments & Session Splits | `Payment`, `SessionSplit` | Stripe Connect | Per-tenant |
| 06 | Automated Communication | `NotificationLog` | Hangfire + Twilio + MailKit | Per-tenant |
| 07 | Studio Map | No entity (reads `Studio.Latitude/Longitude`) | None — public endpoint, no auth | Platform-wide |
| 08 | Platform Subscriptions | `Subscription`, `Plan` | Stripe Billing (separate from Connect) | Issuer-level |

---

## Platform Subscription Architecture

Subscriptions are issuer-level — they control studio (tenant) access to the platform.
They are NOT per-client or per-artist.

### Trial Model

New studios get a 14-day full-featured trial. No credit card required at signup.
After trial expires: read-only grace period of 7 days, then tenant suspended until subscribed.

```
TrialExpiresAt   = CreatedAt + 14 days   (set on Studio entity at registration)
GracePeriodEnd   = TrialExpiresAt + 7 days
```

TenantMiddleware access rules (evaluated in order):

```
1. Subscription.Status == active              → full access
2. Now < TrialExpiresAt                       → full access (trial)
3. TrialExpiresAt < Now < GracePeriodEnd      → read-only access, banner shown
4. Now > GracePeriodEnd && no active sub      → redirect to /subscribe, all writes blocked
5. Subscription.Status == past_due            → redirect to /billing, all writes blocked
6. Subscription.Status == cancelled           → redirect to /subscribe, all writes blocked
```

### Entities

```
Plan (Issuer-owned, not tenant-scoped)
  PlanId, Name, BillingInterval (Monthly | Yearly), PriceMonthly, PriceYearly
  YearlyDiscountPercent (default: 17 — equivalent to 2 months free)

Subscription (links Studio → Plan)
  SubscriptionId, StudioId, PlanId
  Status: trialing | active | past_due | cancelled | grace_period
  TrialExpiresAt, CurrentPeriodEnd, GracePeriodEnd
  StripeSubscriptionId (nullable until card added)
```

### Subscription Flow

```
1. Studio registers → TrialExpiresAt set, Status = trialing, no Stripe object yet
2. Hangfire job fires at TrialExpiresAt-48h → sends trial expiry warning email
3. Hangfire job fires at TrialExpiresAt    → Status = grace_period
4. Hangfire job fires at GracePeriodEnd    → Status = suspended if no active sub
5. Owner selects plan → CreateSubscriptionCommand
6. Infrastructure creates Stripe Billing Subscription
7. Stripe webhook → SubscriptionUpdated → updates Status, CurrentPeriodEnd
```

### Yearly Discount

Yearly plan = monthly price × 10 (2 months free — ~17% discount).
Surface the saving prominently on the pricing page and in trial expiry emails.
BillingInterval enum: Monthly | Yearly.

### Stripe Billing vs Stripe Connect — do not confuse

- Stripe Billing  = platform charges studios for SaaS access (subscriptions)
- Stripe Connect  = studios charge their clients for tattoo sessions (payments)

Both coexist. Billing uses the platform's main Stripe account.
Connect uses per-studio connected accounts.

---

## Studio Map

Public endpoint — no authentication required, no tenant filter.
Returns only published/active studios.

```
GET /api/studios/map
Response: [{ studioId, name, slug, latitude, longitude, city }]

Domain: Studio entity gains Latitude (double), Longitude (double), City (string)
API: endpoint uses AllowAnonymous — exempt from RequireAuthorization rule
Frontend: features/map/ — read-only, no Redux slice needed, plain RTK Query
```

---

## AllowAnonymous Exceptions

Hard Rule #2 requires `.RequireAuthorization()` on every endpoint.
The following are the only documented exceptions:

| Endpoint | Reason | Security mechanism |
|---|---|---|
| `GET /api/studios/map` | Public discovery, no user context | None needed — read-only public data |
| `POST /api/webhooks/stripe/billing` | Called by Stripe servers, no JWT | `Stripe-Signature` HMAC header validated against webhook secret |
| `POST /api/webhooks/stripe/connect` | Called by Stripe servers, no JWT | `Stripe-Signature` HMAC header validated against webhook secret |

"No JWT auth" does not mean "unprotected" for webhook endpoints — the Stripe-Signature
validation is the security mechanism. Always validate it before processing the event.
Never add new AllowAnonymous endpoints without adding a row to this table.

---

## Decisions Log

Record significant architectural decisions here so Claude Code
does not re-litigate them.

| Decision | Choice | Reason |
|---|---|---|
| ORM | EF Core only | Single data access layer, no Dapper |
| Validation | FluentValidation only | No Zod, no DataAnnotations |
| Client state | Redux Toolkit | Replaced Zustand |
| Server state | RTK Query | Replaces TanStack Query, Axios |
| Auth provider | ASP.NET Core Identity | Full control, no Clerk |
| Real-time | SignalR | Built-in to .NET, no third-party |
| Background jobs | Hangfire | .NET native, MySQL-backed |
| Tenant isolation | EF Core query filters | MySQL has no native RLS |
| Connection pool | MySqlConnector built-in | No ProxySQL needed at this scale |
| Platform billing | Stripe Billing | Separate from Stripe Connect — charges studios for SaaS access |
| Studio map | Public RTK Query endpoint | No auth, no tenant scope, lat/lng on Studio entity |
| Trial model | 14-day full trial, no CC required | Maximises trial starts; full access builds habit before paywall |
| Post-trial | 7-day read-only grace period | Reduces churn fear, increases trust and conversion |
| Yearly pricing | Monthly × 10 (2 months free) | Standard SaaS incentive, ~17% discount |
