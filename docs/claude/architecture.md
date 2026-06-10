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
| 09 | Platform Branding Flag | `Studio.ShowPlatformBranding` (bool, default `true`) | None | Per-tenant |
| 10 | Public Portfolio Pages | Reads `Studio`, `Artist` (read-only, no tenant filter) | None — public SEO endpoints | Platform-wide |
| 11 | Referral Code System | `ReferralCode`, `ReferralRedemption` | Stripe Billing discount coupon | Issuer-level |
| 12 | Client Portable Profiles | `ClientProfile` cross-tenant read (opt-in) | `IgnoreQueryFilters` — issuer-scoped only | Cross-tenant (issuer) |
| 13 | Design Share Token | `DesignShareToken` | Cloudflare R2, public time-limited endpoint | Per-tenant |
| 14 | Studio QR Code Generator | No new entity (reads `Studio.Slug`) | QRCoder NuGet (pre-approved, see Decisions Log) | Per-tenant |
| 15 | Industry Analytics Reports | No entity (aggregate reads, issuer-scoped) | Hangfire monthly job, Cloudflare R2 (report storage) | Issuer-level |
| 16 | Booking Confirmation Branding | Reuses `Studio.ShowPlatformBranding` (#09) | MailKit templates, R2 PDF footer | Per-tenant |

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
| `GET /api/v1/public/studios/{slug}` | Public SEO portfolio page | None — read-only, non-sensitive studio info only |
| `GET /api/v1/public/artists/{slug}` | Public SEO artist portfolio | None — read-only, non-sensitive artist info only |
| `GET /api/v1/public/designs/share/{token}` | Client-shared design link | Short-lived token (`DesignShareToken.ExpiresAt`), revocable |
| `GET /api/v1/studios/{id}/qr` | QR code image download | None — points to public portfolio URL only |
| `POST /api/webhooks/stripe/billing` | Called by Stripe servers, no JWT | `Stripe-Signature` HMAC header validated against webhook secret |
| `POST /api/webhooks/stripe/connect` | Called by Stripe servers, no JWT | `Stripe-Signature` HMAC header validated against webhook secret |

"No JWT auth" does not mean "unprotected" for webhook endpoints — the Stripe-Signature
validation is the security mechanism. Always validate it before processing the event.
Never add new AllowAnonymous endpoints without adding a row to this table.

---

## Self-Promotion Module Architecture

Eight features that make the platform market itself. Implement in order — later
features depend on entities introduced by earlier ones.

### Implementation Order & Dependencies

```
01 → Platform Branding Flag        (Studio.ShowPlatformBranding)
02 → Public Portfolio Pages        (Studio.Slug, Artist.Slug)
03 → Booking Confirmation Branding (depends on #01)
04 → Referral Code System          (ReferralCode, ReferralRedemption)
05 → Client Portable Profiles      (cross-tenant read, IPortableProfileService)
06 → Design Share Token            (DesignShareToken, depends on DesignRevision)
07 → Studio QR Code Generator      (depends on #02 — uses portfolio URL)
08 → Industry Analytics Reports    (issuer-only, depends on stable aggregate schema)
```

### Platform Branding Flag

```
Studio entity gains:
  ShowPlatformBranding  bool  default: true
  (stored per-tenant, not on Subscription — survives plan changes)

Plan entity gains:
  AllowBrandingRemoval  bool  default: false
  (issuer sets this true on paid plans)

Enforcement:
  - Booking widget footer: rendered by frontend, reads Studio flag via RTK Query
  - Email footer: MailKit template receives ShowPlatformBranding from handler
  - PDF footer: injected in R2 upload pipeline before write
  - Owner can toggle only if Plan.AllowBrandingRemoval == true
    → validated in UpdateStudioBrandingCommand handler, not in the endpoint
```

### Public Portfolio Slugs

```
Studio.Slug  string  unique, DB index, generated from Studio.Name on creation
Artist.Slug  string  unique, DB index, generated from Artist.DisplayName on creation

Slug rules:
  - lowercase, hyphens only, max 60 chars
  - Generated: "studio-name" → "studio-name-2" if collision
  - Editable by owner once after creation (ArtistAndAbove for artist slug)
  - Stored as-is; never auto-regenerated after first save

Public endpoints (no auth, no tenant filter):
  GET /api/v1/public/studios/{slug}   → PublicStudioResponse
  GET /api/v1/public/artists/{slug}   → PublicArtistResponse
  Both use IgnoreQueryFilters() — documented here as the second approved usage.
```

### Referral Code System

```
ReferralCode
  ReferralCodeId  Guid
  StudioId        Guid   (the referring studio)
  Code            string (8-char uppercase, unique)
  CreatedAt       DateTime
  ExpiresAt       DateTime (nullable — issuer can set expiry)
  IsActive        bool

ReferralRedemption
  ReferralRedemptionId  Guid
  ReferralCodeId        Guid
  NewStudioId           Guid  (the studio that signed up with this code)
  RedeemedAt            DateTime
  DiscountApplied       bool

Flow:
  1. Owner calls GenerateReferralCodeCommand → creates ReferralCode, returns Code
  2. New studio signs up with ?ref=CODE in registration URL
  3. CreateStudioCommand checks for valid ReferralCode, stores in session/temp
  4. On first CreateSubscriptionCommand: applies Stripe Billing coupon
     (one free month, created programmatically via Stripe API)
  5. ReferralRedemption record written, ReferralCode.IsActive may be set false
     if single-use (issuer config)
```

### Client Portable Profiles (Cross-Tenant)

```
This is the ONLY second approved use of IgnoreQueryFilters() in the codebase.

IPortableProfileService (Domain/Interfaces/)
  Task<ClientProfile?> FindByUserIdAsync(Guid userId, CancellationToken ct)
  Task<IReadOnlyList<TattooRecord>> GetHistoryAsync(Guid userId, CancellationToken ct)

Implementation in Infrastructure MUST:
  1. Call _db.ClientProfiles.IgnoreQueryFilters()
  2. Filter by ClientProfile.UserId (not TenantId)
  3. Require opt-in: ClientProfile.AllowCrossTenantRead == true
  4. Return only non-sensitive fields (no payment history, no consent form data)

This service is ONLY injectable in handlers where the command comes from
the client themselves (ClientAndAbove) or IssuerOnly queries.
Never inject it in owner/artist handlers.
```

### Design Share Token

```
DesignShareToken
  DesignShareTokenId  Guid
  Token               string  (Guid.NewGuid().ToString("N") — opaque, 32 chars)
  DesignRevisionId    Guid
  StudioId            Guid    (for quick tenant lookup without filter bypass)
  CreatedByUserId     Guid
  ExpiresAt           DateTime (default: now + 30 days)
  IsRevoked           bool    (owner/artist can revoke)
  ViewCount           int     (informational)

Public endpoint — no auth:
  GET /api/v1/public/designs/share/{token}
  → validates token, checks ExpiresAt and IsRevoked, returns signed R2 URL (short TTL)
  → increments ViewCount
  → never returns studioId or artistId in the response — only image URL + design title
```

### QR Code Generator

```
New NuGet dependency: QRCoder (pre-approved — see Decisions Log)

Endpoint (no auth — public download):
  GET /api/v1/studios/{studioId}/qr?format=png|svg
  Returns QR code pointing to: https://penaearte.com/s/{studio.Slug}
  Content-Type: image/png or image/svg+xml

Frontend:
  Owner settings page shows QR preview + download button
  No Redux slice needed — plain RTK Query endpoint
```

### Industry Analytics Reports

```
Hangfire job: IndustryReportJob (runs first day of each month, issuer-scoped)
  - Queries aggregate data with IgnoreQueryFilters() (third approved usage)
  - Output: anonymized JSON — no studio names, no user IDs
  - Metrics: avg appointments/month, peak booking hours, top session durations,
    platform-wide retention rate, trial→paid conversion rate
  - Written to Cloudflare R2: reports/industry/{year}-{month}.json
  - Issuer dashboard endpoint: GET /api/v1/platform/reports/industry (IssuerOnly)
    returns list of available report months + signed R2 URLs

PII rules:
  - No studio names, no artist names, no client data whatsoever
  - Aggregate only — minimum cohort size of 10 studios before a metric is shown
```

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
| Platform branding | `ShowPlatformBranding` bool on `Studio` (default `true`) | Drives viral growth; removal is a paid upgrade — free tier always shows badge |
| Branding gate | `Subscription.Plan.AllowBrandingRemoval` bool on `Plan` | Decouples plan logic from branding logic; issuer controls which plans unlock removal |
| Public portfolio SEO | Slug-based URLs `/s/{slug}` and `/artist/{slug}` | Human-readable, indexable, studio-owned vanity URLs |
| Portfolio slug | `Studio.Slug` and `Artist.Slug` (unique, lowercase, URL-safe) | Generated on creation, editable by owner once — no collisions enforced by DB unique index |
| Referral codes | `ReferralCode` entity + Stripe Billing coupon | Owners refer other studios; reward is a discount month — Stripe coupon applied at subscription creation |
| Client portable profiles | Cross-tenant read via `IgnoreQueryFilters()` in dedicated `IPortableProfileService` | Only called with explicit `clientId` after the client opts in; never exposed through normal tenant-scoped queries |
| Design share tokens | Short-lived JWT-like opaque token (Guid), stored in `DesignShareToken` table | Revocable, no auth required to view, expiry enforced at query time |
| QR code library | `QRCoder` (NuGet) | Pure .NET, no native deps, zero weight — only pre-approved external lib addition for self-promotion module |
| Industry reports | Issuer-only Hangfire monthly job writing anonymized JSON to R2 | No PII, no per-studio identifiers in output — aggregate only |
