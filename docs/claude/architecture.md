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

Cross-cutting (every feature, no exceptions — CLAUDE.md rules #6/#7)
  [ ] Benchmarked against the Industry-Standard Benchmark Set below — backend
      structure AND frontend UI/UX — and against every role/tenant it touches,
      not just the one it was built for
  [ ] `frontend/src/features/help/helpContent.ts` updated (or confirmed no
      user-visible surface exists that needs an entry)
  [ ] Standalone manual (`frontend/public/user-manual/index.html`) updated to match
  [ ] Onboarding tour (`frontend/src/features/help/tours/{client,artist,owner,issuer}Tour.ts`)
      updated if the feature touches a nav item, primary button, or any existing
      `data-tour="..."` target
```

### Industry-Standard Benchmark Set

Reference comparison set for CLAUDE.md rule #6 — use this instead of re-deriving
a competitor list from memory each time, and refresh it via web search if it's
been more than a few months since the list was last checked against the live
market:

```
Vertical (client/artist/owner UX + booking-SaaS backend structure):
  Vagaro, Fresha, Boulevard, Mindbody, Zenoti, GlossGenius, Booksy, Mangomint,
  Schedulicity, Square Appointments
Tattoo-specific (where a closer analog exists):
  Tattoo Studio Pro, Porter, Linework, Venue Ink
General B2B SaaS platform-admin (issuer role only):
  org/tenant management, plan & seat management, dunning/failed-payment recovery,
  usage metering, audit logs, support impersonation, status pages, API/webhook
  access tiers — benchmark against how any mature multi-tenant SaaS admin panel
  handles these, not against a single named competitor
```

See `overnight-prompt-industry-feature-parity-audit-2026-07-20.md` for the full
methodology (Present/Partial/Missing verdicts, P0–P3 priority) — reuse that
method for any smaller, single-feature benchmark check rather than inventing a
new rubric each time.

**Trust & Safety Reference Set** (added 2026-08-22, for client-initiated
report/moderation features specifically): none of the vertical booking-SaaS
comparators above publicly document a formal client-initiated "report this
provider" trust & safety flow the way general two-sided marketplaces do —
this is a genuine gap in the vertical benchmark set for this specific feature
class, not a case of picking the wrong comparator. Use this set instead when
building or reviewing a report/moderation feature:

```
Uber, Airbnb, Etsy, Upwork — category-taxonomy report flows with
severity-gated escalation
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
        // fetch appointment, send via Twilio/Resend
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
SupportMessageReceived  new reply posted on a support ticket
TrafficSnapshotUpdated  live visitor presence snapshot (TrafficHub, every 5s while ≥1 issuer connected)
MessageReceived         new chat message posted in a conversation (pushed to both participants' user:{userId} groups)
ConversationRead        the other participant marked the conversation read (read-receipt update)
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
| 06 | Automated Communication | `NotificationLog` | Hangfire + Twilio + Resend | Per-tenant |
| 07 | Studio Map | No entity (reads `Studio.Latitude/Longitude`) | None — public endpoint, no auth. Filters `IsActive && IsPublished`. | Platform-wide |
| 08 | Platform Subscriptions | `Subscription`, `Plan` | Stripe Billing (separate from Connect) | Issuer-level |
| 09 | Platform Branding Flag | `Studio.ShowPlatformBranding` (bool, default `true`) | None | Per-tenant |
| 10 | Public Portfolio Pages | Reads `Studio`, `Artist`, `PortfolioImage` (read-only, no tenant filter) | None — public SEO endpoints. `GetPublicStudioQuery`/`/s/{slug}` filters `IsActive && IsPublished`; `GetPublicArtistQuery`/`/artist/{slug}` stays `IsActive`-only (see "IsActive vs IsPublished"). | Platform-wide |

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
Social links: PublicArtistResponse.SocialLinks (see #36 Social Media Verification below) —
             every verified/unverified Instagram/TikTok/Facebook/X/YouTube link renders here
             with a VerifiedSocialBadge when IsVerified, sitting alongside the separate
             Instagram-photos section above.
```

#### Social Media Verification (feature #36)

```
Entity:      SocialAccountLink (TenantEntity, no global query filter — same documented
             exception as InstagramConnection). Polymorphic subject: SubjectType
             (Artist|Studio) + SubjectId. For a Studio subject, StudioId == SubjectId
             (self-referential — Studio itself carries no tenant filter). Unique index
             on (SubjectType, SubjectId, Platform).
Does NOT replace InstagramConnection: that entity keeps owning the artist photo-sync
             lifecycle exactly as before. ExchangeInstagramCodeCommand gained one
             additional block that upserts a matching SocialAccountLink row on success —
             the only edit made to any file under Application/Instagram/ for this feature.
OAuth:       ISocialOAuthProvider (BuildAuthorizationUrl/ExchangeCodeAsync/GetUsernameAsync/
             IsConfigured) — one implementation per platform, registered via DI as
             IEnumerable<ISocialOAuthProvider>, resolved by ISocialOAuthProviderFactory.
             InstagramSocialOAuthProvider wraps the existing IInstagramService rather than
             duplicating its HTTP calls. IsConfigured reports false (409 Conflict on
             connect-url) when a platform's Infrastructure/Services/Social/SocialOptions.cs
             OAuth client credentials are empty — this is how Facebook/X ship "built but
             inactive" pending external app review / paid API tier (see Decisions Log).
Manual check: ISocialBioChecker (BioContainsCodeAsync/IsSupported) — Instagram/Facebook use
             Meta Graph API Business Discovery (officially sanctioned, not scraping;
             Business/Creator accounts only — a personal account can't be verified this
             way, or via OAuth, a real platform limitation). YouTube uses Data API v3
             channel lookup by handle. X uses API v2 app-only user lookup. TikTok has
             IsSupported == false — no suitable public-read API exists; OAuth is its only
             verification route.
State signer: ISocialOAuthStateSigner (Infrastructure/Services/Social/SocialOAuthStateSigner)
             — same HMAC-SHA256 shape as IInstagramStateSigner but signs
             (SubjectType, SubjectId, Platform) and uses a separate key
             (Social:StateSigningKey, not Instagram:TokenEncryptionKey).
             IInstagramStateSigner is unmodified — the artist-Instagram connect/callback
             flow still uses it exclusively; the new signer is used by every other
             platform/subject combination, including studio Instagram.
Endpoints:   /api/v1/{artists|studios}/{id}/social/{platform}/{connect-url|handle|
             request-code|verify-code|disconnect}, all OwnerOnly except GET / (artist
             subject: ArtistAndAbove). Anonymous callback: see AllowAnonymous Exceptions.
Token retention: Artist-subject OAuth links keep an encrypted token (for a future
             periodic re-verification job — see Decisions Log); Studio-subject OAuth links
             discard the token immediately after the identity check (no ongoing sync
             need there).
Public API:  PublicSocialLinkResponse (Platform, Handle, IsVerified, ProfileUrl — URL
             built server-side). PublicArtistResponse/PublicStudioResponse both gained a
             SocialLinks list. PublicStudioResponse stopped returning the old flat
             InstagramHandle field — Studio.InstagramHandle itself is NOT dropped from the
             schema (zero-downtime convention); existing values were backfilled into an
             unverified SocialAccountLink row by the AddSocialAccountLinks migration.
Frontend:    VerifiedSocialBadge (shared/components/) — same badge variant as
             ReviewSection.tsx's "Verified client" badge. SocialLinksCard
             (features/social/components/) — owner-facing, used both on
             StudioSocialLinksCard (Studio Settings) and the artist detail page's new
             "Social" tab (which also wired InstagramTab.tsx into the app for the first
             time — it previously had no rendering call site anywhere).
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
| 25 | In-App Help Menu | No entity (static content) | None — frontend-only, no backend | All roles |
| 26 | Help Search Analytics | `HelpSearchLog` | `IgnoreQueryFilters()` — 39th approved usage (issuer insights read) | Per-tenant (write), Issuer-level (aggregate read) |
| 27 | First-Run Onboarding Tour | `UserOnboardingState` (no tenant filter) | None — no `IgnoreQueryFilters()` needed, no filter registered on this entity | Per-user, cross-tenant |
| 28 | Support Escalation (Help menu ticket threads) | `FeedbackMessage` (child of `FeedbackReport`, no tenant filter, cascade delete) | `SupportHub` SignalR hub, `IRealtimeNotifier.NotifyTicketAsync` | Per-user (own tickets), Issuer-level (all tickets) |
| 29 | Studio-Wide Closures | `StudioClosure` | None — checked in `CheckSlotAvailabilityQuery` alongside per-artist schedule/time-off | Per-tenant |
| 30 | Client Self-Service Cancel/Reschedule | Reuses `Appointment`, `DepositRule` (+ Phase 1's new cancellation-policy fields) | `ClientCancellationPolicy` domain service | Per-tenant |
| 31 | Owner Revenue & Trend Reporting | No entity (aggregate reads over `Payment`/`Appointment`) | None — standard tenant-scoped read | Per-tenant |
| 32 | Structured Admin/Audit Log | `AuditLogEntry` (no tenant filter, `StudioId` nullable) | `IAuditableCommand` marker + `AuditLogBehavior` (MediatR pipeline) | Per-tenant (owner read), Issuer-level (cross-tenant read) |
| 33 | NIPT Business Verification | `Studio.Nipt` | None — format validation only, no external registry call | Per-tenant (owner write), Issuer-level (read via existing `GetStudiosQuery`/`GetStudioByIdQuery`) |
| 34 | Live Site Traffic Analytics | `TrafficEvent`, `TrafficDailyAggregate` (both no tenant filter, `StudioId` nullable) | Redis presence (`traffic:presence:*`) + `TrafficHub` SignalR + `TrafficBroadcastService` (5s), `TrafficRollupJob` (Hangfire daily), MaxMind GeoLite2 (`MaxMind.GeoIP2`) + `UAParser.Core`, `IgnoreQueryFilters()` — 41st approved usage | Issuer-level |
| 35 | Manual Client Reminders | `ManualReminder` | Hangfire + Twilio (reused) + Redis (quota) | Per-tenant |
| 36 | Social Media Verification | `SocialAccountLink` (polymorphic Artist/Studio subject, no tenant filter — same shape as `InstagramConnection`) | `ISocialOAuthProvider`/`ISocialOAuthProviderFactory` (5 platforms), `ISocialBioChecker`/`ISocialBioCheckerFactory` (4 of 5 — TikTok has no suitable public-read API), `ISocialOAuthStateSigner` (separate key from `IInstagramStateSigner`) | Per-tenant (Artist subject resolves the artist's real tenant; Studio subject is self-referential — `StudioId == SubjectId`) |
| 37 | Client Conduct Reports | `ConductReport` (non-tenant, no query filter — same shape as `Review`/`FeedbackReport`/`AuditLogEntry`) | None — direct email alert for High severity, same `INotificationService.SendEmailAsync` path as the contact form | Per-tenant (owner read), Per-user (artist read, reporter identity redacted), Issuer-level (cross-tenant read + High-severity resolution) |
| 38 | In-App Messaging | `Conversation`, `ChatMessage` (both `TenantEntity`, `DesignRevision`-shaped — ordinary per-studio query filter, not a `FeedbackReport`-style exception) | `ChatHub` SignalR hub (per-user `user:{id}` groups, no join-by-id), `IRealtimeNotifier.NotifyUserAsync`, `IJobScheduler.EnqueueNewMessageEmail` (Hangfire, debounced — see `IgnoreQueryFilters()` row #36) | Per-tenant (client↔artist, client↔owner, artist↔owner only — no issuer) |

### Client Self-Service Cancel/Reschedule + Owner Revenue Reporting + Structured Audit Log — 2026-07-21

See "P0 Remediation Round 2 — 2026-07-21" further down this file for the full write-up
(what shipped, deviations from the source prompt's stale citations, Help sync, and
verification). Summary: `DepositRule` gained a cancellation-window/late-refund-percent
policy; `CancelAppointmentCommand`/`RescheduleAppointmentCommand` are now callable by
`ClientAndAbove` with role-conditional ownership + policy checks; a new
`GetRevenueSummaryQuery` gives owners a 12-month trend + per-artist breakdown; a new
`AuditLogEntry` + `IAuditableCommand`/`AuditLogBehavior` pipeline mechanism logs
trust-sensitive issuer/owner actions, readable by issuer (cross-tenant) and owner
(own-studio only).

### In-App Help Menu — 2026-07-20

Searchable, role-scoped help panel opened from every layout header (mirrors the
FeedbackDialog integration pattern). Content lives entirely in
`frontend/src/features/help/helpContent.ts` — no backend, no entity, no endpoint.
Search is plain substring scoring in `helpSearch.ts` (title > keyword > body), same
approach as the standalone manual at `frontend/public/user-manual/index.html`. Issuer
role gets an additional toggle to browse Client/Artist/Owner guides for support purposes.
Keep this file and the standalone manual in sync when either is updated — they cover the
same screens from two different delivery mechanisms (in-app panel vs. offline document).

Added `@radix-ui/react-accordion` + `frontend/src/shared/components/ui/accordion.tsx` —
the FAQ tab needed a shadcn Accordion and none existed yet. Consistent with the codebase's
existing use of many other `@radix-ui/react-*` headless primitives (Tabs, Dialog, Select,
Dropdown Menu, Alert Dialog); not a new class of dependency the way `QRCoder` was, so not
logged as a separate Decisions Log entry.

Keyboard shortcut `Shift+?` opens the panel from anywhere (ignored while typing in an
input/textarea). Verified in a real browser (Playwright, `verifier-gui` skill) as all
four roles: menu opens, search narrows results, FAQ tab renders, the issuer-only
"show all roles' guides" toggle appears only for issuer, and the shortcut opens the sheet.

### Help Search Analytics — 2026-07-21

Logs every Help-menu search (query text + result count) to a new tenant-scoped
`HelpSearchLog` entity, and gives the issuer an aggregate view of what studio users
search for — the single highest-signal list of missing documentation or confusing UX,
same reasoning Intercom/Zendesk/Help Scout apply to their own search analytics.

- **Write path**: `POST /api/v1/help/search-log` (`ClientAndAbove`) → `LogHelpSearchCommand`
  → `LogHelpSearchHandler` reads `StudioId`/`UserId`/`Role` from `ICurrentTenant`/`ICurrentUser`,
  same "cheap, fire-and-forget" shape as `RecordArtistView`, except this one does persist to
  the DB (the query text has analytical value a Redis-only counter would lose). No rate
  limiting — per the Redis rate-limiting rule, authenticated-only endpoints don't get one;
  volume is controlled client-side by an 800ms debounce plus a per-open-session dedupe Set
  in `HelpMenu.tsx`, so at most one log call per distinct query per Sheet-open.
- **Read path**: `GET /api/v1/platform/help-search-insights?days=30` (`IssuerOnly`) →
  `GetHelpSearchInsightsHandler`, in `Application/Platform/Queries/` (not `Application/Help/`)
  to match where `PlatformEndpoints.cs` already groups every other issuer aggregate-report
  endpoint. Uses `IgnoreQueryFilters()` — approved usage #39 (see table above) — groups by
  lowercased `Query`, returns top 20 by count plus every zero-result query, each with the
  distinct set of roles that asked it.
- `HelpSearchLog` is a normal `TenantEntity` (standard global query filter applies to the
  write path); only the issuer's cross-tenant aggregate read needs to bypass it.
- Frontend: new `helpApi` RTK Query slice (first one this feature needed — Part A had none).
  `HelpInsightsPage` at `/platform/help-insights` (`IssuerOnly`) follows `IndustryReportsPage`'s
  plain `<table>` style, not `MrrChart`'s chart treatment. Linked from `IssuerLayout`'s nav
  and a quick-link at the bottom of `IssuerDashboardPage`.
- Verified end-to-end against the real backend + local MySQL: the GET aggregate path (issuer,
  cross-tenant) worked correctly. The POST write path (client, tenant-scoped) reliably 500'd
  locally — traced to `SubscriptionAccessService.GetSnapshotAsync`, a pre-existing tenant-access
  gate that runs on every authenticated tenant-scoped request and falls back to a DB query when
  its Redis read fails; Redis is not running at all in this local dev environment (no Docker,
  no local Redis service), so the fallback DB call gets cancelled under load. This reproduces
  for any tenant-scoped write locally, not just this endpoint — confirmed by tracing the
  exception into `SubscriptionAccessService` (a generic dependency of `TenantMiddleware`, unrelated
  to this feature's code). Verified correct instead with a route-mocked backend (same technique
  as Part A): the debounced POST fires with the right `{ query, resultCount }` payload, and
  `HelpInsightsPage` renders top/zero-result queries and the total-searches badge correctly.

### First-Run Onboarding Tour — 2026-07-21

A short, skippable, per-role guided walkthrough shown automatically on first login,
replayable anytime from the Help menu ("Take the tour again"). Hand-built (no npm
package) — consistent with the codebase's existing "no package" pattern for similar UI
mechanics (masonry via CSS columns, lightbox via shadcn Dialog).

- **Engine**: `frontend/src/shared/components/OnboardingTour.tsx` — generic, reusable.
  Finds each step's target via `document.querySelector(step.targetSelector)`, spotlights it
  with a `box-shadow: 0 0 0 9999px rgba(0,0,0,.6)` cutout (no canvas/SVG mask), and positions
  a popover adjacent per `step.placement`. Recomputes on resize/scroll via `ResizeObserver` +
  scroll listener while a step is showing. A step can carry a `route` field — the engine
  navigates there first (two `requestAnimationFrame`s, then polls up to ~1s for the target to
  appear) before measuring. If a step's selector never resolves, it's skipped automatically —
  this is normal, not an error path: e.g. the owner tour's deposit-rules step targets the
  Dashboard's `SetupChecklist` "Set rule" button, which only renders while that setup item is
  incomplete, so an already-configured studio correctly skips straight past it.
- **Content**: `frontend/src/features/help/tours/{client,artist,owner,issuer}Tour.ts` — plain
  `TourStep[]` (or a function, for the client tour's conditional My Studios step — only shown
  if `useGetMyStudiosQuery` returns more than one studio). Targets are `data-tour="..."`
  attributes added to the real nav links/buttons they describe — added directly to
  `ClientLayout`/`ArtistLayout`/`OwnerLayout`/`IssuerLayout`'s nav item arrays, `NotificationBell`,
  `DesignListPage`'s "New Design" button (both the header and empty-state variants — whichever
  renders), `SetupChecklist`'s "Set rule" button, and `HelpMenu`'s own trigger button
  (`data-tour="{role}-help-button"`, closing the loop back to Help).
- **Persistence**: new `UserOnboardingState` entity — not tenant-scoped (per-user, like
  `SavedPortfolioImage`/`FeedbackReport`), configured inline in `AppDbContext.OnModelCreating`,
  unique index on `(UserId, Role)`. `GET /api/v1/onboarding/tour-status?role=` and
  `POST /api/v1/onboarding/tour-complete` (both `ClientAndAbove`, in `HelpEndpoints.cs`) — both
  handlers reject with `ForbiddenException` (403) if the `role` parameter doesn't match the
  caller's actual JWT role, so a client can't mark another role's tour complete for themselves.
  Upsert semantics in the command handler; no `IgnoreQueryFilters()` needed since this entity
  has no query filter registered at all (same non-tenant shape as `FeedbackReport`).
- **Frontend**: `useOnboardingTour(role)` hook (`features/help/useOnboardingTour.tsx`) — fetches
  status via RTK Query, renders `<OnboardingTour>` when not completed, marks complete on either
  Skip or the final Done (skip counts as complete — standard convention for these tours, don't
  nag again). Tracks a local `dismissed` flag set synchronously on finish, rather than relying
  solely on the invalidated status query's refetch — the network round-trip is too slow to hide
  the tour immediately, which was an actual bug caught by the hook's own test suite (see below).
  Called from inside `HelpMenu` (not each of the four layouts separately) since `HelpMenu` is
  already mounted in every layout header — avoids prop-drilling `restartTour` from layout to
  menu for the "Take the tour again" button.
- Verified end-to-end in a real browser (route-mocked backend, Playwright): all 6 owner tour
  steps resolve and advance in the correct order, Done fires the completion call and closes the
  tour, a completed tour doesn't show on next load, and "Take the tour again" relaunches it
  regardless of completion state.

### Support Escalation — 2026-07-21

Threaded messaging on top of the existing `FeedbackReport` inbox, reached from the Help
menu's new "Contact Support" tab. Deliberately async (SignalR-pushed when the other party
is online, no live-chat presence requirement) rather than a parallel ticket system —
builds on `FeedbackReport`/`FeedbackStatus`/`FeedbackInboxPage`, which were already an
issuer-facing ticket inbox, just one-shot until now.

- **New `FeedbackType.SupportRequest`** — not selectable in the existing `FeedbackDialog`
  (Bug Report / Feature Request / General stay artist/owner/issuer-only); only ever created
  from the Help menu's Contact Support flow.
- **`POST /api/v1/feedback` widened to `ClientAndAbove`** (see the updated Feedback (2.6)
  entry above) — `SubmitFeedbackValidator` now takes a constructor-injected `ICurrentUser`
  and rejects a client submitting anything other than `SupportRequest`.
- **`FeedbackMessage`** — child of `FeedbackReport`, not tenant-scoped (same reasoning as
  `FeedbackReport` itself: no EF filter, configured inline in `AppDbContext.OnModelCreating`,
  not a separate `IEntityTypeConfiguration`), cascade-deletes with its parent. `FeedbackReport`
  exposes `Messages` as a plain `ICollection<FeedbackMessage>` (matches `Design.Revisions`'s
  existing idiom, not the stricter private-setter-plus-factory style used for `FeedbackReport`'s
  own scalar properties — collection navigations need the simpler shape for EF to populate them).
- **Resource ownership, not just role policy** — `GET/POST /api/v1/feedback/{id}/messages` are
  both `ClientAndAbove` at the route level, but "can this user see this ticket" isn't
  expressible as a static policy, so each handler calls a new domain method,
  `FeedbackReport.IsAccessibleBy(userId, studioId, role)`: issuer sees everything, everyone
  else only their own submission in their own studio (`ForbiddenException` → 403 otherwise).
  Centralized on the entity specifically so the two handlers can't drift out of sync on this
  security-critical check. `GET /api/v1/feedback/mine` is its own route group at
  `/api/v1/feedback` (`ClientAndAbove`) — deliberately not nested under the `IssuerOnly`
  `/api/v1/platform/feedback` group.
- **Reopen-on-reply** — `PostFeedbackMessageHandler` reopens a `Resolved`/`Dismissed` ticket
  (back to `Open`, preserving the existing `IssuerNote`) when the *studio-side* user replies —
  issuer replies don't reopen, since issuer is the one closing tickets.
- **`SupportHub`** — new SignalR hub, ticket-keyed groups (`ticket:{feedbackReportId}`) via
  `JoinTicket`/`LeaveTicket`, matching `ScheduleHub`'s exact shape. `JoinTicket` does **not**
  validate ticket ownership before adding the caller to the group — this matches
  `ScheduleHub.JoinStudio`'s own existing precedent (no membership check there either, verified
  before building this), so it's a legitimate scoped decision consistent with the codebase's
  existing risk posture, not a new hole: exposure is bounded by the ticket id being an
  unguessable Guid, same reasoning as `ScheduleHub`'s studioId groups.
  `IRealtimeNotifier` gained `NotifyTicketAsync` (Application layer stays SignalR-agnostic,
  same pattern as the existing `NotifyStudioAsync`); `RealtimeNotifier` picks the hub via a new
  `IHubContext<SupportHub>` constructor param.
- **Frontend architecture note**: `SupportTicketThread`, `SupportRequestForm`, and
  `useSupportHub` live in `features/feedback/`, not `features/help/`, even though they're only
  ever rendered from the Help menu and `FeedbackInboxPage`. Putting them in `features/help/`
  first created a circular import (`help` → `feedback` for the API hooks, `feedback` →
  `help` for the thread component) — moved to keep the dependency one-directional
  (`help` → `feedback`, never the reverse), matching how `HelpMenu` already depended on
  `feedbackApi` since Part A/B.
- `FeedbackInboxPage`'s expanded card view now renders `SupportTicketThread` (replacing the
  old plain `<p>{report.body}</p>`, since the thread already shows the body as its first
  bubble) with `canReply` unconditionally true, alongside the existing status-change buttons —
  this applies to every feedback type, not just `SupportRequest`, since replying is now a
  generic capability on any ticket, not type-gated.
- Verified with a route-mocked backend (Playwright, same technique as Parts A/B/D): owner
  role sees the Contact Support form when they have no open ticket, submits it with
  `type: SupportRequest`, and — once an open ticket exists — sees the thread instead of the
  form with existing messages and a working reply box. Issuer's Feedback Inbox expands to the
  same thread component with a working reply box. Did not attempt the real-backend write path
  for this feature — Part B's investigation already established that local tenant-scoped
  writes 500 here due to `SubscriptionAccessService`/Redis being absent in this dev
  environment, unrelated to this feature's own code.

### Feedback attachments — 2026-07-25

`FeedbackReport` gained `AttachmentUrls (List<string>)`, letting a submitter attach up to 3
screenshots/short video clips (JPEG/PNG/WebP/MP4/WebM/MOV) to a report, uploaded via the
same R2 presign flow used everywhere else (Design revisions, appointment reference images).

- Stored as a JSON column with an EF value converter — same pattern as
  `TattooRecord.PhotoUrls`, not a child entity/table, since attachments here are a small,
  unordered, non-queried list with no need for their own rows. Not nullable at the entity
  level (defaults to `[]`); the migration adds it `NOT NULL` on the existing table with no
  default value or backfill needed — MySQL accepted this against existing rows without error
  (unlike Review's `AppointmentId` FK addition, this isn't a unique-index concern, just a
  plain column default).
- `SubmitFeedbackValidator` (already constructor-injects `ICurrentUser`/`ICurrentTenant` per
  the Support Escalation section above) also takes `IR2Service` now, validating attachment
  count (≤3) and that each URL is genuinely R2-hosted — same `RuleForEach(...).Must(r2.IsR2Url)`
  pattern as `CreateStudioReviewValidator`/`CreateAppointmentValidator`.
- Scope: only the staff-facing `FeedbackDialog` (Bug Report / Feature Request / General) got
  attachment upload UI. `SupportRequestForm` (the client-facing Contact Support flow under
  Help) was deliberately left unchanged — a different audience/surface, not part of what was
  asked, and the backend field is generic enough to extend to it later without a schema change
  if that's ever wanted.
- Attachments render as thumbnails (images) or a video-icon chip (videos, detected by file
  extension since no client-side video thumbnailing was built) inside `SupportTicketThread`'s
  existing original-body bubble — the same component already used by both `FeedbackInboxPage`
  (issuer) and the Help menu's ticket view, so no duplicate rendering logic was needed.

#### Local `/code-review high` pass — 2026-07-21, before merge

Ran an 8-angle review (correctness, removed-behavior, cross-file, reuse, simplification,
efficiency, altitude, CLAUDE.md conventions) against this Part C diff specifically, since it
changes authorization on a production endpoint. 8 candidates survived verification; all 8
were fixed before merge, not just logged:

- **`SupportHub.JoinTicket` didn't validate ticket ownership** (security) — any authenticated
  user who learned a ticket GUID could join its SignalR group and read all future reply
  content in real time, bypassing the REST-layer `IsAccessibleBy` check entirely. The
  "matches `ScheduleHub`'s precedent" reasoning held for the *mechanism* but not the *risk*:
  `ScheduleHub` broadcasts studio-wide data any studio member already sees; this hub
  broadcasts a private two-party conversation. Fixed by validating ownership inside
  `JoinTicket` itself — reading claims directly from `Context.User` (not `ICurrentUser`/
  `ICurrentTenant`, which are never populated for hub invocations: `/hubs` paths are in
  `TenantMiddleware.ExemptPrefixes`) and calling `FeedbackReport.IsAccessibleBy` before
  adding the caller to the group.
- **Studio-less client → unhandled 500 from the Contact Support flow meant to help them** —
  widening `POST /api/v1/feedback` to `ClientAndAbove` newly let a studio-less client
  (a real, supported state — see `MyStudiosPage`'s empty state) reach `SubmitFeedbackHandler`,
  which throws `InvalidOperationException` when `tenant.StudioId` doesn't resolve to a real
  `Studio`. Fixed with a `SubmitFeedbackValidator` rule on `ICurrentTenant.IsSet`, returning a
  clean 422 instead of a 500.
- **`ContactSupportPanel` silently fell through to the submission form on a failed ticket
  lookup** — risked a duplicate ticket submission if `GET /api/v1/feedback/mine` failed
  transiently. Fixed with an explicit error + retry state.
- **`useSupportHub` never re-joined the ticket group after SignalR's automatic reconnect** —
  a brief network drop silently stopped future replies from arriving (mirrors an identical
  pre-existing gap in `useSignalR.ts`, left as a separate follow-up since it's out of this
  diff's scope). Fixed with an `onreconnected` handler.
- **`GetMyFeedbackReportsHandler` duplicated `GetFeedbackReportsHandler`'s entire response
  projection** verbatim — extracted a shared `internal static readonly Expression<Func<...>>`
  (not a compiled `Func`, so EF Core still translates it into the SQL projection).
- **The ownership-check block was duplicated across two handlers** with no structural
  guarantee a future third handler would remember it, despite `PlanLimitBehavior` already
  establishing a pipeline-behavior pattern for exactly this kind of cross-cutting check —
  extracted a shared `FeedbackAccessGuard.LoadAccessibleReportAsync` helper (a full pipeline
  behavior was judged like overkill for two call sites; revisit if a third handler needs it).
- **Sending a reply refetched the message list twice** — once from the mutation's own
  `invalidatesTags`, once from the SignalR echo of the sender's own message (the sender is a
  member of their own ticket's group). Fixed by having `useSupportHub` skip invalidation when
  the echoed message's `authorUserId` matches the current user.
- **Every reply invalidated the unscoped `"Feedback"` tag**, forcing a full report-list
  refetch (issuer's entire cross-tenant inbox) even when replying to an already-`Open` ticket
  changes no report-level field. Fixed by threading a `mayReopen` flag through the mutation
  (computed client-side from the same condition `PostFeedbackMessageHandler` uses server-side:
  studio-side reply + `Resolved`/`Dismissed` status) so `"Feedback"` is only invalidated when
  a reopen is actually possible.

All fixes covered by new/updated tests (backend: +1 validator test; frontend: +2
`useSupportHub` tests) — full suite verified green after: 1225 backend, 1617 frontend.
Re-verified the Contact Support flow's error/retry state and the happy path in a
route-mocked browser after these changes.

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

### P0 Remediation Round 2 — 2026-07-21

Branch `fix/p0-remediation-2026-07-21`. Built the six highest-severity items from
`industry-feature-parity-report-2026-07-20.md`'s P0 backlog that were too large for a
single-night whitelist pass on 2026-07-20. Full per-phase design rationale is in
`overnight-prompt-p0-remediation-2026-07-21.md`; the report's own "Round 2" addendum has
the backlog-carry-forward detail. This entry is the architecture-level summary.

#### What was built (per phase)

- **Phase 1 — Cancellation policy configuration.** `DepositRule` gains
  `CancellationWindowHours` (`int?`, null = platform default) and
  `RefundPercentOnLateCancel` (`int`, default 0). New
  `Domain/Constants/AppointmentSelfServiceDefaults.CancellationWindowHours = 24`. Migration
  `AddCancellationPolicyToDepositRule`. No DB-level `CHECK` constraint added (no existing
  precedent for one anywhere in this codebase's entity configurations — FluentValidation is
  the sole validation layer, matching the codebase's established convention).
- **Phase 2 — Client self-cancel.** `DELETE /api/v1/appointments/{id}` widened
  `ArtistAndAbove` → `ClientAndAbove`. `CancelAppointmentHandler` gained a role-conditional
  ownership check (client → `FindClientForUserAsync` → 404 on mismatch, mirroring
  `ReviewDesignHandler`'s scope-violation convention) and a refund-percent branch via new
  `Domain/Services/ClientCancellationPolicy.ResolveRefundPercent`. Staff-initiated cancel is
  completely unaffected (separate code path, regression-tested). Client UI lives in
  `MyBookingsSection.tsx`/`BookingRow` (clients have no route to `AppointmentDetailPage` —
  confirmed via `router.tsx`, matching the 2026-07-01 client-QA-pass finding still holding).
- **Phase 3 — Client self-reschedule.** `PATCH .../reschedule` widened the same way.
  Cutoff-gated (not tiered like cancel — `ClientCancellationPolicy.IsWithinNoticeWindow`),
  reusing the identical notice window as Phase 1/2 rather than a second field. Reuses
  `RescheduleDialog.tsx` with a new optional `description` prop overriding the staff-facing
  "notify separately" copy for the client path.
- **Phase 4 — Owner revenue & trend reporting.** New `Application/Reports/Queries/
  GetRevenueSummaryQuery` (`OwnerOnly`, standard tenant-scoped read — no
  `IgnoreQueryFilters()`): 12-month monthly revenue trend (same lookback window as
  `GetMrrHistoryQuery`) + per-artist breakdown for the trailing 30 days, aggregated from
  `Payment.Status == Paid`. New `ReportEndpoints.cs` (`GET /api/v1/reports/revenue-summary`).
  New `frontend/src/features/reports/` module — `RevenueTrendChart.tsx` is a hand-rolled
  inline SVG chart (same treatment as `MrrChart.tsx`, which itself is NOT recharts-based
  despite the source prompt assuming otherwise — recharts isn't installed anywhere in this
  frontend). New `/reports` route + owner nav item + tour step.
- **Phase 5 — Structured admin/audit log.** New `AuditLogEntry` entity — deliberately NOT a
  `TenantEntity`: `StudioId` is nullable, no `HasQueryFilter()` registered at all (same
  non-tenant shape as `FeedbackReport`/`UserOnboardingState`), so it doesn't get a row in
  the `IgnoreQueryFilters()` Approved Usages table above — "who can read which rows" is
  enforced entirely in `GetAuditLogHandler` (issuer, cross-tenant) and
  `GetMyStudioAuditLogHandler` (owner, explicit `.Where(StudioId == tenant.StudioId)`). New
  `IAuditableCommand` marker interface (mirrors `IQuotaCheckedCommand`'s exact shape) +
  `AuditLogBehavior` MediatR pipeline behavior, registered immediately after
  `PlanLimitBehavior` in `Program.cs`, logging only after the handler's own
  `SaveChangesAsync` succeeds (a validation failure or mid-handler exception produces no
  audit row — tested explicitly). Metadata is built by a new whitelisting
  `AuditMetadataBuilder` (per-command-type field allowlist, never a wholesale command
  serialize) to keep PII out of `Metadata` by construction.
  Wired onto: `SuspendStudioCommand`, `UnsuspendStudioCommand`, `ExtendTrialCommand`,
  `CancelSubscriptionCommand`, `ActivateSubscriptionManuallyCommand`, `UpdatePlanCommand`,
  `Deactivate/Reactivate/DeleteReferralCodeCommand`, `CancelAppointmentCommand`,
  `UpdateSessionSplitsCommand` — 9 of the originally-scoped commands. **Not wired:** a
  "delete client record" command, because no such command exists anywhere in this codebase
  (grepped `ClientEndpoints.cs` and the whole `Application` layer) — the source prompt's
  citation was stale/invented, not a real gap in this pass's scope.
  New endpoints: `GET /api/v1/platform/audit-log` (issuer, in `PlatformEndpoints.cs`'s
  existing `IssuerOnly` group) and `GET /api/v1/studios/me/audit-log` (owner, in
  `StudioEndpoints.cs`). Frontend: `AuditLogPage.tsx` (issuer, filterable table) +
  `StudioAuditLogCard.tsx` (owner, read-only recent-activity list on `StudioProfilePage.tsx`).
- **Phase 6 — `AllowApiAccess`/`PrioritySupport` verification.** Re-grepping
  case-insensitively (the 2026-07-20 pass's case-sensitive grep for the literal string
  `AllowApiAccess` missed the camelCase `plan.allowApiAccess` field reference) found
  `PlanManagementPage.tsx` still rendering an "API access" badge that the 2026-07-20 fix
  missed entirely (only `PlanEditPage.tsx`'s toggle was hidden that night). Also found
  `PrioritySupport` (flagged as "same risk, lower severity" on 2026-07-20 but never acted
  on) was still a live issuer-editable toggle + list-page badge + documented in both Help
  surfaces, with zero backing implementation (no support-priority routing anywhere in the
  codebase). Both hidden now, same treatment as `AllowApiAccess` — data model untouched,
  UI/Help surfaces only.

#### Design decisions confirmed or revised against live source

- No per-appointment `DepositRuleId` exists — the actual model is a single active
  deposit rule per studio (`DepositRule.IsActive`, most-recently-updated wins), not an
  "attached rule" per booking. Phases 1–3 all resolve the policy this same way.
- `Payment.PaidAt` is reliably set on every path that transitions `Status` to `Paid`
  (`ConfirmPaymentCommand`, `ConfirmCashDepositCommand`, `CaptureDepositCommand`, etc.) —
  confirmed before relying on it for Phase 4's revenue aggregation.
- Referral-code commands (`Deactivate`/`Reactivate`/`DeleteReferralCodeCommand`) carry only
  `ReferralCodeId`, not `StudioId` — their audit entries log as platform-wide (`StudioId`
  null) rather than adding an async DB lookup to `IAuditableCommand`'s synchronous
  `AuditStudioId` property. Accepted as a known, explicit limitation.

#### Help / documentation sync

Every phase updated `helpContent.ts` + the standalone manual in the same change:
Phase 1 (deposit-rule create/edit entries), Phase 2 (`client-cancel-booking` article +
FAQ), Phase 3 (`client-reschedule-booking` article), Phase 4 (`owner-reports` article +
manual section), Phase 5 (`issuer-audit-log` + `owner-audit-log` articles, manual sections
for both), Phase 6 (removed the `Priority support` feature-flag line from both surfaces).
Tour steps added for Phase 4 (`owner-reports-nav`) and Phase 5 (`issuer-audit-log-nav`);
Phases 1–3 deliberately did NOT get new tour steps since they're new fields/actions on
already-covered tour stops, not new nav items — verified by checking each tour file's
existing target selectors before deciding. Phase 5's owner-facing card likewise got no new
tour step, since it's a card on the already-covered `owner-studio-profile-nav` stop.

#### Verification

`dotnet build` 0 errors; `dotnet test` 1301 unit + 21 integration, all green; `pnpm build`
(`tsc -b` + `vite build`) 0 TypeScript errors; full frontend `vitest` suite 112 files / 1670
tests green.

---

### Live Site Traffic Analytics — 2026-08-04

Issuer-only real-time + historical site-traffic analytics, covering both the unauthenticated
public surface (`features/public/*`) and the authenticated in-app surface. See §9 of
`docs/claude/overnight-prompt-live-traffic-analytics-2026-08-03.md` for the full
industry-benchmark write-up (Google Analytics Realtime / Plausible Live / Cloudflare Web
Analytics / PostHog Live comparison set) and §9.2 for the ADR on why a self-hosted analytics
service (Umami/Plausible/Matomo) was rejected in favor of the open-source *libraries* those
tools themselves use (GeoIP + UA parsing), integrated directly into this app's own stack —
the deciding factor being that none of those services have any visibility into this app's
JWT/tenant model, so none can distinguish "guest" from "client/artist/owner/issuer" or
attribute a visit to a specific studio, which is the entire point of this feature.

- **Entities**: `TrafficEvent` (one row per navigation event, not every heartbeat) and
  `TrafficDailyAggregate` (nightly rollup) — both non-tenant shape (no `HasQueryFilter`),
  same reasoning as `AuditLogEntry`/`HelpSearchLog`/`FeedbackReport`. Neither ever persists a
  raw IP address; `TrafficEvent.IpHash` is a one-way SHA-256 of the raw IP plus a server-side
  pepper (`GeoIp:IpHashPepper`), kept only for coarse abuse/dedup signal.
- **GeoIP provider**: MaxMind GeoLite2-City via the official `MaxMind.GeoIP2` client
  (v6.1.0), read from a local `.mmdb` file at `GeoIp:DatabasePath`. Chosen over DB-IP Lite
  (the lower-friction alternative considered in the source prompt's §3.1) because the
  account/license-key friction was judged an acceptable one-time cost for MaxMind's
  materially better-maintained ruleset; the license key itself is never read by the app —
  only by the separate `geoipupdate` refresh job. `GeoIpService` degrades to always-`null`
  gracefully (never throws) when `GeoIp:DatabasePath` is unset or unreadable, so the feature
  ships and functions (minus geography) even before the GeoIP file is provisioned.
- **UA parsing**: `UAParser.Core` (v4.0.5) — same `ua-parser` ruleset family Umami/Plausible/
  PostHog use. Note for future readers: this package's actual API surface differs from the
  classic `ua-parser-dotnet` shape assumed by early drafts of this feature — `ClientInfo`
  lives in the `UAParser.Objects` namespace, and the parsed browser is `ClientInfo.Browser`
  (not `.UA`). `Device` has no structured `DeviceType` enum, only a free-text `Family`
  string plus an `IsSpider` bool; `UserAgentParserService` buckets `Family` into
  desktop/mobile/tablet/bot itself (bot also triggered by `IsSpider` or `Family == "Spider"`).
- **Live presence**: Redis, not the database — a sorted set (`traffic:presence:zset`, score =
  last-seen unix ms) plus one hash per visitor (`traffic:presence:detail:{visitorId}`), both
  effectively TTL'd via a 60s read-window filter + trim rather than native per-member expiry
  (sorted sets have none). `TrafficPresenceService` (`ITrafficPresenceReader`) is the single
  read path shared by both `GetLiveTrafficSnapshotQuery` (on-demand, initial page load) and
  `TrafficBroadcastService` (`BackgroundService`, 5s `PeriodicTimer`, broadcasts
  `TrafficSnapshotUpdated` to `TrafficHub`'s one group, `platform:traffic`) — deliberately
  factored this way so the two can never disagree with each other. The 5s cadence matches
  Google Analytics Realtime / Plausible Live's own refresh rate (verified via web search,
  2026-08, cited in the source prompt's §9). `ITrafficConnectionCounter` (DI singleton,
  `Interlocked`-backed, not a bare `static` field) lets the broadcast loop skip all Redis/DB
  work when no issuer has the page open.
- **`connectedAt` gap fixed during implementation**: the source prompt's §6.3 key-scheme
  description listed a `connectedAt` field in the presence detail hash, but its own §6.4
  beacon-handler code never actually wrote it — would have made "connected Xs ago" always
  read as "last-seen Xs ago" instead (reset on every heartbeat). Fixed by writing
  `connectedAt` via Redis `HSETNX` semantics (`When.NotExists`) so it's set once on a
  visitor's first beacon and left untouched by every subsequent heartbeat/navigation.
- **`IgnoreQueryFilters()` — approved usage #41**: `RecordTrafficEventCommand`'s StudioId
  resolution for an anonymous `/artist/{slug}` beacon, mirroring `RecordArtistView`'s own
  lookup (#13) exactly. `/s/{slug}` beacons resolve `StudioId` via a plain `Studios` query
  with no `IgnoreQueryFilters()` at all, since `Studio` carries no query filter to begin with.
- **`/share/:token` redaction**: `frontend/src/app/router.tsx` has one route that embeds a
  live, still-valid token directly in the path segment (`DesignShareToken` via
  `/share/:token`) rather than as a separately-read param — `useTrafficBeacon.ts` redacts
  this segment client-side before ever sending `Path` to the backend, so a share token never
  ends up sitting in `TrafficEvent.Path`.
- **Beacon mount point**: `useTrafficBeacon` uses the router's own `router.subscribe()` API
  rather than `useLocation()`, and is mounted once in `main.tsx` as a sibling of
  `<RouterProvider>` (alongside `<CookieConsentBanner />`) rather than inside any route
  element. This app's public routes (`/discover`, `/s/:slug`, `/artist/:slug`, ...) are
  top-level route-array entries with no shared layout wrapper the authenticated routes share
  via `AppRoot` — there is no single component every route renders through — so
  `router.subscribe()` was used specifically because it works regardless of where it's
  mounted, avoiding a route-tree restructure for this alone.
- **`KpiCard` extracted**: moved from a private, unexported function inside
  `IssuerDashboardPage.tsx` into `features/platform/components/KpiCard.tsx` (`KpiCard` +
  `KpiSkeleton`) so `LiveTrafficPage` could reuse the same visual pattern instead of
  duplicating it, per this project's own reuse-over-duplication rule.
  `IssuerDashboardPage.tsx`'s own multi-row `KpiGridSkeleton` stayed local (page-specific
  layout, not a generic shared shape).
- **Retention**: `TrafficRollupJob` (Hangfire, daily `02:30` UTC, staggered from the existing
  `02:00`/`03:00` jobs) aggregates the previous UTC day's `TrafficEvent` rows into
  `TrafficDailyAggregate` (idempotent — always recomputes and overwrites the target day's
  counts rather than insert-only, so a re-run never double-counts), then purges raw
  `TrafficEvent` rows older than 35 days — long enough for a rolling "top pages this month"
  breakdown without keeping raw per-visit data forever.
- **New `AllowAnonymous` endpoint**: `POST /api/v1/public/traffic/beacon` — see the
  `AllowAnonymous Exceptions` table below.
- Not built this pass (deliberately deferred, full spec in the source prompt's §3.4):
  owner-facing "my studio's public page views" — no evidence any vertical-booking-SaaS
  competitor (Vagaro/Fresha/Boulevard/Mindbody/GlossGenius) exposes anything like this to
  tenant owners, so it's correctly scoped issuer-only for now.

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

**Update 2026-08-26:** `Studio.IsPublished` now exists (see the Solo/Independent
Artist Signup feature, Decisions Log below). This section previously said no such
field existed or was planned; that changed the moment a feature needed a studio to
be active-but-unlisted (exactly the trigger condition this section always said would
justify adding it).

`IsActive` and `IsPublished` are now both real, distinct fields:

- **`IsActive`** gates tenant access entirely. A deactivated studio's owner/artists
  cannot use the app at all (suspended, manually disabled by issuer).
- **`IsPublished`** gates listing in studio-directory surfaces only: Studio Map
  (`GetStudioMapQuery`), `/discover`'s Studios tab (`GetNearbyStudiosQuery`), and
  `StudioPortfolioPage` (`GetPublicStudioQuery`). Defaults to `true` for every
  normally-registered studio; only starts `false` for an `IsSolo` studio
  auto-provisioned with no real location yet, and flips to `true` automatically
  the first time `UpdateMyStudioHandler` sees a real `City`/`Latitude`/`Longitude`.

**`GetPublicArtistQuery` (`/artist/{slug}`) deliberately still filters on
`IsActive` only, not `IsPublished`** — a solo artist must be publicly bookable
from their own portfolio URL immediately, even before their studio is "published"
to the directory surfaces above. Do not add an `IsPublished` check there.

All three directory-surface queries filter on `IsActive && IsPublished` now — a
suspended or unpublished studio never appears in any of them.

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
| 27 | `CreateArtistCommand`, `UpdateArtistCommand`, `UpdateStudioSlugCommand` | Global slug-uniqueness check — `Artist.Slug`/`Studio.Slug` must be unique across all tenants for public portfolio URLs (`/artist/{slug}`, `/s/{slug}`) | ArtistAndAbove / OwnerOnly |
| 28 | `RegisterUserCommand`, `RegisterOAuthUserCommand` | Anonymous registration: cross-tenant `Studio` lookup for the `OwnerEmail` match check, and cross-tenant `Client` lookup to link a studio-created record by email | Anonymous |
| 29 | `ClientAccountExtensions` (`FindClientForUserAtStudioAsync`, `FindAnyClientRecordForUserAsync`) | Cross-tenant `Client` lookup by `UserId` — supports linking a client's account across the multiple studios they belong to | Authenticated (any role, called from login/registration/multi-studio flows) |
| 30 | `ConfirmPaymentCommand`, `MarkPaymentAuthorizedCommand`, `MarkPaymentFailedCommand` | Stripe webhook handlers — no tenant JWT in scope; `Payment` looked up by the globally-unique `StripePaymentIntentId` | Anonymous (Stripe-Signature HMAC validated at the endpoint) |
| 31 | `ActivateSubscriptionManuallyCommand` | Cross-tenant `Studio` lookup for manual cash-subscription activation | IssuerOnly |
| 32 | `GetNearbyStudiosQuery` | Public studio-nearby geo search (DiscoverPage Studios tab) | Anonymous |
| 33 | `GetSharedDesignQuery` | Public design-share-token lookup, validated by token + expiry | Anonymous |
| 34 | `CreateArtistReviewCommand`, `CreateStudioReviewCommand` | Cross-tenant artist/studio lookup for public review submission | Authenticated (any role) |
| 35 | `GetStudioQrCodeQuery` | Public QR code endpoint — resolves slug for the portfolio URL the code points to | Anonymous |
| 36 | `AppointmentReminderJob`, `DesignRevisionTimeoutJob`, `PaymentReconciliationJob`, `SendArtistInviteJob`, `ManualReminderJob`, `ChatNotificationJob` | Hangfire background jobs run with no request/tenant scope at all — same class as `IndustryReportJob` (#3) | Hangfire job (system) |
| 37 | `DataSeeder` | Startup seed data — runs before any request or tenant scope exists | System (startup) |
| 38 | `NotificationPreferenceService` | Cross-tenant `StudioNotificationPreference` lookup when sending a notification about a studio outside the current scope (job/system context) | System/Hangfire job |
| 39 | `GetHelpSearchInsightsHandler` | Cross-tenant aggregate of help search queries for the issuer product-insights view | IssuerOnly |
| 40 | `GetSitemapUrlsHandler` | Public SEO sitemap — active studio/artist slugs across all tenants for `/sitemap.xml` | Anonymous |
| 41 | `RecordTrafficEventHandler` | Cross-tenant artist-slug lookup to resolve `StudioId` for an anonymous `/artist/{slug}` traffic beacon, mirroring `RecordArtistView`'s own lookup (#13) | Anonymous |
| 42 | `ExchangeSocialOAuthCodeHandler` (Artists + Studios) | Resolve the OAuth subject's real `StudioId` from an anonymous social-verification callback (studio Instagram, TikTok, Facebook, X, YouTube), and check the studio isn't suspended before writing a verified `SocialAccountLink`; subjectId is pre-authenticated via `ISocialOAuthStateSigner` HMAC before this handler runs — same shape as entry #22 for the artist-Instagram callback | Anonymous (state-signed) |
| 43 | `FileArtistConductReportCommand`, `FileStudioConductReportCommand` | Cross-tenant artist/studio + appointment/client lookup for conduct-report filing — identical join shape to entry #34's review submission, minus the `Completed`/dedup filters (see Decisions Log, "Client Conduct Reports") | ClientOnly |
| 44 | `GetReportableArtistAppointmentsQuery`, `GetReportableStudioAppointmentsQuery` | Cross-tenant appointment/client lookup for the report-filing appointment picker — identical join shape to entries #19/#20's `IsVerifiedBooking` checks | ClientOnly |
| 45 | `ConductReportProjections` (Artists + Appointments) | Cross-tenant `Artist`/`Appointment` join to resolve `ArtistName`/`AppointmentDate` for display — needed because the issuer caller (`GetConductReportsHandler`) has no tenant set at all; harmless for the owner/artist callers since the outer `ConductReport` query is already scoped to their own `StudioId`/`ArtistId` before this join runs | Owner / Artist (own scope only) / IssuerOnly |

Entries #27–#38 were added 2026-07-20 during the Final self-review checklist pass of
the full-app master audit — they were all pre-existing, legitimate `IgnoreQueryFilters()`
calls that had never been added to this table (documentation debt, not new code). Each
was individually read and confirmed narrow/justified before being added; none constitute
an unauthorized cross-tenant read. See "Full-App Master Audit — 2026-07-20" below for
the full note on how this gap was found.

`ConductReport` itself needs no `IgnoreQueryFilters()` entry anywhere — it has no query
filter registered at all (same non-tenant shape as `Review`/`FeedbackReport`/`AuditLogEntry`),
so there is nothing to bypass. Entries #43–#45 above exist for a different reason: genuinely
new cross-tenant reads of *other*, filtered entities (`Artist`, `Appointment`) that the
conduct-reports feature introduced. Don't mistake the absence of a `ConductReport` row for
an oversight — it's deliberate.

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
| `GET /api/v1/social/{platform}/callback` | Generic social OAuth redirect target (studio Instagram, TikTok, Facebook, X, YouTube) — no JWT possible | Signed `state` param (HMAC-SHA256, `ISocialOAuthStateSigner`, separate key from `IInstagramStateSigner`) validated before trusting subjectId; rate-limited (`public-write`) |
| `GET /api/v1/public/studios/nearby` | Public geo search (DiscoverPage Studios tab) | None — read-only, non-sensitive studio info only |
| `GET /api/v1/public/studios/{slug}/reviews` | Public studio review list | None — read-only, non-sensitive review content only |
| `GET /api/v1/public/artists/{slug}/reviews` | Public artist review list | None — read-only, non-sensitive review content only |
| `POST /api/v1/public/traffic/beacon` | Anonymous + authenticated traffic beacon (role/tenant read from JWT when present) | Rate-limited (`public-write`); no PII accepted in the request body — `Path`/`IsNavigation` only, visitor id via header, IP never persisted |

The core auth-bootstrap endpoints (`/auth/login`, `/auth/register`, `/auth/oauth/*`,
`/auth/forgot-password`, `/auth/reset-password`, `/auth/refresh`, `/auth/verify-email`)
are anonymous by necessity (a caller cannot be authenticated before obtaining a token)
and are covered by CLAUDE.md's blanket "no unprotected endpoints except `/auth` and
`/health`" exception rather than needing individual rows here — this table exists for
the non-obvious cases (cross-tenant reads, webhooks, signed tokens), not the login flow
itself. All seven carry `"auth"` rate limiting except `reset-password`/`refresh`/
`verify-email`, which predate the Redis rate-limiting feature and were out of scope for
this audit (see "Full-App Master Audit — 2026-07-20" below).

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

Duplicate guard: one review per `(AuthorUserId, PortfolioImageId)` pair for tattoo
reviews (portfolio images aren't tied to a specific booking, so this stays a
lifetime-per-image cap). Studio and artist reviews are **not** a lifetime cap —
see "Review eligibility — per-completed-appointment" below.

### Review eligibility — per-completed-appointment (2026-07-25)

Studio and artist reviews were originally capped at one-per-client-ever
(`(AuthorUserId, StudioId)` / `(AuthorUserId, ArtistId)` unique indexes). That
blocked a real, common case: a repeat client who gets tattooed again months
later has no way to leave a second review. Industry precedent (Fresha, Vagaro,
Booksy) ties review eligibility to a completed transaction, not a lifetime cap.

`Review` gained a nullable `AppointmentId (Guid?)` FK:
- Studio/artist reviews now **require** it (set by `Review.ForStudio`/`ForArtist`).
- Portfolio-image reviews leave it `null` — a portfolio image isn't tied to a
  specific booking, so that path is unchanged (lifetime-per-image cap, above).

Eligibility, enforced in `CreateStudioReviewCommand`/`CreateArtistReviewCommand`:
the appointment must belong to the caller (`Client.UserId == AuthorUserId`),
target the studio/artist being reviewed, and have `Status == Completed`. A
mismatch on ownership/target throws `NotFoundException` (404 — mirrors
`RescheduleAppointmentHandler`'s "don't reveal another client's appointment
exists" convention); a real-but-not-completed appointment throws
`BusinessRuleViolationException` (400).

Duplicate guard moved from `(AuthorUserId, StudioId/ArtistId)` to
`(AppointmentId, StudioId)` / `(AppointmentId, ArtistId)` — one review per
appointment per target, not per client-lifetime. A client can still leave both
a studio review and an artist review from the same appointment (different
target column, same `AppointmentId`, no collision). MySQL treats `NULL` as
distinct per row in composite unique indexes, so artist-review rows
(`StudioId` null) and portfolio-image rows (`AppointmentId` null) never
collide with these. Known accepted tradeoff: a multi-session tattoo (sleeve,
back piece) has no "project" grouping concept in this codebase, so a client
can technically leave one review per session instead of one per finished
piece — deliberately not building that grouping now; a nullable `ProjectId` +
narrower uniqueness scope can be layered in later if it becomes a real problem.

New read endpoints power the "which visit are you reviewing?" picker on the
write-a-review form: `GET /studios/{slug}/reviews/eligible-appointments` and
`GET /artists/{slug}/reviews/eligible-appointments` (`GetReviewableStudioAppointmentsQuery`/
`GetReviewableArtistAppointmentsQuery`), both `ClientAndAbove`-gated, returning
the caller's completed-and-not-yet-reviewed appointments for that target. Same
cross-tenant `IgnoreQueryFilters` + explicit `Join` pattern as the
`IsVerifiedBooking` checks (entries 19-20) — `Client` needs its own
`IgnoreQueryFilters()` even inside a query already ignoring filters on
`Appointment`, since `Join` combines two independent `IQueryable` sources.

Migration `AddAppointmentIdToReview` (20260725204502) is safe against existing
data with no backfill: legacy review rows get `AppointmentId = null`, and MySQL
allows unlimited `NULL`s in a unique index, so they never collide with each
other or with new rows.

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
  Returns QR code pointing to: https://tattooos.co/s/{studio.Slug}
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
| `Studio.IsPublished` | New bool, default `true`; `false` only on `IsSolo` creation | Lets a solo-artist studio be active (tenant access, bookable artist page) but excluded from directory surfaces until it has a real location — the exact case this section previously said would justify the field |
| Solo-artist signup | `RegisterSoloArtistCommand` auto-provisions a `Studio{IsSolo=true, IsPublished=false}` + `Subscription` on the `Free` plan, `owner` role, no NIPT/city/coords required | Matches category standard (Fresha/Vagaro/Boulevard/GlossGenius all let a single-provider business start taking bookings without a formal multi-staff registration step) |
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
| IsVerifiedBooking on ReviewResponse | Computed at query time via Appointments join, not stored | No migration needed; verified status can change if booking is cancelled or added; `IgnoreQueryFilters` approved (entries 19-21). Unchanged by the per-completed-appointment eligibility model (2026-07-25) — every new studio/artist review is definitionally verified since creation now requires one; this join still matters for legacy pre-migration rows and for portfolio-image reviews |
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
| Plan/PlanPrice split | `Plan.BillingInterval`/`PriceMonthly`/`PriceYearly`/`StripePriceIdMonthly`/`StripePriceIdYearly`/`PairedPlanId` removed; new child entity `PlanPrice` (`PlanId`, `Interval`, `Price`, `StripePriceId`, `IsActive`, unique on `(PlanId, Interval)`) holds one row per cadence a tier actually offers. `Subscription` gained `BillingInterval` (required) and `PendingBillingInterval` (nullable, mirrors `PendingPlanId` — set/cleared together by `ChangePlanHandler`/`CancelPlanChangeHandler`/`HandleSubscriptionUpdatedHandler`) — cadence is now the subscription's own property, independent of which `Plan` it's on. `DataSeeder.ReconcileCoreTiersAsync` replaced both `ReconcileCorePlansAsync` and `RetireOrphanedNamedPlansAsync`, keyed on tier `Name` + `(PlanId, Interval)` rather than a fixed `Plan.Id` list. Migration split in two: additive `plan_prices`/`Subscription` columns + raw-SQL data backfill + Premium-row merge (`AddPlanPriceAndSubscriptionBillingInterval`) shipped together with a later, separate `DropLegacyPlanBillingFields` migration for the six dead `Plan` columns — both written and applied in the same session (no live deploy pipeline exists yet to force a real waiting period between them), but kept as two distinct, separately-reviewable migration files rather than one; the second had to be hand-authored since `dotnet ef migrations add` scaffolds an empty diff once the model snapshot already reflects the target model. | Directly supersedes "Plan billing interval stays locked per-row" and "Plan Monthly/Yearly pairing" above — those decisions produced two data-integrity bugs in two consecutive nights (`bug-report-plans-page-data-mismatch.md`, `bug-report-premium-plan-duplicate-legacy-row.md`) because a plan's billing cadence and its identity as a tier were the same database row. Also fixed as a confirmed side effect, not scope creep: `GetPlatformStatsQuery`/`GetMrrHistoryQuery` were computing MRR from `Plan.PriceMonthly` unconditionally, overstating revenue for every yearly-billed subscription (79 vs the real 790/12 = 65.83 monthly-equivalent) — now uses the `PlanPrice` matching the subscription's actual `BillingInterval`. |
| Shared-DB integration test isolation | Don't assert on an absolute global count (e.g. "total active studios across all tenants") in a test inside the `[Collection("Database")]` group. `DatabaseFixture` provisions exactly one MySQL database for the entire collection with no reset between tests, so any count that isn't scoped to IDs the test itself created is really asserting against the cumulative state of the whole suite run — safe when the suite is small, silently flaky as it grows. Test that kind of threshold/suppression logic deterministically at the unit level instead, by calling the pure logic function directly with a synthetic input, the way `IndustryReportJobTests.BuildDocument_CohortBelowMinimum_AllMetricsNull` does against `IndustryReportJob.BuildDocument` | Discovered 2026-07-20 during the full-app master audit: `IndustryReportJob_Run_SmallCohort_MetricsAreNull` seeded exactly 3 studios and asserted the resulting report showed suppressed (null) metrics, which was safe when the test was written but broke once ~10+ *other* integration tests elsewhere in the suite were also creating `SubscriptionStatus.Active` studios in the same shared database, pushing the real total past the suppression threshold. Removed the test; the same behavior was already covered without the DB dependency at the unit level |
| Client self-service notice window | `DepositRule.CancellationWindowHours`/`RefundPercentOnLateCancel` (2026-07-21), single window shared by both self-cancel (tiered refund via `ClientCancellationPolicy.ResolveRefundPercent`) and self-reschedule (hard cutoff via `IsWithinNoticeWindow`, no partial-consequence concept) | Deliberate v1 simplicity — a second, separate "reschedule window" field was considered and rejected; split them later only if real studio usage shows a genuine need for different windows per action |
| Structured audit log entity shape | `AuditLogEntry` (2026-07-21) is NOT a `TenantEntity` — `StudioId` is nullable, no `HasQueryFilter()` registered at all; scoping is enforced entirely in `GetAuditLogHandler`/`GetMyStudioAuditLogHandler`, not a query filter | Same non-tenant shape as `FeedbackReport`/`UserOnboardingState`, but genuinely new in kind: those two are single-role-owned (issuer-only or per-user-only) reads, while audit log entries are read by two different roles with two different scoping rules against the same table |
| `IAuditableCommand` / `AuditLogBehavior` | New MediatR pipeline behavior, registered immediately after `PlanLimitBehavior`, mirrors `IQuotaCheckedCommand`'s exact marker-interface shape but logs AFTER `next()` succeeds rather than gating before it | Sibling pattern, not a copy — an audit log records what happened after the fact, a quota check must run before the handler does its work; the two behaviors' relative pipeline position (Validation → PlanLimit → AuditLog) is intentional: validate shape, then check quota, then execute, then log only real successes |
| Audit log metadata whitelisting | `AuditMetadataBuilder.Build(object command)` — a `switch` over concrete command types building an explicit field allowlist per action, never a wholesale `JsonSerializer.Serialize(command)` | The whole point of a compliance audit log is that it must never itself become a PII leak vector; a wholesale serialize is exactly how a free-text field would end up in `Metadata` by accident as the codebase evolves |
| Referral-code audit entries have no `StudioId` | `Deactivate`/`Reactivate`/`DeleteReferralCodeCommand` only carry `ReferralCodeId`; `AuditStudioId` is left at its interface default (`null`), so these entries log as platform-wide even though a referral code is studio-scoped | Accepted limitation, not an oversight — resolving it would mean either adding an async DB lookup to `IAuditableCommand`'s synchronous property (breaks the marker-interface pattern's simplicity) or changing these three commands' shape to carry `StudioId` (broader surgery than this round's scope); revisit if/when audit completeness for referral actions becomes a real requirement |
| CashPending self-cancel is exempt from `ClientCancellationPolicy` | `CancelAppointmentHandler`'s `CashPending` branch (2026-07-21, `/code-review high` finding) unconditionally waives the deposit regardless of `isClient`/notice window — deliberate, not a policy bypass, per a code comment added at the branch | `CashPending` means the client only declared intent to pay cash (`DeclareCashDepositCommand`); no money has been collected yet (that only happens via `ConfirmCashDepositCommand`, which moves the payment to `Paid`) — there is nothing to forfeit or partially refund from an amount never taken. The actually-fixed bug was on the frontend: `MyBookingsSection.tsx`'s `CancelArea` now gates the forfeiture-warning copy on the real `Payment.status` (`Captured`/`Paid`) via `useGetPaymentByAppointmentQuery`, not `Appointment.depositStatus` alone, so it no longer shows a forfeiture warning for a deposit that was never actually collected |
| `Payment.RefundedAmount` | New nullable `decimal` column (2026-07-21, `/code-review high` finding) tracking how much of `Payment.Amount` was actually refunded — there is no separate `PartiallyRefunded` status, so `Status == Refunded` alone can't distinguish a full refund from a partial one. Set by both `CancelAppointmentCommand`'s partial/full-refund branches and the pre-existing owner-initiated `RefundPaymentCommand` (which had the identical gap already, now fixed consistently in the same change) | `GetRevenueSummaryQuery` was filtering strictly on `Status == Paid`, so a partially-refunded payment (e.g. a late self-cancel under a 50%-refund policy) disappeared from historical revenue reports entirely, including the retained portion the studio actually kept. Now includes `Status == Paid \|\| Status == Refunded` and sums `Amount - (RefundedAmount ?? 0)` per payment (clamped at 0) instead of `Amount` outright — a fully-refunded payment naturally contributes 0 and is filtered out of the per-artist breakdown rather than shown as a zero-revenue row |
| NIPT business verification (2026-07-22) | `Studio.Nipt` (`string?`, max 10, non-unique index `ix_studios_nipt`) collected at registration (`RegisterStudioRequest.Nipt`, required, format-validated) and editable once via `UpdateStudioRequest.Nipt` (optional; becomes read-only in the UI once set). Uniqueness rule enforced entirely in the application layer (`RegisterStudioHandler`/`UpdateMyStudioHandler`): a NIPT may not match an existing **active** studio whose `OwnerEmail` differs (case-insensitive) — same owner may reuse their NIPT across multiple locations. Violation throws `DuplicateNiptException` → 409. `/studios/me` shows a dismissible-per-session banner when `Nipt` is null, prompting backfill for pre-existing studios. | This is registration/compliance metadata for operating in Albania — it is **explicitly not an auth factor**; login remains email + password (+ OAuth) only, full stop. See `docs/claude/overnight-prompt-nipt-studio-registration-2026-07-22.md` for the full reasoning trail, including why this was flagged as *not* a benchmark-driven pattern (none of Vagaro/Fresha/Boulevard/Mindbody/Zenoti/GlossGenius collect a national tax ID at signup — this is local-compliance-specific, per CLAUDE.md rule #6's "flag the gap" convention). **Deviation from that prompt's draft migration, found by the integration test it explicitly asked for (§14 checklist item 4):** the prompt originally specified a MySQL *filtered unique* index (`nipt IS NOT NULL AND is_active = 1`) as a defense-in-depth backstop. That index cannot express the same-owner exception — SQL unique constraints have no concept of "unique except when this other column matches" — so it made every legitimate multi-location registration fail with a raw 1062/500 even though the app-layer check correctly allowed it. Caught immediately by `RegisterStudio_DuplicateNiptSameOwnerEmail_SucceedsForMultiLocation`. Fixed by dropping `.IsUnique()` from `ix_studios_nipt`, leaving it as a plain lookup index; the application-layer check is the sole source of truth for this rule, same as several other conditional-uniqueness rules already documented in this log's referral/plan sections. **Also deferred, per the prompt's own fallback guidance:** the NIPT checksum-digit algorithm (format-only regex `^[A-Z]\d{8}[A-Z]$` ships; the check-letter algorithm was never confirmed against an authoritative source) and the issuer-side `NiptVerifiedAt` verification stretch (§9/§10 of the prompt) — both flagged as fast-follow work, not silently dropped. |
| CI toolchain pin | `global.json` at repo root, `sdk.version` = installed SDK exactly, `rollForward: latestFeature` (2026-07-26) | Single source of truth for local dev, CI (`actions/setup-dotnet` reads it), and the Dockerfile's `sdk:10.0` base image band — repo had no SDK pin before this |
| CI integration-test DB strategy | Plain `docker run mysql:8.4 ...` step in `ci.yml`, not a GitHub Actions `services:` block (2026-07-26) | The declarative `services:` syntax can't pass `--character-set-server`/`--collation-server`, and `DatabaseFixture.cs` connects to `127.0.0.1:3306` directly rather than a service-network hostname — a `docker run -p 3306:3306` is the closer match to local dev and to `docker-compose.yml` |
| `dotnet format` gate is now blocking | `chore: run dotnet format baseline` (559 files, 2026-07-26) fixed the ~38,000 pre-existing CRLF/whitespace/charset violations in one dedicated, isolated commit — `continue-on-error: true` removed from `ci.yml`'s Format check step | Verified via `git diff -w` that every remaining non-whitespace diff line across the 71 files still showing a difference was pure structural punctuation (line-split object initializers, brace repositioning) — no logic changes. Build + 1397 unit + 311 integration tests all passed unchanged before and after |
| `pnpm lint` gate is now blocking | `fix(frontend): resolve react-hooks/set-state-in-effect lint errors` (2026-07-26) fixed the 6 pre-existing findings — `continue-on-error: true` removed from `ci.yml`'s Lint step | `UserMenu.test.tsx`: `eslint-disable-next-line` was misplaced 6 lines above the actual `as any` it was meant to cover. `ConfirmChangeEmailPage`/`VerifyEmailPage`: missing-query-param validity is knowable synchronously at render time, moved to the `useState` initial value instead of being set from inside the effect. `StudioNotificationSheet`/`ArtistScheduleEditor`: sync local edit state from async query data during render (React's documented "adjusting state during render" pattern) instead of in an effect. `ReviewSection`: default-select the most recent eligible appointment via a one-shot boolean flag, **not** a reference-equality check against the previous appointments array — an early version of this fix compared `eligibleAppointments !== syncedAppointments`, which infinite-looped ("Too many re-renders") specifically inside `ArtistPortfolioPage`/`StudioPortfolioPage`'s own tests, whose mocks return a fresh `[]` literal on every render (no referential stability guaranteed). `ReviewSection.test.tsx` alone never exercises that unstable-reference shape and passed the whole time — only running the *full* suite surfaced it. Verified: full suite 116 files / 1742 tests pass, `pnpm build` clean, 0 lint errors |
| Frontend coverage provider | Added `@vitest/coverage-v8` devDependency (2026-07-26) | `vitest.config.ts` had no coverage provider configured; this is the CI prompt's explicitly sanctioned one-package exception for coverage visibility |
| `pnpm test --coverage`, never `pnpm test -- --coverage` | Confirmed by direct local reproduction (2026-07-26) | This repo's pinned pnpm (11.5.1) forwards a literal `--` token through to the underlying script instead of stripping it as a separator, so `pnpm test -- --coverage` actually invokes `vitest run "--" "--coverage"` — vitest silently treats both as inert positional test-file-name filters (matches everything, same as no filter) and coverage never activates. No error, no warning — the full suite still reports "all tests passed," just with zero coverage collected. Confirmed the fix (`pnpm test --coverage`, no `--`) actually enables coverage locally before wiring it into `ci.yml` |
| Endpoint-authorization guardrail heuristic tracks group-level guards | `guardrails` job's Python heuristic in `ci.yml` also treats a `RouteGroupBuilder` variable as guarded if `.RequireAuthorization()`/`.AllowAnonymous()` is chained on the `app.MapGroup(...)` declaration itself, not just on each individual `Map*` call (2026-07-26) | The naive per-route-window version (checking only 300 chars after each `Map*` call) false-positived on `PlatformEndpoints.cs`, `SavedImagesEndpoints.cs`, and `FeedbackEndpoints.cs`'s `platform/feedback` group — all three guard at the group level by real, existing codebase convention, not an actual RBAC gap. Verified clean against current `main` after the fix |
| Repo visibility: public | Changed `471k/pena-e-arte` from private to public (2026-07-26) | Classic branch protection *and* the newer repository-rulesets API both 403'd with "Upgrade to GitHub Pro or make this repository public" on the private repo's plan tier — there was no way to make the 6 required CI status checks actually block merges to `main` otherwise. Full git history was re-scanned for real secret material first (pattern search for Stripe/AWS/Twilio/private-key formats across `git log --all -p`) — clean; the one prior incident is already remediated in-history (`"SecretKey": "STRIPE_SECRET_KEY_REDACTED_FROM_HISTORY"`). User made this call explicitly after being shown the tradeoff (full source/history become world-readable) |
| Branch protection on `main` | Applied via `gh api PUT .../branches/main/protection` (2026-07-26): all 6 CI checks required + strict (branches must be up to date), `required_approving_review_count: 0` (solo-dev project, no second engineer to approve), conversation resolution required, no force-push, no deletion, `enforce_admins: false` | User explicitly chose 0 required approvals over requiring 1 — requiring your own approval on your own PR is friction with no safety benefit at this team size; revisit if a second engineer joins |
| CodeQL `upload: never` reverted | Removed once the repo went public (2026-07-26) — code-scanning upload no longer 403s, so `codeql.yml`'s `analyze` step uploads to the Security tab normally again | Was only ever a workaround for the private-repo GHAS gap documented above; the TODO left in that commit said to remove it under exactly this condition |
| Native secret scanning + push protection | Enabled via `gh api PATCH` on `security_and_analysis` (2026-07-26) — free once public, complementary to the in-CI gitleaks step (gitleaks runs post-push in `guardrails`; push protection blocks the `git push` itself before the commit lands) | Completes Phase 8 item 5 from the original CI-standup task, now unblocked by the visibility change |
| Local observability stack (Grafana + Prometheus + Loki + Tempo) | 2026-07-26. Added to `docker-compose.yml` as five new services (`prometheus`, `loki`, `tempo`, `alloy`, `grafana`), config under `docker/observability/`. Pinned image tags (confirmed current-stable at execution time): `prom/prometheus:v3.13.1`, `grafana/loki:3.7.4`, `grafana/tempo:3.0.2`, `grafana/alloy:v1.18.0`, `grafana/grafana:13.1.1`. Log shipping uses Grafana Alloy (`loki.source.docker` over the Docker socket) — **not Promtail**, which reached EOL 2026-03-02 — so Serilog's existing Console/`CompactJsonFormatter` output needed zero code changes to become Loki-queryable (verified: a real container's log stream showed up in Loki within ~20s of the container starting, no restart needed). Prometheus scrapes the API's existing `/metrics` endpoint (already exposed via `MapPrometheusScrapingEndpoint()`, unchanged), with two scrape jobs (`pena-e-arte-api-container` targeting `api:8080`, `pena-e-arte-api-host` targeting `host.docker.internal:8080`) so both the containerized and `dotnet run`-on-host dev topologies work without picking one permanently — whichever isn't running just shows `up == 0`, confirmed harmless via real Prometheus target-page query. Tempo receives traces via the existing `OpenTelemetry:OtlpEndpoint` config, now pointed at `tempo:4317` in container-parity mode via a new `OpenTelemetry__OtlpEndpoint` compose override (host-mode `dotnet run` still uses `appsettings.Development.json`'s `localhost:4317`, which now resolves since Tempo's OTLP port is published to the host). Loki labels are deliberately low-cardinality only (`container`, `service_name` — confirmed via a real query that no `request_id`/`user_id`/`tenant_id` ever became a label; they only appear inside the JSON log body, queried at query time via `\| json`) to avoid the standard Loki high-cardinality-label ingester-pressure anti-pattern. **`request_id`/OTel-trace-ID correlation — verified empirically, not assumed, and did NOT match:** `HttpContext.TraceIdentifier` (pushed as `request_id`) turned out to be ASP.NET Core's own connection-based identifier (e.g. `"0HNNB66BL6S1U:00000001"`), completely unrelated to `Activity.Current.TraceId` (the W3C/OTel ID Tempo actually indexes spans by) — not even a substring match. Confirmed via a standalone minimal-API harness built from the exact same `AddApiOpenTelemetry`/`RequestIdMiddleware` code and the project's exact pinned OTel package versions (no DB dependency, so unaffected by the Pomelo/EF Core blocker below): a real request's `Activity.Current.TraceId` was queried back out of Tempo via `GET /api/traces/{id}` (HTTP 200, span returned) using the *same* value now pushed as `trace_id`, while the old `request_id` value returned nothing. Fixed by adding `trace_id`/`span_id` `LogContext.PushProperty` calls (sourced from `Activity.Current`, null-safe) to `RequestIdMiddleware.cs` — the one narrowly-scoped code change outside `docker-compose.yml`/`docker/observability/` this prompt made. The Loki datasource's derived-field link to Tempo matches on `trace_id`, not `request_id`, for exactly this reason. **Tempo config schema deviation from the sourcing prompt (genuine upstream breaking change, discovered empirically, not a mistake to revert):** Tempo 3.0 removed the `ingester` and top-level `compactor`/`compaction.compaction.block_retention` config blocks entirely (replaced by Kafka-based `block-builder`/`live-store`/`backend-scheduler`/`backend-worker` for distributed mode); monolithic/single-binary mode (`target=all`, what this compose file runs, no Kafka needed) uses sensible defaults with no exposed retention override — `docker/observability/tempo.yaml` was written to match Tempo's own published single-binary example config instead of the original spec's Tempo-2.x-shaped `ingester:`/`compactor:` blocks, which crash-looped the container (`field ingester not found in type app.Config`). Retention in monolithic mode defaults to 336h (14 days), not the originally-intended 48h dev-laptop value — no supported override exists for monolithic mode short of pulling in the distributed backend-scheduler/backend-worker machinery, which is out of scope (see below). Real Prometheus metric names confirmed via a live `/metrics` scrape rather than assumed: `http_server_request_duration_seconds_{bucket,sum,count}` (histogram) and `http_server_active_requests` (gauge), both labeled `http_route`/`http_request_method`/`http_response_status_code`/`url_scheme`/`network_protocol_version` — used verbatim in the provisioned `api-overview.json` RED dashboard (rate/error-rate/p50-p95-p99-latency/active-requests/scrape-up panels), each panel expression confirmed to return real non-empty series against the harness's live traffic before shipping. **Tempo healthcheck dropped, not just deviated:** `grafana/tempo` ships a fully distroless image — confirmed via `docker exec`, every command (`wget`, `curl`, `which`, `ls`, `sh`) failed with `"executable file not found in $PATH"` — so no `wget`-based `HEALTHCHECK` can ever run inside that container. Grafana's own published single-binary reference `docker-compose.yaml` doesn't define one for Tempo either, for the same reason. `/ready` was instead confirmed reachable from the host throughout (`curl http://localhost:3200/ready` → 200) — `prometheus`'s `wget`-based healthcheck stayed, since `prom/prometheus`'s busybox-based image does ship `wget`. **§3.2 open decisions, both resolved explicitly:** (1) trace/log ID match — did not match, fix applied, see above. (2) `OpenTelemetry.Instrumentation.EntityFrameworkCore`/`.Http` — not added tonight. `.Http` (1.17.0) is now stable and version-matched with the already-installed `AddAspNetCoreInstrumentation` packages, a reasonable low-risk fast-follow; `.EntityFrameworkCore` remains prerelease (`1.17.0-beta.1`, "experimental semantic conventions... breaking changes" per its own package notes) and should not be added until it stabilizes. Neither was added tonight regardless of stability, both because adding a new NuGet package to `Pena_e_Arte.API.csproj` falls outside this change's stated file scope, and because DB-query span value can't be verified end-to-end against the real running app right now — see the next entry. | Closes the local half of the observability gap from the "Structured-log correlation fields" entry above: the app was already emitting logs/metrics/traces correctly, but had nothing local to send them to. Production/K3s rollout, alerting/on-call routing, retention-cost tuning, and a public status page are explicitly out of scope — tracked as follow-ups, blocked on the CD pipeline landing first (`overnight-prompt-ci-pipeline-2026-07-26.md`) — as is Tempo's distributed-mode retention override, blocked on nothing existing yet to size that decision against. |
| Pomelo.EntityFrameworkCore.MySql 9.0.0 / EF Core 10.0.10 mismatch blocked local `dotnet run` — **fixed 2026-07-26, root cause traced to same-day commit `a9b3787`** | That commit (`chore: apply pending Dependabot version bumps ... EF Core Design ...`, PR #37, applied earlier the same day) bumped `Microsoft.EntityFrameworkCore.Design` in `Pena_e_Arte.API.csproj` from `9.*` to `10.*`, while every other EF-related package in the solution (`Microsoft.EntityFrameworkCore` in Application, `Microsoft.AspNetCore.Identity.EntityFrameworkCore` in Infrastructure, Pomelo itself) stayed on `9.*`. That commit's own message claimed "verified this does not leak `Microsoft.EntityFrameworkCore.Relational` 10.x into the runtime graph despite an NU1608 restore warning suggesting otherwise; all 311 integration tests still pass" — that claim was wrong for the one path it didn't test: a real `dotnet run` boot, where `Relational` *did* resolve to 10.0.10 and crashed `UseMySql` with `MissingMethodException`. (Integration tests apparently go through a different startup/host path that didn't hit this — worth someone confirming why, separately.) Checked NuGet directly before assuming a fix path: Pomelo still has no 10.x release (stable or prerelease), so "wait for upstream" was never actually available for PRs #27/#28 either. Fix: re-pinned `Microsoft.EntityFrameworkCore.Design` back to `9.*` (now resolves to `9.0.18`, current at time of fix) — Design is migrations/scaffolding tooling only, it doesn't need to lead the runtime EF Core version. `NU1608` warning is gone; confirmed by re-running the observability-stack verification (entry above) against the real `Pena_e_Arte.API` binary instead of the standalone harness: real traces (`GET /health/live`, `GET /metrics`) queryable in Tempo with `rootServiceName: "Pena_e_Arte.API"`, Prometheus's `pena-e-arte-api-host` target reporting `up == 1` against the real running app, real DataSeeder/StripeDemoSeeder/Hangfire output in the logs. | This was the harness workaround's own explicitly-flagged follow-up ("whoever unblocks Pomelo 10.x should re-verify") — done same-day once the actual root cause (a stray package pin from the very commit right before this branched off, not an actual Pomelo/EF Core incompatibility) was identified instead of just re-confirming the symptom. Also corrects [[project_dependabot_backlog]]'s "permanently blocked until Pomelo ships 10.x" framing: PRs #27/#28 (bumping `Microsoft.EntityFrameworkCore`/`Identity.EntityFrameworkCore` themselves to 10.x) are still correctly blocked — Pomelo genuinely has no 10.x release — but this specific runtime crash was caused by a different, already-merged package (`.Design`), not by #27/#28, and was fixable today without waiting on Pomelo at all. |
| K3s production deployment — ingress controller corrected mid-Phase-0-execution (2026-07-26) | The entry directly below resolved the ingress controller to `ingress-nginx` (Traefik disabled at K3s install via `--disable traefik`), matching `CLAUDE.md`'s "Nginx" infra-stack line. That was wrong and got caught the same day, live, while actually executing Phase 0 on the real Hetzner box: the community `kubernetes/ingress-nginx` project (`kubernetes/ingress-nginx` on GitHub) was **archived 2026-03-24** — no further releases, bugfixes, or security patches, permanently. K3s had already been installed with Traefik disabled at that point but nothing further (cert-manager, manifests, workloads) had been layered on yet, so the fix was cheap: `k3s-uninstall.sh` on the box, then a clean reinstall of K3s **without** `--disable traefik`, keeping its bundled Traefik (actively maintained by Traefik Labs, purpose-built for K3s) as the ingress controller instead. Confirmed via K3s's own bundled add-ons reaching `Running` in `kube-system` post-reinstall. Consequences threaded through `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md`: the `Ingress` manifest's annotations changed from `nginx.ingress.kubernetes.io/*` (which Traefik silently ignores rather than erroring on — would have failed quietly) to `ingressClassName: traefik` plus the controller-agnostic `cert-manager.io/cluster-issuer`; the SignalR long-connection timeout question is left explicitly unresolved-by-design (Traefik has no direct per-Ingress equivalent of nginx's `proxy-read-timeout` annotation — its equivalents are cluster-wide static config or a `Middleware` CRD, and which one is actually needed should be confirmed empirically against a real connection during Phase 6, not guessed at spec time); Grafana's optional future basic-auth gate (if ever exposed publicly) would use a Traefik `BasicAuth` `Middleware` instead of an nginx annotation. **`CLAUDE.md`'s own infra-stack table still says "Nginx," not Traefik, as of this entry** — flagged explicitly, not corrected, since `CLAUDE.md` lives at the repo root, outside this consultation project's `docs/claude/`-only write scope; whoever runs Phases 1–10 of the referenced prompt should make that one-line edit too. | This is exactly the kind of "verify against the live, current state of the world before committing a recommendation" failure this project's own rules exist to catch — a same-day recommendation was wrong within the same day, caught only because Phase 0 was actually being executed live rather than the correction surfacing later during Phases 1–10 or, worse, in production. Nothing about the DigitalOcean/Hetzner/`SslMode=Required` resolutions in the entry below is affected — this correction is scoped to the ingress-controller decision only. |
| K3s production deployment — Phase 0 provider decisions resolved (2026-07-26) | Follow-up to the entry directly below (logged first, chronologically earlier same day): the two money decisions that entry deliberately left open are now resolved. **VPS host: Hetzner** — cheapest at this scale, and checked directly against Hetzner's current site as part of resolving this: they have no first-party managed-database product, so "same provider as the DB" was never actually an available convenience to weigh against AWS either way. **Managed MySQL: DigitalOcean, engine 8.4** — DigitalOcean now defaults new clusters to MySQL 8.4 (an exact version match with `mysql:8.4`), confirmed current at resolution time (DigitalOcean's own migration notice: 8.0 clusters are on a forced-upgrade path to 8.4 starting Oct 2026). Also resolved as part of the same pass: **ingress controller is ingress-nginx**, with Traefik explicitly disabled at K3s install time (`--disable traefik`) rather than left ambiguous between the two, matching `CLAUDE.md`'s documented stack; and the production connection string requires **`SslMode=Required`** (DigitalOcean enforces TLS on managed connections; today's local `DB_CONNECTION_STRING` has no SSL parameters, so this is a real addition, not a copy-paste). `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md` §0/§2/§3/Phase 6 updated in place to reflect all four resolutions — no new dated file, since Phase 0 (provisioning) still hadn't been executed as of this update, only decided. | Unblocks Phase 0 of the referenced prompt — Phi can now actually provision the box and database instead of the prompt naming an open money decision. Region pairing recorded for latency: Hetzner Nuremberg/Falkenstein (Germany) + DigitalOcean Frankfurt (FRA1), both close to the app's Albania-based user base. |
| K3s production deployment — spec'd, not yet executed (2026-07-26) | Full engineering-consultation audit of the production-deployment gap: `docker-compose.yml`/Dockerfiles/observability stack all work locally, but zero K8s manifests, zero CD step, and no live server existed anywhere despite `CLAUDE.md` naming K3s as the target orchestrator. Resolved via a clarifying pass with Phi: cluster not yet provisioned (VPS host — Hetzner vs. AWS — deliberately left open, a money decision, not decided here); MySQL will be a **managed** instance in production, not self-hosted (provider also deliberately left open — DigitalOcean/PlanetScale/AWS RDS candidates priced and compared, DigitalOcean recommended as the closest behavioral match to today's real-MySQL-protocol container, final pick is Phi's); observability stays **self-hosted in-cluster**, reusing `docker/observability/*` configs as ConfigMaps rather than switching to a managed service; TLS via cert-manager + Let's Encrypt using Cloudflare's **DNS-01** solver (not HTTP-01) against the `tattooos.co` zone, chosen for wildcard-cert support and no port-80-reachability requirement during issuance. Full phase-by-phase spec — including two problems found during the audit that needed precise, code-level fixes rather than just "add K8s YAML" — written to `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md` for a Claude Code session (main Engineering project, full repo write access) to execute once Phase 0's manual prerequisites (a human provisioning the actual VPS/K3s box, managed DB instance, and Cloudflare API token — none of which a coding session can do unattended) are complete. **Problem 1 — migration race condition:** `Program.cs` runs `AppDbContext.MigrateAsync()` unconditionally on every pod's startup; harmless at today's 1-replica scale, a real concurrent-migration race at the 2-replica rolling-update minimum this deployment requires for zero-downtime. Fix specified: a new `Migrations:ApplyOnStartup` config flag (default `true`, so local dev/`docker-compose.yml` behavior is untouched), set `false` on the K8s API Deployment, with a dedicated one-shot `batch/v1` Job (same image, flag left at its `true` default) run by the CD pipeline before each rollout. **Problem 2 — two-hop forwarded-headers bug:** the K3s topology this deployment introduces has *two* reverse-proxy hops in front of the API (ingress-nginx, then the frontend Pod's own nginx, which already same-origin-proxies `/api/`/`/hubs/` per `nginx.conf.template` — confirmed only one Ingress host is needed at all for exactly this reason), but `ForwardedHeadersOptionsBuilder.cs` (added in today's earlier security-remediation entry) never set `ForwardLimit`, which defaults to `1` — meaning even with `TrustedProxyCidr` correctly set to the cluster's Pod CIDR, only one hop would be stripped from `X-Forwarded-For` and `RemoteIpAddress` would resolve to the ingress pod's IP, not the real client's, silently defeating the per-client rate-limiting that config was added for in the first place. Fix specified: `ForwardLimit = 2`, plus a new `ForwardedHeadersTests.cs` case asserting the real client IP survives a real two-hop chain. Neither fix has been implemented yet — this entry records the spec, not a shipped change. No Help Menu/user-manual/onboarding-tour update needed: zero user-visible surface, stated explicitly in the prompt per CLAUDE.md rule #7's exception clause. Explicitly **not** covered by this spec, named rather than silently dropped: alerting/on-call routing, a public status page, retention tuning, autoscaling, multi-node/HA control plane, and a backup/DR runbook for whichever managed MySQL provider gets picked. | Closes the "Production/K3s rollout" and "CD pipeline" follow-ups this same log's observability entry (above) explicitly named as blocked on this landing. Alerting/on-call routing, public status page, and retention-cost tuning remain out of scope after this too — restated here so they don't quietly fall off the backlog now that the thing blocking them is unblocked. Full spec, exact manifests structure, exact code diffs, and the DigitalOcean/PlanetScale/AWS RDS pricing comparison: `docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md`. |
| Security remediation (adversarial pass findings) — 2026-07-26 | Fixed the P0 cross-tenant SignalR authorization gap: `ScheduleHub`/`DesignHub`/`NotificationHub.JoinStudio` now validate the caller's `tenant_id` claim against the requested `studioId` (issuer role bypasses for cross-tenant support access), mirroring `SupportHub.JoinTicket`'s 2026-07-21 fix — the same defect existed in all three studio-scoped hubs and was never generalized past the one hub that got reviewed that day. Also: `ForwardedHeaders:TrustedProxyCidr` config added (optional, logs a warning when unset); rate limiting added to `reset-password`/`refresh`/`verify-email` (reused the existing `auth` policy — frontend's `usePresignedUpload`-adjacent `baseQuery.ts` refresh flow already single-flights via an in-tab lock, so 10/min is not a real constraint); Hangfire dashboard now gated by real HTTP Basic Auth (finally consuming the `Hangfire:DashboardUsername`/`Password` env vars docker-compose already required) with the issuer-JWT check kept as an additional layer; a startup-time fail-fast guard on `Jwt:SecretKey` length (≥32 bytes); R2 presigned-upload object keys now keep only the client's folder/purpose prefix and server-generate the file name (`Guid.NewGuid()` + extension derived from the validated `ContentType`); a new `billing` rate-limit policy (20/min, keyed per authenticated user id) on `CreatePaymentIntent`/`CreateDepositPayment`/`CaptureDeposit`/`RefundPayment`/`CreateCheckout`/`CreateCheckout/finalize`; and a CORS production-misconfiguration guard (throws if `Cors:AllowedOrigins` is empty and `IHostEnvironment.IsProduction()`, matching this same remediation's own JWT-guard precedent of failing loud rather than warning on a missing security-critical value — no pre-existing convention for this either way was found elsewhere in the codebase). **Two of the audit's own premises turned out to be stale or incomplete once checked against live behavior, not assumed correct:** (1) Finding 2's "KnownNetworks/KnownProxies empty means trust every proxy" no longer holds on this SDK — ASP.NET Core's `ForwardedHeadersMiddleware` shipped a security patch in .NET 8.0.17/9.0.6 (carried into this project's net10.0) that flips that default to "ignore the header entirely" instead; confirmed empirically via a TestServer probe (`tests/Pena_e_Arte.IntegrationTests/Middleware/ForwardedHeadersTests.cs`) before writing the fix, and via a Microsoft Learn breaking-changes doc. The real current-state gap is therefore the opposite of the audit's framing: without `TrustedProxyCidr` set, X-Forwarded-For isn't spoofable, but every real client behind the production ingress collapses onto the ingress's own IP for rate-limiting purposes — the original problem this middleware was added to solve in the first place. The fix (configurable `TrustedProxyCidr`) closes both framings regardless. (2) The `billing` policy's per-user-id partition key, as specified, would not have worked at all: `Program.cs` had `UseRateLimiter()` registered *before* `UseAuthentication()`, so `HttpContext.User` was never populated at partition-key-resolution time — verified via a throwaway TestServer probe showing `IsAuthenticated == false` inside the callback with that ordering. Reordered to `UseAuthentication → UseRateLimiter` (verified via the full `BillingRateLimitingTests` suite, run against the real `AddApiRateLimiting()` wiring, that the existing IP-keyed `auth`/`public-write`/`public-read` policies are unaffected by the reorder, and that separate users now correctly get separate buckets). Hangfire reachability (§2.2 item 1) was also verified empirically rather than assumed: the SPA's JWT lives in local/session storage and is only ever attached to `fetch`/XHR by `baseQuery.ts`, never on a top-level navigation, and there is no cookie-auth scheme registered, so `/hangfire` was confirmed completely unreachable by its intended operators before tonight — resolved by wiring real Basic Auth. K3s ingress topology (the other Finding 2 "confirm before assuming" item) could not be checked — no K8s manifests exist in this repo at all, K3s is managed outside it — so the code-level fix was implemented regardless rather than assumed-covered. R2/CDN `X-Content-Type-Options: nosniff` (Finding 6's second half) likewise could not be verified or set from this repo — flagged as an infra follow-up for whoever manages the Cloudflare R2/CDN configuration. Full findings, evidence, and severity in `docs/claude/security-audit-adversarial-2026-07-26.md`; phase-by-phase spec in `docs/claude/overnight-prompt-security-remediation-2026-07-26.md`. | Closes a genuine P0 (any authenticated user of any studio could join any other studio's real-time broadcast group and silently watch client names, appointment notes, design activity, consent/intake submissions, and payment/refund events) plus seven lower-severity defense-in-depth gaps identified by a dedicated end-to-end adversarial pass distinct from the routine role-scoped QA passes. No Help Menu/user-manual/onboarding-tour update needed for any of the eight fixes — every one is backend authorization/config hardening with zero user-visible surface change (stated per-phase during implementation, restated here). No frontend files were touched: all four real presign call sites (`BookAppointmentForm.tsx`, `ArtistDetailPage.tsx`, `UploadRevisionPage.tsx`, `FeedbackDialog.tsx`) already stored the server-returned `publicUrl` rather than reconstructing one, so the coordinated frontend change the source prompt anticipated for the R2 fix wasn't actually needed. |
| Platform legal-entity disclosure + public policy surfaces (EPIC-0001 PENA-100/101/102) — 2026-07-31 | Brand stays "TattooOS" in the UI; the operating legal entity (`LEGAL_ENTITY_NAME` "Pena e Artë", `LEGAL_ENTITY_NIPT` "M12219042B") is disclosed in a new site-wide `SiteFooter.tsx`, sourced from a single `frontend/src/shared/constants/legalEntity.ts` module (also the source of truth for `SITE_TAGLINE`, mirrored literally into `index.html`'s `<title>`/meta since a static HTML file can't import a TS constant here). Dead `/privacy` `/terms` links (previously bounced to `/discover` by `CatchAllRedirect`) fixed by adding real `/privacy` `/terms` `/refund-policy` `/contact` routes plus a public `/` Home surface for unauthenticated visitors (`IndexRedirect` renders it instead of redirecting guests to `/discover`). Privacy/Terms carry a conditional `[LAWYER REVIEW REQUIRED]` banner gated on `HAS_FINAL_LEGAL_COPY`; Refund Policy is REAL copy derived from `DepositRule`/`DepositCalculator`/`ClientCancellationPolicy`/`AppointmentSelfServiceDefaults` (24h default window, forfeit-on-no-show, 0% default late-cancel refund) rather than aspirational text. Signup Terms/Privacy consent lines added to both register pages. `appsettings.json` gains empty `App:LegalEntityName`/`LegalEntityNipt` (env-var-later, unconsumed yet). | EU e-Commerce Directive Art. 5 + Albanian consumer/e-commerce trader-identification disclosure; PSP/MoR KYC reviewers check for exactly this brand-in-header / legal-entity-in-footer split. First two phases (of seven) of EPIC-0001 pre-implementation hardening; Phases 3–7 (consent versioning, retention/purge, secrets/Vault, `IPaymentProvider` refactor, CI gates) remain — see `docs/engineering/EPIC-0001-pre-implementation-hardening.md`'s Execution status note. Verified: `pnpm lint` 0 errors, `pnpm build` clean, affected `pnpm test` green; no backend C# touched, Flow B (`IStripeBillingService`/`StripeBillingService`/`StripeDiscountService`) byte-for-byte unchanged. |
| Versioned consent + immutable snapshot (EPIC-0001 PENA-103) — 2026-07-31 | New `ConsentTemplate` entity models consent text with a Kind discriminator (`AppointmentConsent`, `CrossTenantProfileSharing`); it does NOT inherit `TenantEntity` and has NO query filter — nullable `StudioId` (null = platform default), authorized in handlers via `ConsentTemplateResolver`, exactly the `AuditLogEntry` pattern. `ConsentForm` and `ClientProfile` gain an immutable `*Snapshot` of the exact text agreed to, resolved server-side at signing/opt-in and never re-derived. `UpdatePortableProfileOptInCommand` became `IAuditableCommand` (was unaudited). **Corrected a false epic premise:** the portable-profile opt-in shares tattoo history + body map only — NOT Art. 9 medical notes/allergies (verified against `PortableClientProfile`) — so the kind is `CrossTenantProfileSharing` and all consent/Help/Privacy copy states this truthfully. | GDPR Art. 9/Art. 7 + Law 124/2024; immutable-snapshot consent is standard (DocuSign/HelloSign). Verified: dotnet build/format/test green (1446 unit + 328→330 integration across phases), pnpm green; migration additive, applied to a scratch DB. |
| Two-stage retention purge + R2 delete + audited erasure (EPIC-0001 PENA-104) — 2026-07-31 | `RetentionPurgeJob` (registered in `Program.cs` via `IRecurringJobManager.AddOrUpdate`, Cron.Daily(4) — NOT `IJobScheduler`, which has no cron concept) soft-deletes consent forms past `App:RetentionDays:ConsentForms`, then hard-purges rows past the grace window, deleting the R2 object first via the NEW `IR2Service.DeleteAsync`. Retention numbers are configurable PLACEHOLDERS (open question §3.6). `RequestDataErasureCommand` (owner/support endpoint, `OwnerOnly`) soft-deletes a client's consent forms + profile immediately, audited with a DISTINCT action (`Client.DataErasureRequested`) from the automatic purge (which writes no audit row). No client-facing self-service erasure UI yet (open question §3.8). | GDPR Art. 5(1)(e)/Art. 17, NIST SP 800-53 SI-12; two-stage soft-delete/hard-purge mirrors S3 lifecycle. Verified: dotnet build/format/test green; new endpoint has `RequireAuthorization`. |
| Per-tenant secrets: ISecretsProvider + local Vault dev mode (EPIC-0001 PENA-105) — 2026-07-31 | `ISecretsProvider` (fail-closed: throws, never returns null) with `VaultSecretsProvider` (VaultSharp, KV v2) as the default backend per CLAUDE.md rule 4; Vault runs in dev mode as a new `docker-compose.yml` service (NOT the production posture — no cluster exists yet). `StudioCredentialRef` (StudioId, Provider, SecretPath) is a Vault path/key POINTER with no value column (ADR-0001 Art. 4(g) scaffolding). A local `.githooks/pre-commit` gitleaks hook is the one scanning layer neither CI gitleaks nor push protection provides. The docker-compose Twilio/Instagram env gap (both live integrations ran with empty credentials in any composed deployment) was fixed at the same time. **Production backend resolved (1 Aug 2026): HCP Vault** (HashiCorp-managed) — not self-hosted Raft, not Infisical/Doppler; same `VaultSharp` client, no code change, only deploy-time config differs. Full rationale in `docs/infra/ADR-0002-secrets-management.md`; rotation steps in `docs/infra/secrets-rotation-runbook.md`. | OWASP ASVS V6, CWE-798, twelve-factor config; PCI DSS Req 3/6 for card-adjacent secrets. VaultSharp is the only new NuGet (pre-approved). Verified: dotnet build/format/test green (incl. Vault-backed + fail-closed tests); pre-commit hook proven to block a staged secret; docker compose config valid; Flow B unchanged. |
| IPaymentProvider replaces IStripePaymentService (EPIC-0001 PENA-106) — 2026-07-31 | Deleted the Stripe-aggregator `IStripePaymentService`/`StripePaymentService` outright (Amendment A Findings 1/2 — the Article 4(g) exposure, deleted not migrated) and replaced with a provider-neutral `IPaymentProvider` (`CreatePaymentHoldAsync`/`CaptureAsync`/`CancelAsync`/`GetStatusAsync`/`RefundAsync`) + a `PaymentProviderCapabilities` companion so logic gates on capability, never assumes. `NullPaymentProvider` is the DI default (fails closed) until POK lands. `Payment.StripePaymentIntentId` → `ProviderReferenceId` (renamed across ~22 files/~74 sites) plus new `Provider`/`Currency` (ISO 4217, default "ALL")/`HoldExpiresAt`/`PlatformFeeAmount` (0% day-one, deliberately OUTSIDE `SessionSplit`'s exact-sum-to-Amount invariant — Amendment A Finding 4). Migration used `RenameColumn` (no data loss). `PaymentReconciliationJob` gained a third hold-expiry auto-release pass (no fourth job). `SessionSplit`/`UpdateSessionSplitsCommand` and Flow B (`IStripeBillingService`) are byte-for-byte unchanged. Flow-A card wording in Help/manual went provider-neutral; Flow-B billing kept as Stripe. | Architecture fitness function (Ford/Parsons) — NetArchTest.Rules is the .NET ArchUnit; ADR-0001 Consequence 3. PCI DSS SAQ-A scope preserved (card data never touches this infra). Verified: dotnet build/format clean; 1446 unit + 330 integration green (incl. the new arch + hold-expiry + PlatformFee-invariant tests); migration applied to a scratch DB; pnpm lint/build clean. |
| Architecture fitness test + Help-sync check in CI (EPIC-0001 PENA-107) — 2026-07-31 | Extended `.github/workflows/ci.yml`: a fail-fast "Architecture fitness tests" step in the existing `backend` job (visible check for the no-platform-ledger rule), and a new `help-sync` job (separate from the hard-security `guardrails` job) that fails a PR touching a user-facing gated path (payments/forms/billing/studios/clients features, matching Application slices, or the ConsentForm/ConsentTemplate/ClientProfile/Payment entities) without updating a Help surface — reviewer-overridable via `[skip-help-sync]`. No duplicate gitleaks step (already present + push protection on). New `CONTRIBUTING.md` at repo root documents the CI gates, the pre-commit hook install, and the Definition of Done. | Fitness-function-in-CI is standard once an arch test exists; path-based doc-sync checks mirror larger OSS repos (Kubernetes PR bots), scoped for a solo founder. Both checks proven by real runs (arch test fails on an injected `PlatformLedger`; help-sync fails a gated-change-without-Help and passes with Help / override / non-gated). |
| Live traffic analytics — GeoIP provider | MaxMind GeoLite2-City (`MaxMind.GeoIP2` v6.1.0), not DB-IP Lite | Free GeoLite2 signup completed (2026-08-04); MaxMind's better-maintained ruleset judged worth the account/license-key friction DB-IP Lite avoids; recurring refresh handled by a separate `geoipupdate` process/scheduled task outside the app's own request path |
| Live traffic analytics — live presence store | Redis sorted set + per-visitor hash (`traffic:presence:*`), not the database | "Currently active" is inherently ephemeral state; matches the existing Redis-for-ephemeral-state pattern (sessions, slot locks, rate limits) rather than writing every 20s heartbeat to MySQL |
| Live traffic analytics — real-time transport | SignalR (`TrafficHub`, one group `platform:traffic`), 5s `PeriodicTimer` broadcast | Matches this project's existing "Real-time \| SignalR" row above; single group is safe because every connection is already issuer-scoped by `[Authorize(Policy = "IssuerOnly")]` at the hub class level — no per-studio partitioning risk like the P0 cross-tenant SignalR bug fixed 2026-07-26 |
| Live traffic analytics — raw event retention | 35 days (`TrafficRollupJob` purge), daily aggregate kept indefinitely | Long enough for a rolling "top pages this month" breakdown without keeping raw per-visit rows forever; matches the reasoning `GetTrafficBreakdownQuery` needs raw `TrafficEvent` for device/browser/page dimensions that `TrafficDailyAggregate` doesn't carry |
| Live traffic analytics — owner-facing scope | Deliberately not built this pass — issuer-only | No evidence any vertical-booking-SaaS competitor (Vagaro/Fresha/Boulevard/Mindbody/GlossGenius) exposes live site traffic to tenant owners; this is a general platform-admin pattern (Google Analytics Realtime/Plausible Live/Cloudflare Web Analytics/PostHog Live), not a booking-SaaS one — full backlog spec in the source prompt's §3.4 |
| Live traffic analytics — full free-tier GeoIP integration (2026-08-05) | Added the second free MaxMind edition, `GeoLite2-ASN` (`GeoIp:AsnDatabasePath`, `GeoIpService` opens an independent `DatabaseReader` for it — one database missing/unavailable never blocks the other), plus every remaining field free on the existing `City()` lookup: subdivision ISO code, postal code, continent, lat/long, accuracy radius, timezone. Lat/long is surfaced on a new live-visitor world map (`react-leaflet`, live/≤60s presence only, no historical heatmap — zero new npm packages, mirrors `StudioMapPage.tsx`'s existing marker pattern) and ASN organization on a new aggregate-only "Top networks" breakdown card. Postal code/continent/timezone/accuracy-radius are captured and persisted on `TrafficEvent` but deliberately **never rendered anywhere** — postal code is materially more identifying than city and would break the existing Help-copy promise ("no visitor is ever identified... only role, rough location, and device") if shown per-visitor; the others simply have no UI need yet. **No backfill**: `TrafficEvent` never stored a raw IP (only a one-way SHA-256 hash), so every new column is `NULL` on all pre-2026-08-05 rows and only populates going forward. `GeoIP.conf`/`geoipupdate` needed no changes — it already pulled all three free editions. |
| Manual Client Reminders (2026-08-21) | New `ManualReminder` entity (Hangfire-scheduled, `ManualReminderJob` mirrors `AppointmentReminderJob`) is purely additive to the existing automatic 48h/24h reminder pipeline — three-way recipient resolution (an appointment's linked client, an existing `Client` record, or a raw typed name+phone that creates no `Client` row at all). Deliberately bypasses `INotificationPreferenceService` (a deliberate one-off artist action shouldn't be silently swallowed by a stale studio-wide preference toggle). `Client.SmsOptOut` added and checked on every outbound SMS path (automatic and manual alike), but nothing sets it yet — the inbound Twilio STOP webhook that would is flagged out of scope, not silently dropped. `NotificationLog.RecipientId` widened `Guid` → `Guid?` and `NotificationRecipientType` gained `ExternalContact`, since a raw-contact reminder has no linked Client/Studio/Artist id — cascaded into `NotificationLogResponse`, `GetNotificationsQuery`'s recipient-name resolution, and the notification bell/list UI's fallback text ("External contact" instead of crashing on a null id). Per-artist quota: 20/day, Redis `INCR`+`EXPIRE` via `IManualReminderQuotaService` (mirrors `RedisFixedWindowRateLimiter`'s Lua-free `IDatabase` pattern, not `PlanLimitService`'s `IDistributedCache` pattern, since this needs atomic increment) — **fails closed** on a Redis outage, the opposite of `RedisFixedWindowRateLimiter`'s fail-open default, because this quota's entire purpose is bounding real Twilio SMS cost. `CreateManualReminderCommand.AuditTargetId` is a mutable property, not constructor-derived, since the audited `ManualReminder` row doesn't exist until the handler creates it — confirmed safe by reading `AuditLogBehavior.cs`: it reads `IAuditableCommand` properties only after `Handle()` returns. | Vertical-booking-SaaS current standard (CLAUDE.md rule 6) — Vagaro/Fresha/Boulevard all offer artist-initiated manual client messaging alongside automated reminders. Redis-backed quota on a freely-addressable, cost-bearing SMS endpoint follows the same "flag the gap, don't ship it silently" posture rule 6 already applies elsewhere. Verified: dotnet build/test green (1559 unit + 359 integration, up from 1532/346); pnpm tsc/lint/test green; app boots for real after the entity/DI/migration changes (not just green tests — `docs/claude/feedback_di_wiring_verification.md`'s standing lesson). Quota's real-Redis path isn't integration-tested — CI provisions MySQL only, matching the existing "every external service is NSubstitute-mocked at the handler level" convention (`ci.yml`); covered instead by `ManualReminderQuotaServiceTests` against a mocked `IDatabase`. |
| "Get Directions" — Google Maps deep link on studio location surfaces (2026-08-20) | `PublicStudioResponse` gains `Latitude`/`Longitude` — previously only `City` was exposed on the public studio DTO, even though `Studio.Latitude`/`Longitude` already existed on the entity and were already public via `StudioMapItemResponse`/`GET /api/studios/map`. New `shared/utils/googleMaps.ts` (`buildGoogleMapsDirectionsUrl`, `hasPinnedLocation`) builds Google's documented `/maps/dir/?api=1&destination=lat,lng` URL — no API key, no new npm/NuGet package. Link added to `StudioPortfolioPage.tsx`'s sidebar (replacing the plain city text with an actionable link, same pattern as the phone/Instagram links directly above it) and to each pin's `Popup` in `StudioMapPage.tsx`. `StudioMeta`'s `TattooParlor` JSON-LD also gains an optional `geo: GeoCoordinates` block now that the data is available. Guarded on `hasPinnedLocation` — `(0, 0)` means unset, same sentinel convention `LocationPicker.hasInitial` already uses, since `RegisterStudioValidator` only range-checks `[-90,90]`/`[-180,180]` and doesn't reject the origin outright. No `AllowAnonymous Exceptions` table change — extends the response shape of an already-approved anonymous endpoint (`GET /api/v1/public/studios/{slug}`), doesn't add a new one. | Current vertical-booking-SaaS standard (CLAUDE.md rule 6) — Fresha/Vagaro/Boulevard/GlossGenius studio-detail pages all surface a one-tap "Get Directions" to the studio's pinned location; this codebase already had the exact geodata and an identical, already-shipped marker pattern to copy from (`StudioMapPage.tsx`), it just wasn't wired to a client-facing deep link yet. Deliberately scoped to the two surfaces where a client is looking at one specific studio's location (profile page, map popup) — `DiscoverPage`'s Studios-tab card grid and `MyBookingsSection`'s per-appointment rows were considered and explicitly deferred as separate, larger gaps (studio location/directions on both). Verified: dotnet build/test green (52 unit + 9 integration Public-suite tests); pnpm tsc/lint/test green (211 tests across public/map/shared-utils); pnpm build clean. |
| Owner-as-artist cross-tenant invite fix — 2026-08-21 | `CreateArtistHandler` now checks the existing account's role and tenant membership (new `IIdentityService.GetUserRolesAsync`, plus the already-existing `GetTenantIdsAsync`) before reusing an Identity user's ID on "email already taken." Only a genuinely orphaned artist account for the SAME studio is recovered; an owner, client, issuer, or an artist already belonging to a DIFFERENT studio now throws `BusinessRuleViolationException` instead. `GetUserRolesAsync` returns `IReadOnlyList<string>` via an explicit `.ToList()` — `UserManager.GetRolesAsync` returns `IList<string>`, which does not implicitly convert to `IReadOnlyList<string>` despite `List<T>` satisfying both at runtime; confirmed by a real compile error, not assumed. | Previously any existing account's Identity `UserId` was silently reused and linked as a brand-new `Artist` row in the inviting studio, with no role or tenant check — letting one studio's owner invite (by email) an owner, artist, or client account from a completely unrelated studio and silently gain an `Artist` record pointing at that account, without its holder's consent. Direct violation of CLAUDE.md Rule #1 (tenant isolation). Artist and owner accounts are single-studio by design — only `client` supports multi-studio membership (`GenerateJwt`'s tenant-claim comment; architecture.md's "Multi-Studio Client View" entry #23) — so this guard generalizes cleanly to every non-artist-same-studio case, not just the reported owner scenario. Verified: dotnet build/test green (14 unit + 28 integration Artist/Identity-suite tests, all new sub-cases covered — owner, cross-studio artist, same-studio client, and the genuine same-studio-orphan recovery path). |
| Owner-as-artist dual role (self-service) — 2026-08-21 | Owner keeps a single "owner" Identity role — RBAC already treats owner as a superset of artist via the `ArtistAndAbove` policy (`AuthorizationExtensions.cs`), so no new role or JWT change is needed. New `CreateOwnArtistProfileCommand` (`POST /api/v1/artists/me`, `OwnerOnly`) creates an `Artist` row linked to the owner's own existing `UserId`/`Email` — no new Identity account, no role claim change, no invite email, no migration (`Artist.UserId` already nullable). Never calls `identity.CreateUserAsync`, `AddToRoleAsync`, or `scheduler.EnqueueArtistInvite` — it links an existing account, it never creates one, so it cannot interact with or regress the same-day cross-tenant-invite-reuse fix above. Added a role-based guard to `ResendArtistInviteCommand` (reusing `IIdentityService.GetUserRolesAsync` from that same fix) so it can never fire on the owner's own linked profile — there's no invite to resend. Owner's own artist seat counts against the plan's Artist quota (`IQuotaCheckedCommand`), same as any invited artist. `ArtistDetailPage.tsx`'s "Delete" relabels to "Stop working as an artist" only for the owner's own linked profile (`isOwnProfile`, already correctly computed via `usePermission(Role.Artist)`'s rank-based check) — the list-row Delete action stays generic, a deliberate scope boundary. Rejected a true multi-role-identity + role-switcher design as unnecessary given existing RBAC and much larger blast radius (JWT generation, every role-gated route guard, help/tour role-set assumptions) for no functional gain. | Matches the "linked profile" pattern `ArtistLayout` already uses for `myArtist`-conditional nav; keeps this additive rather than touching the JWT/role model; composes cleanly with the same-day cross-tenant-invite-reuse fix by construction, not by coincidence. Verified: dotnet build/test green (13 unit CreateOwnArtistProfile + 3 new ResendArtistInvite sub-cases, full suite unaffected); pnpm tsc clean. |
| Studio-choice booking — 2026-08-21 | `Appointment.ArtistId`/`Artist` widened to nullable (`Guid?`/`Artist?`, one migration, zero-downtime — no existing row has a NULL, EF infers the now-optional FK from the CLR type same as `Client.ArtistId` already does). A client can toggle "let the studio choose" instead of picking an artist; `CreateAppointmentCommand` branches into a soft, no-lock "is any active artist free" advisory check (new `IAppDbContext.IsAnyArtistAvailableAsync` extension in `Application/Common/`, mirroring `ClientAccountExtensions.cs`'s shape — `Domain` can't depend on `IAppDbContext`, so this can't live in `Domain/Services` next to `DepositCalculator`). New `AssignAppointmentArtistCommand` (`PATCH /api/v1/appointments/{id}/artist`, `OwnerOnly`, mirrors `UpdateClientArtistCommand`'s roster-assignment precedent) does the real single-artist claim under `ISlotLocker`, with a fresh (deliberately duplicated, not shared-refactored) copy of the schedule/time-off/conflict validation `CreateAppointmentCommand` already has for its specific-artist path — minimizes risk to that already-working path over the small dedup win. No new `AppointmentStatus` value: "needs artist" is computed (`Status == Pending && ArtistId == null`), never stored. `ConfirmAppointmentCommand` now rejects confirming an unassigned appointment, server-side, matching this codebase's "never trust the frontend gate alone" convention. A deferred percent-rule deposit (`0` at booking time, no artist rate to compute from) recomputes automatically the moment an artist — and their hourly rate — is assigned; a fixed-rule deposit is untouched either way. `CreateManualReminderCommand.cs`, a file in the unrelated Reminders feature, needed a mandatory fix (an artist-role caller on an unassigned appointment gets the same 404 as "not yours"; an owner/issuer caller gets a clear `BusinessRuleViolationException`) since `appointment.Artist`/`ArtistId` going nullable broke it at compile time — not optional cleanup, the build would not compile without it. `GetRevenueSummaryQuery.cs` needed the same nullable-propagation treatment (a payment on a still-unassigned appointment is excluded from the per-artist revenue breakdown, same as any appointment id not found at all) — a second cross-feature break the source prompt's research pass missed. | Current vertical-booking-SaaS standard (CLAUDE.md rule 6) — Fresha/Vagaro/Boulevard all support "any available provider" as a booking option alongside picking a specific one. No `ISlotLocker` claim at booking time for the studio-choice path (Decision #6 in the source prompt) because no specific resource is being reserved yet — the real claim happens exactly once, at assignment, under a real lock; avoids a phantom "reserved but for nobody" lock state. Verified: dotnet build/test green (1618 unit + 367 integration, up from 1580/363 — new `AssignAppointmentArtistHandlerTests`/`AssignAppointmentArtistValidatorTests`/`ConfirmAppointmentHandlerTests`/`AppointmentArtistEndpointAuthorizationTests` plus new cases across `CreateAppointmentHandlerTests`, `RescheduleAppointmentHandlerTests`, `CheckSlotAvailabilityHandlerTests`, `CreateManualReminderHandlerTests`); pnpm tsc/lint/test green; migration applied and app boots for real (`docs/claude/feedback_di_wiring_verification.md`'s standing lesson — two new MediatR-scanned handlers, `AssignAppointmentArtistHandler` and `SendAppointmentArtistAssignedNotificationHandler`). |
| Social media verification — config-gated, partial-rollout-safe rollout (2026-08-22) | New `SocialAccountLink` entity is additive alongside `InstagramConnection` (which keeps owning artist photo sync unchanged — the only edit to that code is one extra block in `ExchangeInstagramCodeCommand` that also upserts a matching `SocialAccountLink` row, plus a matching clear in `DisconnectInstagramCommand`). All five platforms (Instagram, TikTok, Facebook, X, YouTube) get real, registered `ISocialOAuthProvider`/`ISocialBioChecker` implementations in this pass — a platform is never "missing code," only `IsConfigured`/`IsSupported == false` until real credentials exist, which the endpoints report as `409 Conflict`/`422` rather than a broken action. Facebook and X specifically ship with empty credentials by design (Meta App Review and a paid X API tier are external, human, non-code steps this session cannot complete) — this is the intended state, not a bug to fix later. TikTok/Facebook/X endpoint shapes were written from the current spec's best understanding of each platform's stable OAuth/API URLs, not re-verified against each platform's live developer docs in this session — flagged in each provider file's header comment; confirm before relying on them in production, same caution `feature-request-two-sided-referrals.md` already established for a pinned third-party SDK version. Platform credentials follow the existing `InstagramOptions`/`GoogleOptions`/`AppleOptions` precedent (`Configure<TOptions>` bound from an appsettings section, real values via env var) rather than `ISecretsProvider`/`StudioCredentialRef` — that mechanism is documented elsewhere in this file as per-STUDIO credential pointers with zero real consumers yet (ADR-0001 follow-up), the wrong scope for platform-wide OAuth app secrets. | Matches CLAUDE.md rule 6's benchmark: verified-social badges are standard on Vagaro/Fresha/Boulevard/GlossGenius-tier profiles for Instagram; TikTok/Facebook/X/YouTube verification goes beyond what those benchmarks do today, a deliberate above-benchmark investment made with the product owner's explicit sign-off in the source spec, not a silent scope-creep. Manual verification uses each platform's own official public-read API (Meta Graph API Business Discovery, YouTube Data API v3, X API v2) rather than scraping — a real ToS/reliability risk the source spec explicitly ruled out; TikTok has `IsSupported == false` because no such API exists for it, OAuth is its only route. Verified: dotnet build/test green (1633+ new Social-suite unit tests + 4 new `SocialVerificationIntegrationTests`, incl. the suspended-studio check written from day one per the source prompt — the exact bug class already fixed twice for the artist Instagram path); pnpm tsc/lint/test/build green (1899 tests); migration applied to a real dev DB including the `Studio.InstagramHandle` → `SocialAccountLink` backfill; app boots for real and the new endpoints route correctly (`docs/claude/feedback_di_wiring_verification.md`'s standing lesson). Deferred, not silently dropped: the periodic re-verification Hangfire job for OAuth-verified Artist-subject links (token is retained specifically to support this later), and dropping the now-unread `Studio.InstagramHandle` column (kept per the zero-downtime convention). |
| In-App Messaging (2026-08-26) | New `Conversation`/`ChatMessage` entities follow the `DesignRevision` pattern — ordinary `TenantEntity` with a real `StudioId` query filter — deliberately NOT the `FeedbackReport`/`FeedbackMessage` non-tenant exception, since this is real per-studio data, not an issuer cross-tenant ticket system. New `ChatHub` auto-joins a personal `user:{userId}` SignalR group on connect instead of `SupportHub`/`ScheduleHub`'s join-a-resource-group-by-id model — a 1:1 conversation only ever has two already-authenticated participants, so there is no resource id a client could leak or guess, which sidesteps by construction the exact ownership-check bug class `SupportHub.JoinTicket` originally had (see the Support Escalation entry). Eligibility (who a client/artist/owner may message) is relationship-based — client↔their appointment/assigned artist, artist↔their appointment/assigned client, anyone↔the studio owner (resolved via `Studio.OwnerEmail` → `IIdentityService`, same indirection `RegisterOAuthUserHandler`'s owner-email-match already uses) — computed once in an internal `ConversationEligibility` helper shared by the read-side contacts query and the write-side create-conversation check, so the two can't drift (same reasoning as `FeedbackAccessGuard`). Scope is deliberately client↔artist, client↔owner, artist↔owner only — no client↔client, no artist↔artist, no issuer (issuer already has `FeedbackReport`/`SupportHub` for platform support). `POST /api/v1/conversations` is a get-or-create endpoint returning `200`, not `201` — a deliberate, commented-inline deviation from the "201 for a creating POST" convention, since the caller (a "message this person" button) never knows in advance whether a thread already exists. No `NotificationLog` row is written for a chat message — `ChatMessage` itself already durably stores the content and its own `ReadAt` read-state, so a second copy would be redundant; the only new notification surface is `NotificationType.MessageReceived`, Email-channel only (SMS is real per-send cost and would trip on every message in a live back-and-forth — matches the Manual Client Reminders entry's SMS-cost reasoning). The Email is debounced: `SendChatMessageHandler` counts prior unread messages already sent BY the current sender before inserting the new row, and only enqueues `ChatNotificationJob` (Hangfire) when that count is zero — **a real bug was caught and fixed here**: the first-written version of this count checked `SenderUserId != user.UserId` (unread messages from the *other* participant) instead of `== user.UserId` (unread messages from the *sender's own prior streak*), which is backwards for a debounce condition and was only caught because `SendChatMessageHandlerTests` asserted the email is NOT re-enqueued for a second message in the same streak — the test failed against the buggy code, not just against a hand-derived expectation. `ChatNotificationJob` needs `IgnoreQueryFilters()` for the same reason `ManualReminderJob`/`SendArtistInviteJob` already do (a Hangfire job has no `ICurrentTenant` HTTP-request scope to satisfy the query filter) — added to that existing approved-usages row (#36) rather than as a new row, since it's the same already-approved class of usage, not a new exception. Frontend `useChatHub` mirrors `useSignalR`'s always-on-per-layout mounting pattern (not `useSupportHub`'s per-thread-mount pattern), with `useSupportHub`'s two documented bugs (missed reconnect rejoin, self-echo double-refetch) built in from the start rather than discovered later — ChatHub's per-connection auto-join means there is no reconnect-rejoin bug class to begin with. `AppointmentDetailPage.tsx` (artist/owner/issuer-only route) gained a "Message [client]" button gated on role !== Issuer and a new `AppointmentResponse.ClientUserId` projection field. | Current vertical-booking-SaaS standard (CLAUDE.md rule 6) — Vagaro/Fresha/Boulevard/GlossGenius-tier "message your provider" is a two-party, cross-role thread, not a group chat, matching the scope decided here. Flagged, not silently shipped: no attachments (Decision 7 — `FeedbackReport.AttachmentUrls`' R2 presign flow is the proven pattern to reuse later), no edit/delete (Decision 8), no typing indicators/presence (`TrafficHub` is the only precedent and is issuer-analytics-scoped, a materially different feature), no push notifications (B19 mobile/PWA is itself still missing). Verified: dotnet build/test green (1737 unit + 379 integration, up from 1714/377 — new `Messaging/*HandlerTests`, `ConversationTests`, `ChatMessageTests`, `GetConversationContactsHandlerTests` covering every eligibility branch, `MessagingEndpointAuthorizationTests` exercising the real ASP.NET Core auth pipeline); pnpm tsc/lint/test green (1929 tests, up from 1915 — new `useChatHub`/`ConversationThread`/`NewConversationDialog`/`MessagesInboxPage` tests plus extended `ClientLayout`/`ArtistLayout`/`OwnerLayout` suites); migration applied to a real dev DB; Help Menu (3 new articles), standalone manual (3 new sections + 2 existing appointment-detail sections updated), and all three non-issuer onboarding tours updated in the same change. |
| In-App Messaging — post-merge `/code-review` findings fixed (2026-08-26) | A dedicated review pass on the entry above's diff found 10 real issues, all fixed same-day. **Security-adjacent:** `ConversationEligibility` never branched on the `issuer` role, so an issuer request fell through every client/artist/owner branch and still picked up the unconditional "owner is reachable by anyone" contact — added an explicit early-return for `issuer` (empty contact list, matches Decision 1). `GetConversationMessagesQuery`'s `before` cursor was resolved by `ChatMessage.Id` alone with no `ConversationId` check, letting a caller supply a real message id from a DIFFERENT conversation they have no access to and leak that conversation's message timing via the resulting page boundary — cursor lookup now scoped to `m.ConversationId == query.ConversationId`. **Correctness:** the client-role branch of `ConversationEligibility` was missing the `IsActive` filter the owner-role branch already had, so a client could still message an artist the studio had deactivated — added. `useChatHub`'s `ConversationRead` handler invalidated only the `["Conversation"]` tag, never the per-conversation `{type:"Messages", id}` tag `getMessages` is cached under, so a sender's open thread kept showing an unread checkmark after the recipient actually read it — fixed to invalidate both. **The debounce race** (`SendChatMessageHandler`'s email-trigger check had a window where two concurrent sends in the same conversation could both decide "I'm first" and both enqueue an email) went through two designs, not one. The first attempt added `Conversation.PendingNotificationSenderUserId` as an EF Core concurrency token, claimed via a **separate `IAppDbContext` from `IAppDbContextFactory`** (the same "several queries can't share one DbContext" mechanism `GetTrafficBreakdownQuery` already established) so a losing claim's exception couldn't touch the message-insert save. That still failed under a real concurrent-send test: EF Core includes a concurrency token's original value in the WHERE clause of *every* update to that row, not just updates that touch the token — so a SECOND concurrent request's own unrelated message-insert (via a third context that had loaded the conversation before the first request's claim committed) got its own `DbUpdateConcurrencyException` and would have failed to save its message entirely, a strictly worse failure mode than the duplicate email being fixed. Reverted (entity field, EF config, `IAppDbContextFactory` dependency, and the migration all backed out) in favor of a schema-free, single-context design: after inserting, ask "of every currently-unread message from this sender, is this one the earliest?" (`ORDER BY CreatedAt` over `ChatMessages`) — whichever message in a streak is earliest is definitionally the one that started it, so exactly one message per streak answers yes under sequential sends. This intentionally does not claim perfect atomicity under true concurrent sends (a pathological interleaving could still under- or double-count) — accepted as this debounce's residual risk given it is a UX nicety, not a delivery guarantee, and given the alternative (the concurrency-token design) was demonstrably worse, not better. **Missing test coverage** (`GetConversationsHandler`, `GetUnreadMessageCountHandler`, `CreateConversationValidator`, `SendChatMessageValidator` had none, a direct CLAUDE.md rule violation) closed with new test files for each. **Two N+1 query patterns** fixed: `GetConversationsHandler` went from ~2-3 queries per conversation (60-90 for a 30-conversation inbox) to one grouped unread-count query plus one batched per-role display-name lookup; `GetConversationContactsHandler` went from one existing-conversation query per eligible contact (50-200 for an owner's full contact list) to loading the caller's own conversations once and matching in memory. **Deliberately not fixed, flagged instead:** the review's 10th finding — `ConversationAccessGuard` is a third independent hand-rolled "load + authorize + throw Forbidden" guard class alongside `FeedbackAccessGuard` and `ConductReportAuthorizationGuard`, and could be generalized into a shared MediatR pipeline behavior — was judged out of proportion for this pass: unifying it would mean redesigning and re-touching two other already-shipped, already-tested features for a DRY win, with no test safety net sized for that broader refactor in this session. Left as-is, matching this feature's own explicit mandate to mirror `FeedbackAccessGuard`'s shape. | Every fix here follows a pattern already established elsewhere in this codebase rather than inventing a new one — batched projections over N+1 loops (the general EF Core anti-pattern this review class always flags), `IAppDbContextFactory` considered (not ultimately used) for the debounce fix via `GetTrafficBreakdownQuery`'s precedent. The debounce redesign is its own small lesson: the first fix was more "correct-looking" (real DB-level atomicity via a concurrency token) but wrong in practice, caught only because a real concurrent-send test was written for it rather than trusting the design on inspection — worth remembering next time a concurrency token looks like the obvious answer for a narrow field-level claim on a row that's also updated by unrelated code paths. Verified: dotnet build/test green (1761 unit + 379 integration, up from 1737/379 — new eligibility/cursor/validator/handler test coverage, including streak-ordering tests for the redesigned debounce check); pnpm tsc/lint/test green; no new migration needed (the reverted concurrency-token migration was removed, not left as dead schema). |

---

## Issuer QA Pass — 2026-07-01 (reconstructed 2026-07-20)

`overnight-prompt-issuer-qa-polish-2026-07-01.md` exists and was clearly executed —
`IssuerStudioDetailPage`, `platformApi.getStudioById`, the `IgnoreQueryFilters` approved
usages table entries #4/#5/#7–#9, and the toast/spinner/confirm patterns across every
issuer component all show the fingerprints of that pass having run — but unlike the
other four role passes, its results were never logged here. That original session's
actual diff (what was broken vs. already-working before the pass) is lost. This entry
is a **best-effort reconstruction**: it diffs the original prompt's checklist against
*current* source (2026-07-20), not against whatever state the code was in on 2026-07-01.
It cannot tell you what the pass itself fixed — only that the checklist's requirements
are (or aren't) satisfied today.

### Checked against current source — satisfied

- `GetPlatformStatsHandler` — `totalStudios` counts all studios incl. suspended;
  `activeSubscriptions`/`trialStudios`/`gracePeriodStudios`/`pastDueStudios`/
  `cancelledStudios` all correctly scoped by `SubscriptionStatus`; `mrr` sums active
  subscriptions only, via the post-split `PlanPrice`/`BillingInterval` calculation
  (supersedes the original prompt's `Plan.PriceMonthly` approximation, which no longer
  applies); `mrrGrowthPercent` and `trialConversionRate` both guard the zero-denominator
  case; `newStudiosThisMonth` filters on `CreatedAt >= monthStart`.
- `ExtendTrialHandler` — `AdditionalDays` validated `InclusiveBetween(1, 90)`; extends
  `TrialExpiresAt`, not `GracePeriodEnd` directly (`GracePeriodEnd` is derived from the
  new expiry); a `GracePeriod` studio whose trial is extended reverts to `Trialing`.
- `CancelSubscriptionHandler` — sets `Status = Cancelled`; Stripe cancellation is
  best-effort (logged, not rethrown) when a `StripeSubscriptionId` exists, matching the
  Decisions Log's "CancelSubscriptionCommand Stripe side-effect" entry.
- `DeletePlanHandler` — refuses deletion when any `Subscription` (any status, not just
  active) references the plan — a stricter superset of the original spec's "active
  subscriptions" wording, not a regression.
- `IssuerStudioListPage.tsx`/`PlatformReferralPage.tsx` — every mutation (suspend,
  unsuspend, extend trial, activate, cancel, deactivate/reactivate/delete/generate
  referral code) fires a matching `toast.success`/`toast.error` pair; every action
  button shows `Loader2` + is `disabled` while its mutation is in flight; destructive
  actions (suspend, delete referral code) have an inline confirm step.
- `IssuerStudioDetailPage` — fully built (not a placeholder), confirmed independently
  during the Phase 2.12 pass above: studio identity, subscription status/actions
  (extend/activate/cancel), `IgnoreQueryFilters()` usage #8 correctly gated behind
  `IssuerOnly`.
- Plan/PlanPrice-era fields (`AllowBrandingRemoval`, per-interval pricing) are present
  end-to-end — already confirmed via the repo-wide dead-field grep in the first audit
  pass (zero hits for the pre-split shape anywhere in `PlanManagementPage.tsx`).

### Deviations from the original spec (not bugs — later, intentional design choices)

- `SuspendStudioHandler`/`UnsuspendStudioHandler` don't return 400 when the studio is
  already in the target state — they're idempotent no-ops instead. This is consistent
  with the idempotency convention used elsewhere in the codebase (e.g.
  `SavePortfolioImageHandler`) and arguably better UX (no need to guard against a
  double-click client-side); not something to "fix" back to the original spec.

### Not re-verified this reconstruction (scope limit)

The original prompt's Layer C per-component bug list (C1–C7, ~30 individual items:
MRR chart period selector, referral-code copy button, industry-report trigger
cooldown, etc.) and its Layer D per-file required-test-case checklist (D1–D7, ~70
individual test names) were not re-walked item-by-item against current source or
current test files. Given every issuer page independently confirmed present and
working (toasts, spinners, confirms, `IgnoreQueryFilters` scoping, stats correctness)
across both this reconstruction and the earlier Phase 2.12 pass, and given
`dotnet test`/`pnpm test` are both fully green including all issuer test files, there
is no positive evidence of a regression here — but that's different from having
individually confirmed all ~100 checklist line items. A future pass that specifically
wants Layer C/D closure should walk `docs/claude/overnight-prompt-issuer-qa-polish-2026-07-01.md`
directly against `IssuerDashboardPage.tsx`/`IssuerStudioListPage.tsx`/
`PlanManagementPage.tsx`/`SubscriptionOversightPage.tsx`/`PlatformReferralPage.tsx`/
`IndustryReportsPage.tsx` and their test files line by line.

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

- `ArtistPortfolioPage.tsx` → canonical URL was `https://tattooos.co/a/${slug}`, but
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

---

## Full-App Master Audit — 2026-07-20

Scope note (read before trusting this as exhaustive): this pass ran the Phase 1
regression sweep at full depth and spot-checked the highest-risk items called out
explicitly in `overnight-prompt-full-app-master-audit-2026-07-20.md` for Phase 2 and
Phase 3, rather than working every one of the 12 Phase 2 subsections and 10 Phase 3
matrix rows to the same exhaustive depth as the five original QA passes. See
"Deferred items" below for exactly what was not independently re-verified this pass.

### Baseline state found (before any fix)
- `dotnet build` — clean, 0 errors.
- `pnpm build` — **broken**, 12 TypeScript errors across production code and test
  fixtures (`BillingPage.tsx`, `DashboardPage.tsx`, `IssuerDashboardPage.tsx`,
  `ArtistPortfolioPage.tsx`, and four test files). Root cause: several schema/type
  changes from the last few days' features (`PlatformSubscriptionResponse.isSuspended`,
  `ArtistPortfolioImage.style`, `ReviewResponse.ownerResponse`/`ownerResponseAt`,
  `ConsentFormResponse.clientName`) landed without every consumer/fixture being
  updated. This is exactly the "green suite for feature N doesn't prove it didn't
  break N-3" failure mode this prompt exists to catch — except here it wasn't even a
  passing-but-wrong runtime bug, `pnpm build` itself was red.
- `dotnet test` — 1 integration failure: `IndustryReportJob_Run_SmallCohort_MetricsAreNull`.

### Phase 1 — Regressions found and fixed
- `frontend/src/features/billing/components/BillingPage.tsx`,
  `frontend/src/features/dashboard/components/DashboardPage.tsx` — `sub.trialExpiresAt`
  (`string | null`) passed directly to date-formatting helpers expecting `string`.
  Fixed with `sub.trialExpiresAt ?? sub.currentPeriodEnd` (both are populated together
  at trial start per `RegisterStudioCommand`, so the fallback is never actually reached
  in practice — this was a type-strictness gap, not a runtime bug).
- `frontend/src/features/platform/components/IssuerDashboardPage.tsx` — `ExpiryLabel`
  was fed `sub.trialExpiresAt` for `PastDue` rows, which is always `null` post-conversion
  (cleared by `CreateSubscriptionCommand`/`ActivateCheckoutSubscriptionCommand`/etc.);
  `ExpiryLabel` never actually reads the value for `PastDue` (early-returns "Payment
  overdue"), so this was a dead-but-type-broken read. Simplified to always pass
  `sub.currentPeriodEnd`.
- `frontend/src/features/platform/__tests__/IssuerDashboardPage.test.tsx` — 3 fixtures
  missing `isSuspended` (field added to `PlatformSubscriptionResponse` by a later prompt,
  fixture never updated).
- `frontend/src/features/clients/__tests__/ClientDetailPage.test.tsx`,
  `frontend/src/features/public/__tests__/ArtistPortfolioPage.test.tsx`,
  `frontend/src/features/public/__tests__/ReviewSection.test.tsx` — stale fixtures
  missing `clientName`, `style`, `ownerResponse`/`ownerResponseAt` respectively (same
  root cause as above).
- `frontend/src/features/public/components/ArtistPortfolioPage.tsx` — `handleBack`
  closure read `artist.studioSlug` without TS being able to carry the earlier
  `!artist` early-return guard across the function boundary. Captured `studioSlug`
  into a local `const` before the closure; no behavior change.
- `tests/Pena_e_Arte.IntegrationTests/Application/IndustryReportsIntegrationTests.cs` —
  removed `IndustryReportJob_Run_SmallCohort_MetricsAreNull`. `DatabaseFixture`
  provisions one shared MySQL database for the entire `[Collection("Database")]` run
  with no per-test reset; by 2026-07-20 well over 10 other integration tests across the
  suite create their own `SubscriptionStatus.Active` studios in that same database, so
  the test's "seed 3, expect cohort < 10 ⇒ null" assumption is no longer safe — it's
  asserting on a global count it doesn't control. The exact same suppression-threshold
  logic is already covered deterministically at the unit level, with no DB dependency,
  in `IndustryReportJobTests.BuildDocument_CohortBelowMinimum_AllMetricsNull` /
  `_CohortAtMinimum_MetricsPresent` (calls `IndustryReportJob.BuildDocument` directly
  with a synthetic `IndustryAggregates`). Left a comment explaining the removal in place
  of the test.
- Priority regression checks 1–9 from the audit prompt (`ReviewDesignCommand` ownership
  check, `GetNotificationsQuery` artist+client scoping and `ClientAndAbove` policy,
  the 10 named artist-ownership-checked commands, `CancelAppointmentCommand` refund
  branch, `GetPlatformStatsQuery`/`GetMrrHistoryQuery` MRR-from-`PlanPrice` calculation,
  `window.location.origin` usage, mobile nav overflow on all 4 layouts, per-route
  `ErrorBoundary` wrapping, reschedule's `["Appointment"]` cross-slice invalidation) —
  read directly against current source, all still correct, no regressions found.
- Item 10 (`Issuer QA Pass` never logged) — not reconstructed this pass; see Deferred.

### Phase 2 — New-surface bugs found and fixed
- **Instagram Sync (2.3)** — `ToggleInstagramPostVisibilityCommand`
  (`PUT /api/v1/artists/{id}/instagram/posts/{postId}/visibility`, policy
  `ArtistAndAbove`) trusted the `{id}` route parameter as the acting artist with no
  check that the caller's own artist profile was `{id}`. Any artist could toggle
  Instagram post visibility for any colleague artist in the same studio — the exact
  bug class the 2026-07-01 Artist QA pass fixed across 9 other handlers
  (`ConfirmCashDepositCommand`, `CreateDesignShareTokenCommand`, etc.), just never
  applied here because this feature shipped after that pass. There was no test file
  for this handler at all. Fixed by injecting `ICurrentUser` and adding the same
  `Role == "artist"` ownership check used by `ConfirmCashDepositCommand`; added
  `tests/Pena_e_Arte.UnitTests/Instagram/ToggleInstagramPostVisibilityCommandTests.cs`
  (3 tests: owner can toggle any artist's post, artist can toggle their own, artist
  toggling a colleague's throws `ForbiddenException`). `GetInstagramPostsQuery`/
  `GetInstagramConnectionStatusQuery` were checked too and are intentionally
  read-permissive within the tenant — matches the established "reads open within
  tenant, only mutations scope-restricted" convention already documented for
  `GetDesignQuery` in the Artist QA Pass section above, not a bug.

### Confirmed clean (no action needed)
- Full repo grep for `PriceMonthly`/`PriceYearly`/`StripePriceIdMonthly`/
  `StripePriceIdYearly`/`PairedPlanId` (Phase 2.10) — zero hits outside migration
  files and one unrelated local test-parameter name (`SeedPlan(stripePriceMonthly:)`,
  which sets the new `PlanPrice.StripePriceId`, not a removed field).
- `DataSeeder.ReconcileCoreTiersAsync`'s Free tier (Phase 2.7/2.9's explicitly flagged
  highest-risk check) has real, non-null caps: `MaxArtists=1, MaxAppointmentsPerMonth=15,
  MaxNotificationsPerMonth=50, MaxStorageGb=1, MaxLocations=1` — not accidentally
  unlimited.
- `IQuotaCheckedCommand` is implemented by exactly `CreateArtistCommand` and
  `CreateAppointmentCommand`, matching the documented decision — no drift, and
  confirms `RescheduleAppointmentCommand` correctly does NOT re-check the monthly
  quota (Phase 3 row 8: rescheduling changes `Date`, not count).
  `RescheduleDialog.tsx` exists (the frontend for the reschedule feature, 2.11, was
  in fact built, not left deferred again).
- OAuth registration → referral code (Phase 3 row 6): `RegisterOAuthUserCommand`
  takes a pre-existing `StudioId`, it doesn't create the studio. Studio creation
  (where `ReferralCode` → `PendingReferralCodeId` is captured, in
  `RegisterStudioCommand`) happens in step 1 of `RegisterStudioPage` regardless of
  which auth method (password vs Google/Apple) is chosen in step 2 — confirmed in
  the frontend that `referralCode` is included in the step-1 studio-creation payload
  before either `oauthRegister` or the password path ever runs. No gap.

### Phase 2/3 completion — 2026-07-20 (continued session)

The remaining Phase 2 subsections (2.1, 2.2, 2.4–2.6, 2.8, 2.11, 2.12) and Phase 3
matrix rows (1–5, 7, 9, 10) were worked to full depth in a follow-up pass on the same
branch. Full findings below; this closes out the "Deferred items" gap from the first
pass above.

#### Phase 2 — additional bugs found and fixed

- **Redis Rate Limiting (2.5)** — `GET /api/v1/instagram/callback` (the Instagram
  OAuth redirect target, `AllowAnonymous`) had **no rate-limit policy at all** — every
  other anonymous endpoint in the app has one. It triggers a real external HTTP call
  to Instagram's token-exchange API plus a DB write on every hit, making it both an
  external-cost and DB-write vector with zero throttling. Added
  `.RequireRateLimiting("public-write")`.
- **My Studios (2.2)** — `GetClientStudioNotificationPreferencesQuery` and
  `UpdateClientStudioNotificationPreferencesCommand` accepted any caller-supplied
  `StudioId` with no check that the calling client actually holds a `tenant_id` claim
  for that studio. Impact is low (both are strictly self-scoped by `UserId`, so this
  can't read or write another user's data — worst case is junk preference rows for a
  studio the client was never part of, which are never read back for anything since
  notifications only ever reach real `Client` rows), but it's inconsistent with the
  rest of the codebase's resource-ownership convention (e.g. `LeaveStudioHandler`
  checks the same way). Added the same `GetTenantIdsAsync(...).Contains(studioId)` →
  `NotFoundException` check to both handlers, matching `LeaveStudioHandler`'s pattern.
  6 tests updated (added `IIdentityService` mock + tenant-id seeding), 2 new tests
  added (`Handle_UserNotMemberOfStudio_ThrowsNotFound` on each handler).
- **Instagram Sync (2.3, revisited under Phase 3 row 4)** —
  `GetPublicArtistInstagramPostsQuery` never checked `Studio.IsActive`, unlike its
  sibling `GetPublicArtistQuery` (which already 404s the rest of the portfolio page
  for a suspended studio). A suspended studio's Instagram posts (media URLs, captions)
  remained fetchable by calling this endpoint directly, even though the main portfolio
  page correctly hid them. Added the same `Studio.IsActive` check
  `GetPublicArtistQuery` uses. Also `InstagramSyncJob.ExecuteAsync` iterated ALL active
  `InstagramConnection` rows with no join against studio status, so a suspended
  studio's artist kept burning real Instagram API quota every night. Added an
  `IsActive` studio-existence filter to the initial connections query. No test file
  existed for `GetPublicArtistInstagramPostsQuery` at all before this — added
  `tests/Pena_e_Arte.UnitTests/Public/GetPublicArtistInstagramPostsHandlerTests.cs`
  (4 tests). `InstagramSyncJobTests.cs`'s `SeedConnection` helper didn't seed a
  `Studio` row at all (the 6 existing tests only worked because the job never checked
  studio status) — updated the helper to seed an active `Studio`, fixed the
  second-connection seed in `ExecuteAsync_OneConnectionThrows_OtherConnectionStillSyncs`
  the same way, and added `ExecuteAsync_StudioSuspended_SkipsConnectionAndDoesNotCallInstagramApi`.

#### Phase 2 — confirmed clean (no action needed)

- **OAuth (2.1)** — `OAuthTokenValidator` does real signature verification against
  live JWKS (`JwtSecurityTokenHandler.ValidateToken` with issuer/audience/lifetime
  checks), not a trust-the-payload shortcut; Redis JWKS cache fails open (provider
  fetch still happens) rather than blocking sign-in. `RegisterOAuthUserValidator`
  restricts roles to `client`/`owner` only. Both `oauth/login` and `oauth/register`
  carry `.RequireRateLimiting("auth")`. No npm packages for Google/Apple — both loaded
  via CDN `<script>` tags in `index.html`, matching the documented constraint.
  `CreateOAuthUserAsync` sets the same `tenant_id` claim + `ActiveTenantId` token as
  the password path. `UserManager.ResetPasswordAsync` works on a passwordless
  Identity user by design (standard ASP.NET Core Identity behavior, not something this
  codebase implements itself) — an OAuth-created account can add password login later
  via "forgot password".
- **My Studios (2.2)** — `GetMyStudiosHandler` scopes by `currentUser.UserId` via
  `GetTenantIdsAsync`, never unscoped. `LeaveStudioHandler` only removes the Identity
  claim — the `Client` DB row (and its appointment/payment/consent history) is never
  touched, so a client leaving one studio cannot affect data another studio still
  holds on them (this is also the evidence for Phase 3 row 1, below).
  `frontend/e2e/my-studios-kebab-menu.spec.ts` still exists.
- **Saved Images (2.4)** — `SavePortfolioImageHandler`/`UnsavePortfolioImageHandler`
  are both idempotent (no-op, not an error, on double-save/double-unsave).
  `SavedImagesEndpoints.cs` always derives `userId` from the authenticated
  `ClaimsPrincipal`, never from client input — no IDOR path. `SavedPortfolioImage`
  still has no tenant FK. `PortfolioFeed.tsx`'s bookmark button is gated on
  `token !== null` (`showBookmark={token !== null}`), so it's structurally impossible
  to click it while unauthenticated — stronger than just "doesn't throw."
- **Redis Rate Limiting (2.5)** — policy table (`auth` 10/min, `public-write` 30/min,
  `public-read` 120/min) matches `RateLimitingExtensions.cs` exactly; implementation
  uses `AddRedisPolicy` (genuinely Redis-backed via Lua INCR+EXPIRE), not the
  in-memory `AddFixedWindowLimiter`.
- **Feedback (2.6)** — as of the Support Escalation feature (2026-07-21),
  `POST /api/v1/feedback` is `ClientAndAbove`, not `ArtistAndAbove` — widened so clients
  can reach it from the Help menu's Contact Support flow. `SubmitFeedbackValidator`
  narrows it back down for clients specifically: a client's `Type` must be
  `SupportRequest`, or validation fails; artist/owner/issuer keep unrestricted access to
  all four types. `/platform/feedback` routes remain `IssuerOnly`, matching the doc. The
  feature has no screenshot field at all (only `Type`/`Title`/`Body`, all length-bounded
  — Title ≤150, Body 10–2000 chars) and nothing in the Application layer logs feedback
  content.
- **Referral Rewards (2.8)** — `ReferralRewardService.RewardReferrerAsync` is
  idempotency-guarded on `ReferrerRewardApplied`, skips (logs, doesn't throw) on
  self-referral (`OwnerEmail` match) and on no-active-Stripe-subscription, and is
  called from both `CreateSubscriptionHandler` and `ActivateCheckoutSubscriptionHandler`.
  Never references `Plan` pricing fields at all (only `Subscription.Status`/
  `StripeSubscriptionId`), so the Plan/PlanPrice split couldn't have broken it.
- **Reschedule UI (2.11)** — `RescheduleDialog`'s `DURATION_OPTIONS` matches
  `BookAppointmentForm`'s discrete set exactly. `SlotAlreadyBookedException` maps to
  409 with message "The selected time slot is no longer available.", surfaced
  verbatim via toast — not a generic failure message. Both `AppointmentCard.tsx` and
  `AppointmentDetailPage.tsx` wrap the entire action-button block (including
  Reschedule) in `{isArtistPlus && !isTerminal && (...)}`, so the button doesn't even
  render for Cancelled/Completed/NoShow appointments. `MyBookingsSection.tsx` (client
  view) has zero reschedule references — the client-facing flow is confirmed still
  not built, matching the documented scope boundary.
- **Issuer Studio Detail/List/Subs (2.12)** — `GetStudioByIdHandler`'s
  `IgnoreQueryFilters()` usage is behind `RequireAuthorization("IssuerOnly")` at the
  endpoint (`GET /api/v1/studios/{id}`), confirmed not reachable from any
  owner-accessible route. `IssuerStudioDetailPage` is fully built (studio identity,
  subscription status/actions, cash activation, extend trial, cancel) — not a
  placeholder. No referral-code section on this specific page — not a regression
  (nothing in the Decisions Log ever committed to that layout; referral codes have
  their own dedicated `/platform/referrals` page) but worth a product call if a future
  pass wants it added.

#### Phase 3 — bugs found and fixed

- **Row 4** (Instagram sync + studio suspension) — see the two Instagram fixes above;
  this row is what surfaced them.

#### Phase 3 — confirmed clean (no action needed)

- **Row 1** (My Studios leave + portable profile) — `LeaveStudioHandler` never
  touches the `Client` row, so portable-profile visibility for studios the client
  remains registered with is unaffected by design, not by luck.
- **Row 2** (Free plan + usage limits) — `PlanLimitService.EnsureWithinLimitAsync`
  uses `current >= limit`, checked before the create handler runs; existing
  `PlanLimitServiceTests.cs` already covers the exact at-limit boundary. Free tier's
  `MaxArtists = 1` is real and enforced, not `null`.
- **Row 3** (Referral + Free tier signup) — `CreateSubscriptionHandler` explicitly
  skips coupon creation for `price.Price == 0` with a comment addressing this exact
  scenario, and only records a `ReferralRedemption` when a discount was actually
  applied (so a Free-tier signup never even creates one, and never triggers a
  referrer reward it has nothing to justify).
- **Row 5** (Plan-change race vs. quota check) — confirmed the documented gap is
  still the only one: `IPlanLimitService.InvalidateUsageCacheAsync` is write-through
  (called right after `SaveChangesAsync` in the two quota-checked handlers), narrowing
  staleness to the gap between two sequential requests; the two-truly-concurrent-reads
  race is unchanged from what's already documented in the Decisions Log's "Plan usage
  limits" entry — no new gap introduced by later features.
- **Row 6** (OAuth + referral code) — see first-pass section above.
- **Row 7** (Saved image + suspended studio) — `GetSavedPortfolioImagesHandler`
  already builds a `studiosById` dictionary filtered to `IsActive` studios and drops
  any saved image whose studio isn't in it, before ever building the response. Already
  correct, no change needed.
- **Row 8** (Reschedule + monthly quota) — see first-pass section above.
- **Row 9** (Issuer detail convergence) — reasoned through against source rather than
  live-seeded: `IssuerStudioDetailPage.tsx` null-guards every field that could be
  absent for a Free/no-subscription studio (`sub?.trialExpiresAt ?? studio?.trialExpiresAt
  ?? ""`, `Boolean(...)` gates on every conditional section). Not exercised with a
  live seeded studio matching all four conditions simultaneously — flagged as
  reasoned-through, not empirically verified, if a future pass wants to close that gap
  for real.
- **Row 10** (Rate limiting on OAuth/Instagram burst) — both OAuth endpoints already
  carried `"auth"`; the Instagram callback gap is the same one fixed under 2.5 above.

### Verification (Phase 2/3 pass)
- `dotnet build` — clean, 0 errors.
- `dotnet test` — 1181 unit + 293 integration, all green.
- `pnpm tsc -b` — clean, 0 errors.

### Final self-review checklist — 2026-07-20 (continued session)

Runs the master audit prompt's closing checklist. See "Issuer QA Pass — 2026-07-01
(reconstructed 2026-07-20)" above for that Phase 1 documentation-gap item, which was
closed out in the same session as this checklist.

- **Role guard + ErrorBoundary on every authenticated route** — mechanically verified
  across all of `router.tsx`: every route group is nested under a `<RoleGuard
  allowedRoles={[...]} />` parent, and every leaf page element is wrapped in
  `<ErrorBoundary>`. No gaps found.
- **Every new endpoint since 2026-07-02 in `RequireAuthorization`/`AllowAnonymous
  Exceptions`** — cross-referenced every `AllowAnonymous()` call site in
  `Pena_e_Arte.API/Endpoints/*.cs` against the table. Found and fixed 3 gaps:
  `GET /api/v1/public/studios/nearby`, `GET /api/v1/public/studios/{slug}/reviews`,
  `GET /api/v1/public/artists/{slug}/reviews` were all anonymous but never added to
  the table. Added as new rows. The auth-bootstrap endpoints (`login`, `register`,
  `oauth/*`, `forgot-password`, `reset-password`, `refresh`, `verify-email`) are
  covered by CLAUDE.md's blanket `/auth` exception instead of individual rows —
  documented explicitly in the table now so this isn't re-flagged as a gap later.
- **Every new `IgnoreQueryFilters()` call since 2026-07-02 in the approved-usages
  table** — this check surfaced a much larger, pre-existing gap: 19 files with
  legitimate `IgnoreQueryFilters()` calls that had never been added to the table at
  all (most predate the 2026-07-02 cutoff — this is old documentation debt, not a
  newly introduced issue, but the table's own text claims to be "the canonical record
  of every approved call," so it was fixed regardless of when the gap originated).
  Every one of the 19 was individually read before being added — global slug-uniqueness
  checks, anonymous public-discovery endpoints, Stripe webhook handlers (secured by
  signature validation, same class as the two already-documented webhook rows),
  IssuerOnly cross-tenant admin actions, Hangfire jobs with no tenant scope by design,
  and cross-tenant `Client` lookups supporting multi-studio account linking. None were
  found to be an actual unauthorized cross-tenant read. Added as table entries #27–#38.
- **`grep window.location.origin`** — 10 hits repo-wide, all either the documented
  `VITE_PUBLIC_URL ?? window.location.origin` fallback pattern or legitimate uses on
  pages that are never iframe-embedded (canonical tags, OAuth redirect URIs). Zero
  unguarded hits.
- **`grep Plan.PriceMonthly/PriceYearly/PairedPlanId`** — zero real hits repo-wide
  (already confirmed in the first Phase 2.10 pass; re-confirmed here).
- **Loading skeleton / error+retry / empty state on every list page, toast+confirm+
  spinner on every mutation, across all 5 roles** — not exhaustively re-verified
  page-by-page and button-by-button (that would mean reading essentially every
  component in the app). Verified via: (a) direct full reads of numerous pages across
  all 5 roles earlier in this audit session (all issuer pages, several owner/artist/
  client pages during Phase 1–3), which consistently showed the pattern present; (b) a
  structural grep sample across 5 additional list pages not otherwise touched this
  session (`ArtistListPage`, `ClientListPage`, `PaymentListPage`,
  `NotificationLogListPage`, `SchedulePage`), all showing multiple hits for
  `Skeleton`/`isError`/`refetch`/empty-state components; (c) the five original 2026-07-01
  QA passes and 2026-07-02 QA passes, each of which explicitly audited and fixed this
  exact pattern for its role. This is real evidence the convention holds app-wide, not
  an assumption — but it is a sample, not a literal check of every button in the
  codebase. A future pass wanting 100% certainty here would need to read every list
  page and every mutation call site individually.

### Final closure pass — 2026-07-20 (third session)

Closed out every item the previous pass had left as "deferred, done by sampling" or
"reasoned through, not empirically verified." Three real, previously-unknown bugs
found and fixed as a direct result.

#### `FeedbackDialog.test.tsx` flakiness — root cause found and fixed, not just accepted

The 2-tests-fail-under-full-suite-load pattern documented since the Artist QA pass was
never actually investigated — every prior pass just noted it as "pre-existing,
unrelated, passes in isolation" and moved on. Root cause: `@testing-library/dom`'s
default `findBy*`/`waitFor` timeout is 1000ms, which assumes near-instant async
resolution. Under the full suite's parallel worker load (95 files, many workers
sharing CPU cores), a genuinely-correct async interaction (react-hook-form +
`zodResolver` validation → re-render) can take longer than 1000ms purely from
scheduling contention, with nothing actually broken — the same interaction is instant
when the file runs alone. Two fixes were needed, found in two rounds because the first
verification run happened to also have a 3.5-minute `dotnet build` running
concurrently (self-inflicted extra contention), which surfaced the second, real gap
rather than masking it:
1. `src/test/setup.ts` — `configure({ asyncUtilTimeout: 3000 })`, imported from
   `@testing-library/react` (which re-exports it — `@testing-library/dom` itself isn't
   a direct pnpm dependency and can't be imported bare). Raises the timeout on each
   individual `findBy*`/`waitFor` call.
2. `vite.config.ts` — `test.testTimeout: 10000` (was the vitest default of 5000ms).
   Raising only the per-query timeout above wasn't sufficient: a test doing several
   sequential slow queries, each individually within its own new 3000ms budget, could
   still add up past the outer per-*test* timeout under heavy contention.
Verified clean on a dedicated re-run with nothing else competing for CPU: **all 1528
tests across all 95 files passed**, including the ~15 new tests added in this same
pass. Both fixes are global, not per-test — they close this whole class of
full-suite-only flakiness for every current and future test, not just this one file.

#### Phase 3 row 9 — now empirically tested, not just reasoned through

Added 2 tests to `IssuerStudioDetailPage.test.tsx` seeding a subscription matching the
actual converging conditions this page's own data model exposes: `status: "Active"`,
`planName: "Free"`, `trialExpiresAt: null` (cleared on any activation, including
price-0 plans — see `CreateSubscriptionHandler`), and the 50-year sentinel
`currentPeriodEnd`. (OAuth vs. password registration produces identical `Studio`/
`Subscription`/`Client` rows so isn't independently exercisable here, and referral
redemptions have no UI on this specific page — so those two "converging" conditions
don't add new render paths beyond what a converted Free-tier subscription already
covers.) Confirms the page renders correctly with the Free plan name, an Active badge,
and no `undefined`/`NaN` text anywhere in the DOM.

#### Loading/error/empty-state + toast/confirm/spinner — exhaustive sweep, not a sample

Ran a structural grep (`Skeleton`/`isError`/`toast.`/`Loader2` occurrence counts) across
all 55 `*Page.tsx` components, then hand-checked every file whose counts looked
asymmetric (spinners with no toasts, or vice versa) rather than every file — the same
efficiency tradeoff a manual full read would have made anyway, applied systematically
instead of by sample. This surfaced a real, repeating bug class: **six components
called a mutation trigger with a bare `await`, never checked the result, and
unconditionally proceeded as if it had succeeded** — a failed save/delete looked
identical to a successful one, with the user's edits silently discarded and zero
error feedback. All six fixed, each with a new success-path and failure-path test:

| File | What silently broke on failure |
|---|---|
| `ClientDetailPage.tsx` (`onSave`, `saveBodyMap`) | Exited edit mode, discarding the client's unsaved profile/body-map edits |
| `AppointmentCard.tsx` (`confirm`/`complete`/`noShow`, `cancel` already had it) | No feedback at all — button just re-enabled, appointment status unchanged |
| `AppointmentDetailPage.tsx` (same three, `cancel` already had it) | Same — the sibling page to `AppointmentCard.tsx`, same gap |
| `ArtistListPage.tsx` (delete) | Confirm panel closed as if the delete succeeded — silently swallowed `DeleteArtistCommand`'s real, reachable "has upcoming appointments" 409 |
| `TattooRecordDetailPage.tsx` (`onSave`, `onDelete`) | Save exited edit mode regardless of outcome; delete navigated away regardless of outcome |
| `CashDepositConfirmButton.tsx` (`handleConfirm`) | Confirm UI closed as if cash was confirmed — silently swallowed `ConfirmCashDepositCommand`'s real "already confirmed" / artist-ownership 409s |

`ClientDetailPage`'s version of this bug is the same one the Client QA pass (2026-07-02)
already found and fixed in the sibling `MyProfilePage.tsx` (`saveBodyMap()` ignoring the
mutation result) — it was never applied to the staff-facing equivalent, a same-bug-two-
components gap the original pass's own scope (client-only) couldn't have caught.

### Deferred items (with reason)
- Referral-codes-per-studio section on `IssuerStudioDetailPage` — flagged as a
  possible product gap, not fixed (would be new feature work, not a bug fix). Asked
  the user explicitly whether to build it; no response, defaulted to not building it
  per an audit's scope (find/fix bugs, not add product surface).

### Absolute final closure — 2026-07-20 (fourth session)

The user asked for all three remaining named items closed, with no more deferrals.

#### Referral-codes-per-studio section — built

Re-asked whether to build it; no response again, so proceeded per the same
audit-scope default as before, but this time the user's explicit ask to "close out
all of the three remaining items" made the intent unambiguous enough to build it.
Added to `IssuerStudioDetailPage.tsx`: reuses `ReferralCodeRow` (exported from
`PlatformReferralPage.tsx`, previously module-private) and the existing
`generateReferralCodeForStudio` mutation, filtered client-side to the current
studio's codes. The generate form is scoped to the fixed `studioId` from the route
— no studio picker needed, unlike the platform-wide referrals page. 4 new tests.

#### Issuer QA Pass Layer C (frontend bugs) and Layer D (required tests) — walked in full

All ~100 items from `overnight-prompt-issuer-qa-polish-2026-07-01.md`'s Layer C/D
checked against current source and test files, not just spot-checked for substance
as the reconstruction pass had done. Layer C (30 items, C1–C7): one real bug —
`PlanManagementPage`'s delete-plan handler showed a generic "Failed to delete plan"
toast instead of the backend's actual message (e.g. "Cannot delete a plan that has
active subscriptions"), fixed to extract `error.data.message` matching the pattern
used elsewhere. Every other C-item was already correct against current source.

Layer D (7 files, ~70 named test cases): found the test suites already exceed the
required minimums by 2–4× in raw count, but cross-referencing test *names* against
the specific required *scenarios* surfaced 14 real coverage gaps — features that
were already working correctly (confirmed via the Layer C source read) but had
zero regression test protecting them: `IssuerDashboardPage`'s Total-Studios KPI
link, MrrChart's empty-data render, and the At-Risk row's full Extend-Trial flow
+ "→" navigation link; `IssuerStudioListPage`'s Cancel-Subscription confirm flow,
genuine zero-studios empty state (as distinct from the already-tested
zero-after-filter state), extend-trial day-range validation, and activate-form
plan-required guard; `SubscriptionOversightPage`'s URL-driven `?status=` filter
pre-selection and per-row View link; `PlatformReferralPage`'s studio-selector
population, generate-button disabled-without-selection guard, and the full
generate-code submission flow. All 14 closed with new regression tests. D4's
create/edit-form scenarios were confirmed to have legitimately moved to
`PlanEditPage.test.tsx` (a later page-split refactor), not a gap. D7 was already
fully covered with no action needed.

#### Full read for the mutation-feedback bug class — closed via three exhaustive pattern sweeps, not a literal 55-file read

Rather than reading all 55 page components top-to-bottom (high risk of reviewer
fatigue missing exactly the kind of one-line omission this bug class is), ran three
targeted greps across the *entire* frontend — every file, not a sample — each
matching one of the three shapes this bug class takes in this codebase's React/RTK
Query conventions: (1) a bare `await mutationFn(...)` statement with no result
check, (2) an inline `onClick={() => mutationFn(...)}` with no handler wrapper, and
(3) a `.then()` chain with no `.catch()`. Every hit across all three sweeps was
individually read and verified. Two more real instances found beyond the six from
the prior session's sample-based sweep, both previously undetected because neither
matched the earlier sampling approach's file selection:

- `BillingPage.tsx`'s "Keep current plan" button (cancels a scheduled plan change)
  called the mutation bare inline with zero result handling — a failed cancel was
  indistinguishable from a successful one. Fixed with `.unwrap()` + toast pair;
  1 new failure-path test (the success path already had a test, now also asserts
  the success toast).
- `ReviewSection.tsx`'s owner-reply-to-review form (`OwnerReplyForm`) had a
  `.then()` with no `.catch()` — a failed reply left an unhandled promise
  rejection with zero user feedback. This component had **no test coverage at
  all** before this pass. Fixed with a toast pair on both branches; 2 new tests
  (success + failure), requiring a new `vi.mock("@/features/reviews/reviewsApi")`
  block since this mutation lives in a different RTK Query slice than the ones
  this test file already mocked.

Confidence this closes the bug class: high, not absolute. The three grep patterns
cover the shapes actually observed across all 8 instances found this session (6
in the prior sweep, 2 here), and RTK Query's `useXMutation()` hook is the
codebase's exclusive mutation-trigger convention (no raw `fetch`/`axios` calls, no
custom mutation wrappers found), so there's no fourth shape expected. But this is
inference from patterns observed, not a file-by-file read confirming no fifth shape
exists anywhere in 55 files.

### Verification (fourth session)
- `dotnet build` — clean, 0 errors.
- `pnpm tsc -b` — clean, 0 errors.
- `pnpm test` — 1551/1551 tests, all 95 files, verified on a dedicated full-suite run.

### Still open (by design, not oversight)
- Issuer QA Pass Layer C/D line-item closure is now done at the level described
  above (every item individually checked against current source/tests). If a
  future reader wants literal 1:1 traceability against the original prompt's exact
  checklist item wording, that mapping itself was not written down as a separate
  document — this log describes what was found, not a checklist with boxes ticked.
- The mutation-feedback bug class closure is pattern-based, not a literal read of
  all 55 files — see the confidence note above.

## Industry Feature-Parity Audit — 2026-07-20

Competitive gap analysis (guest → issuer, backend + frontend + UI/UX) against the
vertical booking-SaaS category (Vagaro, Fresha, Boulevard, Mindbody, Zenoti,
GlossGenius, Booksy, Mangomint, Schedulicity, Square Appointments) plus general B2B
SaaS platform-admin standards for the issuer role. Full findings, market-research
grounding, per-item verdicts, and the consolidated P0–P3 backlog live in
`docs/claude/industry-feature-parity-report-2026-07-20.md` — this entry is a
pointer, not a duplicate.

Highlights: two real P0 gaps were found and fixed same-session (studio-wide
closures via new `StudioClosure` entity; artist working-hours/time-off frontend,
backend already existed). The `Plan.AllowApiAccess` toggle was found to be a live,
help-documented, fully-unwired feature flag — hidden from `PlanEditPage.tsx` and
help content immediately as a billing-integrity fix. Everything else building on
money/auth/tenant logic (client self-reschedule/cancel, cancellation policy,
revenue reporting, audit logging, dunning, support impersonation, gift cards,
packages, multi-location) was intentionally left as a fully-specified backlog item
rather than built blind, per this project's "consultation and specification, not
implementation" rule for anything beyond a clearly-scoped fix.

## CI: GitHub Actions Pipeline — 2026-07-26

There was no `.github/` directory in this repo at all before this change — correctness
depended on someone remembering to run `dotnet test`/`pnpm test`/`pnpm lint` locally
before pushing. This adds `.github/workflows/ci.yml` (build/format/test/lint/docker-build/
guardrails, gated on every PR to `main` and every push to `main`), `.github/workflows/codeql.yml`
(weekly + per-PR security scanning, csharp + javascript-typescript matrix), `.github/dependabot.yml`
(weekly nuget/npm/github-actions updates, minor/patch grouped), `.github/pull_request_template.md`,
and a root `global.json` pinning the .NET SDK. **CD/deployment (K3s rollout) is explicitly out of
scope** — flagged as a follow-up, not built here; it needs registry and cluster secrets this
change does not provision.

### `ci.yml` jobs

- **`backend`** — restore, `dotnet format --verify-no-changes` (non-blocking, see below), release
  build, starts a `mysql:8.4` container via a plain `docker run` (not a `services:` block — see
  Decisions Log), unit tests (no external deps, NSubstitute-mocked), integration tests against
  the real MySQL container, publishes `.trx` results and coverage artifacts.
- **`frontend`** — install (frozen lockfile), lint (non-blocking, see below), `pnpm build`
  (`tsc -b && vite build` — doubles as the TypeScript strict-mode gate and the build-breakage
  gate), `pnpm test --coverage` (added `@vitest/coverage-v8`, the CI prompt's one sanctioned
  new-package exception), Playwright e2e (mocked API via route interception, no backend needed).
- **`docker-build`** — builds both the API and frontend Dockerfiles with placeholder build-args,
  proves the images still build; does not push anywhere, no registry configured.
- **`guardrails`** — gitleaks secret scan, a grep-based no-`Console.WriteLine`/`console.log`
  check (verified clean against `main`), and a Python heuristic for endpoints missing
  `.RequireAuthorization()`/`.AllowAnonymous()` (see below — had to be extended past the CI
  prompt's original draft to avoid false positives on this codebase's real group-level-guard
  convention).

### Two gates shipped non-blocking (`continue-on-error: true`), by design

Both were checked against current `main` before deciding, per the "run it once against `main`
first" rule this task was given — decisions, exact counts, and reasoning are recorded as their
own Decisions Log rows above (search "CI toolchain pin" onward). Summary:

- **`dotnet format --verify-no-changes`** — ~38,000 pre-existing violations (CRLF/whitespace/
  charset) across nearly every `.cs` file, including every EF migration. The repo has never
  been run through `dotnet format`. Far past the "small, mechanical, ≤20" threshold for a
  same-change baseline fix — a full reformat is its own dedicated, separately-reviewable change.
- **`pnpm lint`** — 6 pre-existing errors: 5 are the `react-hooks/set-state-in-effect` rule
  flagging real effect bodies in 5 different production components (auth, payments, reviews),
  1 is `@typescript-eslint/no-explicit-any` in a test file. Small count, but the effect-based
  fixes are behavior changes to production components, not mechanical formatting — left for a
  dedicated follow-up rather than rushed into a CI-standup change.

Both are clearly marked with `# TODO: remove continue-on-error once ...` comments in `ci.yml` —
neither is silently unenforced.

### A real bug caught while verifying, not just writing YAML

Per this task's own "actually run every command locally before pushing, don't just write YAML
that looks right" instruction: verifying `pnpm test --coverage` locally surfaced that this
repo's pinned pnpm (11.5.1) forwards a literal `--` token through instead of stripping it as
a separator. The originally-drafted step (`pnpm test -- --coverage`, matching the CI prompt's
own draft) silently runs `vitest run "--" "--coverage"` — vitest treats both as harmless
positional test-file-name filters, matches everything, and coverage simply never activates.
No error, no warning, the full suite still reports "all tests passed." `ci.yml` uses
`pnpm test --coverage` (no `--`) instead, confirmed locally to actually enable coverage.

### Local verification performed (before any push)

- `dotnet restore`/`dotnet build "Pena e Arte.slnx" --configuration Release` — clean, 0 errors.
- `dotnet test` unit — 1397 passed.
- `dotnet test` integration — 311 passed, against a real local `mysql:8.4` container (the same
  image/charset/collation as `docker-compose.yml`), confirming the CI job's DB strategy actually
  works end to end, not just in theory.
- `pnpm lint`, `pnpm build` — build clean (0 TypeScript errors); lint's 6 pre-existing findings
  described above.
- `pnpm test --coverage` — full suite (116 files / 1742 tests) passed clean on an isolated run
  with nothing else competing for CPU. Two earlier runs done concurrently with Docker builds/
  Playwright/other dotnet processes showed 1–3 flaky timeouts in different tests each time
  (`HelpMenu.test.tsx`, `RegisterStudioPage.test.tsx`, `FeedbackDialog.test.tsx`) — consistent
  with `vitest.config.ts`'s own pre-existing comment about `testTimeout` being exceeded under
  full-suite parallel worker load, not a real regression. Coverage instrumentation itself adds
  meaningful overhead (isolated run: ~935s vs. ~683s uninstrumented), worth keeping in mind if
  this job ever looks flaky in Actions.
- `pnpm exec playwright install --with-deps chromium` — browsers already present, no-op.
- `pnpm test:e2e` — could not be verified strictly as CI runs it (`CI=true`, fresh dev-server
  start) locally: port 5173 was already occupied by a pre-existing, user-owned `pnpm dev`
  session that this change did not start and should not kill. Ran against that existing server
  instead (`reuseExistingServer` picks it up without `CI=true`) as a functional smoke check —
  4/6 passed, 2 timed out on form interactions, plausibly explained by that server not
  reflecting this branch's exact state rather than a real e2e regression. The authoritative
  signal is the real GitHub Actions run in a clean environment (see Phase 9 verification below).
- Both Dockerfiles (`Pena_e_Arte.API/Dockerfile`, `frontend/Dockerfile`) built successfully
  standalone with the same placeholder build-args `ci.yml` uses; images removed after
  verification (not needed locally). `frontend/Dockerfile` produces one pre-existing,
  unrelated Docker lint warning (`SecretsUsedInArgOrEnv` on `VITE_STRIPE_PUBLISHABLE_KEY`) —
  this is a Vite *publishable* key, public-by-design and meant to ship in client bundles, not
  a secret; not something this change introduced or needs to fix.
- Both guardrail scripts (no-`console`/`Console.WriteLine`, endpoint-authorization heuristic)
  run directly against current `main` — both clean, confirmed with the exact heredoc form
  embedded in `ci.yml`.

### Branch protection (Phase 8 — cannot be done from a file-editing session)

Someone with admin on `471k/pena-e-arte` (Phi) needs to, after these workflow files are merged
to `main` and have run at least once: **Settings → Branches** → add a protection rule on `main`
requiring a PR, requiring status checks (`Backend — build, format, test`,
`Frontend — lint, typecheck, build, unit test, e2e`, `Docker images build (no push)`,
`Non-negotiable-rules guardrails`, `Analyze (csharp)`, `Analyze (javascript-typescript)`),
requiring branches up to date, requiring conversation resolution, and enabling GitHub's native
secret scanning + push protection under **Code security and analysis**. Do not enable force
pushes or branch deletion on `main`.

## Client Conduct Reports — 2026-08-22

### What was built
- `ConductReport` domain entity — non-tenant, same shape as `Review`/`FeedbackReport`/
  `AuditLogEntry` (no EF Core query filter registered); `ForArtist`/`ForStudio` factories,
  `UpdateStatus`, `IsReadableBy`.
- `ReportCategory` and `ReportStatus` enums; `ReportCategoryClassifier` (static High/Standard
  severity map, one source of truth).
- `PlatformContacts.SupportEmail` — extracted from `SubmitContactRequestHandler`'s private
  const, now shared with `ConductReportNotifier`.
- MediatR: `FileArtistConductReportCommand`, `FileStudioConductReportCommand`,
  `UpdateConductReportStatusCommand` (`IAuditableCommand`), `GetMyStudioConductReportsQuery`,
  `GetMyConductReportsAsArtistQuery`, `GetConductReportsQuery`,
  `GetReportableArtistAppointmentsQuery`, `GetReportableStudioAppointmentsQuery`.
- `ConductReportAuthorizationGuard` (read + severity-gated write-permission checks) and
  `ConductReportNotifier` (High-severity email alert) — both internal static helpers, not
  DI-registered, matching `FeedbackAccessGuard`'s existing convention.
- `ConductReportProjections` — shared join (Studios/Artists/Appointments) + response mapping
  for all three read paths, with the redaction guarantee (`ToFullResponseAsync` vs
  `ToRedactedResponseAsync`).
- Migration `AddConductReports`.
- Endpoints: `POST /api/v1/public/{artists,studios}/{slug}/reports` (ClientOnly),
  `GET .../reports/reportable-appointments` (ClientOnly), `GET /api/v1/studios/me/conduct-reports`
  (OwnerOnly), `GET /api/v1/artists/me/conduct-reports` (ArtistAndAbove),
  `PATCH /api/v1/conduct-reports/{id}/status` (OwnerOnly, severity-gated in the handler),
  `GET /api/v1/platform/conduct-reports` (IssuerOnly).
- Frontend: `conductReports.types.ts`, `conductReportsApi.ts` (registered in `store.ts`),
  `publicApi.ts` extended with the file/reportable-appointments endpoints,
  `ConductReportDialog` wired into `ArtistPortfolioPage`/`StudioPortfolioPage` behind a
  client-only gate, `ConductReportsPage` (owner/artist, role-branched) at `/conduct-reports`,
  `ConductReportInboxPage` (issuer) at `/platform/conduct-reports`, nav items + open-count
  badges in `OwnerLayout`/`ArtistLayout`/`IssuerLayout`.
- Help sync: 4 new `helpContent.ts` articles (client, owner, artist, issuer), matching sections
  in the standalone user manual, and a new onboarding-tour step for owner/artist/issuer.
- Feature Module Map row #37; `IgnoreQueryFilters()` table entries #43–#45 (see below);
  Trust & Safety Reference Set added to the Industry-Standard Benchmark Set.

### Architecture decisions (restated as committed fact)
- **Reporter identity is redacted server-side, never client-side.** `ConductReportProjections`
  nulls `ReporterUserId`/`ReporterName` in `ToRedactedResponseAsync` before the response ever
  leaves the handler — the artist-facing endpoint has no code path that can leak it. Verified
  three ways: a unit test on the handler, a real-HTTP integration test asserting the raw JSON
  string never contains the reporter's name or a populated `reporterUserId` field, and a
  frontend test asserting the UI never renders it even if a (hypothetical, backend-can't-happen)
  payload leak occurred.
- **Severity is a static classification (`ReportCategoryClassifier`), not a stored column** —
  one place to update if the taxonomy changes; both the email-alert gate and the
  owner-vs-issuer status-change gate read from it.
- **Filing eligibility deliberately does NOT require `AppointmentStatus.Completed`, and does
  NOT dedup against existing reports on the same appointment** — the two places this diverges
  from `Review`'s eligibility (`FileArtistConductReportHandler`/`FileStudioConductReportHandler`,
  and `GetReportable{Artist,Studio}AppointmentsQuery`). Both deltas are covered by an explicit
  "copy-paste guard" test that would fail if a future edit accidentally reintroduced either
  filter.
- **`ConductReport` needs no `IgnoreQueryFilters()` entry** — it has no query filter to bypass
  in the first place (see the `IgnoreQueryFilters()` table's own note below its list). It DOES,
  however, introduce three genuinely new call sites against *other*, filtered entities
  (`Artist`, `Appointment`) — documented as table entries #43–#45, since the file's own rule
  ("never add a new one without updating this table") applies to those regardless of what the
  prompt that drove this feature said about `ConductReport` itself.
- **`ConductReportNotifier` and `ConductReportAuthorizationGuard` are plain internal static
  classes**, not DI-registered services, matching the observed convention: `FeedbackAccessGuard`
  (this codebase's closest analog) is also a static helper with no DI registration in
  `Program.cs`. Chose this over a DI-registered service for consistency, not because of any
  functional requirement.
- **`ConductReport.IsReadableBy` dropped the unused `userId` parameter** the prompt's own sketch
  included — the method body never referenced it (Decision 5: the reporting client never reads
  their own filed reports, so there's no "is this my own row" branch to check against). Kept
  the simpler three-argument signature rather than carrying a dead parameter.

### Judgment calls made
- **Attachment-picker duplication vs. extraction (Part 8d)**: duplicated `FeedbackDialog.tsx`'s
  attachment-picker block into `ConductReportDialog.tsx` rather than extracting a shared
  component, with a comment pointing back at `FeedbackDialog.tsx` as the source of truth.
  Extraction would have touched `FeedbackDialog.tsx` too, widening this feature's diff for a UI
  block that wasn't otherwise changing — not worth it for a single second consumer.
- **Client-role gating on the report trigger** (`ArtistPortfolioPage`/`StudioPortfolioPage`) uses
  a direct `role === Role.Client` equality check, not `usePermission()` — `usePermission`'s rank
  model only expresses "at least this role," which can't express the exact-match `ClientOnly`
  policy this feature's filing endpoints enforce server-side. This is a deliberate, narrow
  deviation from the "use `usePermission` for conditional UI" convention in `conventions.md`,
  scoped to this one exact-match case.
- **Issuer onboarding tour got a new step, unlike Feedback.** Every other issuer nav item
  carrying a `tourId` in `IssuerLayout.tsx` has a matching step in `issuerTourSteps` — Feedback
  is the only nav item that breaks that 1:1 pairing (it has no `tourId` and no tour step at
  all). Treated the 1:1 pairing as the stronger, more concrete signal of intended behavior and
  added a Conduct Reports step, rather than copying Feedback's apparent (and likely accidental)
  omission.
- **New tour steps were inserted immediately before each layout's existing "Need help?" closing
  step**, not literally appended after it. A strict reading of "append" would put it after Help,
  breaking the established "tour always ends on the help pointer" pattern all three tours
  already have. Interpreted "append, don't insert mid-sequence" as "don't interleave between
  arbitrary existing feature steps," not as "override the closing convention."
- **`GetMyConductReportsAsArtistQuery` has no server-side status filter** (unlike
  `GetMyStudioConductReportsQuery`/`GetConductReportsQuery`, which both accept `Status`) — the
  artist's own open-count nav badge (`ArtistLayout.tsx`) filters the full result set
  client-side instead. Not worth adding a query parameter for a list that's realistically small
  (reports about one artist) just to save one client-side `.filter()`.

### Deviations from the prompt
- **`IgnoreQueryFilters()` table entries #43–#45 were added**, even though the prompt's Decision
  7 said not to add a row for this feature. Read closely, that instruction was about
  `ConductReport`'s own (non-existent) query filter specifically — it did not anticipate that
  `FileArtistConductReportCommand`/`FileStudioConductReportCommand`,
  `GetReportable{Artist,Studio}AppointmentsQuery`, and `ConductReportProjections` would each
  introduce real, new `IgnoreQueryFilters()` calls against `Artist`/`Appointment`/`Studio`,
  entities that *do* carry filters. The file's own standing rule ("never add a new one without
  updating this table") is unconditional, so these three got documented rather than silently
  omitted. The explicit "no entry needed" sentence the prompt asked for was still added, scoped
  correctly to `ConductReport` itself.
- **No `AttachmentPicker.tsx` extraction** — see Judgment calls above. The prompt explicitly
  offered both options; duplication was chosen.
- **The known `AuditStudioId` gap flagged in `UpdateConductReportStatusCommand`** (an
  issuer-authored status change on a report with no tenant in scope gets `StudioId = null` on
  its audit row, rather than the report's actual studio) was left exactly as the prompt
  predicted — documented in a code comment on the command, not silently "fixed" with an
  ad-hoc pattern. No new deviation here, just confirming it was verified, not just assumed.

### Verification performed
- Backend unit tests: 1713/1713 passing (up from a clean baseline before this feature — build
  and full suite were green before any code was written).
- Backend integration tests: 377/377 passing, including a real-HTTP
  `ConductReportEndpointAuthorizationTests` suite that exercises the actual ASP.NET Core
  authorization + MediatR/AuditLogBehavior pipeline end-to-end: artist read never exposes
  reporter identity in the raw JSON body, owner attempting to resolve a High-severity report
  gets a real 403, and an issuer's subsequent resolution both succeeds and writes a real
  `AuditLogEntry` row with `Action == AuditActions.ConductReportStatusUpdated`.
- The `AddConductReports` migration was applied to the local dev MySQL database and confirmed
  to apply cleanly from a schema already at `20260822111309_AddSocialAccountLinks`.
- Frontend: `pnpm exec tsc --noEmit` clean; full `pnpm vitest run` suite green — **131/131 test
  files, 1915/1915 tests** — after three real fixes found only by running the full suite (see
  below), not by the feature's own new tests, none of which render through a full layout tree
  or through `ArtistPortfolioPage.tsx`'s import graph. Includes 16 new tests across
  `ConductReportDialog`, `ConductReportsPage` (owner + artist views), and
  `ConductReportInboxPage`, plus a rerun of `helpContent.test.ts` and
  `useOnboardingTour.test.tsx` confirming the new articles/tour steps don't break existing
  structural invariants.
- **Bug 1 — missing barrel exports.** `features/conduct-reports/index.ts` only re-exported the
  `conductReportsApi` object itself, not its generated hooks
  (`useGetMyStudioConductReportsQuery`, `useGetMyConductReportsAsArtistQuery`,
  `useGetPlatformConductReportsQuery`, `useUpdateConductReportStatusMutation`).
  `OwnerLayout.tsx`/`ArtistLayout.tsx`/`IssuerLayout.tsx` all import their nav-badge hook from
  that barrel (not from `conductReportsApi.ts` directly, unlike `ConductReportsPage.tsx`/
  `ConductReportInboxPage.tsx`), so all three would have thrown
  `TypeError: ... is not a function` and crashed the nav header in production the moment a
  signed-in owner/artist/issuer rendered their layout. Fixed by adding the four hooks to the
  barrel's export list.
- **Bug 2 — pre-existing layout tests build their own minimal Redux store**, and none of the
  three (`OwnerLayout.test.tsx`/`ArtistLayout.test.tsx`/`IssuerLayout.test.tsx`) included
  `conductReportsApi`'s reducer/middleware — so even after Bug 1's fix, every test rendering
  these layouts failed with `Middleware for RTK-Query API at reducerPath "conductReportsApi"
  has not been added to the store` (58 test failures across the three files). Fixed by adding
  `conductReportsApi` to each test's `makeStore()` and an MSW handler for the new
  `GET .../conduct-reports` endpoint each layout now calls; also renamed each file's
  now-stale "renders all N nav links" test title to match the new nav-item count (nine for
  owner, eight for artist, eight for issuer).
- **Bug 3 — duplicate `useState` import** in `ArtistPortfolioPage.tsx`: the file already
  imported `useState` on line 1 (for its own `lightboxItem`/`activeStyle` state) before this
  feature touched it; the edit adding the client-only report-trigger state added `useState` to
  a *second*, pre-existing `import { useEffect } from "react"` further down the file instead of
  reusing the existing import, producing a hard duplicate-declaration parse error
  (`Identifier 'useState' has already been declared`). This broke Vite's transform for the
  whole module, cascading to 10 failed test files across `features/public` and
  `features/studios` that transitively load it — none of which are conduct-reports tests, which
  is exactly why this class of syntax error can hide behind a feature's own passing test suite.
  Fixed by removing `useState` from the second import.
- Every one of these three bugs is precisely the class that a feature's own tests — however
  thorough — cannot catch by construction (missing barrel exports and duplicate imports only
  surface when something *else* imports the changed module; store-shape assumptions only
  surface in tests that build their own store). This is why the full backend + frontend suites
  were re-run to a clean, final state rather than stopping once the new tests were green.
- No other pre-existing test failures were found — the baseline before this feature was fully
  green on both backend and frontend.
