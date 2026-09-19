# Overnight Prompt — Studio Street Address at Signup + Live Address-to-Map Geocoding

> Feed this file directly to Claude Code (main Engineering project, full repo write
> access) as the task prompt. It is self-contained: exact files, exact current code,
> exact target code, exact tests, exact docs to sync. Read the whole file before
> writing anything — later sections depend on decisions made in §2 and §3.

**Date logged:** 2026-08-20
**Requested by:** Phi
**Origin:** Product request — "add address input fields for the owner when he is
signing up his studio, and the map to automatically find the location in real time
based on the address he already added to the input field." This is the Engineering
Consultation project's spec for that request; it does not implement anything itself
(see this project's own scope rules) — everything below is written for the session
that will.

---

## 1. Goal

Today, `RegisterStudioPage` (`/register`, Step 1) collects a studio's `City`,
`Latitude`, and `Longitude` — but never a street address — and the only way to set
a location is clicking/dragging a pin on a Leaflet map (`LocationPicker`), which
then reverse-geocodes the pin to fill in `City`. There is no way to type a street
address and have anything happen.

Add:

1. A required **street address** field (plus optional address line 2 and postal
   code) to studio registration, persisted on `Studio`.
2. **Live forward-geocoding**: as the owner types their street address, the map
   pin, `Latitude`/`Longitude`, and `City` update automatically (debounced, not
   on every keystroke) — mirroring the reverse-geocode-on-pin-drop UX that already
   exists, just in the opposite direction.
3. The same fields, editable afterward on `/studios/me` (`StudioProfilePage`),
   matching how `City`/`Latitude`/`Longitude` are already both register-time and
   edit-time fields — leaving address out of the edit flow would be a direct
   inconsistency with the entity's existing City/Lat/Lng precedent and would trap
   pre-existing studios with no way to ever add one.
4. Surfacing the full address on the studio's **public** profile page
   (`/s/{slug}`, `StudioPortfolioPage`), which is where CLAUDE.md rule #6's
   benchmark set (Vagaro/Fresha/Boulevard/Mindbody/Zenoti/GlossGenius) actually
   shows a studio's full street address with a "Get directions" link — city alone,
   which is all that's shown today, is below that bar.

Applicable non-negotiable rules from `CLAUDE.md` for this change: #2 (RBAC — no
new endpoints, existing `studios` endpoints keep their existing policies), #3
(never log PII — a business street address is not personal PII, see §7.4), #6
(match industry standard — see §3.1), #7 (keep Help in sync — see §12), plus the
general "no unclear `var`", "no `any`", "test Application-layer logic" rules.

---

## 2. Decisions already made — implement as specified, do not re-litigate

1. **Three new nullable `string` columns on `Studio`**: `AddressLine1`,
   `AddressLine2`, `PostalCode`. `AddressLine1` is the only one that drives
   geocoding and the only one required for *new* registrations (enforced in
   `RegisterStudioValidator`, not at the DB level — same "required at the app
   layer, nullable at the DB layer for backfill" shape `Nipt` already
   established). `AddressLine2`/`PostalCode` are always optional, forever —
   same shape as `PhoneNumber`/`InstagramHandle`.

2. **No new npm package, no new API key, no backend geocoding proxy.**
   Geocoding continues to be done directly from the browser against Nominatim
   (OpenStreetMap), exactly like the three call sites that already do this:
   `location-picker.tsx`'s `reverseGeocode`, and `DiscoverPage.tsx`'s
   `reverseGeocode`/`handleLocationSearch`. This is a deliberate consistency
   choice, not an endorsement — see §3.2 for the gap this leaves and why it's
   being accepted anyway tonight.

3. **Forward-geocoding lives in a new shared hook**
   (`frontend/src/shared/hooks/useAddressGeocode.ts`), not inside
   `location-picker.tsx`. `LocationPicker` already supports being driven
   externally — its existing `useEffect` (lines ~108–115 of the current file)
   syncs `pin`/`label`/`flyTarget` whenever its controlled `value` prop changes,
   which is exactly the mechanism `RegisterStudioPage`'s existing map-click flow
   already uses. The new hook reuses that same path: it resolves an address to
   `{lat, lng, city}` and the page calls the exact same three `setValue(...)`
   calls the `LocationPicker onChange` handler already makes. **Do not add an
   `address` prop to `LocationPickerValue` or otherwise change
   `location-picker.tsx`'s public interface** — it needs zero changes for this
   feature, and every existing test that mocks it
   (`vi.mock("@/shared/components/ui/location-picker", ...)`) keeps working
   unmodified.

4. **Debounced live geocoding, not a submit-triggered or button-triggered
   search.** The request explicitly says "in real time" — implement it as
   debounced-while-typing (default 700ms of no keystrokes, minimum 5 characters
   before firing), not `DiscoverPage.tsx`'s button/Enter-triggered
   `handleLocationSearch` pattern. The map pin stays fully click/drag-correctable
   afterward exactly as it is today — geocoding only ever *proposes* a location,
   never locks the pin.

5. **The pin is the source of truth, the address text is not re-derived from
   it.** This is one-directional (address → map), matching exactly what was
   asked for. Do **not** add logic that rewrites the `AddressLine1` text field
   from a manually-dropped pin's reverse-geocode result — that's a different,
   unrequested feature (and reverse-geocode results are frequently a messy
   `display_name`, not a clean editable street address). If the owner
   manually repositions the pin after a geocode, the typed address text is left
   exactly as they wrote it; only `Latitude`/`Longitude`/`City` change.

6. **Full street address is shown on the public studio page
   (`StudioPortfolioPage`), with a "Get directions" link built from the address
   text (a Google Maps search URL), not from coordinates.**
   `PublicStudioResponse` does not currently carry `Latitude`/`Longitude` at
   all, and adding them is unrelated scope (an embedded public mini-map is a
   bigger, separate feature — noted as a backlog idea in §3.1, not built
   tonight). A `https://www.google.com/maps/search/?api=1&query=<address>`
   link needs no coordinates.

---

## 3. Decisions you must make explicit note of / flag, not silently assume

3.1. **Industry-standard benchmark check (CLAUDE.md rule #6):** unlike NIPT
     (flagged in `docs/claude/overnight-prompt-nipt-studio-registration-2026-07-22.md`
     as *not* benchmark-driven), collecting a full street address at
     onboarding and displaying it on the public studio page **is** the
     standard pattern across Vagaro/Fresha/Boulevard/Mindbody/Zenoti/GlossGenius
     — all six show a full address with a map/directions affordance on the
     studio's public page, not just a city name. Say so explicitly in the PR
     description, since this is the opposite framing from the NIPT precedent
     and both should be traceable to real reasoning, not copy-pasted caution.
     A genuine gap this pass does **not** close, flagged for backlog rather
     than silently dropped: none of those competitors make the owner type a
     raw address string into a plain `<input>` with a top-1-guess geocode —
     they all use a provider-backed address **autocomplete** (Google Places,
     Mapbox, etc.) that suggests real candidate addresses as you type. §10
     below specs an optional stretch (a candidate dropdown from Nominatim's
     own top-5 results) that narrows this gap without adding a paid API
     dependency; it is not a full substitute and should be logged as backlog
     if not implemented tonight.

3.2. **Pre-existing architectural gap this pass inherits, not introduces —
     flag it, don't silently ship around it.** This codebase already calls
     Nominatim directly from the browser in three places (see §2.2). Nominatim's
     own usage policy asks for a maximum of ~1 request/second for this kind of
     unauthenticated use and recommends self-hosting or a paid provider for
     production traffic at any real scale; a browser `fetch` cannot set a
     custom `User-Agent` (only the browser's own `Referer` is sent). Tonight's
     change adds a **fourth** call site doing the same thing, for direct
     consistency with the other three — that is the correct call for a single
     feature PR, but it makes the aggregate exposure worse, and someone should
     eventually decide whether to move all four call sites behind a small
     backend geocoding proxy (cheap: one `GET /api/v1/geocode?q=...` endpoint
     that adds a real `User-Agent`, applies server-side rate limiting per the
     existing `public-read` policy, and can add a Redis cache) or a paid
     provider. **Do not build that proxy tonight** — it's a cross-cutting
     change touching three existing call sites plus this new one, clearly
     outside this task's scope — but add this exact gap as a new row in
     `docs/claude/architecture.md`'s Feature Module Map "known gaps" style
     entries (see §13) so it doesn't quietly stay invisible.

3.3. **Should `AddressLine1` become required (hard-blocking) on
     `UpdateStudioRequest` for studios that already have one, the same way
     NIPT becomes read-only once set?** No — default for this task: keep it
     fully editable forever, with no read-only lock. A NIPT is a legal tax ID
     with fraud/uniqueness stakes; a street address is not — studios
     legitimately relocate, and locking it would be actively harmful. If this
     default is wrong, that is a product call for Phi to make explicitly
     later, not something to infer here.

3.4. **Whether to also add `Latitude`/`Longitude` to `PublicStudioResponse` for
     an embedded public map on the studio page.** Deliberately **out of scope**
     tonight per §2.6 — flag as a backlog idea in the same architecture.md
     entry from §3.2, do not build it.

---

## 4. Scope boundary — do not touch

- `location-picker.tsx`'s public interface (`LocationPickerValue`, props) — see
  §2.3. Internal implementation is untouched entirely; this file should have a
  **zero-line diff**.
- `StudioMapPage.tsx`, `GetStudioMapQuery`/`StudioMapItemResponse` — the
  `/studios/map` public map popup shows `City`, matching what real competitor
  map pins show in a list-density view; full address only belongs on the
  detail page. No change needed or wanted here (see §8's DTO audit).
- `NearbyStudioResponse`/`GetNearbyStudiosQuery` (Discover page's nearby-studio
  list rows) — same reasoning, list rows show city + distance, not full
  address, matching real competitor list UX. No change.
- `MyStudioResponse`/`/auth/my-studios` (the multi-studio switcher list) —
  lightweight by design (Id/Name/Slug/City/CoverImageUrl/IsStudioActive), no
  reason to carry address. No change.
- NIPT-anything. This task is unrelated to NIPT; don't touch
  `RegisterStudioPage`'s NIPT field, its read-only-after-set behavior on
  `StudioProfilePage`, or `DuplicateNiptException`.
- Anything Stripe/payments/subscription-related.
- `frontend/src/features/auth/**` (Login, ClientRegister — this task only
  touches the owner studio-registration flow, not client signup).

---

## 5. Domain layer

**File:** `Pena_e_Arte.Domain/Entities/Studio.cs`

Add three properties after `Nipt` (do not reorder or touch anything else):

```csharp
public class Studio
{
    public Guid     Id               { get; init; } = Guid.NewGuid();
    public string   Name             { get; set; } = string.Empty;
    public string   Slug             { get; set; } = string.Empty;
    public string   City             { get; set; } = string.Empty;
    public string   OwnerEmail       { get; set; } = string.Empty;
    public string?  Description      { get; set; }
    public string?  CoverImageUrl    { get; set; }
    public string?  PhoneNumber      { get; set; }
    public string?  InstagramHandle  { get; set; }
    public string?  Nipt             { get; set; }
    public string?  AddressLine1     { get; set; }   // ADD — street address; nullable for backfill, required for new registrations (app-layer, RegisterStudioValidator)
    public string?  AddressLine2     { get; set; }   // ADD — suite/unit/floor, always optional
    public string?  PostalCode       { get; set; }   // ADD — always optional
    public double   Latitude         { get; set; }
    public double   Longitude        { get; set; }
    public bool     IsActive         { get; set; } = true;
    public bool     ShowPlatformBranding { get; private set; } = true;
    public DateTime? SlugLockedAt    { get; set; }

    public void UpdateBranding(bool show) => ShowPlatformBranding = show;
    public DateTime TrialExpiresAt   { get; set; }
    public string?  StripeCustomerId { get; set; }
    public DateTime CreatedAt        { get; init; } = DateTime.UtcNow;
    public long     StorageUsageBytes { get; set; }
    public Guid?    PendingReferralCodeId { get; set; }
    public Subscription? Subscription { get; set; }
}
```

---

## 6. Infrastructure layer

### 6.1 EF configuration

**File:** `Pena_e_Arte.Infrastructure/Persistence/Configurations/StudioConfiguration.cs`

Add, alongside the existing `Property` calls (after the `Nipt` line):

```csharp
builder.Property(s => s.AddressLine1).HasMaxLength(300);
builder.Property(s => s.AddressLine2).HasMaxLength(150);
builder.Property(s => s.PostalCode).HasMaxLength(20);
```

No new index — unlike `Nipt`, these columns are never queried by (no
uniqueness rule, no lookup-by-address anywhere in this pass).

### 6.2 Migration

Generate with:

```bash
dotnet ef migrations add AddStudioAddress \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

Expected `Up`/`Down`, matching `20260722115948_AddStudioNipt.cs`'s exact shape
(verify the generated output matches — same MySQL charset annotation, same
`#nullable disable` header, same namespace):

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pena_e_Arte.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudioAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "studios",
                type: "varchar(300)",
                maxLength: 300,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "studios",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "studios",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AddressLine1", table: "studios");
            migrationBuilder.DropColumn(name: "AddressLine2", table: "studios");
            migrationBuilder.DropColumn(name: "PostalCode", table: "studios");
        }
    }
}
```

Apply locally with:

```bash
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```

Zero-downtime note: additive nullable columns, no index, no backfill needed —
safe to deploy in one step per `database.md`'s migration-order guidance.

---

## 7. Application layer

### 7.1 Registration command/handler/validator

**File:** `Pena_e_Arte.Application/Studios/Commands/RegisterStudioCommand.cs`

In `RegisterStudioHandler.Handle`, extend the `Studio` object construction:

```csharp
Studio studio = new()
{
    Name = req.Name,
    Slug = slug,
    City = req.City,
    OwnerEmail = req.OwnerEmail,
    Nipt = normalizedNipt,
    AddressLine1 = req.AddressLine1.Trim(),
    AddressLine2 = string.IsNullOrWhiteSpace(req.AddressLine2) ? null : req.AddressLine2.Trim(),
    PostalCode   = string.IsNullOrWhiteSpace(req.PostalCode)   ? null : req.PostalCode.Trim(),
    Latitude = req.Latitude,
    Longitude = req.Longitude,
    IsActive = true,
    TrialExpiresAt = trialEnd,
    PendingReferralCodeId = pendingReferralCodeId,
};
```

And extend the final `return new StudioResponse(...)` call — it already mixes
positional and named arguments (`PhoneNumber: null, InstagramHandle: null,
Nipt: studio.Nipt`); append the same way so param order in the record (§8)
can't silently break this call site:

```csharp
return new StudioResponse(
    studio.Id, studio.Name, studio.Slug, studio.City,
    studio.Latitude, studio.Longitude,
    studio.ShowPlatformBranding,
    AllowBrandingRemoval: false,
    studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive,
    studio.SlugLockedAt,
    PhoneNumber: null, InstagramHandle: null, Nipt: studio.Nipt,
    AddressLine1: studio.AddressLine1, AddressLine2: studio.AddressLine2, PostalCode: studio.PostalCode);
```

**File:** `Pena_e_Arte.Application/Studios/Validators/RegisterStudioValidator.cs`

Add after the existing `Latitude`/`Longitude` rules:

```csharp
RuleFor(x => x.Request.AddressLine1).NotEmpty().MaximumLength(300);
RuleFor(x => x.Request.AddressLine2).MaximumLength(150);
RuleFor(x => x.Request.PostalCode).MaximumLength(20);
```

### 7.2 Update command/handler/validator (post-registration edit)

**File:** `Pena_e_Arte.Application/Studios/Commands/UpdateMyStudioCommand.cs`

In `UpdateMyStudioHandler.Handle`, add alongside the existing
`PhoneNumber`/`InstagramHandle` null-if-blank assignments (same pattern —
blank submitted means "clear the field", not "leave unchanged"; this is a
deliberate difference from how `Nipt` behaves on this same handler, per §3.3):

```csharp
studio.AddressLine1 = string.IsNullOrWhiteSpace(command.Request.AddressLine1) ? null : command.Request.AddressLine1.Trim();
studio.AddressLine2 = string.IsNullOrWhiteSpace(command.Request.AddressLine2) ? null : command.Request.AddressLine2.Trim();
studio.PostalCode   = string.IsNullOrWhiteSpace(command.Request.PostalCode)   ? null : command.Request.PostalCode.Trim();
```

And extend the handler's `return new StudioResponse(...)` call the same way
as §7.1:

```csharp
return new StudioResponse(
    studio.Id, studio.Name, studio.Slug, studio.City,
    studio.Latitude, studio.Longitude,
    studio.ShowPlatformBranding,
    AllowBrandingRemoval: false,
    studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive,
    studio.SlugLockedAt, studio.PhoneNumber, studio.InstagramHandle, studio.Nipt,
    AddressLine1: studio.AddressLine1, AddressLine2: studio.AddressLine2, PostalCode: studio.PostalCode);
```

**File:** `Pena_e_Arte.Application/Studios/Validators/UpdateMyStudioValidator.cs`

Add (all optional on update — no `NotEmpty()`, per §3.3):

```csharp
RuleFor(x => x.Request.AddressLine1).MaximumLength(300);
RuleFor(x => x.Request.AddressLine2).MaximumLength(150);
RuleFor(x => x.Request.PostalCode).MaximumLength(20);
```

### 7.3 `GetMyStudioQuery` — same `StudioResponse` shape

**File:** `Pena_e_Arte.Application/Studios/Queries/GetMyStudioQuery.cs`

`GetMyStudioHandler.Handle`'s `return new StudioResponse(...)` call must also
append the three new fields, or `/studios/me` will always report `null`
address even after it was saved:

```csharp
return new StudioResponse(
    studio.Id, studio.Name, studio.Slug, studio.City,
    studio.Latitude, studio.Longitude,
    studio.ShowPlatformBranding,
    allowBrandingRemoval,
    studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive,
    studio.SlugLockedAt, studio.PhoneNumber, studio.InstagramHandle, studio.Nipt,
    AddressLine1: studio.AddressLine1, AddressLine2: studio.AddressLine2, PostalCode: studio.PostalCode);
```

This is the easiest of the four `StudioResponse` construction sites to miss —
it's the one that isn't in either diff shown above. Grep
`Pena_e_Arte.Application` for `new StudioResponse(` before finishing this
section to confirm all call sites were found (expected: exactly these three —
`RegisterStudioHandler`, `UpdateMyStudioHandler`, `GetMyStudioHandler`).

### 7.4 `GetPublicStudioQuery` — public DTO

**File:** `Pena_e_Arte.Application/Public/Queries/GetPublicStudioQuery.cs`

Extend the final `return new PublicStudioResponse(...)` call:

```csharp
return new PublicStudioResponse(
    studio.Id,
    studio.Name,
    studio.Slug,
    studio.City,
    studio.Description,
    studio.CoverImageUrl,
    studio.PhoneNumber,
    studio.InstagramHandle,
    studioReviewStats is { Count: > 0 } ? Math.Round(studioReviewStats.Avg, 1) : null,
    studioReviewStats?.Count ?? 0,
    galleryImages,
    artistSummaries,
    ShowBookingCta: true,
    AddressLine1: studio.AddressLine1, AddressLine2: studio.AddressLine2, PostalCode: studio.PostalCode);
```

### 7.5 Logging

Per CLAUDE.md rule #3: a business street address is not personal PII in the
sense that rule is guarding against (names/emails/phones/card data of
*people*) — it's public-facing business info, same category as `City` already
logged in places like `RegisterStudioHandler`'s existing
`logger.LogInformation("Studio registered {@StudioId} ...")`. Do not add the
raw address to any log line regardless — there's no logging need for it here,
so this is moot as long as no new `logger.LogInformation`/`LogWarning` call
is added that includes `AddressLine1`/`AddressLine2`/`PostalCode`. Grep the
diff for `Address` inside any `logger.Log*` call before finishing as a
sanity check.

---

## 8. Contracts — and the public-DTO audit (do this before anything else ships)

Every DTO in `Pena_e_Arte.Contracts` deriving from `Studio` needs an explicit
yes/no on carrying the new fields — do not skip DTOs "because they probably
don't need it."

**`Pena_e_Arte.Contracts/Requests/RegisterStudioRequest.cs`** — add
`AddressLine1` as **required**. Because C# positional records require every
non-default parameter before any default one, it must go immediately after
`Nipt` (the last currently-required param), not next to `City`/`Latitude`
where it would read more naturally:

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record RegisterStudioRequest(
    string Name,
    string Slug,
    string City,
    double Latitude,
    double Longitude,
    string OwnerEmail,
    string Nipt,
    string AddressLine1,
    string? AddressLine2 = null,
    string? PostalCode = null,
    string? ReferralCode = null);
```

Grep the whole repo for `new RegisterStudioRequest(` before finishing this
file — if anything constructs it positionally (expected: nothing outside
JSON model binding, which binds by property name, not position; but check
test fixtures too), update the call site's argument order.

**`Pena_e_Arte.Contracts/Requests/UpdateStudioRequest.cs`** — add all three,
optional (all have defaults, so placement is free; put them after `Nipt` for
readability):

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record UpdateStudioRequest(
    string Name,
    string City,
    double Latitude,
    double Longitude,
    string? PhoneNumber = null,
    string? InstagramHandle = null,
    string? Nipt = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? PostalCode = null);
```

**`Pena_e_Arte.Contracts/Responses/StudioResponse.cs`** — add all three,
optional, after `Nipt`:

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record StudioResponse(
    Guid Id,
    string Name,
    string Slug,
    string City,
    double Latitude,
    double Longitude,
    bool ShowPlatformBranding,
    bool AllowBrandingRemoval,
    DateTime TrialExpiresAt,
    DateTime CreatedAt,
    bool IsActive,
    DateTime? SlugLockedAt,
    string? PhoneNumber = null,
    string? InstagramHandle = null,
    string? Nipt = null,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? PostalCode = null);
```

**`Pena_e_Arte.Contracts/Responses/Public/PublicStudioResponse.cs`** — add
all three. This is the one DTO in this audit that's a deliberate change in
kind, not just plumbing: it's the first time a studio's exact street address
becomes visible to anonymous internet users. That is the intended,
benchmark-matching behavior per §3.1 — a tattoo studio's whole business model
depends on being findable, unlike (say) a client's home address. Still worth
being explicit about in the PR description rather than silently widening a
public DTO:

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicStudioResponse(
    Guid StudioId,
    string Name,
    string Slug,
    string City,
    string? Description,
    string? CoverImageUrl,
    string? PhoneNumber,
    string? InstagramHandle,
    double? AverageRating,
    int ReviewCount,
    IReadOnlyList<string> GalleryImages,
    IReadOnlyList<PublicArtistSummary> Artists,
    bool ShowBookingCta,
    string? AddressLine1 = null,
    string? AddressLine2 = null,
    string? PostalCode = null);
```

**Explicitly unchanged, with reasoning (per §4):**

| DTO | Change | Why not |
|---|---|---|
| `StudioMapItemResponse` (`/studios/map` pins) | None | Map popup shows `City` only, matching list-density UX; full address belongs on the detail page it links to |
| `NearbyStudioResponse` (Discover nearby-studio list) | None | Same reasoning — list rows show city + distance, not full address, matching every benchmark competitor's list view |
| `MyStudioResponse` (`/auth/my-studios` switcher) | None | Lightweight studio-switcher list by design; no display need for address |

---

## 9. Frontend

### 9.1 New shared hook — `frontend/src/shared/hooks/useAddressGeocode.ts` (new file)

```typescript
import { useEffect, useRef, useState } from "react";

export interface GeocodedLocation {
  lat: number;
  lng: number;
  city: string;
  country: string;
}

export type GeocodeStatus = "idle" | "loading" | "success" | "error";

interface UseAddressGeocodeOptions {
  minLength?: number;
  debounceMs?: number;
  enabled?: boolean;
}

/**
 * Debounced forward-geocoding via Nominatim (OpenStreetMap) — mirrors the
 * existing pattern in location-picker.tsx's reverseGeocode and
 * DiscoverPage.tsx's handleLocationSearch/reverseGeocode (same host, same
 * unauthenticated usage, no new dependency, no new API key). See
 * docs/claude/architecture.md's "known gaps" entry for why all four
 * client-side call sites should eventually move behind a backend proxy —
 * out of scope for this hook.
 *
 * Calls `onResolved` at most once per settled (debounced) address value.
 * Drops any response that resolves after the input has changed again, so a
 * fast typer never has an earlier, now-stale geocode overwrite what a later
 * keystroke already triggered.
 */
export function useAddressGeocode(
  address: string,
  onResolved: (loc: GeocodedLocation) => void,
  { minLength = 5, debounceMs = 700, enabled = true }: UseAddressGeocodeOptions = {},
) {
  const [status, setStatus] = useState<GeocodeStatus>("idle");
  const latestAddressRef = useRef(address);
  latestAddressRef.current = address;

  useEffect(() => {
    if (!enabled) return;
    const trimmed = address.trim();
    if (trimmed.length < minLength) {
      setStatus("idle");
      return;
    }

    const controller = new AbortController();
    const timer = setTimeout(async () => {
      setStatus("loading");
      try {
        const res = await fetch(
          `https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=1&q=${encodeURIComponent(trimmed)}`,
          { headers: { "Accept-Language": "en" }, signal: controller.signal },
        );
        const results = (await res.json()) as Array<{
          lat: string;
          lon: string;
          address?: {
            city?: string;
            town?: string;
            village?: string;
            municipality?: string;
            country?: string;
          };
        }>;

        // Stale-response guard: a newer debounced call is already queued/
        // running if the address changed while this one was in flight.
        if (latestAddressRef.current.trim() !== trimmed) return;

        if (results.length === 0) {
          setStatus("error");
          return;
        }

        const [first] = results;
        const a = first.address ?? {};
        onResolved({
          lat: parseFloat(first.lat),
          lng: parseFloat(first.lon),
          city: a.city ?? a.town ?? a.village ?? a.municipality ?? "",
          country: a.country ?? "",
        });
        setStatus("success");
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setStatus("error");
      }
    }, debounceMs);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [address, enabled, minLength, debounceMs]);

  return { status };
}
```

### 9.2 `RegisterStudioPage.tsx`

Schema additions (after `nipt`, before `email`):

```typescript
addressLine1: z.string().min(1, "Street address is required").max(300),
addressLine2: z.string().max(150).optional(),
postalCode:   z.string().max(20).optional(),
```

`STEP_1_FIELDS` — add `"addressLine1"` (address line 2 / postal code stay
ungated, same treatment as every other optional field on this page):

```typescript
const STEP_1_FIELDS = ["name", "slug", "city", "nipt", "addressLine1", "latitude", "longitude"] as const;
```

`defaultValues` — add `addressLine1: "", addressLine2: "", postalCode: ""`.

Import and wire the hook:

```typescript
import { useAddressGeocode } from "@/shared/hooks/useAddressGeocode";

// inside the component, alongside the other watch() calls:
const addressLine1Value = watch("addressLine1");

const { status: geocodeStatus } = useAddressGeocode(addressLine1Value, ({ lat, lng, city }) => {
  setValue("latitude", lat, { shouldValidate: true });
  setValue("longitude", lng, { shouldValidate: true });
  setValue("city", city, { shouldValidate: true });
});
```

JSX — insert a new field group directly above the existing "Studio location"
`LocationPicker` block in Step 1:

```tsx
<div className="space-y-1.5">
  <Label htmlFor="addressLine1">Street address</Label>
  <Input
    id="addressLine1"
    placeholder="Rruga e Kavajës 10"
    {...register("addressLine1")}
    aria-invalid={!!errors.addressLine1}
    aria-describedby="addressLine1-help"
  />
  <p id="addressLine1-help" className="text-xs text-muted-foreground flex items-center gap-1">
    {geocodeStatus === "loading" && (
      <>
        <Loader2 className="h-3 w-3 animate-spin" />
        Locating on the map…
      </>
    )}
    {geocodeStatus === "error" && "Couldn't find that address automatically — you can also click the map below to set your studio's location."}
    {(geocodeStatus === "idle" || geocodeStatus === "success") &&
      "The map below updates automatically as you type — drag the pin afterward if it's not quite right."}
  </p>
  {errors.addressLine1 && (
    <p className="text-xs text-destructive">{errors.addressLine1.message}</p>
  )}
</div>

<div className="grid grid-cols-2 gap-3">
  <div className="space-y-1.5">
    <Label htmlFor="addressLine2">Address line 2 (optional)</Label>
    <Input id="addressLine2" placeholder="Suite, floor, unit" {...register("addressLine2")} />
  </div>
  <div className="space-y-1.5">
    <Label htmlFor="postalCode">Postal code (optional)</Label>
    <Input id="postalCode" {...register("postalCode")} />
  </div>
</div>
```

`onSubmit` — extend the `registerStudio({...})` payload:

```typescript
const studio = await registerStudio({
  name:         values.name,
  slug:         values.slug,
  city:         values.city,
  nipt:         values.nipt,
  addressLine1: values.addressLine1,
  addressLine2: values.addressLine2 || undefined,
  postalCode:   values.postalCode || undefined,
  latitude:     values.latitude,
  longitude:    values.longitude,
  ownerEmail:   values.email,
  ...(pendingReferralCode ? { referralCode: pendingReferralCode } : {}),
}).unwrap();
```

### 9.3 `studiosApi.ts` — TS interfaces

```typescript
export interface RegisterStudioRequest {
  name:          string;
  slug:          string;
  city:          string;
  latitude:      number;
  longitude:     number;
  ownerEmail:    string;
  nipt:          string;
  addressLine1:  string;
  addressLine2?: string;
  postalCode?:   string;
  referralCode?: string;
}

export interface StudioResponse {
  id:                   string;
  name:                 string;
  slug:                 string;
  city:                 string;
  latitude:             number;
  longitude:            number;
  showPlatformBranding: boolean;
  allowBrandingRemoval: boolean;
  trialExpiresAt:       string;
  createdAt:            string;
  isActive:             boolean;
  slugLockedAt:         string | null;
  phoneNumber:          string | null;
  instagramHandle:      string | null;
  nipt:                 string | null;
  addressLine1:         string | null;
  addressLine2:         string | null;
  postalCode:           string | null;
}

export interface UpdateStudioRequest {
  name:             string;
  city:             string;
  latitude:         number;
  longitude:        number;
  phoneNumber?:     string | null;
  instagramHandle?: string | null;
  nipt?:            string | null;
  addressLine1?:    string | null;
  addressLine2?:    string | null;
  postalCode?:      string | null;
}
```

`StudioMapItem` — **unchanged**, per §8's DTO audit table.

### 9.4 `StudioProfilePage.tsx`

Schema — add (note: **optional**, not required, unlike the register-page
schema — matches the backend's optional-on-update validator from §7.2, and
matches how `nipt` is already `.optional().or(z.literal(""))` on this same
page):

```typescript
addressLine1: z.string().max(300).optional().or(z.literal("")),
addressLine2: z.string().max(150).optional().or(z.literal("")),
postalCode:   z.string().max(20).optional().or(z.literal("")),
```

Add a dismissible-per-session backfill banner, mirroring the existing
`nipt-banner-dismissed` mechanism verbatim (same session-storage key
pattern, different key and copy):

```typescript
const [addressBannerDismissed, setAddressBannerDismissed] = useState(
  () => sessionStorage.getItem("address-banner-dismissed") === "true",
);

function dismissAddressBanner() {
  sessionStorage.setItem("address-banner-dismissed", "true");
  setAddressBannerDismissed(true);
}
```

```tsx
{studio && !studio.addressLine1 && !addressBannerDismissed && (
  <Alert className="flex items-start justify-between gap-3">
    <AlertDescription className="flex-1">
      Add your studio's street address so clients can find you.{" "}
      <button
        type="button"
        onClick={() => addressInputRef.current?.scrollIntoView({ behavior: "smooth", block: "center" })}
        className="font-medium underline underline-offset-4"
      >
        Add now
      </button>
    </AlertDescription>
    <button
      type="button"
      onClick={dismissAddressBanner}
      aria-label="Dismiss"
      className="text-xs text-muted-foreground hover:text-foreground"
    >
      Dismiss
    </button>
  </Alert>
)}
```

(`addressInputRef` — a new `useRef<HTMLInputElement | null>(null)`, same
scroll-into-view/focus pattern `niptInputRef`/`handleAddNiptNow` already use.)

Wire the same `useAddressGeocode` hook as §9.2 (identical call shape), and
add the same three form fields (Street address / Address line 2 / Postal
code) directly above the existing "Location" `LocationPicker` block. `reset()`
in the `useEffect` that loads `studio` data must include the three new fields:

```typescript
reset({
  name:            studio.name,
  city:            studio.city,
  latitude:        studio.latitude,
  longitude:       studio.longitude,
  phoneNumber:     studio.phoneNumber ?? "",
  instagramHandle: studio.instagramHandle ?? "",
  nipt:            studio.nipt ?? "",
  addressLine1:    studio.addressLine1 ?? "",
  addressLine2:    studio.addressLine2 ?? "",
  postalCode:      studio.postalCode ?? "",
});
```

`onSubmit` already spreads `values` directly into `updateStudio(values)` — no
change needed there as long as the schema/interface additions above line up
with `UpdateStudioRequest`.

### 9.5 `StudioPortfolioPage.tsx` — public display

Two places currently render `studio.city` (a compact header row and a detail
section — grep `studio.city` in this file to find both exactly). At **both**,
render the full address when present, falling back to city-only when it
isn't (pre-existing studios that haven't backfilled yet):

```tsx
<div className="flex items-center gap-1.5 text-sm text-muted-foreground">
  <MapPin className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
  <span>
    {studio.addressLine1
      ? `${studio.addressLine1}${studio.addressLine2 ? `, ${studio.addressLine2}` : ""}, ${studio.city}`
      : studio.city}
  </span>
</div>
```

Add a "Get directions" link near the detail-section occurrence only (not the
compact header), shown only when `addressLine1` is present:

```tsx
{studio.addressLine1 && (
  <a
    href={`https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(
      `${studio.addressLine1}, ${studio.city}`,
    )}`}
    target="_blank"
    rel="noopener noreferrer"
    className="text-xs font-medium text-primary hover:underline"
  >
    Get directions →
  </a>
)}
```

`PublicStudioResponse`'s TS-side type (wherever it's declared in
`publicApi.ts`/`public.types.ts` — locate it, it's not in the files already
quoted in this prompt) needs the same three new optional string fields added.

**Do not** touch `ArtistPortfolioPage.tsx` or `DiscoverPage.tsx`'s studio
cards — both only show city in a list-row context, consistent with the §8 DTO
decision that those surfaces don't carry address at all.

---

## 10. Stretch — address candidate dropdown (optional, see §3.1)

Not required tonight. If time allows, upgrade `useAddressGeocode` from
"silently take the top-1 Nominatim result" to a small candidate list:
`limit=5` instead of `limit=1`, return `results: GeocodedLocation[]` instead
of firing `onResolved` automatically, and render a small dropdown under the
address input (same interaction shape as a native `<datalist>` or a simple
absolutely-positioned `<ul>`, styled consistent with existing dropdown/select
patterns in `shared/components/ui/`) so the owner picks the correct candidate
rather than trusting a single guess. This meaningfully narrows the gap named
in §3.1 without a paid API. If not implemented tonight, log it as a named
backlog item in the same architecture.md entry from §13 — do not silently
drop it.

---

## 11. Tests

### 11.1 Backend

**`tests/Pena_e_Arte.UnitTests/Studios/RegisterStudioHandlerTests.cs`** — add
a case asserting a successful registration persists `AddressLine1`
(trimmed), `AddressLine2`/`PostalCode` null when omitted, and that the
returned `StudioResponse` carries all three.

**`tests/Pena_e_Arte.UnitTests/Studios/RegisterStudioValidatorTests.cs`** —
add cases: empty `AddressLine1` fails validation; `AddressLine1` over 300
chars fails; `AddressLine2`/`PostalCode` are valid when omitted; over-length
`AddressLine2`(150)/`PostalCode`(20) fail.

**`tests/Pena_e_Arte.UnitTests/Studios/UpdateMyStudioHandlerTests.cs`** — add
cases: updating with a new `AddressLine1` persists it; submitting a blank
`AddressLine1` clears it to `null` (per §7.2's deliberate blank-clears
behavior — different from how this same file already tests `Nipt`'s
leave-unchanged-if-blank behavior; do not copy that test's assertion, write
a new one matching address's actual behavior).

**`tests/Pena_e_Arte.UnitTests/Studios/UpdateMyStudioValidatorTests.cs`** —
add over-length cases for all three, and confirm empty/omitted `AddressLine1`
is valid (optional on update).

**`tests/Pena_e_Arte.UnitTests/Public/GetPublicStudioHandlerTests.cs`** — add
a case asserting `PublicStudioResponse.AddressLine1`/`AddressLine2`/
`PostalCode` surface correctly when set, and are `null` (not an exception)
when the studio predates this feature.

### 11.2 Frontend

**New file:** `frontend/src/shared/hooks/__tests__/useAddressGeocode.test.ts`
— mock global `fetch`, use `vi.useFakeTimers()`. Cover: fires exactly one
request after the debounce window elapses with no further changes; does not
fire below `minLength`; a stale in-flight request's result is dropped if the
address changed again before it resolved (assert `onResolved` is called with
the *latest* address's result, not an earlier one, when both are mocked to
resolve out of order); aborts the in-flight request on unmount; `status`
transitions `idle → loading → success`/`error` correctly.

**`frontend/src/features/studios/__tests__/RegisterStudioPage.test.tsx`** —
the existing `LocationPicker` mock (`vi.mock("@/shared/components/ui/location-picker", ...)`)
needs no changes per §2.3. Additionally mock the new hook
(`vi.mock("@/shared/hooks/useAddressGeocode")`) so no test hits real Nominatim
— same reasoning this file already gives for mocking `LocationPicker`
("uses Leaflet and real map tiles — not viable in jsdom"; Nominatim network
calls aren't viable in a unit test either). Update the `fillStep1` helper to
also type into the new required "Street address" field:

```typescript
async function fillStep1(user: ReturnType<typeof userEvent.setup>, studioName = "Ink & Soul Studio") {
  await user.type(screen.getByLabelText(/studio name/i), studioName);
  await user.type(screen.getByLabelText(/business tax id/i), "L01234567A");
  await user.type(screen.getByLabelText(/street address/i), "Rruga e Kavajës 10");
  await user.click(screen.getByTestId("mock-location-picker"));
}
```

Add a new test: leaving "Street address" blank and clicking "Next" shows the
required-field error and blocks advancing to Step 2 (same shape as the
existing bad-NIPT test at the file's line ~193-213).

Add a new test asserting the submitted `registerStudio` payload includes
`addressLine1`/`addressLine2`/`postalCode` (mock the hook to a no-op so this
test only exercises the plain form-field wiring, not geocoding).

**`frontend/src/features/studios/__tests__/StudioProfilePage.test.tsx`** —
mirror the same hook mock; add a case asserting the backfill banner renders
when `studio.addressLine1` is `null` and is dismissible (mirroring the
existing NIPT banner test in this same file almost exactly — find it and
copy its shape).

**`frontend/src/features/public/__tests__/StudioPortfolioPage.test.tsx`** —
add a case asserting the full address renders when present, city-only
fallback when `addressLine1` is `null`, and the "Get directions" link is
present/absent accordingly.

---

## 12. Help sync (CLAUDE.md rule #7 — mandatory in this same change)

### 12.1 In-app Help Menu

**File:** `frontend/src/features/help/helpContent.ts`

The `owner-studio-profile` article (id `owner-studio-profile`, currently
around line 655) already says "address/city" in its `steps` text even though
there was no address field until now — that line becomes literally true
instead of aspirationally worded. Update:

```typescript
{
  id: "owner-studio-profile",
  roles: [Owner],
  title: "Edit your studio profile",
  route: "/studios/me",
  keywords: ["studio settings", "studio name", "address", "street address", "description", "nipt", "tax id", "business id"],
  summary: "Edit your studio's public details — name, street address, phone, Instagram, description — and your business tax ID (NIPT), which clients don't see but is used for invoicing and verification.",
  steps: [
    "Go to Studio Settings.",
    "Click \"Edit\" and update your studio name, street address, phone number, Instagram handle, or description. The map updates automatically as you type your address — drag the pin afterward if it needs adjusting.",
    "If you haven't added your NIPT yet, enter it in the Business tax ID field — format is one letter, 8 digits, one letter (e.g. L01234567A). Once saved, this field becomes read-only; contact support to change it.",
    "Click \"Save\" to publish the changes.",
  ],
  tips: [
    "Your NIPT is never shown to clients or on your public booking page — it's for invoicing and business verification only.",
    "Your street address is shown on your public studio page with a \"Get directions\" link, so make sure it's accurate.",
  ],
  relatedArticleIds: ["owner-branding", "owner-embed", "owner-qr-code", "owner-referral"],
},
```

A new article/step is not needed for `RegisterStudioPage` itself — confirm
whether a getting-started/owner-registration article exists (search
`helpContent.ts` for a `guest`/`owner` registration entry); if one exists,
add one line noting the street-address field and live map update; if none
exists, this page is out of this article system's coverage today and no new
one needs to be created solely for this change.

### 12.2 Standalone user manual

**File:** `frontend/public/user-manual/index.html`

Update the `#owner-studio-profile` section's intro paragraph (currently:
`"...name, phone, Instagram, business tax ID (NIPT), location, and URL
slug..."`) to mention street address, and its steps list (currently step 2:
`"Update Studio name, Phone, Instagram handle, and Location (drag the map
pin), then Save changes."`) to describe typing an address as the primary
path, with pin-dragging as the fallback/correction method:

```html
<p>The studio settings hub at <code>/studios/me</code>: name, street address, phone, Instagram, business tax ID (NIPT), location, and URL slug at the top, followed by branding, QR code, embed code, referral, notification preference, and recent-activity cards below.</p>
```

```html
<li>Update <span class="step-title">Studio name</span>, <span class="step-title">Street address</span> (the map updates automatically as you type — drag the pin afterward to fine-tune), <span class="step-title">Phone</span>, and <span class="step-title">Instagram handle</span>, then <span class="step-title">Save changes</span>.</li>
```

Optional polish, not required: the illustrative wireframe SVGs in this file
(e.g. the `#owner-studio-profile` section's `<figure class="wireframe">`
around line 2251, and the earlier decorative Register-studio SVG around line
744 depicting "Studio location (map picker)") are simplified mockups that
already omit some real fields (neither shows the NIPT field either) — adding
an "Address" row to them is cosmetic, not required for the Help-sync gate.

### 12.3 Onboarding tour

**File:** `frontend/src/features/help/tours/ownerTour.ts`

The existing `owner-studio-profile-nav` step already says "Edit your
studio's public details, branding, booking widget, QR code, and referral
code here" — generic enough that it doesn't need a code change; address is
one of "your studio's public details" already covered by that wording. No
change required, but confirm this reasoning holds (i.e. don't add a new tour
step just for one new field on a page the tour already points at).

---

## 13. `docs/claude/*.md` sync

**File:** `docs/claude/database.md`

Update the "Studio Entity Fields" consolidated reference (currently lines
~69-88) to add the three new fields, matching the exact style already used:

```csharp
public class Studio  // NOT a TenantEntity — issuer-owned
{
    public Guid     Id               { get; init; } = Guid.NewGuid();
    public string   Name             { get; set; }
    public string   Slug             { get; set; }  // url-safe unique identifier
    public string   City             { get; set; }
    public string?  Nipt             { get; set; }  // business tax ID (NUIS) — nullable for backfill, not an auth factor
    public string?  AddressLine1     { get; set; }  // street address — nullable for backfill, required for new registrations (app-layer)
    public string?  AddressLine2     { get; set; }  // suite/unit/floor — always optional
    public string?  PostalCode       { get; set; }  // always optional
    public double   Latitude         { get; set; }
    public double   Longitude        { get; set; }
    public bool     IsActive         { get; set; }
    public DateTime TrialExpiresAt   { get; set; }  // CreatedAt + 14 days
    public string?  StripeCustomerId { get; set; }  // Stripe Billing (SaaS subscription)
    public DateTime CreatedAt        { get; init; } = DateTime.UtcNow;
}
```

**File:** `docs/claude/architecture.md`

1. Find and update the Feature Module Map entries for `/register`
   (`RegisterStudioPage`) and `/studios/me` (`StudioProfilePage`) — follow the
   exact formatting convention already used for the `/discover` entry (route,
   component, file path, then indented notes lines, e.g. the block starting
   `/discover           DiscoverPage  public/components/DiscoverPage.tsx`
   around line 882). Note the new street-address field, the live debounced
   forward-geocode behavior, and that it reuses the existing
   `LocationPicker`-driven `setValue(lat/lng/city)` path rather than adding a
   new prop to that component.
2. Add a new row (or short subsection, matching whichever style the nearest
   existing entries use) documenting the §3.2 gap: four client-side call
   sites now hit Nominatim directly with no backend proxy, no server-set
   `User-Agent`, and no server-side rate limiting — flagged as a fast-follow,
   not fixed tonight.
3. If §10's stretch (candidate dropdown) was not implemented, log it here too
   as a named backlog item, same convention as the NIPT prompt's checksum
   follow-up.

**File:** `DECISIONS.md`

Append a new dated row to the decisions table, matching the exact format of
the existing `NIPT business verification (2026-07-22)` row (id | what/where |
why), summarizing: the three new `Studio` columns and their nullability
shape; the choice to keep forward-geocoding entirely client-side for
consistency with the three existing Nominatim call sites (and the flagged gap
that leaves); the one-directional (address → map, not map → address) scope
decision; and which public DTOs were and weren't widened, with the one-line
reasoning from §8's table.

---

## 14. Verification checklist — do not mark this done until all of these pass

1. `dotnet build` — clean, no warnings introduced.
2. `dotnet ef migrations add AddStudioAddress ...` generates the expected
   `Up`/`Down` shape from §6.2; `dotnet ef database update` applies cleanly
   against a real/scratch MySQL 8.4 instance.
3. `dotnet test tests/Pena_e_Arte.UnitTests` — full suite green, including
   every new case from §11.1.
4. Grep `Pena_e_Arte.Application` and `Pena_e_Arte.Contracts` for
   `new StudioResponse(` and `new PublicStudioResponse(` — confirm every call
   site (expected: exactly 3 for `StudioResponse`, exactly 1 for
   `PublicStudioResponse`) includes the new fields.
5. Grep the whole repo for `new RegisterStudioRequest(` — confirm no
   positional-construction call site was broken by inserting `AddressLine1`
   before the trailing optional params.
6. `pnpm test` (`vitest run`) — full suite green, including
   `useAddressGeocode.test.ts` (new) and the updated
   `RegisterStudioPage.test.tsx`/`StudioProfilePage.test.tsx`/
   `StudioPortfolioPage.test.tsx` cases.
7. `pnpm lint` — clean on every file touched.
8. `pnpm build` — clean.
9. Manually confirm (or via Playwright if convenient) the actual "real time"
   behavior against real Nominatim: typing a real, valid street address into
   Step 1 of `/register` moves the map pin and fills `City` within roughly
   one debounce window, without submitting the form or leaving the field;
   typing garbage/an unfindable address shows the inline "couldn't find that
   address" hint without crashing or leaving the pin in a bad state; the pin
   remains manually draggable afterward.
10. Confirm `location-picker.tsx` has a **zero-line diff** (§2.3/§4).
11. Confirm Help Menu (`helpContent.ts`), the standalone manual
    (`user-manual/index.html`), and the onboarding tour reasoning (§12.3) are
    all addressed in this same change, per CLAUDE.md rule #7.
12. Confirm `docs/claude/architecture.md`, `docs/claude/database.md`, and
    `DECISIONS.md` all carry this change, per §13.
13. Re-read §3's four flagged decisions and confirm each was either
    implemented as specified or explicitly logged as backlog — none silently
    dropped.
