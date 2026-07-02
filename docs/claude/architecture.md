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

## Payment Architecture — Card & Cash Only

> **Stripe Connect is not available in the platform's country.**
> The platform uses the aggregator model for card payments, and records cash
> payments manually. There is no PayPal, no Connect, no third-party payout service.

```
CLIENT DEPOSITS (client pays studio, at booking)
  Card  → Stripe Payment Element (platform aggregator Stripe account, manual capture)
           Client pays → Captured (held) → Paid (captured at session end)
  Cash  → Client declares intent (CashPending) → Owner/artist confirms receipt (Paid)

PLATFORM SUBSCRIPTIONS (owner pays platform, SaaS access)
  Card  → Stripe Billing (unchanged, platform Stripe account)
  Cash  → Owner contacts issuer out-of-band;
           Issuer calls ActivateSubscriptionManuallyCommand
```

**Key rule:** `IStripePaymentService` must NEVER pass `RequestOptions { StripeAccount = ... }`.
Every PaymentIntent goes to the platform account. This is enforced by the interface — the
`connectedAccountId` parameter does not exist.

**`StripeConnectService`** is marked `[Obsolete]`. Do not call it. Do not re-introduce it.

**Cash flow:**
- `DeclareCashDepositCommand` — called by client at booking; creates `Payment` with
  `Method = Cash`, `Status = CashPending`.
- `ConfirmCashDepositCommand` — called by artist or owner when cash is physically received;
  sets `Status = Paid`, mirrors `DepositStatus.Paid` on the `Appointment`.
- `ActivateSubscriptionManuallyCommand` — IssuerOnly; activates a studio subscription
  after a cash subscription payment is confirmed out-of-band.

---

## Feature Module Map

Maps each product feature to its domain entities, infrastructure dependencies, and ownership layer.

| # | Feature | Domain Entities | Infrastructure | Scope |
|---|---|---|---|---|
| 01 | Appointment Booking + Deposits | `Appointment`, `DepositRule` | Stripe (aggregator), Hangfire | Per-tenant |
| 02 | Consultation & Consent Forms | `IntakeForm`, `ConsentForm` | Cloudflare R2 (PDF storage) | Per-tenant |
| 03 | Design Approval Workflow | `DesignRevision`, `DesignApproval` | Cloudflare R2 (images), SignalR | Per-tenant |
| 04 | Client Profile & Tattoo History | `ClientProfile`, `TattooRecord`, `BodyMap` (value object) | Cloudflare R2 (photos) | Per-tenant |
| 05 | Payments & Session Splits | `Payment`, `SessionSplit` | Stripe (aggregator, card) + Cash (manual) | Per-tenant |
| 06 | Automated Communication | `NotificationLog` | Hangfire + Twilio + MailKit | Per-tenant |
| 07 | Studio Map | No entity (reads `Studio.Latitude/Longitude`) | None — public endpoint, no auth | Platform-wide |
| 08 | Platform Subscriptions | `Subscription`, `Plan` | Stripe Billing (separate from Connect) | Issuer-level |
| 09 | Platform Branding Flag | `Studio.ShowPlatformBranding` (bool, default `true`) | None | Per-tenant |
| 10 | Public Portfolio Pages | Reads `Studio`, `Artist`, `PortfolioImage` (read-only, no tenant filter) | None — public SEO endpoints | Platform-wide |

#### StudioPortfolioPage (`/s/{slug}`)

```
Component:   public/components/StudioPortfolioPage.tsx
Auth:        AllowAnonymous. No auth required.
Layout:      Two-column desktop: lg:grid-cols-[1fr_300px] — left column (hero, info, artists,
             gallery, reviews) + sticky right sidebar (CTA, phone, Instagram, city).
Hero:        CoverImageUrl or initials monogram, h-72. Gradient overlay shows studio name in h1.
Gallery:     Aggregated from artists' PortfolioImages (max 3 per artist, max 9 total,
             round-robin). Lightbox via shadcn Dialog (no extra package).
Artist cards: Enriched with avatar (ProfileImageUrl or initials monogram), primary specialty,
             per-artist rating from PublicArtistSummary. ChevronRight affordance + aria-label.
Dedup:       DistinctBy(a => a.Id) in GetPublicStudioHandler — guards against bad data.
Contact:     PhoneNumber, InstagramHandle added by AddStudioContactInfo migration.
Reviews:     Studio-level aggregate (AverageRating, ReviewCount) displayed under name.
             Per-artist aggregate shown on each artist card.
Back nav:    "Browse studios" → /discover, min-h-[44px] touch target.
CTA:         bg-violet-600 filled button. Unauthenticated → /login?redirect=/book?studio={slug}.
```

#### ArtistPortfolioPage (`/a/{slug}`)

```
Component:   public/components/ArtistPortfolioPage.tsx
Auth:        AllowAnonymous. ClaimsPrincipal injected by ASP.NET Core for IsOwnProfile.
Layout:      Two-column desktop: lg:grid-cols-[340px_1fr] — sticky left panel (avatar, bio,
             specializations, rate, booking CTA) + right column (portfolio masonry, reviews).
Masonry:     CSS columns (no package). Lightbox via shadcn Dialog.
Avatar:      Artist.ProfileImageUrl (DB column added by AddArtistProfileImageUrl migration);
             falls back to initials monogram when null.
New fields:  ProfileImageUrl, Specializations, HourlyRate, AverageRating, ReviewCount,
             IsOwnProfile — all projected in GetPublicArtistQuery.
Portfolio:   portfolioImages → List<ArtistPortfolioImage> (imageId + imageUrl).
             Formerly Artist.PortfolioImages JSON column — replaced by PortfolioImage entity.
             Lightbox per image; right panel shows ReviewSection with target="tattoo".
View tracking: POST /api/v1/public/artists/{slug}/view (fire-and-forget Redis counter).
Instagram:   InstagramHandle added by overnight-prompt-instagram-sync (not yet in contract —
             add to PublicArtistResponse after that migration runs).
```
| 11 | Referral Code System | `ReferralCode`, `ReferralRedemption` | Stripe Billing discount coupon | Issuer-level |
| 12 | Client Portable Profiles | `ClientProfile` cross-tenant read (opt-in) | `IgnoreQueryFilters` — issuer-scoped only | Cross-tenant (issuer) |
| 13 | Design Share Token | `DesignShareToken` | Cloudflare R2, public time-limited endpoint | Per-tenant |
| 14 | Studio QR Code Generator | No new entity (reads `Studio.Slug`) | QRCoder NuGet (pre-approved, see Decisions Log) | Per-tenant |
| 15 | Industry Analytics Reports | No entity (aggregate reads, issuer-scoped) | Hangfire monthly job, Cloudflare R2 (report storage) | Issuer-level |
| 16 | Booking Confirmation Branding | Reuses `Studio.ShowPlatformBranding` (#09) | MailKit templates, R2 PDF footer | Per-tenant |
| 17 | Platform Statistics API | No entity (aggregate reads, issuer-scoped) | `IgnoreQueryFilters()` — 4th approved usage | Issuer-level |
| 18 | Subscription Oversight + Trial Extension | `Studio.TrialExpiresAt`, `Subscription.Status` | `IgnoreQueryFilters()` — 5th approved usage | Issuer-level |
| 19 | Platform Referral Code Management | `ReferralCode`, `ReferralRedemption` | `IgnoreQueryFilters()` — 6th approved usage | Issuer-level |
| 20 | Issuer Dashboard Page | No entity (reads features 17–19) | `platformApi` RTK Query slice | Issuer-level |
| 21 | Bookmark / Saved Images | `SavedPortfolioImage` (cross-tenant, user-scoped, no TenantEntity) | `savedImagesApi` RTK Query slice (separate base URL `/api/v1/`) | Per-user, cross-tenant |

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

### Stripe Billing vs Stripe Connect — important distinction

- Stripe Billing       = platform charges studios for SaaS access (subscriptions) — **ACTIVE**
- Stripe (aggregator)  = platform collects client card deposits into its own account — **ACTIVE**
- Stripe Connect       = NOT USED — not available in the platform's country

Studio payouts are NOT handled by the platform. Client pays studio (deposit) via card or cash.
See "Payment Architecture — Card & Cash Only" section for the full picture.

---

## Studio Map

Public endpoint — no authentication required, no tenant filter.
Returns only published/active studios.

### IsActive vs IsPublished — intentional design decision

The SP-02 spec referred to an `IsPublished` boolean on `Studio`. No such
field exists or is planned. The public portfolio endpoints (`GetPublicStudioQuery`,
`GetPublicArtistQuery`) and the studio map endpoint filter on `Studio.IsActive`
instead.

This is **intentional**: `IsActive` already covers the intended behaviour —
deactivated studios (suspended, manually disabled by issuer) do not appear in
public-facing endpoints. A separate `IsPublished` field would add complexity
without adding expressive power given the current subscription and trial model.

If a future feature requires a studio to be active but unlisted (e.g. soft-launch
mode), add `IsPublished bool` to `Studio` at that time and update this section.
Until then, do not add `IsPublished` to the entity or the EF Core config.

```
GET /api/studios/map
Response: [{ studioId, name, slug, latitude, longitude, city }]

Domain: Studio entity gains Latitude (double), Longitude (double), City (string)
API: endpoint uses AllowAnonymous — exempt from RequireAuthorization rule
Frontend: features/map/ — read-only, no Redux slice needed, plain RTK Query
```

---

## DiscoverPage

```
/discover           DiscoverPage  public/components/DiscoverPage.tsx
                                  No auth required. Uses navigator.geolocation (browser API — useEffect ok).
                                  Nominatim reverse-geocode on geo success to show "Near [City, Country]".
                                  Nominatim forward-geocode in event handler for manual city search.
                                  Two tabs: Portfolio (default) and Studios.

Portfolio tab:      PortfolioFeed  public/components/PortfolioFeed.tsx
                                  API: GET /api/v1/public/portfolio/feed?radiusKm&page[&lat&lng][&style]
                                  Handler: GetPortfolioFeedQuery (Application/Public/Queries/)
                                  No auth. Approved AllowAnonymous exception — public discovery.
                                  Scoring: Bayesian avg rating + log10(views+1)*0.5
                                  View counts: Redis, key = portfolio:views:{artistId}
                                  All images per artist; ordered by artist Bayesian rank (no 3-per-artist cap).
                                  Pagination: page/pageSize; component-level allImages state accumulation (NOT RTK merge).
                                  Style filter: StyleChips component (9 chips incl. "All"); resets page on change.
                                  Masonry: JS round-robin distributeToColumns<T> + useColumnCount (1/2/3 cols responsive).
                                  Attribution strip: always-visible translucent overlay (artist, studio, rating).
                                  Bookmark button: hover/focus visible when authenticated; savedImagesApi for toggle.
                                  "Near me" toggle filters feed to the user's radius.
                                  Tiles are buttons (open lightbox); lightbox shows image + style badge + per-image ReviewSection.
                                  PortfolioImageResponse includes imageId, style, imageAverageRating, imageReviewCount.

Artist View Counter  POST /api/v1/public/artists/{slug}/view
                                  No auth. Fires from ArtistPortfolioPage on mount.
                                  Redis INCR only — no DB write, no MediatR.
                                  Approved: non-domain, non-PII write.

Studios tab:        API: GET /api/v1/public/studios/nearby?lat&lng&radiusKm
                                  NearbyStudioResponse includes AverageRating + ReviewCount (from Reviews table,
                                  no query filter — computed in GetNearbyStudiosQuery handler).
                                  Query is skipped unless Studios tab is active.
```

---

## EmbedPage

```
/embed/:studioSlug  EmbedPage     Booking widget for embedding via <iframe> on studio
                                  websites. Served from VITE_PUBLIC_URL domain. Uses
                                  AllowAnonymous. No auth, no Redux — reads public studio
                                  data only. Generated snippet lives in EmbedCodeCard.tsx.
```

When `EmbedPage` runs inside an iframe on a third-party site, `window.location.origin`
is the admin app's origin, not the public marketing site. The studio page URL is therefore
built using `VITE_PUBLIC_URL` (env var) with `window.location.origin` as a fallback.

---

## IgnoreQueryFilters() Approved Usages

This table is the canonical record of every approved `IgnoreQueryFilters()` call.
Never add a new one without updating this table and the Decisions Log.

| # | Location | Purpose | Who calls it |
|---|---|---|---|
| 1 | `IPortableProfileService` impl | Cross-tenant client profile read (opt-in only) | Client themselves or IssuerOnly |
| 2 | Public portfolio handlers (`GetPublicStudioQuery`, `GetPublicArtistQuery`) | SEO public endpoints, no tenant scope | Anonymous |
| 3 | `IndustryReportJob` | Monthly aggregate report generation, no PII | Hangfire job (issuer-scoped) |
| 4 | `GetPlatformStatsHandler` | Platform KPI aggregate (total studios, MRR, conversion) | IssuerOnly |
| 5 | `GetPlatformSubscriptionsHandler`, `ExtendTrialHandler` | All subscriptions cross-tenant; trial extension | IssuerOnly |
| 6 | `GetPlatformReferralCodesHandler`, `DeactivateReferralCodeHandler` | All referral codes cross-tenant | IssuerOnly |
| 7  | `CancelSubscriptionHandler`             | Subscription cancellation cross-tenant                           | IssuerOnly |
| 8  | `GetStudioByIdHandler`                  | Cross-tenant single-studio read for admin detail page            | IssuerOnly |
| 9  | `IssuerGenerateReferralCodeHandler`     | Cross-tenant studio lookup + referral code generation for issuer | IssuerOnly |
| 10 | `ReactivateReferralCodeHandler`         | Cross-tenant referral code reactivation                          | IssuerOnly |
| 11 | `DeleteReferralCodeHandler`             | Cross-tenant referral code deletion (unredeemed only)            | IssuerOnly |
| 12 | `GetPortfolioFeedHandler` (Artists)     | Cross-tenant artist portfolio discovery; public feed             | Anonymous  |
| 12 | `GetPortfolioFeedHandler` (Studios)     | Cross-tenant studio name/slug lookup for portfolio response      | Anonymous  |
| 13 | `RecordArtistView` endpoint             | Cross-tenant artist slug lookup for Redis view counter           | Anonymous  |
| 14 | `GetPortfolioImageReviewsHandler`       | Cross-tenant public portfolio image review lookup                | Anonymous  |
| 15 | `CreatePortfolioImageReviewHandler`     | Cross-tenant portfolio image lookup for review creation          | Authenticated (any role) |
| 16 | `SavePortfolioImageHandler`             | Cross-tenant portfolio image existence check before saving       | ClientAndAbove |
| 17 | `GetSavedPortfolioImagesHandler` (images) | Cross-tenant portfolio images + artist join for saved-images list | ClientAndAbove |
| 18 | `GetSavedPortfolioImagesHandler` (studios) | Cross-tenant studio name lookup for saved-images response projection | ClientAndAbove |
| 19 | `GetArtistReviewsQuery` (Appointments + Clients) | `IsVerifiedBooking` check — completed appointments with this artist cross-tenant | Anonymous |
| 20 | `GetStudioReviewsQuery` (Appointments + Clients) | `IsVerifiedBooking` check — completed appointments at this studio cross-tenant | Anonymous |
| 21 | `GetPortfolioImageReviewsQuery` (Appointments + Clients) | `IsVerifiedBooking` check — completed appointments at the image's studio cross-tenant | Anonymous |

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
| `GET /api/v1/public/portfolio/feed` | Public discovery portfolio feed | None — read-only public images, no PII |
| `POST /api/v1/public/artists/{slug}/view` | Anonymous view counter for feed ranking | None — write-only to Redis, non-domain data |
| `GET /api/v1/public/portfolio/{imageId}/reviews` | Public per-image review list | None — read-only, non-sensitive review content only |
"No JWT auth" does not mean "unprotected" for webhook endpoints — the Stripe-Signature
validation is the security mechanism. Always validate it before processing the event.
Never add new AllowAnonymous endpoints without adding a row to this table.

---

## PortfolioImage Entity — Decision Log

### Why a dedicated table instead of a JSON column

`Artist.PortfolioImages` was originally stored as `List<string>` (JSON column).
This was replaced by a `PortfolioImage` entity for the following reasons:

1. **Reviewable target** — `Review.PortfolioImageId` FK requires a real row to reference.
   JSON column entries have no identity and cannot be FK-targeted.
2. **Independent metadata** — `imageAverageRating` and `imageReviewCount` are aggregated
   per image. These require a stable identity to group against.
3. **Queryability** — `GetPortfolioFeedQuery` queries all images across all tenants.
   A proper table allows indexed `ArtistId`, `StudioId` lookups. JSON column requires
   full-scan JSON_TABLE in MySQL.

### Entity shape

```csharp
PortfolioImage : TenantEntity  // StudioId, CreatedAt, UpdatedAt, DeletedAt
  Id (Guid, PK)
  ArtistId   (Guid, FK → Artist.Id)
  ImageUrl   (string, max 2048)
  // StudioId inherited from TenantEntity — enables global query filter
```

### Review target extension

`Review` gained a nullable `PortfolioImageId (Guid?)` FK.
- Studio review:  `StudioId != null, ArtistId == null, PortfolioImageId == null`
- Artist review:  `ArtistId != null, StudioId == null, PortfolioImageId == null`
- Tattoo review:  `PortfolioImageId != null, StudioId == null, ArtistId == null`

Duplicate guard: one review per `(AuthorUserId, PortfolioImageId)` pair
(same constraint as per `(AuthorUserId, ArtistId)` and per `(AuthorUserId, StudioId)`).

### Frontend shape change

`PublicArtistResponse.portfolioImages` changed from `string[]` to
`ArtistPortfolioImage[]` (`{ imageId: string; imageUrl: string }`).
Any component or test consuming this field must use `.imageUrl` not `[index]` directly.

### Migration notes

`AddPortfolioImageEntity` (20260627220204) migrates existing JSON data to rows:
```sql
INSERT INTO PortfolioImages (Id, StudioId, ArtistId, ImageUrl, CreatedAt, UpdatedAt)
SELECT UUID(), a.StudioId, a.Id, img.value, UTC_TIMESTAMP(), UTC_TIMESTAMP()
FROM artists a
CROSS JOIN JSON_TABLE(COALESCE(a.PortfolioImages, '[]'), '$[*]' COLUMNS (value VARCHAR(2048) PATH '$')) AS img
WHERE a.PortfolioImages IS NOT NULL AND JSON_LENGTH(a.PortfolioImages) > 0;
```
Then drops `artists.PortfolioImages` column.

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

## Artist QA Pass — 2026-07-01

Bug-hunt + polish pass over the artist role, per `docs/claude/overnight-prompt-artist-qa-polish-2026-07-01.md`.
Backend: 1203/1203 tests green. Frontend: 1232/1232 green (two pre-existing flaky
tests under full-parallel load — `StudioProfilePage.test.tsx`, `BookPage.test.tsx`
— both pass in isolation and are unrelated to this pass).

### Bugs found and fixed

**Backend — artist-scope / authorization gaps (all allowed one artist to act on
another artist's data by guessing a GUID, since only tenant scope was enforced):**

- `DeleteArtistTimeOffCommand.cs` → no ownership check at all → added artist-owns-artist check
- `GetAppointmentsQuery.cs` → returned every studio appointment to every caller,
  including artists (colleague's schedule leak) → artist role now auto-scoped to
  their own `ArtistId`; added owner-only `artistId` filter param
- `GetDesignsQuery.cs` → artist calling with no filter (the normal case — `DesignListPage`
  never sent one) saw every studio design → artist role now auto-scoped
- `CreateDesignCommand.cs` → trusted the client-supplied `artistId`, letting an artist
  assign a new design to a colleague → now always overridden with the caller's own artist id
- `UploadDesignRevisionCommand.cs` → no check that the design belongs to the calling artist
- `DeleteDesignRevisionCommand.cs` → no check at all
- `CreateDesignShareTokenCommand.cs` → no artist-scope check; also created a new token
  on every call even when a valid one already existed (duplicate live share links) →
  added scope check + de-dup (reuses existing non-expired, non-revoked token)
- `RevokeDesignShareTokenCommand.cs` → no artist-scope check
- `ConfirmCashDepositCommand.cs` → no artist-scope check — any artist could confirm
  cash for any appointment in the tenant, not just their own
- `GetNotificationsQuery.cs` → artist calling with no filter (both `NotificationBell`
  and `NotificationLogListPage` always call this way) saw every tenant notification →
  artist role now auto-scoped to `RecipientType.Artist` + own artist id
- Finished pre-existing, uncommitted partial work on `UpdateArtistCommand.cs` /
  `UpsertArtistScheduleCommand.cs` / `AddArtistTimeOffCommand.cs` ownership checks
  (found mid-flight at session start) — fixed 3 unit tests broken by the incomplete
  change and added missing artist-scope regression tests for all of the above

**Frontend:**

- `ArtistDetailPage.tsx` → Edit button was gated on `canManage` (owner-only), so an
  artist could never edit their own profile → now `(canManage || isOwnProfile)`;
  Delete stays owner-only
- `ArtistDetailPage.tsx` Schedule tab → fetched *all* studio appointments client-side
  and filtered by `artistId === id` (performance + minor exposure issue) → now passes
  `artistId` server-side only for owners; artists rely on the backend auto-scope
- `DesignDetailPage.tsx` → never fetched the design itself at all — no title, no client
  name, no status anywhere on the page, only the revision list. There was no
  `GET /api/v1/designs/{id}` endpoint in the backend to fetch it from. Added
  `GetDesignQuery` + endpoint, wired the page to show title/client/status header
  and a "changes requested" callout banner

### Polish implemented (Phase 2, scoped to the highest-value items — see Skipped below)

- **P1.1** Document titles (`useDocumentMeta`) added to Schedule, Clients, Designs,
  Design detail (dynamic), Intake Forms, Consent Forms, Deposit Rules, Notifications,
  Artist detail (dynamic), Appointment detail
- **P1.2** Mobile nav overflow on `ArtistLayout` (`overflow-x-auto scrollbar-none shrink min-w-0`)
- **P2.2** Client name now shown on `AppointmentCard` (new `AppointmentResponse.ClientName`,
  joined server-side in `GetAppointmentsQuery`/`GetAppointmentQuery` only — not on
  mutation-returning handlers)
- **P2.3** Status colour-coding (left border) on `AppointmentCard`
- **P4.1** `DesignListPage` sorts `ChangesRequested` designs first; new `DesignStatusBadge`
  shown on `DesignCard` and `DesignDetailPage`; amber callout banner on
  `DesignDetailPage` when status is `ChangesRequested`
- **P3.5** "View public profile" link on an artist's own profile (when a slug exists)
- **P5.1** "View {client}'s profile" link on `AppointmentDetailPage`

### Decisions made

| Decision | Choice | Reason |
|---|---|---|
| Design status | Computed at query time from the latest `DesignRevision.Approval.Status`, not a stored column | No migration needed; `Expired` approvals are treated the same as `ChangesRequested` (artist needs to re-upload either way) |
| `GetDesignQuery` (`GET /api/v1/designs/{id}`) | New query, tenant-scoped only (no artist restriction) | Matches the existing read-permissive convention already used by `GetDesignRevisionsQuery` — reads are open to all `ClientAndAbove` roles within the tenant, only mutations are scope-restricted |
| `AppointmentResponse.ClientName` | Optional trailing field, populated only by `GetAppointmentsQuery`/`GetAppointmentQuery` | Adding it to every command handler that returns an `AppointmentResponse` (Confirm/Cancel/Complete/etc.) would require an `Include(a => a.Client)` on each; scoped to the two read paths that actually need it for now |
| Design review (`ReviewDesignCommand`) artist/owner access | Left unchanged — no additional ownership check added | Consistent with the existing `FindClientForUserAsync` convention elsewhere in the app: the check only applies when `currentUser.Role == "client"`; staff (artist/owner) are trusted to act within their own tenant on `ClientAndAbove` endpoints. Restricting this would be an inconsistent, one-off deviation from that pattern |

### Skipped / deferred (with reason)

- **P2.1** "Book appointment from schedule" — a genuinely new feature (extracting
  `BookAppointmentForm` into a shared component + wiring an artist-facing creation
  flow), too large for this pass
- **P2.4** "Next up" indicator on today's schedule — lower value than the security
  fixes; time was reallocated
- **P3.1** Working-hours / time-off editing UI on the artist's own profile — the
  backend already fully supports this and is now correctly artist-scoped
  (`GetArtistScheduleQuery` / `UpsertArtistScheduleCommand` / `AddArtistTimeOffCommand`),
  but the editing UI itself is a sizeable new component that wasn't built this pass
- **P3.2 / P3.3 / P3.4** Bio field, avatar upload, Instagram handle on the artist's
  own profile — none of these exist in `UpdateArtistRequest` or the `Artist` entity
  today; would need contract + entity + migration changes, deferred
- **P4.2** Share-link QR code — needs a new endpoint plus `QRCoder` usage, deferred
- **P5.2 / P5.3** `PortableProfileToggle` help text, tattoo-history gallery/lightbox
  on `ClientDetailPage` — not touched this pass
- **P6.1** Notification deep-linking to the source entity — `NotificationLog` has no
  `Type`/entity-reference field at all; adding one touches every Hangfire
  notification-sending command in the app, too large for this pass
- **P6.2** Per-user notification preferences — the real data model
  (`StudioNotificationPreference`) is studio-wide by design, not per-user as the
  prompt assumed. This is intentional architecture, not a bug — no change made
- **P7.1–P7.5** Global toast/confirmation/spinner/error-retry/accessibility audit —
  spot-checked during the Layer B/C review and found already compliant in most
  flows (the existing test suite already asserts these); a full line-by-line audit
  of every artist-accessible button was not performed
- Reschedule feature — the backend endpoint (`RescheduleAppointmentCommand`) exists
  but there is no frontend UI for it at all, for either role. Pre-existing gap, not
  built this pass
- `ArtistListPage` "this is you" indicator on the artist's own row — not implemented

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
| Platform stats endpoint | Single `GET /api/v1/platform/stats` aggregate query, no new entity | Simpler than materialised views; acceptable latency at current scale |
| Subscription oversight | `GET /api/v1/platform/subscriptions` + `PATCH .../trial` + `PATCH .../cancel` | Issuer must see all tenants' subscription health in one view; `IgnoreQueryFilters()` approved (usages #5, #7) |
| Platform referral management | Issuer deactivation of any studio's referral code | Prevents abuse of referral system at the platform level without giving owners cross-tenant access |
| Trial extension | `PATCH /api/v1/platform/subscriptions/{studioId}/trial` (issuer only, max 90 days) | Common sales/support action; capped at 90 days to prevent abuse |
| Issuer dashboard routing | Issuer role routes to `/platform` (not `/dashboard`) with its own `IssuerLayout` | Dashboard is owner-specific; issuer needs platform-admin home screen with KPI widgets |
| `AllowBrandingRemoval` on UpdatePlan | Exposed via `UpdatePlanRequest.AllowBrandingRemoval` (bool) | Issuer needs to control which plans unlock branding removal; was missing from update contract |
| Duplicate plan routes | Deleted `IssuerEndpoints.cs`; canonical plan CRUD is under `/api/v1/billing/plans` | Eliminated duplicate route registration; frontend always used billing path |
| `platformApi` RTK Query slice | New `features/platform/platformApi.ts` for all issuer platform endpoints | Keeps issuer platform concerns isolated from billing/studio slices |
| Payment model: aggregator vs marketplace | Aggregator (platform's own Stripe account collects all card payments) | Stripe Connect not available in platform country; aggregator avoids connected accounts entirely |
| Client payment methods | Card (Stripe Payment Element) + Cash (manual) — no PayPal | Simplest model matching actual studio workflow; studios often accept cash deposits in person |
| Cash payment flow | `DeclareCashDepositCommand` (client) → `ConfirmCashDepositCommand` (owner/artist) | Two-step prevents fraud; owner must physically confirm before status changes to Paid |
| Cash subscription activation | `ActivateSubscriptionManuallyCommand` (IssuerOnly) | Issuer confirms cash payment out-of-band then activates in-platform; rare but necessary |
| Studio payouts | Not handled by the platform — out of scope | Platform collects deposit; studio-to-artist split is an internal business matter |
| Stripe keys in config | `Stripe:PublishableKey`, `Stripe:SecretKey` in `appsettings.Development.json` (gitignored) | Never in source; env vars in production |
| `ClientPaymentMethod` enum | `Card` \| `Cash` (removed `Stripe`, removed `PayPal`) | Matches the two accepted payment methods; `Card` is technology-agnostic (Stripe is the impl) |
| `PaymentStatus.CashPending` | Added to `PaymentStatus` enum | Represents the window between client's cash declaration and owner's confirmation |
| `IsPublished` vs `IsActive` on `Studio` | Use `IsActive` only | `IsPublished` was in the SP-02 spec but never implemented. `IsActive` covers the same use case. Adding a second flag would create redundant state. |
| Portfolio feed masonry layout | JS round-robin column distribution via `distributeToColumns<T>` + `useColumnCount` hook | CSS `columns` property causes visual reflow on append; round-robin JS pre-assigns items to columns deterministically, avoiding shifting and enabling proper `role="list"` semantics |
| Portfolio feed infinite scroll | Component-level `allImages` state + `useEffect` page accumulation (NOT RTK Query `merge`/`serializeQueryArgs`) | RTK Query `merge` is tricky with style filter resets; component-level state gives full control over resets on style/location change |
| Portfolio style filter | `PortfolioImage.Style` string? + `TattooStyle` constants class; filter chip sends `style=` query param | Constants class shared between backend validation and frontend chips; string type avoids DB enum migration on every new style |
| Saved images entity | `SavedPortfolioImage` is NOT a `TenantEntity` — intentional | Saved images are user-scoped and cross-tenant by design; a user can save images from any studio. Adding a tenant FK would break cross-tenant discovery. |
| Saved images API slice | `savedImagesApi` — separate RTK Query slice with `baseUrl: "/api/v1/"` | `publicApi` uses `baseUrl: "/api/v1/public/"` — saved-images endpoints live at `/api/v1/saved-images/`. Dual-base within one slice requires URL manipulation hacks; a dedicated slice is cleaner |
| Portfolio tile attribution strip | Always-visible translucent overlay below each image (artist name + studio + rating) | Hover-only overlays fail WCAG 2.1 criterion 1.4.13 (content on hover/focus); always-visible strip ensures attribution is always accessible |
| StarRating split into display + interactive | Separate `StarRating` (display) and `InteractiveStarRating` (write form) exports | Touch targets, hover preview, and live readout only needed on interactive variant; display-only component stays lightweight |
| ReviewSection order: list before form | Aggregate → reviews list → write form (form always last) | Industry trust pattern: users need to read existing reviews before writing one; form at bottom reduces form-before-content anti-pattern |
| IsVerifiedBooking on ReviewResponse | Computed at query time via Appointments join, not stored | No migration needed; verified status can change if booking is cancelled or added; `IgnoreQueryFilters` approved (entries 19-21) |
| Lightbox prev/next navigation | Index-based navigation through allImages array; keyboard arrows (←/→) also supported | Enables discovery across portfolio without closing/reopening lightbox; position indicator shows context |
| "Book with artist" CTA in lightbox | Primary violet Link to `/artist/:slug`; secondary "View artist profile" link | Closes lightbox on navigate; converts engaged viewers without requiring an extra click to find the booking CTA |
| `authSlice` remember-me storage split | `"local"` (remember-me) writes to `localStorage`; `"session"` (default) writes to `sessionStorage` | Session-scoped tokens never survive browser restart; cross-tab sync fires correctly from the right storage type |
| `CancelSubscriptionCommand` Stripe side-effect | Best-effort `CancelSubscriptionAsync` call after `db.SaveChangesAsync()` — errors logged, not rethrown | DB record is the source of truth; a Stripe timeout must not roll back an already-committed cancellation |
| `totalStudios` KPI | Counts ALL studios including suspended (`studios.Count`, not `active.Count`) | "Total Studios" = all tenants in the system; suspended count is tracked separately on `suspendedStudios` |
| Issuer nav overflow | `overflow-x-auto scrollbar-none shrink min-w-0` on `<nav>` in `IssuerLayout` | Mobile viewports have too little horizontal space for six nav items; overflow scrolls without visible scrollbar |
| Issuer studio list sort | Suspended → PastDue → GracePeriod → Trialing → Active → NoSubscription → Cancelled | Highest-attention items surface first; issuer should never have to scroll to find at-risk studios |
| `location.state.highlight` in studio list | Scroll-to + ring-2 ring on target row, fades after 1.8 s via `dimHighlight` state | Dashboard at-risk row "→" link sets `state.highlight = studioId`; studio list picks it up on mount |
| `GenerateReferralCodeForStudio` request body | Optional `GenerateReferralCodeForStudioRequest` body with `ExpiresAt?: DateTime` | Expiry dates allow issuer to issue time-boxed referral codes (e.g., conference promotion); null = no expiry |
| Issuer page document titles | All 7 issuer routes call `useDocumentMeta` with page-specific titles | Browser tabs and screen readers benefit from descriptive titles; all issuer pages now have unique titles |
| Per-page error boundaries in issuer routes | `ErrorBoundary` wraps each issuer route element in `router.tsx` | Root `ErrorBoundary` catches everything but shows a blank app; per-page wrapping preserves the layout and nav while showing error UI for a single page |
| Industry report trigger cooldown | 60-second `useEffect` countdown on trigger button (approved browser timer side-effect) | Prevents accidental double-triggering of an expensive Hangfire job |
