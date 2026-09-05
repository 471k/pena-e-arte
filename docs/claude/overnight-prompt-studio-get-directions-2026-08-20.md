# Overnight Prompt — "Open in Google Maps" for Studio Location

**Goal:** Give clients a one-tap "Get Directions" link that opens a studio's pinned
location in Google Maps, on the two public/guest surfaces where a visitor is actually
looking at one specific studio's location: the studio profile page
(`StudioPortfolioPage.tsx`) and the studio map pin popup (`StudioMapPage.tsx`).

This requires a small backend change first: `PublicStudioResponse` (the client-facing
studio DTO) currently exposes only `City` — not `Latitude`/`Longitude` — even though
`Studio.Latitude`/`Longitude` already exist on the entity and are already public via
`StudioMapItemResponse` (`GET /api/studios/map`). No new endpoint, no new
`AllowAnonymous` row — this only grows the response shape of an already-approved
anonymous endpoint (`GET /api/v1/public/studios/{slug}`).

No new npm or NuGet packages — this uses Google's documented, key-free Maps URL API
(`https://www.google.com/maps/dir/?api=1&destination=lat,lng`), the same one you'd get
from Google's own "Get Directions" button anywhere on the web.

All changes must pass `dotnet build`, `dotnet test`, `pnpm tsc --noEmit`, `pnpm lint`,
and `pnpm test --run` before the session ends.

---

## Read First

1. `CLAUDE.md`
2. `docs/claude/frontend.md`
3. `docs/claude/backend.md`
4. `docs/claude/architecture.md` — specifically the "Studio Map" section and the
   `AllowAnonymous Exceptions` table
5. `docs/claude/conventions.md`

---

## Source Files to Read Before Starting

Read each file in full before changing anything:

- `Pena_e_Arte.Domain/Entities/Studio.cs`
- `Pena_e_Arte.Contracts/Responses/Public/PublicStudioResponse.cs`
- `Pena_e_Arte.Application/Public/Queries/GetPublicStudioQuery.cs`
- `Pena_e_Arte.Application/Studios/Validators/RegisterStudioValidator.cs` (confirms
  `Latitude`/`Longitude` are only range-validated `[-90,90]`/`[-180,180]` — `(0,0)` is
  not rejected, which is why the frontend needs its own "is this actually pinned"
  guard — see Section 2)
- `tests/Pena_e_Arte.UnitTests/Public/GetPublicStudioHandlerTests.cs`
- `frontend/src/features/public/publicApi.ts`
- `frontend/src/features/public/components/StudioPortfolioPage.tsx`
- `frontend/src/features/public/__tests__/StudioPortfolioPage.test.tsx`
- `frontend/src/features/map/components/StudioMapPage.tsx`
- `frontend/src/features/map/__tests__/StudioMapPage.test.tsx`
- `frontend/src/features/studios/studiosApi.ts` (confirms `StudioMapItem` already
  carries `latitude`/`longitude` — no backend change needed for the map popup)
- `frontend/src/shared/components/ui/location-picker.tsx` — read the `hasInitial`
  check (`value.lat !== 0`) — this is the existing app-wide convention for
  "(0,0) means unset" that Section 2's `hasPinnedLocation` guard mirrors
- `frontend/src/shared/utils/formatRelativeTime.ts` and `uuid.ts` — for the shape/style
  of a small pure-function file in `shared/utils/`
- `frontend/public/user-manual/index.html` — read the full `#guest-map` and
  `#guest-studio-portfolio` sections before editing them (Section 6)

---

## Files to Change

| File | What changes |
|---|---|
| `Pena_e_Arte.Contracts/Responses/Public/PublicStudioResponse.cs` | Add `Latitude`, `Longitude` |
| `Pena_e_Arte.Application/Public/Queries/GetPublicStudioQuery.cs` | Map the two new fields |
| `tests/Pena_e_Arte.UnitTests/Public/GetPublicStudioHandlerTests.cs` | Assert the two new fields |
| `frontend/src/shared/utils/googleMaps.ts` | **New file** — `buildGoogleMapsDirectionsUrl`, `hasPinnedLocation` |
| `frontend/src/shared/utils/__tests__/googleMaps.test.ts` | **New file** — unit tests for the two helpers |
| `frontend/src/features/public/publicApi.ts` | Add `latitude`, `longitude` to `PublicStudioResponse` |
| `frontend/src/features/public/components/StudioPortfolioPage.tsx` | Sidebar "Get Directions" link; `geo` in JSON-LD |
| `frontend/src/features/public/__tests__/StudioPortfolioPage.test.tsx` | New tests for the link and its (0,0) guard |
| `frontend/src/features/map/components/StudioMapPage.tsx` | "Get directions →" line in each pin's popup |
| `frontend/src/features/map/__tests__/StudioMapPage.test.tsx` | New test for the popup link |
| `frontend/public/user-manual/index.html` | `#guest-studio-portfolio` and `#guest-map` sections |
| `docs/claude/architecture.md` | New Decisions Log row |

`frontend/src/features/help/helpContent.ts` and every file under
`frontend/src/features/help/tours/` are **deliberately not touched** — see Section 6
for why, stated explicitly rather than silently skipped, per `CLAUDE.md` rule 7.

---

## Section 1 — Backend: expose `Latitude`/`Longitude` on `PublicStudioResponse`

### 1-A: `PublicStudioResponse.cs`

Add `Latitude`/`Longitude` right after `City`, matching `Studio.cs`'s own field order:

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicStudioResponse(
    Guid StudioId,
    string Name,
    string Slug,
    string City,
    double Latitude,
    double Longitude,
    string? Description,
    string? CoverImageUrl,
    string? PhoneNumber,
    string? InstagramHandle,
    double? AverageRating,
    int ReviewCount,
    IReadOnlyList<string> GalleryImages,
    IReadOnlyList<PublicArtistSummary> Artists,
    bool ShowBookingCta);
```

### 1-B: `GetPublicStudioHandler` (`GetPublicStudioQuery.cs`)

In the final `return new PublicStudioResponse(...)`, insert `studio.Latitude,
studio.Longitude,` right after `studio.City,` — the constructor is positional, so the
argument order must match 1-A exactly:

```csharp
return new PublicStudioResponse(
    studio.Id,
    studio.Name,
    studio.Slug,
    studio.City,
    studio.Latitude,
    studio.Longitude,
    studio.Description,
    studio.CoverImageUrl,
    studio.PhoneNumber,
    studio.InstagramHandle,
    studioReviewStats is { Count: > 0 } ? Math.Round(studioReviewStats.Avg, 1) : null,
    studioReviewStats?.Count ?? 0,
    galleryImages,
    artistSummaries,
    ShowBookingCta: true);
```

No other change to this handler. No new `IgnoreQueryFilters()` call, no new
`AllowAnonymous` row — `GET /api/v1/public/studios/{slug}` is already row 2 of the
`AllowAnonymous Exceptions` table in `architecture.md`; this only grows what an
already-approved anonymous endpoint returns.

---

## Section 2 — Frontend: `shared/utils/googleMaps.ts` (new file)

Create `frontend/src/shared/utils/googleMaps.ts`:

```ts
/**
 * Builds a Google Maps "get directions" deep link for a pinned lat/lng, using
 * Google's documented, key-free Maps URL API:
 * https://developers.google.com/maps/documentation/urls/get-started#directions-action
 *
 * The same URL works everywhere: it opens the native Google Maps app via universal
 * link on iOS/Android when installed, and falls back to Google Maps on the web.
 */
export function buildGoogleMapsDirectionsUrl(latitude: number, longitude: number): string {
  const params = new URLSearchParams({
    api: "1",
    destination: `${latitude},${longitude}`,
  });
  return `https://www.google.com/maps/dir/?${params.toString()}`;
}

/**
 * A studio's location is only meaningfully "pinned" once it's away from (0, 0) —
 * same sentinel convention `LocationPicker`'s `hasInitial` check already uses
 * (`shared/components/ui/location-picker.tsx`). `RegisterStudioValidator` only
 * range-checks latitude/longitude ([-90,90]/[-180,180]); it doesn't reject the
 * origin outright, so this guard is a real, load-bearing check, not defensive
 * paranoia — a studio whose location was never actually set should not render a
 * "Get Directions" link that points at Null Island.
 */
export function hasPinnedLocation(latitude: number, longitude: number): boolean {
  return !Number.isNaN(latitude) && !Number.isNaN(longitude) && !(latitude === 0 && longitude === 0);
}
```

Create `frontend/src/shared/utils/__tests__/googleMaps.test.ts` with `describe`/`it`
blocks (match this repo's existing `shared/utils/__tests__` style) covering:

- `buildGoogleMapsDirectionsUrl` produces
  `https://www.google.com/maps/dir/?api=1&destination=41.1579%2C-8.6291` for
  `(41.1579, -8.6291)` (note: assert against the URL-encoded comma
  `%2C` that `URLSearchParams` actually produces, not a literal comma — build the
  expected string with `URLSearchParams` in the test too, don't hand-encode it).
- `hasPinnedLocation(0, 0)` → `false`.
- `hasPinnedLocation(41.1579, -8.6291)` → `true`.
- `hasPinnedLocation(NaN, -8.6291)` → `false`.
- `hasPinnedLocation(0, -8.6291)` → `true` (only the exact origin is "unset" — a real
  studio could legitimately sit on the equator or the prime meridian alone).

---

## Section 3 — Frontend: `StudioPortfolioPage.tsx`

### 3-A: Import the new utilities

```ts
import { buildGoogleMapsDirectionsUrl, hasPinnedLocation } from "@/shared/utils/googleMaps";
```

### 3-B: Sidebar — replace the plain city row with an actionable "Get Directions" link

Find this block (inside the sidebar contact card, directly below the Instagram link):

```tsx
              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <MapPin className="h-4 w-4 shrink-0" aria-hidden="true" />
                {studio.city}
              </div>
```

Replace it with:

```tsx
              {hasPinnedLocation(studio.latitude, studio.longitude) ? (
                <a
                  href={buildGoogleMapsDirectionsUrl(studio.latitude, studio.longitude)}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-2 text-sm text-muted-foreground
                             hover:text-foreground transition-colors min-h-[44px]"
                  aria-label={`Get directions to ${studio.name} in ${studio.city} — opens Google Maps`}
                >
                  <MapPin className="h-4 w-4 shrink-0" aria-hidden="true" />
                  Get Directions — {studio.city}
                </a>
              ) : (
                <div className="flex items-center gap-2 text-sm text-muted-foreground">
                  <MapPin className="h-4 w-4 shrink-0" aria-hidden="true" />
                  {studio.city}
                </div>
              )}
```

This matches the exact interaction pattern of the phone (`tel:`) and Instagram links
immediately above it in the same sidebar card: same className shape, same
`min-h-[44px]` touch target, same `target="_blank" rel="noopener noreferrer"` for the
outbound one. Do not touch the hero/content-area `<MapPin />` + city line further up
the page (around the studio description) — that stays plain, informational text; one
actionable "Get Directions" link per page is enough and matches the sidebar-owns-CTAs
layout this page already uses for "Book an Appointment", phone, and Instagram.

### 3-C: `StudioMeta` — add `geo` to the JSON-LD, now that coordinates are available

`StudioMeta` currently takes `city` but not `latitude`/`longitude`. Extend its props
and the `useStructuredData` call:

```tsx
function StudioMeta({
  name, slug, description, coverImageUrl, city, latitude, longitude, averageRating, reviewCount,
}: {
  name: string; slug: string; description: string | null; coverImageUrl: string | null;
  city: string; latitude: number; longitude: number; averageRating: number | null; reviewCount: number;
}) {
  useDocumentMeta({
    title:       `${name} — Book a Tattoo on TattooOS`,
    description: description ?? `Book your next tattoo at ${name}.`,
    ogImage:     coverImageUrl ?? undefined,
    canonical:   `https://tattooos.co/s/${slug}`,
  });
  useStructuredData({
    "@context":    "https://schema.org",
    "@type":       "TattooParlor",
    name,
    description:   description ?? undefined,
    url:           `https://tattooos.co/s/${slug}`,
    image:         coverImageUrl ?? undefined,
    address:       { "@type": "PostalAddress", addressLocality: city },
    ...(hasPinnedLocation(latitude, longitude)
      ? { geo: { "@type": "GeoCoordinates", latitude, longitude } }
      : {}),
    ...(reviewCount > 0
      ? { aggregateRating: { "@type": "AggregateRating", ratingValue: averageRating, reviewCount } }
      : {}),
  });
  return null;
}
```

And update its call site in `StudioPortfolioPage`:

```tsx
      <StudioMeta
        name={studio.name}
        slug={studio.slug}
        description={studio.description}
        coverImageUrl={studio.coverImageUrl}
        city={studio.city}
        latitude={studio.latitude}
        longitude={studio.longitude}
        averageRating={studio.averageRating}
        reviewCount={studio.reviewCount}
      />
```

### 3-D: `publicApi.ts` — extend `PublicStudioResponse`

```ts
export interface PublicStudioResponse {
  studioId:        string;
  name:            string;
  slug:            string;
  city:            string;
  latitude:        number;
  longitude:       number;
  description:     string | null;
  coverImageUrl:   string | null;
  phoneNumber:     string | null;
  instagramHandle: string | null;
  averageRating:   number | null;
  reviewCount:     number;
  galleryImages:   string[];
  artists:         PublicArtistSummary[];
  showBookingCta:  boolean;
}
```

---

## Section 4 — Frontend: `StudioMapPage.tsx`

### 4-A: Import the new utilities

```ts
import { buildGoogleMapsDirectionsUrl, hasPinnedLocation } from "@/shared/utils/googleMaps";
```

### 4-B: Add a second link line to each pin's popup

`StudioMapItem` (`studiosApi.ts`) already carries `latitude`/`longitude` — no backend
change is needed for this surface. Find the `Popup` block inside the `Marker` map:

```tsx
              <Popup>
                <div className="min-w-[160px] space-y-2 py-0.5">
                  <p className="font-semibold text-sm leading-tight">{studio.name}</p>
                  <p className="flex items-center gap-1 text-xs text-muted-foreground">
                    <MapPin className="h-3 w-3 shrink-0" />
                    {studio.city}
                  </p>
                  <a
                    href={`/s/${studio.slug}`}
                    className="block text-xs font-medium text-primary hover:underline"
                  >
                    View studio →
                  </a>
                </div>
              </Popup>
```

Add the new link directly under `View studio →`:

```tsx
              <Popup>
                <div className="min-w-[160px] space-y-2 py-0.5">
                  <p className="font-semibold text-sm leading-tight">{studio.name}</p>
                  <p className="flex items-center gap-1 text-xs text-muted-foreground">
                    <MapPin className="h-3 w-3 shrink-0" />
                    {studio.city}
                  </p>
                  <a
                    href={`/s/${studio.slug}`}
                    className="block text-xs font-medium text-primary hover:underline"
                  >
                    View studio →
                  </a>
                  {hasPinnedLocation(studio.latitude, studio.longitude) && (
                    <a
                      href={buildGoogleMapsDirectionsUrl(studio.latitude, studio.longitude)}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="block text-xs font-medium text-primary hover:underline"
                    >
                      Get directions →
                    </a>
                  )}
                </div>
              </Popup>
```

Every studio plotted on this map came from `GET /api/studios/map`, which requires a
real `Latitude`/`Longitude` pair to appear at all (the marker itself is positioned at
`[studio.latitude, studio.longitude]`) — the `hasPinnedLocation` guard here is
belt-and-suspenders consistency with Section 3, not a scenario this page can actually
hit today, but keep it: it costs nothing and means both surfaces share one rule.

---

## Section 5 — Tests

### 5-A: Backend — `GetPublicStudioHandlerTests.cs`

Add `Latitude`/`Longitude` to the `MakeStudio` helper's default:

```csharp
    private static Studio MakeStudio(string slug = "test-studio", bool active = true) => new()
    {
        Name = "Test Studio",
        Slug = slug,
        City = "Porto",
        Latitude = 41.1579,
        Longitude = -8.6291,
        IsActive = active,
        PhoneNumber = "+351 912 000 000",
        InstagramHandle = "teststudio",
    };
```

Extend the existing `Handle_ActiveStudio_ReturnsStudioWithContactFields` test with two
more assertions:

```csharp
        result.Should().NotBeNull();
        result!.PhoneNumber.Should().Be("+351 912 000 000");
        result.InstagramHandle.Should().Be("teststudio");
        result.Latitude.Should().Be(41.1579);
        result.Longitude.Should().Be(-8.6291);
```

### 5-B: Frontend — `StudioPortfolioPage.test.tsx`

Add `latitude: 41.1579, longitude: -8.6291,` to the `STUDIO` mock object (right after
`city: "Porto",`).

Add two new tests, placed near the existing `"renders phone number link..."` /
`"renders Instagram link..."` tests:

```tsx
  it("renders 'Get Directions' link to Google Maps when coordinates are set", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({ data: STUDIO, isLoading: false, isError: false });
    renderPage();
    const link = screen.getByRole("link", { name: /get directions/i });
    expect(link).toHaveAttribute(
      "href",
      "https://www.google.com/maps/dir/?api=1&destination=41.1579%2C-8.6291",
    );
    expect(link).toHaveAttribute("target", "_blank");
    expect(link).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("falls back to plain city text when the studio has no pinned location", () => {
    mockUseGetPublicStudioQuery.mockReturnValue({
      data: { ...STUDIO, latitude: 0, longitude: 0 },
      isLoading: false,
      isError: false,
    });
    renderPage();
    expect(screen.queryByRole("link", { name: /get directions/i })).not.toBeInTheDocument();
    expect(screen.getByText(STUDIO.city)).toBeInTheDocument();
  });
```

Match whatever the existing mock/query-hook variable name actually is in this file
(read the file first — do not assume `mockUseGetPublicStudioQuery` is the exact name
without checking) and use the file's existing `renderPage()`-style helper if one
exists, rather than duplicating render setup.

### 5-C: Frontend — `StudioMapPage.test.tsx`

The seed `STUDIOS` array already has real `latitude`/`longitude`. Add a new test:

```tsx
  it("renders a 'Get directions' link in each popup pointing to Google Maps", () => {
    renderPage();
    const links = screen.getAllByRole("link", { name: /get directions/i });
    expect(links).toHaveLength(STUDIOS.length);
    expect(links[0]).toHaveAttribute(
      "href",
      "https://www.google.com/maps/dir/?api=1&destination=41.15%2C-8.61",
    );
  });
```

Adjust the exact expected `destination=` value to match whichever studio in `STUDIOS`
ends up first in render order — verify by running the test, don't guess.

---

## Section 6 — Help sync

Per `CLAUDE.md` rule 7, every feature must update Help — this section states exactly
what does and does not apply here, rather than silently skipping anything.

### 6-A: `frontend/src/features/help/helpContent.ts` — **not touched, deliberately**

`HelpRole` only has `Client`, `Artist`, `Owner`, `Issuer` — there is no `Guest` role,
because the in-app Help Menu is chrome inside the authenticated app shell. Both
surfaces this feature touches (`StudioPortfolioPage`, `StudioMapPage`) are public/guest
pages that don't render the Help Menu at all — `PublicPageHeader` has no Help button.
There is no existing Client-role help article about viewing a studio's location either
(`client-book-appointment` only covers the booking form itself). Nothing in
`helpContent.ts` needs to change for this feature to be fully documented.

### 6-B: Onboarding tours — **not touched, deliberately**

`frontend/src/features/help/tours/clientTour.ts` (the only tour that could plausibly
reference client-facing studio browsing) only highlights `client-book-nav`,
`client-my-studios-nav`, `client-designs-nav`, and `client-help-button` — none of which
sit on `StudioPortfolioPage` or `StudioMapPage`. No tour step needs updating.

### 6-C: `frontend/public/user-manual/index.html` — **required, two sections**

This is the surface that actually documents guest/public pages
(`data-role="guest"` sections). Both relevant sections need updating.

**`#guest-studio-portfolio` section:**

1. Update the intro paragraph. Change:

   ```html
   <p>A studio's public page at <code>/s/&lt;slug&gt;</code> — a hero cover photo, description, star rating, the studio's artist roster, a portfolio gallery, and reviews, with a sticky "Book an Appointment" button and contact details in the sidebar.</p>
   ```

   to:

   ```html
   <p>A studio's public page at <code>/s/&lt;slug&gt;</code> — a hero cover photo, description, star rating, the studio's artist roster, a portfolio gallery, and reviews, with a sticky "Book an Appointment" button and contact details — phone, Instagram, and a "Get Directions" link to Google Maps — in the sidebar.</p>
   ```

2. Update the wireframe SVG's sidebar contact line. Change:

   ```html
   <text x="536" y="210" font-size="9" fill-opacity="0.6">📞 phone · @instagram · city</text>
   ```

   to:

   ```html
   <text x="536" y="210" font-size="9" fill-opacity="0.6">📞 phone · @instagram · 🧭 Get Directions</text>
   ```

3. Update step 6 in the "Steps (in page order)" list. Change:

   ```html
   <li>In the sidebar: <span class="step-title">Book an Appointment</span> button (sends signed-out visitors to sign in first), phone number, Instagram handle, and city.</li>
   ```

   to:

   ```html
   <li>In the sidebar: <span class="step-title">Book an Appointment</span> button (sends signed-out visitors to sign in first), phone number, Instagram handle, and a <span class="step-title">Get Directions</span> link that opens the studio's pinned location in Google Maps.</li>
   ```

4. Add a new tip callout, directly after the existing "the studio owner can reply
   publicly..." tip:

   ```html
   <div class="callout callout-tip"><strong>Tip:</strong> "Get Directions" opens Google Maps in a new tab — on a phone, this hands off straight to the Google Maps app if it's installed.</div>
   ```

**`#guest-map` section:**

1. Update step 2 in the "Steps" list. Change:

   ```html
   <li><span class="step-title">Click a pin</span> to open a popup with the studio's name, city, and a "View studio" link.</li>
   ```

   to:

   ```html
   <li><span class="step-title">Click a pin</span> to open a popup with the studio's name, city, a "View studio" link, and a "Get directions" link that opens Google Maps.</li>
   ```

2. Update the wireframe SVG's popup: grow the popup rectangle and add a second text
   line. Change:

   ```html
   <rect x="350" y="250" width="150" height="60" rx="4" fill="currentColor" fill-opacity="0.08" stroke="currentColor" stroke-opacity="0.2"/>
   <text x="360" y="270" font-size="10" font-weight="700">Ink &amp; Soul Studio</text>
   <text x="360" y="285" font-size="9" fill-opacity="0.6">Lisbon · View studio →</text>
   ```

   to:

   ```html
   <rect x="350" y="250" width="150" height="75" rx="4" fill="currentColor" fill-opacity="0.08" stroke="currentColor" stroke-opacity="0.2"/>
   <text x="360" y="270" font-size="10" font-weight="700">Ink &amp; Soul Studio</text>
   <text x="360" y="285" font-size="9" fill-opacity="0.6">Lisbon · View studio →</text>
   <text x="360" y="298" font-size="9" fill-opacity="0.6">Get directions →</text>
   ```

---

## Section 7 — Architecture docs

Add a new row at the end of the `## Decisions Log` table in `docs/claude/architecture.md`
(directly before the `---` that precedes `## Issuer QA Pass — 2026-07-01`):

```markdown
| "Get Directions" — Google Maps deep link on studio location surfaces (2026-08-20) | `PublicStudioResponse` gains `Latitude`/`Longitude` — previously only `City` was exposed on the public studio DTO, even though `Studio.Latitude`/`Longitude` already existed on the entity and were already public via `StudioMapItemResponse`/`GET /api/studios/map`. New `shared/utils/googleMaps.ts` (`buildGoogleMapsDirectionsUrl`, `hasPinnedLocation`) builds Google's documented `/maps/dir/?api=1&destination=lat,lng` URL — no API key, no new npm/NuGet package. Link added to `StudioPortfolioPage.tsx`'s sidebar (replacing the plain city text with an actionable link, same pattern as the phone/Instagram links directly above it) and to each pin's `Popup` in `StudioMapPage.tsx`. `StudioMeta`'s `TattooParlor` JSON-LD also gains an optional `geo: GeoCoordinates` block now that the data is available. Guarded on `hasPinnedLocation` — `(0, 0)` means unset, same sentinel convention `LocationPicker.hasInitial` already uses, since `RegisterStudioValidator` only range-checks `[-90,90]`/`[-180,180]` and doesn't reject the origin outright. No `AllowAnonymous Exceptions` table change — extends the response shape of an already-approved anonymous endpoint (`GET /api/v1/public/studios/{slug}`, row 2), doesn't add a new one. | Current vertical-booking-SaaS standard (CLAUDE.md rule 6) — Fresha/Vagaro/Boulevard/GlossGenius studio-detail pages all surface a one-tap "Get Directions" to the studio's pinned location; this codebase already had the exact geodata and an identical, already-shipped marker pattern to copy from (`StudioMapPage.tsx`), it just wasn't wired to a client-facing deep link yet. Deliberately scoped to the two surfaces where a client is looking at one specific studio's location (profile page, map popup) — `DiscoverPage`'s Studios-tab card grid and `MyBookingsSection`'s per-appointment rows were considered and explicitly deferred, both flagged as separate, larger gaps below. |
```

---

## Out of Scope — flagged explicitly, not silently dropped

Two related gaps came up while scoping this and are real, but are each a bigger change
than "add a maps link" — naming them here per `CLAUDE.md` rule 6/rule 7's own
"flag the gap explicitly" convention rather than letting them quietly disappear:

1. **`MyBookingsSection.tsx` doesn't show which studio an appointment is at, at all** —
   only artist name and duration. For a client who belongs to more than one studio
   (explicitly supported — see `architecture.md`'s multi-studio client model and
   `client-my-studios-nav`), an upcoming-appointment row with no studio name or
   location is arguably the more valuable place for a "Get Directions" link than the
   profile page, since it's the page a client checks right before actually leaving for
   an appointment. Fixing it needs `AppointmentResponse` to carry studio
   name/city/lat/lng (it currently only has `studioId`), which is a real, separately-
   scoped backend + frontend change, not a one-line addition to this prompt's scope.

2. **`DiscoverPage`'s Studios tab** (`NearbyStudioResponse`) shows a grid of nearby
   studio cards with `distanceKm` already computed server-side, but no raw
   `latitude`/`longitude` — so it can't build a Maps link without its own backend
   change to `GetNearbyStudiosQuery`/`NearbyStudioResponse`. Given the page is a dense
   result grid rather than a single-studio detail view, a per-card "Get Directions"
   link may also just be visual clutter rather than the industry-standard pattern —
   worth a real design call, not an assumption, before building it.

Both are real candidates for a follow-up prompt, not silently dropped.

---

## Section 8 — Build checklist

Run all of these before ending the session; every one must be clean:

```bash
# 1. Backend build (new PublicStudioResponse fields + handler mapping)
dotnet build

# 2. Backend tests
dotnet test

# 3. Frontend type check
cd frontend && pnpm tsc --noEmit

# 4. Lint
pnpm lint

# 5. All frontend tests must pass (including the new googleMaps/StudioPortfolioPage/StudioMapPage tests)
pnpm test --run

# 6. Frontend build
pnpm build
```

---

## Summary of Changes

### New features:
- "Get Directions" link on the public studio profile page's sidebar, opening the
  studio's pinned location in Google Maps in a new tab.
- "Get directions →" link in every studio pin's popup on the public studio map.
- `PublicStudioResponse` now exposes `Latitude`/`Longitude` (previously only `City`).
- `StudioMeta`'s `TattooParlor` JSON-LD structured data gains a `geo: GeoCoordinates`
  block, improving map-pack/rich-result SEO eligibility.
- Both new links are guarded against studios with an unset `(0, 0)` location, and hide
  cleanly (falling back to plain city text, or omitting the popup line) rather than
  linking to Null Island.

### Explicitly out of scope (see "Out of Scope" section above):
- Studio location/directions on `MyBookingsSection` per-appointment rows.
- Studio location/directions on `DiscoverPage`'s Studios-tab card grid.

### Help sync:
- `frontend/public/user-manual/index.html` updated (`#guest-studio-portfolio`,
  `#guest-map`).
- `helpContent.ts` and onboarding tours deliberately not touched — justified in
  Section 6, both surfaces are guest-only pages with no Help Menu.

---

## Hard Rules Reminder

- Tenant isolation: not applicable here — both touched endpoints are the existing
  approved anonymous public-portfolio surface; no query filter is bypassed, no new
  `IgnoreQueryFilters()` call is added.
- RBAC: not applicable — no new endpoint, no new authorization policy.
- No PII in logs: not applicable — no new logging in this change.
- No new secrets: this uses Google's key-free Maps URL API — do not introduce a Google
  Maps API key, Maps JavaScript SDK, or any paid Google Maps product. If a future
  feature genuinely needs an embedded interactive Google map (not just a deep link),
  that's a distinct, separately-scoped decision (this app already uses Leaflet +
  OpenStreetMap tiles for its own maps — `StudioMapPage.tsx`, `location-picker.tsx` —
  and switching tile providers is out of scope here).
- No new ORM, no new frontend state library: not applicable.
- Structured logs only: not applicable — no new logging.
- Every user-facing change ships with its Help-sync obligations in the same change
  (Section 6) — done, with the no-op cases justified rather than skipped.
- Match current industry standards (rule 6): this is precisely a "the current
  vertical-booking-SaaS standard" gap-closer — see Section 7's Decisions Log entry.
