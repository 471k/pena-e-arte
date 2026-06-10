# Self-Promotion Feature Prompts — Claude Code

> Paste one prompt at a time into Claude Code.
> Complete each feature (including tests) before moving to the next.
> Run `dotnet test && pnpm test` after every feature before proceeding.

---

## How to Use These Prompts

Each prompt is self-contained. It tells Claude Code exactly what to read,
what to build, where to put things, and what "done" means.
Do not modify the prompts — they are pre-loaded with project context.

**Branch per feature:**
```
git checkout -b feat/sp-01-platform-branding
git checkout -b feat/sp-02-portfolio-pages
... and so on
```

---

## Feature 01 — Platform Branding Flag

```
Read docs/claude/architecture.md and docs/claude/backend.md and docs/claude/frontend.md before writing any code.

Branch: feat/sp-01-platform-branding

### What to build
Add a "Powered by Pena e Artë" branding system that appears on all client-facing
touchpoints for studios on the free tier, and can be removed by studios on paid plans.

### Backend changes

1. Domain — `Studio` entity:
   Add `bool ShowPlatformBranding { get; private set; } = true;`
   Add method `void UpdateBranding(bool show) => ShowPlatformBranding = show;`

2. Domain — `Plan` entity:
   Add `bool AllowBrandingRemoval { get; private set; } = false;`

3. Application — new command:
   `UpdateStudioBrandingCommand(Guid StudioId, bool ShowBranding) : IRequest<Unit>`
   Handler: fetch Studio, fetch Studio's active Subscription → Plan.
   If `!Plan.AllowBrandingRemoval && !showBranding` → throw `DomainException("Your current plan does not allow removing platform branding.")`.
   Otherwise call `studio.UpdateBranding(showBranding)` and save.
   Policy: OwnerOnly.

4. Application — update `GetStudioQuery` response:
   Add `ShowPlatformBranding` to `StudioResponse` DTO.

5. Infrastructure — migration:
   Add `ShowPlatformBranding` (bool, default true) to `Studios` table.
   Add `AllowBrandingRemoval` (bool, default false) to `Plans` table.

6. API — new endpoint in `StudioEndpoints.cs`:
   `PATCH /api/v1/studios/{id}/branding` → `UpdateStudioBrandingCommand`
   RequireAuthorization("OwnerOnly").

### Frontend changes

7. Features — owner settings page:
   In `features/studios/components/`, add `BrandingSettingsCard.tsx`.
   Shows a toggle "Show 'Powered by Pena e Artë' on booking widget".
   If `!plan.allowBrandingRemoval`: toggle is disabled with tooltip "Upgrade to remove branding".
   Uses `useUpdateStudioBrandingMutation` from `studiosApi.ts`.

8. Booking widget footer:
   In `features/booking/components/BookingWidget.tsx`, read `studio.showPlatformBranding`
   from the RTK Query studio response. If true, render:
   `<footer class="..."><a href="https://penaearte.com" target="_blank">Powered by Pena e Artë</a></footer>`
   Tailwind only. No inline styles.

### Email template
9. In `Infrastructure/Services/MailKit/Templates/AppointmentConfirmation.html`:
   Add a conditional footer block at the bottom (controlled by a template variable
   `{{show_branding}}`). The handler that sends confirmation emails must pass
   `showBranding: studio.ShowPlatformBranding`.

### Tests
- Unit: `UpdateStudioBrandingHandler` — test that a free-plan studio cannot disable branding,
  and that a paid-plan studio can.
- Integration: PATCH endpoint returns 403 for artist role, 200 for owner.

### Constraints
- Business logic (plan check) lives in the handler, NOT in the endpoint.
- No new dependencies.
- Follow the full "Adding a New Feature" checklist in architecture.md.
- Never log PII. Include tenant_id and user_id in all log lines.
```

---

## Feature 02 — Public Portfolio Pages

```
Read docs/claude/architecture.md and docs/claude/backend.md and docs/claude/frontend.md before writing any code.

Branch: feat/sp-02-portfolio-pages

### What to build
SEO-indexed public pages for each studio and each artist.
URLs: /s/{slug} and /artist/{slug}.
No authentication required. No tenant filter.

### Backend changes

1. Domain — `Studio` entity:
   Add `string Slug { get; private set; }` (max 60 chars).
   Add static helper `GenerateSlug(string name)`: lowercase, replace spaces and
   special chars with hyphens, strip consecutive hyphens, trim.

2. Domain — `Artist` entity:
   Add `string Slug { get; private set; }`.
   Same slug generation rules.

3. Infrastructure — unique DB indexes:
   `Studios.Slug` — unique index.
   `Artists.Slug` — unique index.
   Handle collision in `CreateStudioHandler` and `CreateArtistHandler`:
   if slug exists, append "-2", "-3" etc. until unique.

4. Application — new queries (NO tenant filter on these):
   `GetPublicStudioQuery(string Slug) : IRequest<PublicStudioResponse?>`
   `GetPublicArtistQuery(string Slug) : IRequest<PublicArtistResponse?>`
   Handlers: use `_db.Studios.IgnoreQueryFilters().Where(s => s.Slug == slug && s.IsPublished)`.
   This is a documented AllowAnonymous exception — see architecture.md AllowAnonymous Exceptions table.

5. Response DTOs (Contracts/Responses/Public/):
   `PublicStudioResponse`: studioId, name, slug, city, description, coverImageUrl,
     artists (list of PublicArtistSummary), showBookingCta: true.
   `PublicArtistResponse`: artistId, name, slug, bio, portfolioImages (list of urls),
     studioName, studioSlug, showBookingCta: true.
   NEVER include: email, phone, tenantId, userId, payment data.

6. API — new endpoint group `PublicEndpoints.cs`:
   `GET /api/v1/public/studios/{slug}` → `GetPublicStudioQuery` — `.AllowAnonymous()`
   `GET /api/v1/public/artists/{slug}` → `GetPublicArtistQuery` — `.AllowAnonymous()`
   Add both to the AllowAnonymous Exceptions table in architecture.md (already documented).

7. Application — `UpdateStudioSlugCommand(Guid StudioId, string NewSlug) : IRequest<Unit>`
   Policy: OwnerOnly.
   Validate: slug is URL-safe (regex), not already taken, max 60 chars.
   One change allowed (enforce with `Studio.SlugLockedAt DateTime?` — set on first change).

### Frontend changes

8. New feature folder: `features/public/`
   - `publicApi.ts` — RTK Query with baseQuery pointing to `/api/v1/public/`, NO auth headers.
   - `components/StudioPortfolioPage.tsx` — public, no role guard.
   - `components/ArtistPortfolioPage.tsx` — public, no role guard.

9. Routes in `app/router.tsx`:
   `/s/:slug` → `<StudioPortfolioPage />` — no RoleGuard, no layout wrapper.
   `/artist/:slug` → `<ArtistPortfolioPage />` — no RoleGuard, no layout wrapper.

10. SEO meta tags in each page component (React Helmet or equivalent):
    `<title>{studio.name} — Book a Tattoo on Pena e Artë</title>`
    Open Graph: og:title, og:description, og:image (coverImageUrl).
    Canonical: `https://penaearte.com/s/{slug}`.

11. "Book here" CTA button at the top of both pages.
    Links to the studio's booking flow (if client is logged in) or to /login?redirect=...

### Tests
- Unit: `GenerateSlug` static method — spaces, special chars, collision handling.
- Integration: GET /api/v1/public/studios/{slug} returns 200 without auth token,
  returns 404 for unpublished studio.
- Integration: GET /api/v1/public/artists/{slug} returns 200 without auth token.

### Constraints
- IgnoreQueryFilters() usage here is the second documented exception — add a comment
  `// Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions`
  above every IgnoreQueryFilters() call in these handlers.
- Never expose tenantId, userId, email, or phone in PublicStudioResponse/PublicArtistResponse.
- No new npm/NuGet packages.
- Follow the full "Adding a New Feature" checklist in architecture.md.
```

---

## Feature 03 — Booking Confirmation Branding

```
Read docs/claude/architecture.md and docs/claude/backend.md before writing any code.
Feature 01 (platform branding flag) must be merged before starting this.

Branch: feat/sp-03-confirmation-branding

### What to build
Inject the "Booked via Pena e Artë" badge into appointment confirmation emails
and into the PDF consent/confirmation documents, controlled by Studio.ShowPlatformBranding.

### Backend changes

1. Email template — `Infrastructure/Services/MailKit/Templates/AppointmentConfirmation.html`:
   At the very bottom of the body, inside a `<table>` footer row, add:
   `{{#if show_branding}}<tr><td style="text-align:center;padding:16px 0;font-size:12px;color:#999;">Booked via <a href="https://penaearte.com" style="color:#999;">Pena e Artë</a></td></tr>{{/if}}`
   (Use the template engine already in use — do not introduce a new one.)

2. Application — `SendAppointmentConfirmationCommand` handler (or wherever the
   confirmation email is dispatched):
   Fetch `studio.ShowPlatformBranding` and pass it as `show_branding` in the
   template variables.

3. PDF generation (if using a PDF service in Infrastructure/Services/):
   In the PDF footer rendering method, check `studio.ShowPlatformBranding`.
   If true, append a 8px footer line: "Generated via Pena e Artë · penaearte.com"
   in light gray, right-aligned.
   This is the ONLY change to the PDF service — do not touch other templates.

### Tests
- Unit: handler passes `show_branding: true` when studio flag is true,
  and `show_branding: false` when flag is false.
- Integration: confirmation email sent after appointment creation contains
  branding footer when studio flag is true.

### Constraints
- No new template engine. Use what is already in the Infrastructure/Services/MailKit/ folder.
- No changes to the booking flow itself — this is email/PDF output only.
- No new dependencies.
```

---

## Feature 04 — Referral Code System

```
Read docs/claude/architecture.md and docs/claude/backend.md and docs/claude/frontend.md before writing any code.

Branch: feat/sp-04-referral-codes

### What to build
Studio owners can generate a referral code. New studios that sign up with the code
get a discount month. The referring studio gets tracked.

### Domain changes

1. New entity `ReferralCode` (Domain/Entities/):
   ReferralCodeId  Guid
   StudioId        Guid   (referring studio)
   Code            string (8 chars, uppercase, unique)
   CreatedAt       DateTime
   ExpiresAt       DateTime?
   IsActive        bool   default: true

2. New entity `ReferralRedemption` (Domain/Entities/):
   ReferralRedemptionId  Guid
   ReferralCodeId        Guid
   NewStudioId           Guid
   RedeemedAt            DateTime
   DiscountApplied       bool

3. No domain logic beyond basic validity check on ReferralCode — keep domain clean.

### Application changes

4. `GenerateReferralCodeCommand(Guid StudioId) : IRequest<ReferralCodeResponse>`
   Handler: generate 8-char code (uppercase alpha, check uniqueness in DB),
   create `ReferralCode` entity, save. Policy: OwnerOnly.

5. `GetReferralCodeQuery(Guid StudioId) : IRequest<ReferralCodeResponse?>`
   Returns active code for this studio, or null. Policy: OwnerOnly.

6. `GetReferralStatsQuery(Guid StudioId) : IRequest<ReferralStatsResponse>`
   Returns: code, redemption count, discountsApplied count. Policy: OwnerOnly.

7. Update `CreateStudioCommand`:
   Accept optional `string? ReferralCode` in the request.
   If provided: validate it maps to an active, non-expired ReferralCode.
   Store the ReferralCodeId in a temporary field on Studio (`PendingReferralCodeId Guid?`)
   — do not apply the discount yet, only on first subscription creation.

8. Update `CreateSubscriptionCommand` handler:
   If `studio.PendingReferralCodeId != null`:
   a. Create a Stripe Billing coupon: 1 month free (100% off for 1 month).
   b. Apply the coupon to the new Stripe Subscription at creation.
   c. Create a `ReferralRedemption` record.
   d. Clear `Studio.PendingReferralCodeId`.
   e. Set `ReferralCode.IsActive = false` if it is single-use
      (check against a new `Plan`-level config or a simple boolean on ReferralCode).

### API changes

9. New `ReferralEndpoints.cs`:
   `POST /api/v1/studios/{id}/referral-codes`   → GenerateReferralCodeCommand  — OwnerOnly
   `GET  /api/v1/studios/{id}/referral-codes`   → GetReferralCodeQuery          — OwnerOnly
   `GET  /api/v1/studios/{id}/referral-stats`   → GetReferralStatsQuery         — OwnerOnly

### Frontend changes

10. In `features/studios/components/`, add `ReferralCodeCard.tsx`:
    Shows current code (or "Generate code" button if none).
    Shows redemption stats below.
    Copy-to-clipboard button for the referral signup URL:
    `https://penaearte.com/register?ref={code}`

11. Registration page: if `?ref=CODE` query param present, store in Redux auth slice
    (`pendingReferralCode: string | null`) so it survives the multi-step registration flow.
    Pass it in the `CreateStudioRequest` body at the final step.

### Tests
- Unit: code generation produces 8-char uppercase string, retries on collision.
- Unit: handler blocks discount if code is expired or inactive.
- Integration: full referral flow — create code, register new studio with code,
  create subscription, verify ReferralRedemption is created and discount flag is true.

### Constraints
- Stripe coupon creation goes in Infrastructure/Services/Stripe/ — not in the handler.
  Handler calls an `IStripeDiscountService` interface; Infrastructure implements it.
- No new NuGet/npm packages.
- Never log the referral code value as PII — log the ReferralCodeId only.
- Follow the full "Adding a New Feature" checklist in architecture.md.
```

---

## Feature 05 — Client Portable Profiles

```
Read docs/claude/architecture.md and docs/claude/backend.md before writing any code.
Feature 02 (portfolio pages) must be merged — it introduces the Slug pattern needed
for cross-studio navigation.

Branch: feat/sp-05-portable-profiles

### What to build
Clients opt in to making their tattoo history (body map + records) visible to
other studios on the platform. This is cross-tenant by design and requires an
explicit IgnoreQueryFilters() usage — the third documented exception.

### Domain changes

1. `ClientProfile` entity gains:
   `bool AllowCrossTenantRead { get; private set; } = false;`
   `DateTime? CrossTenantOptInAt { get; private set; }`
   `void OptInToCrossTenant()` — sets both fields.
   `void OptOutOfCrossTenant()` — sets `AllowCrossTenantRead = false`, clears date.

2. New domain interface (Domain/Interfaces/):
   `IPortableProfileService`
   ```
   Task<PortableClientProfile?> FindByUserIdAsync(Guid userId, CancellationToken ct);
   Task<IReadOnlyList<PortableTattooRecord>> GetHistoryAsync(Guid userId, CancellationToken ct);
   ```
   `PortableClientProfile` and `PortableTattooRecord` are separate response types —
   they never include email, phone, address, payment data, or consent form data.
   They include: display name, body map (anonymized locations only), tattoo records
   (image, style, date, artist first name only).

### Infrastructure changes

3. Implement `PortableProfileService : IPortableProfileService` in Infrastructure/Services/:
   Uses `_db.ClientProfiles.IgnoreQueryFilters()` filtered by `UserId` and
   `AllowCrossTenantRead == true`.
   Add comment above every IgnoreQueryFilters() call:
   `// Approved exception #3: portable profiles — see architecture.md Self-Promotion Module Architecture`

### Application changes

4. `UpdatePortableProfileOptInCommand(bool OptIn) : IRequest<Unit>`
   Resolves `userId` from `ICurrentUser`. Finds the client's profile (WITH normal
   query filter — this is their own tenant). Sets opt-in/opt-out.
   Policy: ClientAndAbove (clients managing their own profile).

5. `GetPortableProfileQuery(Guid ClientUserId) : IRequest<PortableClientProfile?>`
   Uses `IPortableProfileService`. Returns null if not opted in.
   Policy: ArtistAndAbove (artists viewing a client who presents their portable profile).

### API changes

6. New endpoints in `ClientEndpoints.cs`:
   `PATCH /api/v1/clients/me/portable-profile` → UpdatePortableProfileOptInCommand — ClientAndAbove
   `GET   /api/v1/clients/{userId}/portable-profile` → GetPortableProfileQuery    — ArtistAndAbove

### Frontend changes

7. In `features/clients/components/`, add `PortableProfileToggle.tsx`:
   Switch component in client's own profile settings.
   Explains what opting in shares (body map locations, tattoo history, no contact info).
   Shows a warning: "Any artist on Pena e Artë will be able to view your tattoo history."
   Uses `useUpdatePortableProfileOptInMutation`.

8. In `features/clients/components/ClientDetailPanel.tsx` (artist-facing):
   If `portableProfile` data exists (from `useGetPortableProfileQuery`), show a
   "Tattoo History (from other studios)" section with the records.

### Tests
- Unit: OptInToCrossTenant sets AllowCrossTenantRead = true and CrossTenantOptInAt.
- Unit: PortableProfileService returns null when AllowCrossTenantRead is false.
- Integration: artist can view portable profile of opted-in client.
- Integration: artist cannot view portable profile of opted-out client (returns null, not 404).

### Constraints
- IPortableProfileService MUST only be injectable in handlers where the acting user
  is either the client themselves (ClientAndAbove) or IssuerOnly.
  Never inject it in owner-only or artist-only command handlers.
- PortableTattooRecord must never contain: userId, email, phone, tenantId,
  payment amounts, consent form data.
- The IgnoreQueryFilters() call must have the documented comment above it.
- No new dependencies.
- Follow the full "Adding a New Feature" checklist in architecture.md.
```

---

## Feature 06 — Design Share Token

```
Read docs/claude/architecture.md and docs/claude/backend.md and docs/claude/frontend.md before writing any code.
Feature 03 (Design Approval Workflow, feature #03 in Feature Module Map) must exist —
this feature links to DesignRevision entities.

Branch: feat/sp-06-design-share-token

### What to build
Artists and owners can generate a shareable link for an approved design revision.
The link works without authentication, expires in 30 days, and is revocable.

### Domain changes

1. New entity `DesignShareToken` (Domain/Entities/):
   DesignShareTokenId   Guid
   Token                string  (Guid.NewGuid().ToString("N") — 32 chars, opaque)
   DesignRevisionId     Guid
   StudioId             Guid    (denormalized for fast lookup without filter bypass)
   CreatedByUserId      Guid
   ExpiresAt            DateTime (default: UtcNow + 30 days)
   IsRevoked            bool    (default: false)
   ViewCount            int     (default: 0)

### Application changes

2. `CreateDesignShareTokenCommand(Guid DesignRevisionId) : IRequest<DesignShareTokenResponse>`
   Handler: verify DesignRevision belongs to tenant (normal query filter).
   Generate token, create entity, save.
   Policy: ArtistAndAbove.

3. `RevokeDesignShareTokenCommand(Guid DesignShareTokenId) : IRequest<Unit>`
   Handler: set IsRevoked = true. Policy: ArtistAndAbove.

4. `GetSharedDesignQuery(string Token) : IRequest<SharedDesignResponse?>`
   Handler: find DesignShareToken by Token (IgnoreQueryFilters — public lookup).
   If expired or revoked → return null (endpoint returns 404).
   Increment ViewCount. Return signed R2 URL (short TTL, e.g. 15 minutes) + design title.
   This is a documented AllowAnonymous exception — see architecture.md.
   
   Comment above IgnoreQueryFilters() call:
   `// Approved exception: design share token — public lookup, validated by token + expiry`

   `SharedDesignResponse`: imageUrl (signed R2), title, studioName (display only), expiresAt.
   Never include: studioId, artistId, userId, tenantId, clientId.

5. Response DTO `DesignShareTokenResponse`: token, shareUrl, expiresAt.
   shareUrl = `https://penaearte.com/share/{token}`.

### API changes

6. In `DesignEndpoints.cs`:
   `POST /api/v1/designs/revisions/{revisionId}/share-token`  → CreateDesignShareTokenCommand — ArtistAndAbove
   `DELETE /api/v1/designs/share-tokens/{id}`                 → RevokeDesignShareTokenCommand — ArtistAndAbove

7. New `PublicDesignEndpoints.cs`:
   `GET /api/v1/public/designs/share/{token}` → GetSharedDesignQuery — AllowAnonymous()

### Frontend changes

8. In `features/designs/components/`, add `ShareDesignButton.tsx`:
   Button on the design approval panel (ArtistAndAbove view).
   On click: calls `useCreateDesignShareTokenMutation`, shows a modal with
   copy-to-clipboard link and expiry date. Also shows a "Revoke" button.

9. New public page: `features/public/components/SharedDesignPage.tsx`
   Route: `/share/:token` — no RoleGuard, no layout wrapper.
   Calls `useGetSharedDesignQuery(token)`.
   Shows the design image full-screen with studio name and "Book your own tattoo" CTA
   linking to the studio's portfolio page (`/s/{studioSlug}`).
   If token is invalid/expired → show "This link has expired" message.

### Tests
- Unit: GetSharedDesignQuery returns null for expired token, null for revoked token.
- Unit: ViewCount increments on each valid view.
- Integration: GET public endpoint returns 200 without auth for valid token.
- Integration: GET public endpoint returns 404 for expired token.

### Constraints
- SharedDesignResponse must never contain studioId, artistId, tenantId, or any user identifier.
- Signed R2 URL TTL must be short (≤ 15 min) — the share token itself is the long-lived credential.
- No new dependencies.
- Follow the full "Adding a New Feature" checklist in architecture.md.
```

---

## Feature 07 — Studio QR Code Generator

```
Read docs/claude/architecture.md and docs/claude/backend.md and docs/claude/frontend.md before writing any code.
Feature 02 (portfolio pages) must be merged — the QR code points to /s/{slug}.

Branch: feat/sp-07-qr-code

### Dependency approval
This feature requires ONE new NuGet package: QRCoder.
This is pre-approved in the Decisions Log in architecture.md.
Add it to Pena_e_Arte.Infrastructure.csproj only. Do NOT add it anywhere else.
Run `dotnet add Pena_e_Arte.Infrastructure/Pena_e_Arte.Infrastructure.csproj package QRCoder`

### What to build
Studios can download a QR code that points to their public portfolio page.
The QR code is generated on-demand — not stored.

### Infrastructure changes

1. New service `IQrCodeService` (Domain/Interfaces/):
   `byte[] GeneratePng(string url, int pixelSize = 20);`
   `string GenerateSvg(string url);`

2. Implement `QrCodeService : IQrCodeService` in `Infrastructure/Services/`:
   Use `QRCoder.QRCodeGenerator` to generate the code.
   PNG: use `PngByteQRCode` renderer, size parameter = pixelSize.
   SVG: use `SvgQRCode` renderer.
   Register in DI as scoped.

### Application changes (none needed — this is a direct infrastructure call)

3. No MediatR handler needed for this — the endpoint calls IQrCodeService directly
   via a thin wrapper query. Create:
   `GetStudioQrCodeQuery(Guid StudioId, string Format) : IRequest<QrCodeResponse>`
   `QrCodeResponse(byte[] Data, string ContentType)`
   Handler: fetch `studio.Slug`, call `_qrCode.GeneratePng(url)` or `GenerateSvg(url)`,
   return bytes + content type.
   Policy: ArtistAndAbove (staff can download, but also public — see endpoint note below).

### API changes

4. In `StudioEndpoints.cs`, add:
   `GET /api/v1/studios/{id}/qr?format=png`
   Returns `Results.File(data, contentType, $"{studio.Slug}-qr.png")`.
   This endpoint is AllowAnonymous (documented in architecture.md AllowAnonymous Exceptions).
   The QR code contains only the public portfolio URL — no sensitive data.

### Frontend changes

5. In `features/studios/components/`, add `QrCodeSection.tsx`:
   Shown in Owner settings page, in a "Marketing" section.
   Displays the QR code image (fetched via RTK Query as blob URL).
   "Download PNG" button triggers the endpoint download.
   Caption: "Scan to book — add this to your window, business cards, or social bio."

6. `studiosApi.ts` — add:
   `getStudioQrCode: builder.query<string, Guid>` returning a blob URL.
   Use `responseHandler: async (response) => URL.createObjectURL(await response.blob())`.

### Tests
- Unit: QrCodeService.GeneratePng returns non-empty byte array for a valid URL.
- Integration: GET /api/v1/studios/{id}/qr returns 200 with Content-Type: image/png without auth.

### Constraints
- QRCoder added to Infrastructure project only.
- QR code content is ONLY the public portfolio URL — never embed any tenant or user data.
- No new npm packages.
- Follow the full "Adding a New Feature" checklist in architecture.md.
```

---

## Feature 08 — Industry Analytics Reports

```
Read docs/claude/architecture.md and docs/claude/backend.md and docs/claude/frontend.md before writing any code.
This feature is issuer-only. No tenant-scoped data may appear in report output.

Branch: feat/sp-08-industry-reports

### What to build
A monthly Hangfire job aggregates anonymized platform-wide metrics and publishes a JSON
report to Cloudflare R2. Issuer dashboard shows a list of reports with download links.

### Infrastructure — Hangfire job

1. New job class `IndustryReportJob` in `Infrastructure/Jobs/`:
   Runs on the first day of each month (register in Program.cs:
   `RecurringJob.AddOrUpdate<IndustryReportJob>("industry-report", j => j.RunAsync(), Cron.Monthly())`).

   The job MUST:
   a. Call `_db.Database.SetCommandTimeout(300)` — these are long aggregation queries.
   b. Use `IgnoreQueryFilters()` for all queries — fourth documented exception.
      Add comment: `// Approved exception #4: industry report aggregate — issuer-level, no PII`
   c. Enforce minimum cohort size: if a metric has fewer than 10 contributing studios,
      replace it with null in the JSON output.

   Metrics to collect (all aggregate, no identifiers):
   - `total_active_studios`: count of studios with active subscription
   - `avg_appointments_per_studio_per_month`: average over last 90 days
   - `peak_booking_hour`: hour (0–23) with most appointment starts, platform-wide
   - `top_session_durations_minutes`: [30, 60, 90, 120, 180] — count of each bucket
   - `trial_to_paid_conversion_rate`: studios that converted in last 90 days / total trials started
   - `avg_retention_months`: avg months between first and last appointment per studio

   Output shape:
   ```json
   {
     "generated_at": "ISO8601",
     "period": "2026-06",
     "metrics": { ... },
     "cohort_size": 42,
     "note": "Metrics suppressed where cohort < 10."
   }
   ```

2. Upload to R2 at key: `reports/industry/{year}-{month:D2}.json`.
   Use the existing `IStorageService` (or `IR2StorageService`) already in Infrastructure/Services/.

### Application changes

3. `GetIndustryReportsQuery() : IRequest<IReadOnlyList<IndustryReportSummaryResponse>>`
   Handler: list objects in R2 under `reports/industry/` prefix.
   For each: return `{ period, generatedAt, downloadUrl (signed, 24h TTL) }`.
   Policy: IssuerOnly.

### API changes

4. In a new `PlatformEndpoints.cs` (or existing if it exists):
   `GET /api/v1/platform/reports/industry` → GetIndustryReportsQuery — IssuerOnly.

### Frontend changes

5. In `features/platform/components/` (or `features/issuer/`), add `IndustryReportsPanel.tsx`:
   Shown in the issuer dashboard only (IssuerLayout).
   Lists available reports by month, each with a "Download JSON" link (signed URL).
   If no reports yet: "Reports are generated on the 1st of each month."

### Tests
- Unit: job skips a metric and outputs null when cohort < 10.
- Unit: job does not include any studio name, studioId, userId, or email in output.
- Integration: GET /api/v1/platform/reports/industry returns 403 for OwnerOnly role.
- Integration: GET /api/v1/platform/reports/industry returns 200 for issuer role.

### Constraints
- IgnoreQueryFilters() in this job is documented exception #4 — the comment is mandatory.
- Report JSON must never contain: studioId, tenantId, userId, studio name, artist name,
  email, phone, or any other identifying value.
- Aggregate only. Minimum cohort of 10 for every metric — use null if below threshold.
- No new dependencies.
- Follow the full "Adding a New Feature" checklist in architecture.md.
```
