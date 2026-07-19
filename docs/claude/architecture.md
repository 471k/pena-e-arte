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

#### ArtistPortfolioPage (`/artist/{slug}`)

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
Instagram:   Full sync shipped (feat(api) commit f7e2962): OAuth connect
             (GET /api/v1/artists/{id}/instagram/connect-url, owner-only) + anonymous
             callback endpoint validates signed state; InstagramSyncJob (Infrastructure/Jobs)
             runs nightly, tenant-wide, refreshes tokens, upserts InstagramPost rows;
             per-post visibility toggle via PUT .../posts/{postId}/visibility; artist-side
             UI in features/artists/components/InstagramTab.tsx; public posts surfaced via
             GetPublicArtistInstagramPostsQuery on ArtistPortfolioPage.
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
| 22 | Google/Apple OAuth Sign-In | No new entity — Identity `IdentityUser` created passwordless via `CreateOAuthUserAsync` | `IOAuthTokenValidator` (JWKS via `IHttpClientFactory`, cached 1h in Redis); Google/Apple JS SDKs via CDN, no npm packages | Per-tenant (owner/client only) |
| 23 | Multi-Studio Client View | No new entity (`Studio` + Identity claims) | `IIdentityService.GetTenantIdsAsync` | Per-user, cross-tenant |
| 24 | Plan Usage Limits + Owner Visibility | No new entity (`Plan.Max*`, `Studio.StorageUsageBytes`) | `IPlanLimitService`/`PlanLimitService` (Redis-cached), `PlanLimitBehavior` (MediatR pipeline) | Per-tenant (enforcement/visibility), Issuer-level (validation report) |

```
OAuth Sign-In    Backend:  POST /api/v1/auth/oauth/login    (AllowAnonymous, rate-limited)
                           POST /api/v1/auth/oauth/register  (AllowAnonymous, rate-limited)
                           OAuthLoginCommand, RegisterOAuthUserCommand
                           IOAuthTokenValidator / OAuthTokenValidator
                           IIdentityService.LoginWithVerifiedEmailAsync
                           IIdentityService.CreateOAuthUserAsync
                 Frontend: OAuthButtons (shared component)
                           useGoogleSignIn, useAppleSignIn (shared hooks)
                           LoginPage — "Continue with Google/Apple"
                           RegisterStudioPage — OAuth path in step 2
                 Notes:    JS SDKs loaded from CDN in index.html. No npm packages.
                           JWKS fetched via IHttpClientFactory, cached in Redis 1h.
                           Backend validates ID token signature — frontend sends raw token.
                           Apple Sign In requires HTTPS even in development.
                 Security: RegisterOAuthUserHandler enforces the same OwnerEmail-match
                           check as RegisterUserHandler for role="owner" (guest-QA-pass
                           fix, 2026-07-02) — the original spec predated that fix and
                           would have reopened the owner-takeover vulnerability if
                           implemented as originally written. RegisterOAuthUserValidator
                           likewise restricts roles to client/owner only (no artist/issuer),
                           matching RegisterUserValidator.
```

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
| 22 | `ExchangeInstagramCodeHandler` (Artists) | Resolve artist's StudioId from an anonymous OAuth callback; artistId is pre-authenticated via `IInstagramStateSigner` HMAC before this handler runs | Anonymous (state-signed) |
| 23 | `GetPublicArtistInstagramPostsQuery` (Artists) | Cross-tenant artist slug lookup for public Instagram post feed | Anonymous |
| 24 | `GetIssuerStudioSummaryHandler` | Cross-tenant: studio + owner lookup, artist/client/appointment counts for a single studio | IssuerOnly |
| 25 | `GetPlanUsageReportHandler` | Cross-tenant: all studios + plans + artist/appointment/notification counts, for the issuer plan-usage validation report | IssuerOnly |
| 26 | `ReferralRewardService` | Cross-tenant: `ReferralRedemption`, `ReferralCode`, `Studio` (referrer + new), `Subscription` (referrer) — rewards the referring studio's Stripe subscription when their code converts a new paying studio | System (triggered from subscription-creation handlers, any tenant) |

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
| `GET /api/v1/public/artists/{slug}/instagram-posts` | Public synced Instagram feed for artist portfolio | None — read-only, only `IsVisible` posts, no PII |
| `GET /api/v1/instagram/callback` | Instagram OAuth redirect target, no JWT possible | Signed `state` param (HMAC-SHA256, `IInstagramStateSigner`) validated before trusting artistId |
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

## Owner QA Pass — 2026-07-02

Bug-hunt + polish pass over the owner role, per `docs/claude/overnight-prompt-owner-qa-polish-2026-07-01.md`.
Backend: 1220/1220 tests green. Frontend: 1239/1239 green (build clean, no flaky
failures observed this run).

### Bugs found and fixed

**Backend:**

- `CancelAppointmentCommand.cs` → the card-refund branch only checked
  `PaymentStatus.Captured`, never `Paid`. A very reachable path (Stripe auto-capture,
  `CreateDepositPaymentCommand`'s webhook-healing branch) leaves a payment `Paid`
  directly. Cancelling that appointment skipped the actual Stripe refund call but
  still force-set `Appointment.DepositStatus = Refunded` — the UI would claim a
  refund happened when the client's card was never credited. Fixed to refund on
  both `Captured` and `Paid`, and to only flip `DepositStatus` inside the branch
  where a refund action actually occurred (a `Pending`/never-charged card intent no
  longer gets mislabelled "Refunded")
- `DeleteArtistCommand.cs` → soft-deleted unconditionally, no check for upcoming
  non-terminal appointments → added a 409-equivalent `BusinessRuleViolationException`
- `UpsertArtistScheduleCommand.cs` → validator didn't reject duplicate `DayOfWeek`
  entries in one request; two entries for the same day silently produced two DB rows
  instead of the second overwriting the first
- `AddArtistTimeOffCommand.cs` → no overlap check against the artist's existing
  time-off periods before inserting
- Session splits had no read path at all: `PaymentResponse` never returned `Splits`,
  so `SessionSplitsEditor` always showed "No session splits defined" even
  immediately after a successful save. Added `PaymentResponse.Splits`, included
  `Payment.SessionSplits` in `GetPaymentByAppointmentQuery`, and had
  `UpdateSessionSplitsCommand` return the freshly-saved splits directly
- Both Stripe webhook handlers (`BillingEndpoints.cs`) let any exception thrown
  *after* signature verification propagate to `ExceptionMiddleware`, returning a
  non-200 status and causing Stripe to retry an event whose failure was our bug, not
  a transient one. Wrapped event processing in try/catch — log and still return 200
- `Studio.PhoneNumber` / `Studio.InstagramHandle` already existed as DB columns and
  were already exposed on the *public* studio response, but were missing from
  `GetMyStudioQuery`/`UpdateMyStudioCommand` entirely — the owner had no way to set
  either field. Added to both contracts; Instagram handle strips a leading `@`

**Frontend:**

- `DashboardPage.tsx` → both "Book Appointment" buttons (header and empty-state)
  navigated to `/appointments/new`, which doesn't exist and never did — fixed to
  `/schedule` (the actual gap — an owner-facing appointment-creation form — is a
  larger feature, tracked below, same as the artist pass's P2.1)
- `DashboardPage.tsx` → had its own sticky `<header>` stacked on top of
  `OwnerLayout`'s, producing a double header — converted to a plain non-sticky `<div>`
- `DashboardPage.tsx` → `formatTime` used the browser's default locale while
  `formatDate` used `en-GB`, giving inconsistent time formatting — standardized to `en-GB`
- `SetupChecklist.tsx` → the "Set artist working hours" step checked
  `(artist as { hasSchedule?: boolean }).hasSchedule`, a field that doesn't exist
  anywhere on `ArtistResponse` — always `undefined`, so this step could never
  complete and the checklist could never fully clear. No cheap way to check real
  per-artist schedule data client-side, so the step was removed rather than left
  permanently broken. The "Set a deposit rule" step also linked to a nonexistent
  `/settings/deposits` route — fixed to `/deposit-rules/new`
- `OwnerLayout.tsx` → same mobile-nav-overflow gap already fixed in `ArtistLayout`
  during the artist pass — added `overflow-x-auto scrollbar-none shrink min-w-0`
- `paymentsApi.ts` `confirmCashDeposit` → invalidated only its own `Payment` tag.
  `Appointment` lives in a separate RTK Query slice (`appointmentsApi`), which
  `invalidatesTags` cannot reach across — so appointment deposit-status badges never
  refreshed after confirming cash without a manual reload. Added an
  `onQueryStarted` that dispatches `appointmentsApi.util.invalidateTags(["Appointment"])`
  on success (no prior cross-slice-invalidation pattern existed in this codebase;
  this is the first one)
- `SessionSplitsEditor.tsx` → never received the payment's total amount at all, so
  it showed no running total, no over/under warning, and let Save fire with splits
  that didn't sum to the payment amount. Added a `paymentAmount` prop, a running
  total display, an inline warning, and gated `Save` on the total matching
- `PaymentListPage.tsx` → error state had no retry action, just static text

### Polish implemented

- **P1.1** Document titles added to `PaymentListPage`, `PaymentDetailPage`,
  `BillingPage`, `SubscribePage`, `StudioProfilePage`
- **P1.3** Every authenticated app route in `router.tsx` now wraps its element in
  `<ErrorBoundary>` (previously only the issuer `/platform` routes did)
- **P9.1 / P9.2** Instagram handle and phone number fields added to
  `StudioProfilePage`'s main form (backend fields already existed, just weren't wired)
- QR code section: added a "Download SVG" button — the backend already supported
  `?format=svg`, the frontend simply never called it (`useGetStudioQrCodeQuery` was
  hardcoded to `format: "png"`)

### Decisions made

| Decision | Choice | Reason |
|---|---|---|
| Cash/card refund eligibility | Refund on `PaymentStatus.Captured` **or** `Paid` | Both are reachable "money has left the client's card" states depending on capture timing; only `Pending`/`Failed`/already-`Refunded` have nothing to refund |
| Cross-slice RTK Query invalidation | `dispatch(otherApi.util.invalidateTags([...]))` inside `onQueryStarted` | `paymentsApi` and `appointmentsApi` are separate `createApi` instances; `invalidatesTags` only reaches tags within the same slice |
| `DeleteDepositRuleCommand` "in use" check | Not implemented — reviewed, not a bug | No `DepositRuleId` FK exists anywhere on `Appointment`; the deposit amount is snapshotted onto the appointment at booking time, so a rule has nothing referencing it after use and is always safely deletable |
| `GetSubscriptionQuery` 404-on-missing-subscription | Left as-is — reviewed, unreachable in practice | `RegisterStudioCommand` unconditionally creates a `Trialing` `Subscription` row at signup; every studio has one |
| Business-rule-violation status code (422 vs 409/403) | Left as `BusinessRuleViolationException` → 422 everywhere | Consistent codebase-wide convention (`ExceptionMiddleware`); several checklist items assumed 409/403 for specific cases, but changing only those would be an inconsistent one-off deviation |
| `SetupChecklist` "working hours" step | Removed rather than fixed | No backend field exists to cheaply check "does this artist have a schedule" from the studio-wide artist list; building one was out of scope for this pass |

### Skipped / deferred (with reason)

- Owner-facing appointment-creation UI (`SchedulePage` "+ New" / a real
  `/appointments/new`) — same large gap already deferred as P2.1 in the artist pass;
  fixed only the immediate 404 by redirecting to `/schedule`
- `PaymentDetailPage`'s inline cash-confirm UI duplicates `CashDepositConfirmButton`
  instead of reusing it (they now behave slightly differently — the shared component
  has a confirm step, the inline one doesn't) — cosmetic/DRY issue, not a functional
  bug, not fixed
- `PaymentListPage`'s status filter is derived only from statuses present in the
  currently-loaded (cursor-paginated) page rather than a fixed, server-known set —
  a studio whose first page is all one status gets no filter UI. Deferred; fixing
  properly needs either a server-side distinct-statuses endpoint or a hardcoded
  filter set regardless of what's loaded
- Full P7-equivalent global toast/confirmation/spinner/accessibility audit across
  every owner-accessible button — not exhaustively performed; spot-checks during
  Layer B/C found most flows already compliant

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
| Structured-log correlation fields | `RequestIdMiddleware` pushes `request_id` onto every log line via `LogContext`; `RequestLoggingEnrichment.Enrich` tags the per-request Serilog summary line with `request_id` always and `user_id`/`tenant_id` once authenticated | Closes the gap on CLAUDE.md's "logs must include tenant_id, user_id, request_id" rule — `request_id` needed to wrap the whole pipeline (registered before `UseSerilogRequestLogging`), while `user_id`/`tenant_id` need claims from `UseAuthentication()` and so use Serilog's `EnrichDiagnosticContext` hook instead of a `LogContext` scope. Still logs to console only — no Loki/Seq sink configured (would need a real endpoint; no deployment pipeline exists in this repo to point one at yet) |
| OAuth flow shape | Frontend-first ID token flow (not backend redirect flow) | SPA with zero new npm packages; Google/Apple JS SDKs loaded from CDN `<script>` tags inject `window.google` / `window.AppleID`; backend validates the resulting ID token with the existing `JwtSecurityTokenHandler` + provider JWKS rather than running an OAuth redirect dance itself |
| OAuth account creation | `CreateOAuthUserAsync` calls `userManager.CreateAsync(user)` with no password — passwordless account, `EmailConfirmed = true` since the provider already verified the email | Google/Apple already verify email ownership; re-requiring our own confirmation flow would be redundant friction |
| OAuth owner registration authorization | `RegisterOAuthUserHandler` requires `info.Email == studio.OwnerEmail` for role="owner", mirroring `RegisterUserHandler` | The original `overnight-prompt-oauth-2026-06-25.md` spec predated the guest-QA-pass owner-takeover fix (2026-07-02) and did not include this check — implementing it as originally written would have reopened that exact vulnerability via a second (OAuth) registration path. `RegisterOAuthUserValidator` likewise restricts roles to client/owner only, matching `RegisterUserValidator`. |
| Apple Sign In HTTPS requirement | No workaround added; documented as a dev-environment limitation | Apple Sign In requires HTTPS even in local development (proxy or tunnel needed) — out of scope to solve here |
| Plan billing interval stays locked per-row | Confirmed 2026-07-18: `Plan.BillingInterval` remains authoritative for checkout (`CreateSubscriptionCommand`/`CreateSubscriptionCheckoutCommand`/`ChangePlanCommand` unchanged). A tier that wants both cadences gets two `Plan` rows linked via `PairedPlanId`, not one row with a caller-chosen interval | Matches `overnight-prompt-plan-management-audit-2026-07-18b.md`'s forbidden-action #3 ("the billing-interval gate is intentional business logic, not a bug") — that same-day audit already built the dual-price display (Fix #2) on top of the locked-per-row assumption ("reference only, not charged"). Moving interval choice to checkout would invalidate that shipped, tested UI. Superseding this would require correcting that audit doc, not just this codebase. |
| Plan usage limits | `Plan` gained `MaxArtists`, `MaxAppointmentsPerMonth`, `MaxNotificationsPerMonth`, `MaxStorageGb`, `MaxLocations` (all `int?`, null = unlimited), `AllowApiAccess`, `PrioritySupport`. Enforced via `IQuotaCheckedCommand` marker interface + `PlanLimitBehavior` (MediatR pipeline behavior, registered after `ValidationBehavior`) + `IPlanLimitService`/`PlanLimitService` (Redis-cached usage counts, 30s TTL, mirrors `SubscriptionAccessService`'s pattern). Throws `PlanLimitExceededException` → 403 `PLAN_LIMIT_EXCEEDED`. `IPlanLimitService.InvalidateUsageCacheAsync(quotaType)` is called by `CreateArtistHandler`/`CreateAppointmentHandler` immediately after their `SaveChangesAsync` succeeds (write-through invalidation, added 2026-07-18 night), narrowing the 30s staleness window to the gap between two sequential requests — it does NOT eliminate the race for two truly concurrent requests that both read the cache before either write lands; that needs a DB-level atomic counter or advisory lock and is explicitly out of scope. | Only `CreateArtistCommand` (Artists) and `CreateAppointmentCommand` (AppointmentsPerMonth) are wired to the marker interface so far. NOT wired (fast-follow): the 7 notification-send commands (NotificationsPerMonth), `UploadDesignRevisionCommand` + other photo/PDF upload commands (StorageBytes, needs `Studio.StorageUsageBytes` incremented on each R2 write/delete — the counter field exists, the increment call sites don't yet), and a future location-create command (Locations, once multi-location ships — `PlanLimitService` currently always reports usage `1` for this dimension). |
| Plan Monthly/Yearly pairing | `Plan.PairedPlanId` (`Guid?`, self-referencing, no FK constraint) links a tier's Monthly and Yearly rows. `UpdatePlanHandler` propagates limit/feature-flag fields (never price, `BillingInterval`, or Stripe price IDs) to the paired row and keeps the link symmetric. `DeletePlanHandler` clears the sibling's `PairedPlanId` rather than leaving it dangling | Direct consequence of the locked-per-row decision above: two rows per tier need something to keep their limits from drifting apart when an issuer edits one. |
| Plan usage report (validation tool, not enforcement) | `GetPlanUsageReportHandler` (`IgnoreQueryFilters()` #25, IssuerOnly) surfaces real per-studio usage against each `Plan.Max*` cap — artists, appointments/notifications this calendar month, storage GB — sorted by closest-to-cap first. Rendered as a table inside the existing `IndustryReportsPage.tsx` (`/platform/reports`), not a new nav item. Ships with the seeded `Plan.Max*` numbers (e.g. Starter = 40 appointments/month) completely unchanged | The per-tier limit numbers were guesses when the feature shipped same-day; this report exists so real studio behavior can be checked against them before they're trusted to block real customers. Changing the numbers themselves is a separate, explicit decision for later — this prompt only builds the visibility tool. |
| Two-sided referral reward | `ReferralRedemption` gained `ReferrerRewardApplied` (bool) + `ReferrerRewardCouponId` (string?). `IReferralRewardService`/`ReferralRewardService` (`IgnoreQueryFilters()` #26) issues a one-month-free Stripe coupon to the referring studio's active subscription once the referred studio's own discount lands, called from both `CreateSubscriptionHandler` and `ActivateCheckoutSubscriptionHandler` after `SaveChangesAsync`. Idempotent via `ReferrerRewardApplied`; skips (logs, does not throw) on self-referral (`OwnerEmail` match) or when the referrer has no active Stripe subscription (Free/Trialing/cash-billed) | Note: `Studio`/`Subscription`/`ReferralCode`/`ReferralRedemption` carry no `HasQueryFilter` in `AppDbContext` at all (see "Issuer-level" `DbSet`s in `IAppDbContext`) — `IgnoreQueryFilters()` here is a no-op functionally, kept only to match the established documentation convention for cross-tenant reads on these entities (usages #6, #9–#11). Reward size (one month, mirroring the referred side), the no-active-Stripe-subscription case, self-referral policy, and stacking behavior for reusable codes are all product decisions left open — see `// TODO(product)` comments in `ReferralRewardService`. |
| Core plan reconciliation replaces one-time plan seed | `DataSeeder.SeedPlansAsync()` (insert-once, guarded by `Plans.Any(Id == StarterPlanId)`) replaced with `DataSeeder.ReconcileCorePlansAsync()` (always runs on startup; upserts by fixed Id — inserts Starter/Growth/PremiumMonthly/PremiumYearly/Pro if missing, corrects Name/BillingInterval/price/discount/branding/Max*/AllowApiAccess/PrioritySupport/PairedPlanId if the row already exists with stale values). Stripe price IDs are excluded from reconciliation (populated by `StripeDemoSeeder`/real Stripe config, not source-controlled here). `SeedFreePlanAsync`'s independent insert-once-by-Id guard is unchanged. | The one-time guard left any environment whose `Plans` table was first populated before the `Max*` fields and Premium's corrected pricing existed permanently stuck on that stale snapshot — see `bug-report-plans-page-data-mismatch.md`. Consequence worth flagging explicitly: an issuer editing Starter/Growth/Premium/Pro in place via `PlanManagementPage` will have that edit reverted on the next deploy, since these five rows are now source-of-truth-owned, not database-owned. An issuer who needs a bespoke arrangement for one studio should clone a new `Plan` row with its own Id instead of editing one of the five canonical tiers. |
| Orphaned legacy plan retirement | `DataSeeder.RetireOrphanedNamedPlansAsync()` — always runs, immediately after `ReconcileCorePlansAsync()`. Finds any `Plan` row named exactly "Free"/"Starter"/"Growth"/"Premium"/"Pro" whose `Id` isn't one of the six canonical constants, reassigns every referencing `Subscription.PlanId` and `Subscription.PendingPlanId` to the correct canonical replacement (Premium's replacement chosen by the orphan's own `BillingInterval`), clears any sibling `Plan.PairedPlanId` still pointing at it, then deletes it. No-op once no orphan remains, so safe to run every boot indefinitely. | `ReconcileCorePlansAsync` (previous entry) only ever matches by fixed Id — a canonically-named plan under any other Id (e.g. Premium's pre-Monthly/Yearly-split row) is invisible to it, so its insert-if-missing branch adds a correct row *alongside* the leftover rather than replacing it, producing a visible duplicate card (`bug-report-premium-plan-duplicate-legacy-row.md`). Accepted trade-off: this matches by name only, so it cannot distinguish a genuine pre-split leftover from an issuer-created custom plan that happens to share a reserved tier name — an issuer needing a bespoke plan should use a distinct name. Confirmed via `AppDbContextModelSnapshot.cs` that `Subscription.PlanId` and `Subscription.PendingPlanId` are the only two FKs anywhere referencing `Plan.Id`; `Plan.PairedPlanId` is a self-reference with no FK constraint. |

---

## Client QA Pass — 2026-07-02

Bug-hunt + polish pass over the client role, per `docs/claude/overnight-prompt-client-qa-polish-2026-07-01.md`.
Backend: 953 unit + 273 integration tests green. Frontend: 1239/1239 green (tsc clean,
no flaky failures observed).

### Bugs found and fixed

**Backend:**

- `ReviewDesignCommand.cs` → **critical, real security bug.** The handler had no
  `ICurrentUser` at all and performed no ownership check whatsoever — it loaded a
  `DesignRevision` purely by `DesignRevisionId` and let anyone approve or reject it.
  Any authenticated client could approve/reject a revision on a design that wasn't
  theirs by guessing/enumerating GUIDs. This directly contradicts a decision recorded
  during the artist pass, which assumed (without verifying) that the established
  `FindClientForUserAsync` ownership pattern already applied here — it did not. Fixed
  by injecting `ICurrentUser`, including `DesignRevision.Design`, and 404ing when
  `currentUser.Role == "client"` and the resolved client doesn't own the design
- `GetNotificationsQuery.cs` → same missing-scope bug pattern as the artist branch
  fixed in an earlier pass, but for `client`. There was no `else if (Role == "client")`
  branch, so execution fell into the permissive `query.RecipientId.HasValue` case —
  a client could pass an arbitrary `RecipientId` (another client's, or the studio's)
  and read those notification logs, or omit it and get the full unfiltered studio log.
  **Compounding bug:** the endpoint's route policy was `ArtistAndAbove`, which
  blocked the `client` role entirely — and `ClientLayout` renders `NotificationBell`,
  which calls this exact endpoint, meaning every client's notification bell was
  silently broken (403) in production. Fixed both: added the client-scoping branch,
  and changed the route to `ClientAndAbove`
- `CreateAppointmentValidator.cs` → `DurationMinutes` was validated with
  `InclusiveBetween(30, 480)` — any integer in that range, not the discrete set of
  session lengths the booking form actually offers (`[30, 45, 60, 90, 120, 180, 240,
  300, 360, 480]`). Tightened to `Must(d => ValidDurations.Contains(d))`, mirroring
  `BookAppointmentForm.tsx`'s `VALID_DURATIONS`
- `GetMyClientProfileQuery.cs`, `UpdateMyBodyMapCommand.cs`,
  `UpdatePortableProfileOptInCommand.cs` → **real functional gap.** A brand-new
  client with no owner/artist-created `ClientProfile` row yet could never set their
  own body map or sharing preference: the get-query 404'd (frontend showed
  "unavailable"), and both update commands *also* 404'd instead of creating the row,
  so there was no path to ever create one from the client side. Fixed to match the
  owner-side `UpsertClientProfileCommand`'s existing create-or-update behaviour: the
  get-query now returns empty defaults instead of 404ing, and both commands
  auto-create the profile row on first save
- `SignConsentFormValidator.cs` → `SignatureData` had `MaximumLength(5000)` but no
  minimum, even though the frontend's Zod schema requires `min(2)` for the typed
  full-name signature. A 1-character (or whitespace) signature could bypass the
  frontend and be recorded as a legally-binding consent signature. Added
  `MinimumLength(2)`
- Verified and left unchanged (matches established patterns, not bugs): `clientId`
  trust in `CreateAppointmentCommand`/`SubmitIntakeFormCommand` (always overridden
  from `ICurrentUser`, request value ignored for the `client` role); duplicate-consent
  409 in `SignConsentFormCommand`; `CheckSlotAvailabilityQuery`'s working-hours/
  time-off/conflict checks; `GetDesignsQuery`/`GetIntakeFormsQuery`/
  `GetConsentFormsQuery` client-scoping; the portable-profile response
  (`PortableClientProfile`) genuinely contains no PII (only `DisplayName`,
  `BodyMapLocations`, `TattooHistory`) — the sharing toggle's "your contact
  information is never shared" copy is accurate

**Frontend:**

- `DepositCheckoutPage.tsx` → Stripe `return_url` used `window.location.origin`
  instead of `VITE_PUBLIC_URL`, matching the same class of bug already fixed
  elsewhere in the codebase. Also added a missing "Back to booking" link on both
  success states and switched the Stripe Elements `appearance.theme` to `"night"`
  when the app is in dark mode (was hardcoded to `"stripe"`, i.e. always light)
- `MyBookingsSection.tsx` → `appt.notes` (client-submitted notes on the appointment)
  was never rendered anywhere in `BookingRow`, even though the field exists on
  `AppointmentResponse` — added
- `SubmitIntakeFormPage.tsx` / `SignConsentFormPage.tsx` → the appointment-picker
  dropdown queried all appointments with no status filter, so Cancelled/Completed/
  NoShow appointments were selectable alongside real upcoming ones. Filtered to
  `Pending`/`Confirmed` only, added an empty-state hint when none are eligible, and
  disabled the consent-form submit button in that case
- `SignConsentFormPage.tsx` → the mutation's error state showed one generic message
  regardless of cause; a 409 (already signed for this appointment) looked identical
  to a network failure. Added 409-specific copy to both the inline error and the
  toast
- `IntakeFormListPage.tsx` / `ConsentFormListPage.tsx` → neither page had a way for a
  client to actually get to the submit/sign form — no CTA button anywhere, and the
  empty-state copy ("...appear here after clients submit them during booking") was
  written for staff, not the client reading it about themselves. Added a role-gated
  "Submit intake form" / "Sign consent form" button in the header and empty state,
  client-specific empty-state copy, hid the form-count badge when zero, and added a
  retry action to the error state
- `MyProfilePage.tsx` → `saveBodyMap()` called the mutation, ignored the result, and
  unconditionally exited edit mode — a failed save was invisible (draft silently
  discarded) and a successful one gave no confirmation. Added success/error toasts
  and only exit edit mode on success
- `PortableProfileToggle.tsx` → same missing-toast gap on both the success and
  rollback-on-failure paths; added toasts and a brief explanation of what the toggle
  does before the switch (previously just a one-line label with no context)
- `ClientLayout.tsx` → 5 nav items plus logo/bell/user-menu in one non-wrapping flex
  row overflowed on narrow viewports. Added `overflow-x-auto scrollbar-none shrink
  min-w-0` (same fix already applied to `ArtistLayout`/`OwnerLayout`/`IssuerLayout`
  in earlier passes) and a responsive short label ("Book" vs "Book Appointment")
- `DesignDetailPage.tsx` → there was a `ChangesRequested` banner for the artist but
  no equivalent banner telling the *client* their feedback is needed while a design
  is `InReview` — added, gated on `canReview`
- Missing `useDocumentMeta` document titles added to `BookPage`, `SubmitIntakeFormPage`,
  `SignConsentFormPage`, `MyProfilePage`, `DepositCheckoutPage`
- `MyBookingsSection.tsx` → added an upcoming-count badge to the "My bookings" card
  header, and a "This appointment was cancelled — Book a new appointment" hint under
  cancelled rows (previously just showed the status badge with no next step)

### Decisions made

| Decision | Choice | Reason |
|---|---|---|
| `ClientProfile` auto-create | `UpdateMyBodyMapCommand`/`UpdatePortableProfileOptInCommand` create the row on first save instead of 404ing | Matches the owner-side `UpsertClientProfileCommand` convention; a client must be able to set their own data even if no staff member has touched their profile yet |
| `GetMyClientProfileQuery` on missing profile | Return empty defaults, not 404 | A missing profile row is a normal state for a new client, not an error; both frontend consumers already handled the 404 gracefully, but real defaults are strictly better UX than an "unavailable" message |
| `GetNotifications` route policy | `ClientAndAbove` (was `ArtistAndAbove`) | The client-facing `NotificationBell` calls this endpoint from every client screen; blocking the role entirely was an unreachable-feature bug, not an intentional restriction |
| Design-review ownership check | Added `ICurrentUser` + `FindClientForUserAsync` scoping to `ReviewDesignHandler` | The handler had zero ownership enforcement; this was assumed-but-never-verified during the artist pass — corrected here rather than left for a future pass |

### Skipped / deferred (with reason)

- `DepositArea` in `MyBookingsSection.tsx` doesn't distinguish a genuine payment-fetch
  error from the normal "no payment created yet" 404 (same convention documented in
  the owner pass's `PaymentDetailPage` fix) — not changed, since introducing an
  error branch here risks showing confusing error text for the completely normal
  no-deposit-yet case without deeper `error.status` inspection
- Full P7-equivalent global toast/confirmation/spinner/accessibility audit across
  every client-accessible button — not exhaustively performed; targeted fixes applied
  where Layer B/C review found concrete gaps (`MyProfilePage`, `PortableProfileToggle`,
  list-page CTAs)
- P2.6 (post-booking "what happens next" info section), P4.1 (real file upload for
  intake forms), P5.3 (revision image lightbox) — scoped out as larger UI additions
  rather than bug fixes or small polish; left for a future pass

## Guest/Visitor QA Pass — 2026-07-02

Bug-hunt + polish pass over the unauthenticated guest/visitor surface, per
`docs/claude/overnight-prompt-guest-qa-polish-2026-07-01.md`. Backend: 958 unit + 273
integration tests green. Frontend: 1260/1260 green (tsc clean, `pnpm build` clean).

### Bugs found and fixed

**Backend — critical:**

- `RegisterUserCommand.cs` / `RegisterUserValidator.cs` → **critical, real security
  bug.** The public, `[AllowAnonymous]` `POST /api/v1/auth/register` endpoint accepted
  `role: "issuer"` (cross-tenant platform admin) or `role: "owner"` for **any**
  `studioId` — including studio IDs publicly discoverable via
  `/api/v1/public/studios/nearby` — with zero authorization check binding the caller
  to that studio. Any anonymous caller could self-mint a platform-admin account, or
  attach a rogue "owner" account to an existing studio it didn't create, gaining
  owner-level tenant access (clients, payments, appointments). Fixed by (1)
  restricting the public validator's `ValidRoles` to `client`/`owner` only — `artist`
  and `issuer` accounts are never created through this endpoint — and (2) requiring
  `req.Email` to match the studio's `OwnerEmail` (set at studio-creation time by
  `RegisterStudioCommand`) before an `owner` registration is allowed to proceed.
  6 new/updated tests
- `ForgotPasswordCommand.cs` / `AuthEndpoints.cs` → **critical, real security bug.**
  `POST /api/v1/auth/forgot-password` returned the raw password-reset token directly
  in the JSON response body (`{ resetToken: token }`) instead of emailing it — meaning
  anyone who knew a victim's email address could read the reset token straight off the
  API response and take over that account immediately, with no access to the victim's
  inbox required. The frontend never even read this field, so the feature was also
  functionally broken for real users (no email was ever sent). Fixed by rendering and
  sending a `RenderPasswordReset` email (new `IEmailRenderer` method, following the
  existing `RenderEmailVerification`/`RenderArtistInvite` pattern) and changing the
  endpoint to always return an identical, token-free response regardless of whether
  the account exists. 3 new tests
- `InfrastructureServiceExtensions.cs` → password-reset and email-confirmation tokens
  used ASP.NET Identity's default 24-hour `TokenLifespan`. Tightened to 1 hour via
  `DataProtectionTokenProviderOptions`
- `RateLimitingExtensions.cs` → the `"auth"` and `"public-write"` rate-limit policies
  used the `AddFixedWindowLimiter(name, options)` shorthand, which creates **one
  global bucket shared by every caller** rather than partitioning per client — a
  single client sending 10 rapid requests could exhaust the login/register limiter
  for every other visitor on the platform (a trivial DoS against auth itself).
  Rewrote both as `AddPolicy` partitioned by `RemoteIpAddress`, and added a new
  per-IP `"public-read"` policy (120 req/min) applied to every previously-unthrottled
  public `GET` endpoint (`/public/studios/*`, `/public/artists/*`,
  `/public/portfolio/*`, `/public/designs/share/*`, `/studios/map`, `/studios/{id}/qr`)
  and `"auth"` to `POST /studios` (studio self-registration)

**Frontend:**

- `ArtistPortfolioPage.tsx` → canonical URL was `https://penaearte.com/a/${slug}`, but
  the router serves this page at `/artist/:slug` — the canonical tag pointed at a
  route that doesn't exist. Fixed to `/artist/${slug}`; regression test added
- `SharedDesignPage.tsx` → missing `useDocumentMeta` (document title never set to the
  design title), no `onError` fallback on the design image (a broken/expired R2 URL
  rendered a blank alt-text box), and the `getSharedDesign` RTK Query endpoint had no
  `keepUnusedDataFor: 0` — an expired design could be served from cache showing an
  image the backend would now 404 on. All three fixed; 2 new tests
- `LoginPage.tsx` → the `?redirect=` query param was passed straight to
  `navigate()` with no validation — hardened to only accept same-origin relative
  paths (`startsWith("/")`, rejecting `//`) as defense-in-depth against open-redirect
- `ClientRegisterPage.tsx` → `onSubmit` awaited `registerUser(...).unwrap()` then
  `login(...).unwrap()` with no `try/catch` around either call. If registration
  succeeded but the immediate auto-login failed (network blip, race condition), the
  thrown error was unhandled — the user was left on a stuck form with no feedback,
  account created but not signed in. Wrapped both calls in their own `try/catch`;
  the login-failure path now toasts "Account created. Please sign in manually." and
  redirects to `/login`. This page had **zero test coverage** before this pass — added
  a full test file (12 tests) covering the interstitial, validation, success path,
  409/429 errors, and the new login-failure fallback
- `EmbedPage.tsx` → a studio with zero artists rendered nothing in the "Our artists"
  section instead of an empty-state message. Added "Artists being added soon."

### Polish implemented

- **P1.2 JSON-LD structured data** — new `useStructuredData` hook (mirrors
  `useDocumentMeta`'s inject/cleanup pattern); `StudioPortfolioPage` emits a
  `TattooParlor` schema with `aggregateRating` when reviews exist, `ArtistPortfolioPage`
  emits a `Person` schema
- **P2.1 DiscoverPage tab URL persistence** — `activeTab` now reads from and writes to
  `?tab=` via `useSearchParams` instead of local component state, so a shared
  `/discover?tab=studios` link opens on the right tab
- **P7.2 EmbedPage empty-artists state** (listed above under bug fixes since it matches
  a "Missing:" item in the source prompt)

### Decisions made

| Decision | Choice | Reason |
|---|---|---|
| Public register endpoint role set | Restricted to `client`/`owner` only, not `artist`/`issuer` | No frontend flow uses this endpoint for `artist` or `issuer` — those are dead-but-reachable inputs that only enabled the privilege-escalation bug. Artist accounts belong behind an authenticated owner-invitation flow (not present in this codebase yet); issuer is platform-admin-only and must never be self-registered |
| Owner registration binding | `req.Email` must equal `studio.OwnerEmail` (no schema migration) | Avoids adding an `OwnerUserId` column / migration for this pass; `OwnerEmail` is already set at studio-creation time by the same flow that immediately calls register, so this fully closes the takeover vector with a one-line check |
| Rate limiter storage | Kept in-process (`AddPolicy` + `RemoteIpAddress` partitioning), not Redis-backed | Fixed the more urgent bug (global-not-per-IP bucket) within scope; a fully distributed Redis-backed limiter (required for correctness across multiple API replicas, and to match CLAUDE.md's "state that should be in Redis" rule) is a larger infra change deferred below |
| Token lifespan | Global `DataProtectionTokenProviderOptions.TokenLifespan = 1h` (affects both password-reset and email-confirmation) | Both tokens share ASP.NET Identity's default "Default" provider; a per-purpose split would need a second named token provider registration, which wasn't warranted for closing out a TTL hardening item this pass |

### Skipped / deferred (with reason)

- **Redis-backed distributed rate limiting** — current fix is correct per-instance
  (per-IP, not global) but each API replica still keeps its own in-memory bucket,
  meaning a multi-pod deployment's effective limit multiplies by replica count. This
  violates CLAUDE.md's "state that should be in Redis (sessions, slots, rate limits)"
  rule, which predates this pass. Implementing a Redis-backed `PartitionedRateLimiter`
  is a contained but non-trivial infra change deserving its own pass with dedicated
  Redis-backed test coverage
- **`/embed/:slug` CSP / `X-Frame-Options` scoping (P8.1)** — no security-headers
  middleware exists anywhere in this API today (verified), so `/embed` currently works
  by default (nothing blocks framing) but every other route also has zero clickjacking
  protection. This is a deploy-layer concern (nginx/Cloudflare, per the Infra stack in
  CLAUDE.md) with no config files present in this repo to edit — flagged for the ops
  side rather than guessed at here
- P4.1 (artist portfolio style filter chips), P4.2 (sticky "Book with {artist}" CTA),
  P3.5 (review pagination past 10), P3.4 (owner review-response display — no
  `ownerResponse` field exists on `Review` yet, so this is a new feature, not a bug),
  P5.2 (password strength indicator), P5.3 (email-verification banner on `/book`) —
  scoped out as UI additions beyond bug-fix/small-polish scope; left for a future pass

## P-02 Stripe Health Check — 2026-07-02

- Added `Stripe.BalanceService` to DI in `InfrastructureServiceExtensions.cs`
- Created `Pena_e_Arte.API/Extensions/StripeHealthCheck.cs`
- Registered as `"stripe"` with `tags: ["ready"]` in `Program.cs`
- `/health/ready` now probes DB, Redis, and Stripe before reporting ready
- Unit tests: `tests/Pena_e_Arte.UnitTests/HealthChecks/StripeHealthCheckTests.cs`
- Rate limit note: left comment in `Program.cs` about `MaximumAge` for high pod counts

## Feedback / Bug Report Feature — 2026-07-02

### What was built
- `FeedbackReport` domain entity (non-tenant, issuer reads cross-studio)
- `FeedbackType` and `FeedbackStatus` domain constants
- MediatR: `SubmitFeedbackCommand`, `UpdateFeedbackStatusCommand`, `GetFeedbackReportsQuery`
- FluentValidation: `SubmitFeedbackValidator`, `UpdateFeedbackStatusValidator`
- Migration: `AddFeedbackReports`
- Endpoints:
  - `POST /api/v1/feedback` (ArtistAndAbove)
  - `GET /api/v1/platform/feedback?type=&status=` (IssuerOnly)
  - `PATCH /api/v1/platform/feedback/{id}/status` (IssuerOnly)
- `FeedbackDialog` component in `ArtistLayout` + `OwnerLayout` header
- `FeedbackInboxPage` at `/platform/feedback` (IssuerOnly)
- IssuerLayout: "Feedback" nav item with Open-count badge
- `feedbackApi` RTK Query slice registered in store

### Architecture decisions
- `FeedbackType` and `FeedbackStatus` are real C# `enum` types (not string-constant
  classes), stored via `HasConversion<string>()` — matching the codebase's existing
  convention (`AppointmentStatus`, `DesignApprovalStatus`, `BillingInterval`, etc.), not
  a one-off string-constants pattern. Request/response contracts still carry `string`
  for these fields; handlers use `Enum.Parse`/`Enum.TryParse(ignoreCase: true)` and
  `.ToString()` at the boundary, the same pattern used by `CreatePlanCommand`/`GetPlansQuery`.
- `FeedbackReport` is NOT a `TenantEntity` — no EF Core global query filter, configured
  inline in `AppDbContext.OnModelCreating` (same pattern as `Review` and
  `SavedPortfolioImage`, not a separate `IEntityTypeConfiguration` file).
  `SubmitFeedbackHandler` reads `StudioId` from `ICurrentTenant` and stores it.
  `GetFeedbackReportsHandler` queries across all studios without `IgnoreQueryFilters()`
  because no filter is registered for this entity.
- `FeedbackDialog` is controlled (open/onOpenChange props) — callers own state.
  This avoids prop-drilling the dialog into deeply nested components.
- Issuer note is submitted alongside status update (not auto-saved) to keep API calls
  intentional and predictable.

## Redis-Backed Distributed Rate Limiting — 2026-07-02

### Problem solved
ASP.NET Core's built-in `FixedWindowLimiter` is in-process. With N replicas,
each pod tracked its own counter — effective limit was N × permitLimit before
any pod rejected a request. Useless at scale.

### Solution
`RedisFixedWindowRateLimiter` — a custom `System.Threading.RateLimiting.RateLimiter`
subclass backed by a Redis atomic Lua script (INCR + EXPIRE + TTL in one round-trip).
One instance per (policy, client IP) pair, cached by `PartitionedRateLimiter`.
All state lives in Redis; the object is a stateless wrapper.

### Key decisions
- **No new NuGet packages** — `StackExchange.Redis` already in the project.
- **Fail open** — Redis blip allows the request through + logs a warning.
  A rate-limiter outage is not worth taking the API down.
- **Fixed window via INCR + EXPIRE** — simple, atomic, correct.
  The TTL returned from Redis is used as the `Retry-After` header value.
- **IdleDuration = window** — tells `PartitionedRateLimiter` to evict idle
  IP entries after the window expires, preventing memory leaks.
- **PostConfigure<IConnectionMultiplexer, ILoggerFactory>** — resolves Redis
  from DI without changing `AddApiRateLimiting()` signature or `Program.cs`.
- **ForwardedHeaders middleware** — added (was absent), so `RemoteIpAddress`
  reflects the real client IP behind the K8s/Nginx ingress.

### .NET 10 API surface note
The `RateLimiter` abstract base class in this SDK (net10.0) differs from older
docs/examples: the public acquire method is `AcquireAsync`/`AcquireAsyncCore`
(not `WaitAndAcquireAsync`/`WaitAndAcquireAsyncCore`), `GetStatistics()` is
abstract (must be implemented, not optional), and `MetadataName.RetryAfter` is
a strongly-typed `MetadataName<TimeSpan>` — comparing against the string-based
`TryGetMetadata(string, out object?)` overload requires `.Name`. Also,
`ForwardedHeadersOptions` now lives in `Microsoft.AspNetCore.Builder`, not
`Microsoft.AspNetCore.HttpOverrides` (the enum `ForwardedHeaders` is still in
`HttpOverrides`). Verify against the installed SDK before copying rate-limiter
code from older blog posts/examples.

### Policies (unchanged limits)
| Policy       | Limit | Window | Endpoints                                    |
|---|---|---|---|
| auth         |  10   | 1 min  | login, register, oauth, forgot-password      |
| public-write |  30   | 1 min  | review submit, artist view tracking          |
| public-read  | 120   | 1 min  | portfolio feed, studio/artist pages, QR, map |

### Files changed
- `Pena_e_Arte.API/Extensions/RedisFixedWindowRateLimiter.cs` (NEW)
- `Pena_e_Arte.API/Extensions/RateLimitingExtensions.cs` (REPLACED)
- `Pena_e_Arte.API/Program.cs` (ForwardedHeaders added — was missing)
- `Pena_e_Arte.API/Pena_e_Arte.API.csproj` (InternalsVisibleTo added for
  `Pena_e_Arte.UnitTests`, matching the existing Infrastructure project pattern —
  `RedisFixedWindowRateLimiter` is `internal`)
- `tests/Pena_e_Arte.UnitTests/RateLimiting/RedisFixedWindowRateLimiterTests.cs` (NEW — 15 tests)

### No changes to
- Any endpoint file (`RequireRateLimiting` calls identical — already correct
  on every public/auth route)
- Any migration
- Any NuGet dependency

## Multi-Studio Plan — Phase 2: My Studios Page — 2026-07-04

### What was added
- `GET /api/v1/auth/my-studios` (`ClientOnly`, no rate limit) — returns all studios
  the authenticated client holds a `tenant_id` Identity claim for, ordered by name.
  Returns `MyStudioResponse[]` (StudioId, Name, Slug, City, CoverImageUrl, IsStudioActive).
- `GetMyStudiosQuery` + `GetMyStudiosHandler` — reads tenant IDs from `IIdentityService.GetTenantIdsAsync`
  then fetches `Studio` rows. Studios are not tenant-scoped (no IgnoreQueryFilters needed).
- `MyStudiosPage` at `/my-studios` (client-only route) — lists studio cards with
  cover image/initials monogram, city, active ring, switch button, and a link to the public portfolio.
- `Building2` nav item added to `ClientLayout` between "Book Appointment" and "My Designs".

### Key decisions
- **IsCurrentlyActive computed on the frontend**, not the server. The Redux store already holds
  `auth.tenantId`. Comparing `studio.studioId === tenantId` in the component is cheaper,
  always fresh, and eliminates cache-invalidation overhead after switching.
- **Navigate to /book after switch** — clears the user's mental model to "I'm now in a new studio"
  and lands them on the most immediately useful page. No full-page reload needed; stale RTK Query
  cache for the old tenant will be replaced by fresh fetches triggered by the new page.
- **ClientOnly policy** — consistent with the existing SwitchStudio endpoint. Artists and owners
  each belong to exactly one studio; this feature is meaningless for them.
- **No validator needed** — `GetMyStudiosQuery` takes no user-supplied parameters.

### Files added/changed
Backend:
- `Pena_e_Arte.Contracts/Responses/MyStudioResponse.cs` (NEW)
- `Pena_e_Arte.Application/Auth/Queries/GetMyStudiosQuery.cs` (NEW)
- `Pena_e_Arte.API/Endpoints/AuthEndpoints.cs` (GET /auth/my-studios added)
- `tests/Pena_e_Arte.UnitTests/Auth/GetMyStudiosHandlerTests.cs` (NEW — 7 tests)

Frontend:
- `frontend/src/features/auth/authApi.ts` (MyStudioResponse interface + getMyStudios query)
- `frontend/src/features/auth/components/MyStudiosPage.tsx` (NEW)
- `frontend/src/features/auth/index.ts` (export added)
- `frontend/src/app/router.tsx` (/my-studios route added)
- `frontend/src/layouts/ClientLayout.tsx` (Building2 nav item added)
- `frontend/src/features/auth/__tests__/MyStudiosPage.test.tsx` (NEW — 14 tests)

## Public Portfolio Pages — Nav Header — 2026-07-04

### Problem
`StudioPortfolioPage` (/s/:slug) and `ArtistPortfolioPage` (/artist/:slug) are public routes
reachable via Google, shared links, and Instagram bios. Neither page had a nav header, so
unauthenticated visitors landing directly on these pages had no sign-in or sign-up entry point.

### Solution
- Extracted `AuthenticatedNav` from `DiscoverPage.tsx` into a new shared file:
  `frontend/src/features/public/components/PublicPageHeader.tsx`
- Created `PublicPageHeader` component — sticky header with brand mark + sign-in/sign-up
  links (logged-out) or account dropdown (logged-in). No props — reads Redux internally.
- Added `<PublicPageHeader />` to the loaded, error, and (placeholder in) loading states of
  both portfolio pages.
- Adjusted sticky sidebar `top` offset from `top-6` to `top-[72px]` to account for header height.

### Files changed
- `frontend/src/features/public/components/PublicPageHeader.tsx` (NEW)
- `frontend/src/features/public/components/DiscoverPage.tsx` — removed inline `AuthenticatedNav`,
  imported it from `PublicPageHeader.tsx`; removed `BrandMark` duplication
- `frontend/src/features/public/components/StudioPortfolioPage.tsx` — added header + error state header + skeleton placeholder + sidebar top adjustment
- `frontend/src/features/public/components/ArtistPortfolioPage.tsx` — same as above
- `frontend/src/features/public/index.ts` — added exports
- `frontend/src/features/public/__tests__/PublicPageHeader.test.tsx` (NEW — 16 tests)
- `frontend/src/features/public/__tests__/StudioPortfolioPage.test.tsx` — 6 new header tests
- `frontend/src/features/public/__tests__/ArtistPortfolioPage.test.tsx` — 6 new header tests

## Guest QA Deferred UI Items — 2026-07-04

### Item 1: Artist portfolio style filter chips
- `ArtistPortfolioImageResponse` now includes `Style?: string`
- `GetPublicArtistQuery` projects `p.Style` into the response
- `ArtistPortfolioPage` shows filter chips when ≥ 2 distinct styles exist in the artist's
  images. Chips derived from the loaded data, not the global STYLES list — no dead chips.

### Item 2: Sticky Book CTA on mobile
- Fixed bottom bar (`lg:hidden`) added to `ArtistPortfolioPage`
- Content area gets `pb-20 lg:pb-8` to prevent overlap
- No JS visibility logic — pure CSS responsive hiding

### Item 3: Review pagination
- `ReviewList` shows first 10 reviews, "Show N more" button reveals the rest
- No backend change — backend already returns up to 50; slicing is client-side

### Item 4: Owner review-response
- `Review.Respond(string)` method added to domain entity
- Migration: `AddOwnerResponseToReview` adds `OwnerResponse` (nullable LONGTEXT) and
  `OwnerResponseAt` (nullable DATETIME)
- `ReviewResponse` contract updated with both fields
- All three review query projections updated
- `RespondToReviewCommand` + handler + validator added (Application layer)
- `ReviewEndpoints.cs` — `POST /api/v1/reviews/{reviewId}/respond` (OwnerOnly)
- `ReviewCard` shows owner response as indented border-left quote block
- `StudioPortfolioPage` passes `canRespond` when `role === "owner" && tenantId === studioId`
- Inline `OwnerReplyForm` rendered per unanswered review when `canRespond` is true
- `reviewsApi` (new RTK Query slice, authenticated `baseQuery`) is a separate cache from
  `publicApi` — its mutation dispatches `publicApi.util.invalidateTags(...)` in
  `onQueryStarted` so the public review lists refresh after a reply is posted, since RTK
  Query tag invalidation does not cross `createApi` slice boundaries.

### Item 5: Password strength meter
- `PasswordStrengthMeter` shared component — 4-level (weak/fair/good/strong), no external deps
- Added to `ClientRegisterPage` and `RegisterStudioPage`
- `RegisterStudioPage` password field upgraded from plain `<Input type="password">` to `<PasswordInput>`
- The meter's "weak" hint text ("at least 8 characters") overlaps the Zod validator's error
  message — existing tests asserting on that substring via `findByText` must scope to the
  specific `#password-error` element instead of a page-wide text query.

### Item 6: Email-verification banner on /book
- `email_verified` JWT claim added in `GenerateJwt` (based on Identity `EmailConfirmed`)
- `User.emailVerified?: boolean` added to `roles.ts`; decoded in `decodeToken`
- `BookPage` shows an amber banner with "Resend verification email" action when
  `user.emailVerified === false` (strict — undefined = old token = no banner)

### Files added/changed
Backend:
- `Pena_e_Arte.Domain/Entities/Review.cs` — OwnerResponse, OwnerResponseAt, Respond()
- `Pena_e_Arte.Contracts/Responses/Public/ReviewResponse.cs` — two new fields
- `Pena_e_Arte.Contracts/Responses/Public/ArtistPortfolioImageResponse.cs` — Style
- `Pena_e_Arte.Contracts/Requests/RespondToReviewRequest.cs` (NEW)
- `Pena_e_Arte.Application/Public/Queries/GetPublicArtistQuery.cs` — Style in projection
- `Pena_e_Arte.Application/Public/Queries/GetStudioReviewsQuery.cs` — owner response fields
- `Pena_e_Arte.Application/Public/Queries/GetArtistReviewsQuery.cs` — owner response fields
- `Pena_e_Arte.Application/Public/Queries/GetPortfolioImageReviewsQuery.cs` — owner response fields
- `Pena_e_Arte.Application/Reviews/Commands/RespondToReviewCommand.cs` (NEW)
- `Pena_e_Arte.API/Endpoints/ReviewEndpoints.cs` (NEW)
- `Pena_e_Arte.Infrastructure/Services/IdentityService.cs` — email_verified JWT claim
- Migration: `20260704212814_AddOwnerResponseToReview`
- Tests: `GetPublicArtistHandlerTests` (Style), `GetStudioReviewsHandlerTests` (NEW),
  `GetArtistReviewsHandlerTests` (NEW), `RespondToReviewHandlerTests` (NEW)

Frontend:
- `frontend/src/shared/types/roles.ts` — User.emailVerified
- `frontend/src/shared/utils/jwt.ts` — decode email_verified
- `frontend/src/shared/components/ui/PasswordStrengthMeter.tsx` (NEW)
- `frontend/src/features/public/publicApi.ts` — ArtistPortfolioImage.style, ReviewResponse fields
- `frontend/src/features/public/components/ArtistPortfolioPage.tsx` — style chips + mobile CTA
- `frontend/src/features/public/components/ReviewSection.tsx` — pagination, owner response, canRespond
- `frontend/src/features/public/components/StudioPortfolioPage.tsx` — canRespond wiring
- `frontend/src/features/appointments/components/BookPage.tsx` — email verification banner
- `frontend/src/features/auth/components/ClientRegisterPage.tsx` — strength meter
- `frontend/src/features/studios/components/RegisterStudioPage.tsx` — PasswordInput + strength meter
- `frontend/src/features/reviews/reviewsApi.ts` (NEW)
- `frontend/src/app/store.ts` — registered reviewsApi reducer + middleware

## My Studios Page — UX Polish — 2026-07-04

### Issues resolved
1. **False-affordance "Current" button** → replaced with a plain `<span>` badge. No button role,
   no disabled state, no click handler. Tests updated to assert it is NOT a button.
2. **Ring-style active border** → replaced with `border-emerald-500/40 bg-emerald-950/10`.
   Communicates selection without looking like a focus ring or error state.
3. **"Active" badge semantic color** → `bg-emerald-500/15 text-emerald-500` (was `primary`).
   "Suspended" badge was already `destructive` — no change.
4. **Monogram avatar contrast** → `bg-muted border-border/50` (was `bg-primary/10`). Consistently
   separates from the card background across light/dark themes.
5. **External link touch target** → 32×32 `inline-flex` wrapper with `hover:bg-accent` padding.
   aria-label clarified to "View {name} public profile".
6. **"Join another studio" CTA** → added as a ghost button in the list sub-header row.
   Navigates to `/discover`. Absent from empty state (where "Discover studios" button already exists).
7. **Header "Discover" shortcut** → always-visible ghost button on the right side of the
   sticky header. Navigates to `/discover`.

### Files changed
- `frontend/src/features/auth/components/MyStudiosPage.tsx` (complete rewrite)
- `frontend/src/features/auth/__tests__/MyStudiosPage.test.tsx` (1 test updated, 6 added)

## My Studios — Overflow Menu, Leave Studio, Notification Preferences — 2026-07-05

### Features added
1. **Kebab overflow menu**: each studio card now has a `MoreVertical` `DropdownMenu` with three
   items: "View public profile" (Link to `/s/{slug}`), "Manage notifications" (opens a Sheet),
   "Leave studio" (opens an AlertDialog). The standalone external-link icon is removed.

2. **Leave Studio** (full stack):
   - `IIdentityService.RemoveTenantClaimAsync` — removes the `tenant_id` claim and clears the
     active-tenant token (`"App"`/`"ActiveTenantId"`) if it matches the removed studio.
   - `LeaveStudioCommand` — validates membership via `GetTenantIdsAsync`, calls
     `RemoveTenantClaimAsync`, returns `IsLeavingActiveTenant`.
   - `DELETE /api/v1/auth/my-studios/{studioId}` — `ClientOnly`.
   - Frontend: `AlertDialog` confirmation, then either the studios list auto-refreshes via
     `invalidatesTags: ["MyStudios"]` (non-active tenant), or
     `dispatch(logout()) → navigate("/discover")` (leaving the active studio, since the current
     JWT is no longer valid for any tenant).
   - The `Client` DB row is NOT deleted — appointment/payment/consent history is retained so a
     later `SwitchStudioCommand` back into the studio reuses the existing row.

3. **Per-studio client notification preferences** (full stack):
   - New entity `ClientNotificationPreference (Id, UserId, StudioId, Type, Channel, IsEnabled)`,
     reusing the existing `NotificationType`/`NotificationChannel` enums (no new string-constant
     type). Configured via `ClientNotificationPreferenceConfiguration`, unique index on
     `(UserId, StudioId, Type, Channel)`. **No global query filter** — scoped by `(UserId, StudioId)`
     in every query, since a client may hold preferences for a studio that isn't their active JWT
     tenant.
   - Restricted server-side to the 5 client-facing `NotificationType` values (AppointmentCreated,
     AppointmentConfirmed, AppointmentCancelled, DepositCaptured, PaymentRefunded) — owner-facing
     types (IntakeFormSubmitted, ConsentFormSigned, DesignReviewed) are silently ignored on write.
   - Reuses the existing `Contracts.Responses.NotificationPreferenceItem(Type, Channel, IsEnabled)`
     record rather than introducing a duplicate DTO shape.
   - Default: all enabled (computed client-side in the query handler when no row is saved yet).
   - `GET/PUT /api/v1/auth/my-studios/{studioId}/notification-preferences` — `ClientOnly`.
   - `StudioNotificationSheet`: right-side `Sheet` with a toggle table, lazy-loaded
     (`skip: !open`), auto-closes on successful save.

### Frontend UI primitives added
`dropdown-menu.tsx`, `alert-dialog.tsx` (new `@radix-ui/react-dropdown-menu` +
`@radix-ui/react-alert-dialog` deps), and `sheet.tsx` (built on the already-installed
`@radix-ui/react-dialog`, side-panel variant via `class-variance-authority`). `buttonVariants`
exported from `button.tsx` so `AlertDialogAction`/`AlertDialogCancel` can reuse the same styles.

### Gotcha: Dialog-based overlay opened from a DropdownMenuItem
Opening a `Sheet`/`Dialog` synchronously from a `DropdownMenuItem`'s `onClick` — where the
dialog's content shape then changes shape while open (e.g. a loading spinner resolving into a
table after the preferences fetch completes) — can trigger an infinite `focus()`/`focusin` loop
between Radix's `FocusScope`/`DismissableLayer` and JSDOM in tests (JSDOM re-dispatches focus
events even when an element is already the `activeElement`; real browsers don't). Symptom is
`RangeError: Maximum call stack size exceeded` or an outright worker-process crash, not a normal
test failure. Two-part fix:
1. `src/test/setup.ts` — patch `HTMLElement.prototype.focus` to no-op when the element is already
   `document.activeElement`, matching real browser behavior.
2. `StudioNotificationSheet.tsx` — pass `onOpenAutoFocus={(e) => e.preventDefault()}` to
   `SheetContent` so Radix doesn't fight over initial focus while the sheet's content is still
   loading.
`AlertDialog` opened the same way from a `DropdownMenuItem` did NOT hit this loop (its content
doesn't change shape after opening), so the same pattern is safe for the "Leave studio" flow
without the extra `onOpenAutoFocus` override.

**Follow-up (2026-07-05, found via real-browser Playwright, not vitest/jsdom):** the jsdom fix
above didn't catch the actual production bug — jsdom never applies real CSS `pointer-events`, so
a stuck `body { pointer-events: none }` left behind by the modal `DropdownMenu` was invisible to
every unit test. Reported symptom: after opening the kebab menu once, the whole page (including
the kebab button itself) stops responding to clicks until a hard refresh. Root causes, found by
writing a throwaway Playwright spec against real Chromium and bisecting with `git stash`:
1. `DropdownMenu` is modal by default (locks `body` pointer-events while open, restores on
   close). Combined with opening a second modal overlay (the Sheet) from one of its items, the
   two overlays' body-lock bookkeeping raced and could leave the lock stuck. Fix: `<DropdownMenu
   modal={false}>` on the per-card kebab menu — it's a small actions menu, not a true modal, so
   losing the background-interaction lock is an acceptable tradeoff.
2. The "Manage notifications" item's `onSelect` called `event.preventDefault()` to defer opening
   the Sheet — but per Radix's API, preventing `onSelect` keeps the dropdown open indefinitely.
   The dropdown stayed open (invisibly, under the Sheet), so pressing Escape closed the
   still-open dropdown instead of the Sheet. Fix: keep the `setTimeout` deferral but drop the
   `preventDefault()` — the dropdown now closes normally and the Sheet still opens on the next
   tick, avoiding the original jsdom focus race without reintroducing this bug.
Regression test: `frontend/e2e/my-studios-kebab-menu.spec.ts` — asserts `document.body.style
.pointerEvents !== "none"` and that the kebab is still clickable after each close path
(Leave-Cancel, Manage-notifications-Escape, Manage-notifications-X-button).

### Files added
Backend:
- `Pena_e_Arte.Domain/Entities/ClientNotificationPreference.cs`
- `Pena_e_Arte.Contracts/Responses/LeaveStudioResponse.cs`
- `Pena_e_Arte.Contracts/Responses/ClientNotificationPreferencesResponse.cs`
- `Pena_e_Arte.Contracts/Requests/UpdateClientNotificationPreferencesRequest.cs`
- `Pena_e_Arte.Application/Auth/Commands/LeaveStudioCommand.cs`
- `Pena_e_Arte.Application/Auth/Commands/UpdateClientStudioNotificationPreferencesCommand.cs`
- `Pena_e_Arte.Application/Auth/Queries/GetClientStudioNotificationPreferencesQuery.cs`
- `Pena_e_Arte.Infrastructure/Persistence/Configurations/ClientNotificationPreferenceConfiguration.cs`
- Migration: `AddClientNotificationPreferences`
- `tests/Pena_e_Arte.UnitTests/Auth/LeaveStudioHandlerTests.cs`
- `tests/Pena_e_Arte.UnitTests/Auth/GetClientStudioNotificationPreferencesHandlerTests.cs`
- `tests/Pena_e_Arte.UnitTests/Auth/UpdateClientStudioNotificationPreferencesHandlerTests.cs`

Backend modified:
- `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs` — `RemoveTenantClaimAsync`
- `Pena_e_Arte.Infrastructure/Services/IdentityService.cs` — implementation
- `Pena_e_Arte.Application/Persistence/IAppDbContext.cs` — new `DbSet`
- `Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs` — `DbSet` (no query filter)
- `Pena_e_Arte.API/Endpoints/AuthEndpoints.cs` — DELETE + GET/PUT routes
- `tests/Pena_e_Arte.UnitTests/Helpers/FakeDbContext.cs` — new `DbSet`

Frontend added:
- `frontend/src/shared/components/ui/dropdown-menu.tsx`
- `frontend/src/shared/components/ui/alert-dialog.tsx`
- `frontend/src/shared/components/ui/sheet.tsx`
- `frontend/src/features/auth/components/StudioNotificationSheet.tsx`
- `frontend/src/features/auth/__tests__/StudioNotificationSheet.test.tsx`
- `frontend/e2e/my-studios-kebab-menu.spec.ts` — real-browser regression test (see follow-up above)

Frontend modified:
- `frontend/src/shared/components/ui/button.tsx` — exported `buttonVariants`
- `frontend/src/features/auth/authApi.ts` — 3 new endpoints + interfaces
- `frontend/src/features/auth/components/MyStudiosPage.tsx` — kebab menu, leave dialog, notif sheet,
  `modal={false}` + non-prevented deferred `onSelect` (see follow-up above)
- `frontend/src/features/auth/__tests__/MyStudiosPage.test.tsx` — 1 test replaced, 7 added
- `frontend/src/test/setup.ts` — JSDOM `focus()` re-dispatch patch (see gotcha above)
- `frontend/package.json` — `@radix-ui/react-dropdown-menu`, `@radix-ui/react-alert-dialog`

## My Designs Page — UX Audit Fixes — 2026-07-04

### Issues resolved
1. **Header/body left-edge misalignment** — header content now wrapped in
   `max-w-4xl mx-auto px-4 py-3` matching `<main>`. Both edges align on all viewports.

2. **Redundant Palette icon in counter** — removed from the design count row.
   Palette now appears once in the header title and once in the empty state only.

3. **Role-blind empty state copy** — `ResourceEmptyState` body is branched on `canCreate`:
   - Artist/Owner/Issuer: "Upload a tattoo design to start tracking approvals."
   - Client: "Your artist will upload designs here for your approval."

4. **False affordance: search bar with nothing to search** — search `<div>` is now
   wrapped in `{hasDesigns && (...)}`. Renders only when records exist.

5. **Accessible name missing from search input** — `aria-label="Search designs by title"` added.

6. **ClientLayout active nav color** — changed from `bg-primary text-primary-foreground`
   (resolves near-white in dark theme) to `bg-violet-600 text-white` matching app-wide convention.

7. **ClientLayout nav touch targets** — `py-1.5` → `py-2.5 sm:py-1.5` ensures
   mobile touch targets are ≥40px at the breakpoint where short labels are active.

### New shared component
- `frontend/src/shared/components/ResourceEmptyState.tsx`
  Props: `icon`, `heading`, `body`, `action?`.
  Canonical empty-state shell for all resource list pages — use this instead of
  inline flex+icon+p+p+button patterns. `MyStudiosPage` can adopt it in a follow-up.

### Files changed
- `frontend/src/features/designs/components/DesignListPage.tsx` (fix 1–5)
- `frontend/src/layouts/ClientLayout.tsx` (fix 6–7)
- `frontend/src/shared/components/ResourceEmptyState.tsx` (new)
- `frontend/src/shared/components/index.ts` — barrel export
- `frontend/src/features/designs/__tests__/DesignListPage.test.tsx` (+7 tests)
- `frontend/src/shared/components/__tests__/ResourceEmptyState.test.tsx` (new, 5 tests)
- `frontend/src/layouts/__tests__/ClientLayout.test.tsx` — updated stale `bg-primary`
  assertion to `bg-violet-600` after fix 6

## User Manual — 2026-07-05

A single self-contained offline HTML manual covering all five roles.

- File: `frontend/public/user-manual/index.html`
- URL (dev): `http://localhost:5173/user-manual/index.html`
- No external deps — fully offline capable
- Covers: Guest (11 sections), Client (14 sections), Artist (14 sections), Owner
  (30 sections), Issuer (8 sections), and a Reference Glossary (6 sections) — 83
  screen/topic sections plus 1 Introduction section, 84 total
- Integration: see the HTML comment block at the bottom of the file for embed options
- Section IDs follow the pattern `{role}-{feature}` for deep-linking

## Consent Form Detail — Bug Fixes & UI/UX Overhaul — 2026-07-06

### Bugs fixed
- **B-01 (CRITICAL) — Signature rendering:** `signatureData` now detected as image
  (`data:image/` prefix) and rendered as `<img>`, or as italic text for typed names.
  Previously the raw base64 string was injected into a `<p>` node as text.
- **B-02 (CRITICAL) — Raw UUIDs:** `ConsentFormDetailResponse` (new) resolves
  `ClientName`, `AppointmentDate`, `ArtistName` server-side. No UUID is shown to
  end users on the detail page. List page uses `ClientName` from enriched
  `ConsentFormResponse`.
- **B-03 — Timestamp integrity guard:** `GetConsentFormByIdHandler` logs a warning
  when `SignedAt < CreatedAt` so the anomaly is visible in Loki/Grafana.
- **B-04 — WCAG AA contrast:** `DetailRow` labels changed from `text-muted-foreground`
  (~3.8:1) to `text-foreground/65` (~6:1 on dark, verified against #000).
- **B-05 — Missing UX:** Copy-to-clipboard on form ID, Download link for PDF consent,
  link to client profile, link + "Back to appointment" button, `useDocumentMeta` added.
- **B-06 — Not-found state:** 404 responses render a dedicated empty state with
  explanatory text, distinct from the generic error state.

### Architecture decisions
- `ConsentFormDetailResponse` is a separate Contracts record (not an extended type) so
  list endpoints remain lightweight — no forced LEFT JOIN on every list load.
- `ConsentFormResponse` (list) was extended with `ClientName` via SQL projection in
  `GetConsentFormsQuery.Select(f => new ConsentFormResponse(..., f.Client.FirstName + ...))`.
  No `.Include()` needed — EF Core/Pomelo translates the nav-property access to a JOIN.
- `SignatureDisplay` component detects `data:image/` prefix; renders `<img>` or italic
  text accordingly. This handles both the current text-name UI and any legacy
  canvas-signature data in the DB without a migration.
- `useCopyToClipboard` added to `shared/hooks/`, reusable across other entity
  detail pages (appointment ID, client ID, design ID, etc.).
- `formatRelative` is a local helper (not a library) — keeps the dependency count stable.
- **`ConsentForm.Client` and `Appointment.Artist` are required navigations** — EF Core
  translates `Include`/`ThenInclude` on them into an INNER JOIN, not a LEFT JOIN. Since
  both FKs are enforced in the real schema, this can never orphan a row in production,
  but it means test fixtures must always seed a real matching `Client`/`Artist` row (a
  dangling FK in a test silently filters the whole `ConsentForm` out of the result
  instead of surfacing a null navigation).

### Files changed
Backend:
- `Pena_e_Arte.Contracts/Responses/ConsentFormResponse.cs` (extended + new detail record)
- `Pena_e_Arte.Application/ConsentForms/Queries/GetConsentFormByIdQuery.cs` (enriched)
- `Pena_e_Arte.Application/ConsentForms/Queries/GetConsentFormsQuery.cs` (SQL projection)
- `Pena_e_Arte.Application/ConsentForms/Commands/SignConsentFormCommand.cs` (Map updated)
- `Pena_e_Arte.API/Endpoints/FormEndpoints.cs` (return type updated)
- `tests/Pena_e_Arte.UnitTests/ConsentForms/GetConsentFormByIdHandlerTests.cs` (rewritten)
- `tests/Pena_e_Arte.UnitTests/ConsentForms/GetConsentFormsHandlerTests.cs` (Client fixtures)
- `tests/Pena_e_Arte.IntegrationTests/Application/FormHandlerIntegrationTests.cs` (updated)

Frontend:
- `frontend/src/features/forms/form.types.ts` (new ConsentFormDetailResponse)
- `frontend/src/features/forms/consentFormsApi.ts` (updated return type)
- `frontend/src/features/forms/components/ConsentFormDetailPage.tsx` (full rewrite)
- `frontend/src/features/forms/components/ConsentFormListPage.tsx` (clientName)
- `frontend/src/shared/hooks/useCopyToClipboard.ts` (new)
- `frontend/src/features/forms/__tests__/ConsentForms.test.tsx` (fixtures + 6 new tests)
- `frontend/src/features/forms/index.ts` (export ConsentFormDetailResponse)

### Verification
- `dotnet build` — clean
- `dotnet test` — 1086 unit + 289 integration, all green
- `pnpm tsc --noEmit` — clean
- `pnpm test` — 1383 frontend tests, all green (full suite)
- No new EF migration required (no schema change)
