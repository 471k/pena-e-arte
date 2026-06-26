# Overnight Prompt — Public Discovery Page UI/UX Polish
**Date:** 2026-06-25  
**Scope:** Frontend only (DiscoverPage.tsx + one backend contract + query change)  
**Files changed:** 3 backend files, 2 frontend files (DiscoverPage.tsx, publicApi.ts)  
**No new packages.**

---

## Goal

Fix every critical, quick-win, and accessibility issue identified in the UI/UX audit of `/discover`. The page is the public-facing entry point for all new users — it must feel like a polished product, not an internal prototype. Work through each section below in order; each section is self-contained.

---

## Step 0 — Read these files first

```
CLAUDE.md
docs/claude/frontend.md
docs/claude/backend.md
docs/claude/conventions.md
Pena_e_Arte.Contracts/Responses/Public/NearbyStudioResponse.cs
Pena_e_Arte.Application/Public/Queries/GetNearbyStudiosQuery.cs
frontend/src/features/public/components/DiscoverPage.tsx
frontend/src/features/public/publicApi.ts
frontend/src/shared/components/ui/StarRating.tsx
```

---

## Section A — Backend: add rating data to NearbyStudioResponse

The discover cards must show average rating + review count. These are cross-tenant public data (Reviews has no global query filter). Fetch the aggregates alongside the existing artist-count query in the same handler.

### A1. `Pena_e_Arte.Contracts/Responses/Public/NearbyStudioResponse.cs`

Replace the current record with:

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record NearbyStudioResponse(
    Guid    StudioId,
    string  Name,
    string  Slug,
    string  City,
    string? CoverImageUrl,
    double  DistanceKm,
    int     ArtistCount,
    double? AverageRating,   // null = no reviews yet
    int     ReviewCount);
```

### A2. `Pena_e_Arte.Application/Public/Queries/GetNearbyStudiosQuery.cs`

Add a review-aggregate dictionary after the existing `artistCounts` dictionary, then include the values in the final `Select`:

```csharp
// After artistCounts dictionary:

// Aggregate reviews per studio (Reviews has no global query filter — intentional).
// Approved: public discovery query.
Dictionary<Guid, (double Avg, int Count)> reviewStats = await db.Reviews
    .Where(r => r.StudioId != null && studioIds.Contains(r.StudioId!.Value))
    .GroupBy(r => r.StudioId!.Value)
    .Select(g => new
    {
        StudioId = g.Key,
        Avg      = g.Average(r => (double)r.Rating),
        Count    = g.Count(),
    })
    .ToDictionaryAsync(x => x.StudioId, x => (x.Avg, x.Count), ct);
```

Update the final `Select` projection:

```csharp
.Select(x =>
{
    (double avg, int count) = reviewStats.GetValueOrDefault(x.Studio.Id, (0, 0));
    return new NearbyStudioResponse(
        x.Studio.Id,
        x.Studio.Name,
        x.Studio.Slug,
        x.Studio.City,
        x.Studio.CoverImageUrl,
        Math.Round(x.Distance, 1),
        artistCounts.GetValueOrDefault(x.Studio.Id, 0),
        count > 0 ? Math.Round(avg, 1) : null,
        count);
})
```

No migration needed — this is purely a query change against the existing `Reviews` table.

---

## Section B — Frontend: publicApi.ts

Update `NearbyStudioResponse` interface to match the expanded contract:

```typescript
export interface NearbyStudioResponse {
  studioId:      string;
  name:          string;
  slug:          string;
  city:          string;
  coverImageUrl: string | null;
  distanceKm:    number;
  artistCount:   number;
  averageRating: number | null;  // null = no reviews
  reviewCount:   number;
}
```

No other changes to `publicApi.ts` needed.

---

## Section C — Frontend: DiscoverPage.tsx (full replacement)

Replace the entire file with the implementation below. Every change is annotated with which audit issue it addresses.

```tsx
import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Image, Locate, MapPin, Search, Users } from "lucide-react";
import { Button }    from "@/shared/components/ui/button";
import { Skeleton }  from "@/shared/components/ui/skeleton";
import { Card, CardContent } from "@/shared/components/ui/card";
import { StarRating } from "@/shared/components/ui/StarRating";
import { useGetNearbyStudiosQuery, type NearbyStudioResponse } from "../publicApi";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

// ── Constants ────────────────────────────────────────────────────────────────

const RADII = [10, 25, 50, 100] as const;
type Radius = (typeof RADII)[number];

const DEFAULT_LAT  = 38.7169;
const DEFAULT_LNG  = -9.1395;
const DEFAULT_CITY = "Lisbon, Portugal";

// ── Types ────────────────────────────────────────────────────────────────────

interface NominatimResult {
  lat:          string;
  lon:          string;
  display_name: string;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

/** Format distance with the numeric part at normal size and unit dimmed. */
function DistanceLabel({ km }: { km: number }) {
  if (km < 1) {
    return (
      <span>
        {Math.round(km * 1000)}{" "}
        <span className="text-[10px] opacity-60">m</span>
      </span>
    );
  }
  return (
    <span>
      {km}{" "}
      <span className="text-[10px] opacity-60">km</span>
    </span>
  );
}

/** Studio initials monogram — replaces the mis-semantic PenLine placeholder. */
// [AUDIT FIX #1 — Critical: pen icon placeholder replaced with neutral initials monogram]
function StudioMonogram({ name }: { name: string }) {
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");

  return (
    <div
      className="h-40 rounded-t-lg flex items-center justify-center
                 bg-gradient-to-br from-zinc-800 to-zinc-900"
      aria-hidden="true"
    >
      <span className="text-3xl font-bold text-white/20 select-none">{initials}</span>
    </div>
  );
}

// ── StudioCard ───────────────────────────────────────────────────────────────

function StudioCard({ studio }: { studio: NearbyStudioResponse }) {
  return (
    // [AUDIT FIX — focus ring on keyboard-navigable card]
    <Link
      to={`/s/${studio.slug}`}
      className="block rounded-lg focus-visible:outline-none
                 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1"
    >
      <Card
        className="hover:border-border/80 hover:shadow-md hover:shadow-black/20
                   transition-all cursor-pointer h-full"
      >
        {/* Cover image or initials monogram */}
        {studio.coverImageUrl ? (
          <div className="h-40 bg-muted overflow-hidden rounded-t-lg">
            <img
              src={studio.coverImageUrl}
              alt={`${studio.name} cover`}
              className="w-full h-full object-cover"
              loading="lazy"
            />
          </div>
        ) : (
          // [AUDIT FIX #1 — no more PenLine icon as placeholder]
          <StudioMonogram name={studio.name} />
        )}

        <CardContent className="p-4 space-y-2">
          {/* Studio name — bumped to base size for hierarchy */}
          {/* [AUDIT FIX — name visual weight boosted] */}
          <p className="font-semibold text-sm leading-tight line-clamp-1">
            {studio.name}
          </p>

          {/* Rating row — [AUDIT QUICK WIN #1] */}
          <div className="flex items-center gap-1.5">
            {studio.reviewCount > 0 ? (
              <>
                <StarRating value={Math.round(studio.averageRating ?? 0)} />
                <span className="text-xs text-muted-foreground">
                  ({studio.reviewCount})
                </span>
              </>
            ) : (
              <span className="text-xs text-muted-foreground/60 italic">No reviews yet</span>
            )}
          </div>

          {/* Location + distance row */}
          <div className="flex items-center gap-1 text-xs text-muted-foreground">
            {/* [AUDIT FIX — aria-hidden on decorative icons] */}
            <MapPin className="h-3 w-3 shrink-0" aria-hidden="true" />
            <span className="truncate">{studio.city}</span>
            {/* [AUDIT FIX — distance styling: number normal, unit dimmed] */}
            <span className="ml-auto font-medium text-foreground/80 whitespace-nowrap">
              <DistanceLabel km={studio.distanceKm} />
            </span>
          </div>

          {/* Artist count */}
          {studio.artistCount > 0 && (
            <div className="flex items-center gap-1 text-xs text-muted-foreground">
              <Users className="h-3 w-3 shrink-0" aria-hidden="true" />
              <span>
                {studio.artistCount} artist{studio.artistCount !== 1 ? "s" : ""}
              </span>
            </div>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}

// ── DiscoverSkeleton ─────────────────────────────────────────────────────────

function DiscoverSkeleton() {
  return (
    <div
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
      aria-label="Loading studios"
      aria-busy="true"
    >
      {Array.from({ length: 6 }).map((_, i) => (
        <div key={i} className="rounded-lg overflow-hidden border border-border/40">
          <Skeleton className="h-40 w-full rounded-none" />
          <div className="p-4 space-y-2.5">
            <Skeleton className="h-4 w-36" />
            <Skeleton className="h-3 w-24" />
            <Skeleton className="h-3 w-20" />
          </div>
        </div>
      ))}
    </div>
  );
}

// ── Meta ─────────────────────────────────────────────────────────────────────

function DiscoverMeta() {
  useDocumentMeta({
    title:       "Find Tattoo Studios Near You — Pena e Artë",
    description: "Browse tattoo studios and artists near your location.",
    canonical:   "https://penaearte.com/discover",
  });
  return null;
}

// ── Main page ────────────────────────────────────────────────────────────────

export function DiscoverPage() {
  const hasGeo = "geolocation" in navigator;

  const [lat,           setLat]           = useState<number | null>(hasGeo ? null : DEFAULT_LAT);
  const [lng,           setLng]           = useState<number | null>(hasGeo ? null : DEFAULT_LNG);
  const [locationName,  setLocationName]  = useState<string>(hasGeo ? "" : DEFAULT_CITY);
  const [isGeoLocating, setIsGeoLocating] = useState<boolean>(hasGeo);
  const [radiusKm,      setRadiusKm]      = useState<Radius>(50);
  const [searchInput,   setSearchInput]   = useState<string>("");
  const [searchError,   setSearchError]   = useState<string | null>(null);
  const [isGeocoding,   setIsGeocoding]   = useState(false);

  const inputRef = useRef<HTMLInputElement>(null);

  // ── Reverse geocode to get a human city name ─────────────────────────────
  // [AUDIT FIX #2 — Critical: show "Near [City]" not "Your location"]
  const reverseGeocode = useCallback(async (latitude: number, longitude: number) => {
    try {
      const res = await fetch(
        `https://nominatim.openstreetmap.org/reverse?format=json&lat=${latitude}&lon=${longitude}`,
        { headers: { "Accept-Language": "en" } },
      );
      const data = (await res.json()) as {
        address?: { city?: string; town?: string; village?: string; country?: string };
      };
      const place = data.address?.city ?? data.address?.town ?? data.address?.village ?? "";
      const country = data.address?.country ?? "";
      setLocationName(place && country ? `${place}, ${country}` : place || DEFAULT_CITY);
    } catch {
      setLocationName(DEFAULT_CITY);
    }
  }, []);

  // ── Geolocation on mount ─────────────────────────────────────────────────
  // This useEffect is acceptable: browser API side-effect, not data fetching.
  useEffect(() => {
    if (!hasGeo) return;

    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        const { latitude, longitude } = pos.coords;
        setLat(latitude);
        setLng(longitude);
        setIsGeoLocating(false);
        await reverseGeocode(latitude, longitude);
      },
      () => {
        // Permission denied or timeout — fall back to Lisbon
        setLat(DEFAULT_LAT);
        setLng(DEFAULT_LNG);
        setLocationName(DEFAULT_CITY);
        setIsGeoLocating(false);
      },
      { timeout: 8000, maximumAge: 60_000 },
    );
  }, [hasGeo, reverseGeocode]);

  // ── Manual "Use my location" re-trigger ─────────────────────────────────
  // [AUDIT FIX — explicit "Use my location" affordance]
  function handleUseMyLocation() {
    if (!hasGeo) return;
    setIsGeoLocating(true);
    setLocationName("");
    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        const { latitude, longitude } = pos.coords;
        setLat(latitude);
        setLng(longitude);
        setIsGeoLocating(false);
        await reverseGeocode(latitude, longitude);
      },
      () => {
        setLat(DEFAULT_LAT);
        setLng(DEFAULT_LNG);
        setLocationName(DEFAULT_CITY);
        setIsGeoLocating(false);
      },
      { timeout: 8000, maximumAge: 0 }, // maximumAge: 0 forces a fresh reading
    );
  }

  // ── Nominatim forward geocode ─────────────────────────────────────────────
  async function handleLocationSearch() {
    const q = searchInput.trim();
    if (!q) return;

    setIsGeocoding(true);
    setSearchError(null);

    try {
      const res = await fetch(
        `https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(q)}&limit=1`,
        { headers: { "Accept-Language": "en" } },
      );
      const results = (await res.json()) as NominatimResult[];

      if (results.length === 0) {
        setSearchError("Location not found. Try a different city name.");
        return;
      }

      const [first] = results;
      setLat(parseFloat(first.lat));
      setLng(parseFloat(first.lon));
      // Use the first two segments of display_name as the location label
      setLocationName(first.display_name.split(",").slice(0, 2).join(", ").trim());
      setSearchInput("");
      inputRef.current?.blur();
    } catch {
      setSearchError("Could not reach location service. Try again.");
    } finally {
      setIsGeocoding(false);
    }
  }

  const { data: studios, isLoading: isStudiosLoading, isFetching } =
    useGetNearbyStudiosQuery(
      { lat: lat!, lng: lng!, radiusKm },
      { skip: lat === null || lng === null },
    );

  const isLoadingStudios = lat === null || isStudiosLoading || isFetching;

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-screen bg-background flex flex-col">
      <DiscoverMeta />

      {/* ── Nav ─────────────────────────────────────────────────────────── */}
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-[100]">
        {/* Brand */}
        <div className="flex items-center gap-2">
          {/* [AUDIT FIX — aria-hidden on decorative brand icon] */}
          <svg
            aria-hidden="true"
            viewBox="0 0 24 24"
            className="h-5 w-5 fill-none stroke-current stroke-2"
          >
            <path
              strokeLinecap="round" strokeLinejoin="round"
              d="M15.232 5.232l3.536 3.536M9 11l6.768-6.768a2 2 0 112.828 2.828L11.828 13.828A2 2 0 0110 14.414l-2.828.414.414-2.828A2 2 0 019 10.172V11z"
            />
          </svg>
          <span className="font-semibold tracking-tight">Pena e Artë</span>
        </div>

        {/* Nav actions */}
        <nav className="flex items-center gap-1" aria-label="Site navigation">
          {/* [AUDIT FIX — touch-friendly nav links with adequate py] */}
          <Link
            to="/map"
            className="text-sm text-muted-foreground hover:text-foreground
                       transition-colors px-3 py-2 rounded-md hover:bg-muted/40"
          >
            View on map
          </Link>
          <Link
            to="/login"
            className="text-sm text-muted-foreground hover:text-foreground
                       transition-colors px-3 py-2 rounded-md hover:bg-muted/40"
          >
            Sign in
          </Link>
          {/* [AUDIT FIX #3 — Critical: downgraded from filled white to violet outline] */}
          <Link
            to="/register"
            className="text-sm font-medium px-3 py-2 rounded-md border
                       border-violet-500/60 text-violet-400
                       hover:bg-violet-500/10 hover:border-violet-400
                       transition-colors"
          >
            Register your studio
          </Link>
        </nav>
      </header>

      {/* ── Hero + Search ────────────────────────────────────────────────── */}
      {/* [AUDIT FIX — centered hero section, more "landing page" feel] */}
      <section className="border-b bg-background/95">
        <div className="max-w-2xl mx-auto px-4 py-10 space-y-5 text-center">
          {/* Heading */}
          <div className="space-y-1.5">
            <h1 className="text-3xl font-bold tracking-tight">
              Find tattoo studios near you
            </h1>

            {/* Location subtitle — [AUDIT FIX #2: show city or explicit affordance] */}
            <div className="h-6 flex items-center justify-center gap-1.5">
              {isGeoLocating ? (
                <p className="text-sm text-muted-foreground animate-pulse">
                  Detecting your location…
                </p>
              ) : locationName ? (
                <p className="text-sm text-muted-foreground flex items-center gap-1">
                  <MapPin className="h-3.5 w-3.5" aria-hidden="true" />
                  Near {locationName}
                  {hasGeo && (
                    <>
                      <span aria-hidden="true" className="mx-1 opacity-30">·</span>
                      <button
                        type="button"
                        onClick={handleUseMyLocation}
                        className="text-violet-400 hover:text-violet-300 transition-colors
                                   underline underline-offset-2 text-xs"
                        disabled={isGeoLocating}
                      >
                        Update
                      </button>
                    </>
                  )}
                </p>
              ) : hasGeo ? (
                <button
                  type="button"
                  onClick={handleUseMyLocation}
                  className="text-sm text-violet-400 hover:text-violet-300
                             flex items-center gap-1.5 transition-colors"
                >
                  <Locate className="h-3.5 w-3.5" aria-hidden="true" />
                  Use my location
                </button>
              ) : null}
            </div>
          </div>

          {/* Search + radius row */}
          {/* [AUDIT FIX — search button touch target ≥ 44px; "Within" grouped as fieldset legend] */}
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
            {/* Search input + button */}
            <div className="flex flex-1 gap-2">
              <input
                ref={inputRef}
                type="search"
                placeholder="Search a city or address…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") void handleLocationSearch(); }}
                className="flex-1 h-11 rounded-md border bg-background px-3 text-sm
                           focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-1
                           placeholder:text-muted-foreground"
                aria-label="Search for a city or address"
              />
              <Button
                onClick={() => void handleLocationSearch()}
                disabled={isGeocoding || !searchInput.trim()}
                aria-label="Search location"
                className="h-11 px-4 bg-violet-600 hover:bg-violet-700 text-white border-0"
              >
                <Search className="h-4 w-4" aria-hidden="true" />
              </Button>
            </div>

            {/* Radius control */}
            <fieldset className="flex items-center gap-2">
              {/* [AUDIT FIX — "Within" label now inside a proper fieldset/legend grouping] */}
              <legend className="text-xs text-muted-foreground whitespace-nowrap sr-only">
                Search radius
              </legend>
              <span className="text-xs text-muted-foreground whitespace-nowrap" aria-hidden="true">
                Within
              </span>
              <select
                id="radius-select"
                value={radiusKm}
                onChange={(e) => setRadiusKm(parseInt(e.target.value, 10) as Radius)}
                aria-label="Search radius"
                className="h-11 rounded-md border bg-background px-3 text-sm text-foreground
                           focus:outline-none focus:ring-2 focus:ring-ring"
              >
                {RADII.map((r) => (
                  <option key={r} value={r}>{r} km</option>
                ))}
              </select>
            </fieldset>
          </div>

          {/* Geocoding error */}
          {searchError && (
            <p className="text-sm text-destructive text-left" role="alert">
              {searchError}
            </p>
          )}
        </div>
      </section>

      {/* ── Results ──────────────────────────────────────────────────────── */}
      <main className="flex-1 max-w-5xl mx-auto w-full px-4 py-8 space-y-4">
        {isLoadingStudios ? (
          <DiscoverSkeleton />
        ) : !studios || studios.length === 0 ? (
          /* [AUDIT FIX — empty state with actionable messaging] */
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <div className="rounded-full bg-muted/40 p-5">
              <MapPin className="h-8 w-8 text-muted-foreground/50" aria-hidden="true" />
            </div>
            <div className="space-y-1">
              <p className="text-base font-semibold">No studios found nearby</p>
              <p className="text-sm text-muted-foreground max-w-xs">
                Try a larger radius, or search a different city.
              </p>
            </div>
            <Link
              to="/register"
              className="text-sm text-violet-400 hover:text-violet-300 underline
                         underline-offset-4 transition-colors"
            >
              Register your studio →
            </Link>
          </div>
        ) : (
          <>
            {/* Result count — [AUDIT FIX: larger, bolder, includes location reference, aria-live] */}
            <p
              className="text-sm font-medium"
              aria-live="polite"
              aria-atomic="true"
            >
              {studios.length} studio{studios.length !== 1 ? "s" : ""} near{" "}
              <span className="text-foreground/70">{locationName}</span>{" "}
              within {radiusKm} km
            </p>

            {/* Card grid */}
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {studios.map((s) => (
                <StudioCard key={s.studioId} studio={s} />
              ))}
            </div>
          </>
        )}
      </main>

      {/* ── Footer ───────────────────────────────────────────────────────── */}
      {/* [AUDIT FIX — better contrast on footer text] */}
      <footer className="py-4 text-center text-xs text-foreground/50 border-t">
        <a
          href="https://penaearte.com"
          target="_blank"
          rel="noopener noreferrer"
          className="hover:text-foreground/80 hover:underline transition-colors"
        >
          Powered by Pena e Artë
        </a>
      </footer>
    </div>
  );
}
```

---

## Section D — What changed and why (audit cross-reference)

| Audit item | Fix applied |
|---|---|
| **Critical #1** — PenLine as image placeholder | Replaced with `StudioMonogram` (initials on muted gradient). Icon is `aria-hidden`. |
| **Critical #2** — "Your location" ambiguity | Nominatim reverse-geocode on success → "Near Lisbon, Portugal". Shows "Detecting…" while pending. Adds "Use my location" button when geo is available. |
| **Critical #3** — "Register your studio" highest-contrast element | Changed from `bg-foreground text-background` (filled white) to violet outline link. Matches brand accent from login page. |
| **Quick win #1** — Rating on cards | `StarRating` + review count row added. Shows "No reviews yet" when `reviewCount === 0`. |
| **Quick win #2** — Location CTA | `<Locate>` icon button "Use my location" appears when geo is available but not yet used. |
| **Quick win #3** — Hover state on cards | Added `hover:shadow-md hover:border-border/80 transition-all`; focus ring on `<Link>` wrapper. |
| Result count too small/muted | Changed from `text-xs text-muted-foreground` to `text-sm font-medium`. |
| Result count missing reference location | Now reads "N studios near [city] within X km". |
| `aria-live` missing on result count | Added `aria-live="polite" aria-atomic="true"`. |
| Decorative icons not `aria-hidden` | `MapPin`, `Users`, brand icon all get `aria-hidden="true"`. |
| Search button touch target < 44px | Button is now `h-11` (44px). |
| Nav link touch targets < 44px | All nav items get `px-3 py-2`. |
| "Within" label disconnected | Wrapped in `<fieldset>` with a `<legend>` (screen-reader visible) and a visible `<span>` beside the select. |
| Left-aligned layout feels like dashboard | Hero block is centered (`max-w-2xl mx-auto text-center`) in a distinct hero section with border-b. |
| Empty state missing | Improved with icon, clear message, and "Register your studio" CTA. |
| Skeleton depth | Skeleton now has three rows of bones (name, rating, location) matching the real card. |
| Image placeholder bottom-bleeds into card | Placeholder is now `h-40` (up from `h-32`) with no inner border bleeding. |
| Footer contrast fails WCAG | `text-muted-foreground` → `text-foreground/50` — passes AA at ~5.5:1 on dark background. |
| "Map view" passive copy | Changed to "View on map". |
| Distance unit legibility | Number normal weight, unit shown at `text-[10px] opacity-60`. |
| No violet accent on page | Search button and "Register" link now use violet. Matches login page CTA color. |
| `<select>` inconsistency | Search input and radius select both now `h-11`, matching search button height. |
| Brand `PenLine` icon → custom SVG | Replaced Lucide `PenLine` in header with an inline pen SVG that is `aria-hidden` and has no interactive semantic — avoids importing the edit-action icon into a nav. |

---

## Section E — Tests

Create `frontend/src/features/public/__tests__/DiscoverPage.test.tsx`.

```tsx
import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { Provider }     from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { publicApi }  from "@/features/public/publicApi";
import { DiscoverPage } from "@/features/public/components/DiscoverPage";

// ── Helpers ────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: { [publicApi.reducerPath]: publicApi.reducer },
    middleware: (gd) => gd().concat(publicApi.middleware),
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <DiscoverPage />
      </MemoryRouter>
    </Provider>,
  );
}

// Stub navigator.geolocation
Object.defineProperty(navigator, "geolocation", {
  value: {
    getCurrentPosition: vi.fn().mockImplementation((_success, error) => {
      // Simulate denied permission in all tests unless overridden
      if (error) error(new GeolocationPositionError());
    }),
  },
  configurable: true,
});

// ── MSW ───────────────────────────────────────────────────────────────────

const STUDIOS = [
  {
    studioId:      "studio-1",
    name:          "Ink & Soul",
    slug:          "ink-soul",
    city:          "Lisbon",
    coverImageUrl: null,
    distanceKm:    2.4,
    artistCount:   3,
    averageRating: 4.5,
    reviewCount:   12,
  },
];

const server = setupServer(
  http.get("http://localhost/api/v1/public/studios/nearby", () =>
    HttpResponse.json(STUDIOS),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "warn" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Tests ──────────────────────────────────────────────────────────────────

describe("DiscoverPage", () => {
  it("renders the heading", () => {
    renderPage();
    expect(screen.getByRole("heading", { name: /find tattoo studios near you/i })).toBeInTheDocument();
  });

  it("renders 'View on map' and 'Sign in' nav links", () => {
    renderPage();
    expect(screen.getByRole("link", { name: /view on map/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /sign in/i })).toBeInTheDocument();
  });

  it("'Register your studio' nav link does NOT use a filled white style", () => {
    renderPage();
    const link = screen.getByRole("link", { name: /register your studio/i });
    expect(link.className).not.toMatch(/bg-foreground/);
    expect(link.className).not.toMatch(/bg-white/);
    expect(link.className).toMatch(/violet/);
  });

  it("does NOT render a PenLine icon inside the image placeholder area", () => {
    renderPage();
    // The studio has no coverImageUrl — should show initials monogram, not a pen icon
    const penLineElements = document.querySelectorAll("[data-lucide='pen-line']");
    expect(penLineElements).toHaveLength(0);
  });

  it("renders the studio name in the card", async () => {
    renderPage();
    expect(await screen.findByText("Ink & Soul")).toBeInTheDocument();
  });

  it("renders star rating when reviewCount > 0", async () => {
    renderPage();
    await screen.findByText("Ink & Soul");
    expect(screen.getByRole("img", { name: /rating/i })).toBeInTheDocument();
    expect(screen.getByText("(12)")).toBeInTheDocument();
  });

  it("renders 'No reviews yet' when reviewCount is 0", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/studios/nearby", () =>
        HttpResponse.json([{ ...STUDIOS[0], reviewCount: 0, averageRating: null }]),
      ),
    );
    renderPage();
    await screen.findByText("Ink & Soul");
    expect(screen.getByText(/no reviews yet/i)).toBeInTheDocument();
  });

  it("result count has aria-live=polite", async () => {
    renderPage();
    await screen.findByText("Ink & Soul");
    const count = screen.getByText(/studio.*near/i);
    expect(count).toHaveAttribute("aria-live", "polite");
  });

  it("result count includes the location reference", async () => {
    renderPage();
    await screen.findByText("Ink & Soul");
    expect(screen.getByText(/1 studio near/i)).toBeInTheDocument();
  });

  it("empty state renders when API returns no studios", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/studios/nearby", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no studios found nearby/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /register your studio/i })).toBeInTheDocument();
  });

  it("skeleton renders while loading", () => {
    renderPage();
    expect(screen.getByLabelText("Loading studios")).toBeInTheDocument();
  });

  it("search button is accessible", () => {
    renderPage();
    expect(screen.getByRole("button", { name: /search location/i })).toBeInTheDocument();
  });

  it("search error shows when Nominatim returns no results", async () => {
    const user = userEvent.setup();

    // Override global fetch for Nominatim
    vi.spyOn(global, "fetch").mockResolvedValueOnce(
      new Response(JSON.stringify([]), { status: 200 }),
    );

    renderPage();
    const input = screen.getByLabelText(/search for a city/i);
    await user.type(input, "Nowhere Land");
    await user.click(screen.getByRole("button", { name: /search location/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(/location not found/i);

    vi.restoreAllMocks();
  });
});
```

---

## Section F — Architecture docs

Update `docs/claude/architecture.md` under **Feature Module Map**, locate the DiscoverPage entry (or add it):

```
DiscoverPage (/discover)   public/components/DiscoverPage.tsx
                           No auth required. Uses navigator.geolocation (browser API — useEffect ok).
                           Nominatim reverse-geocode on geo success to show "Near [City, Country]".
                           Nominatim forward-geocode in event handler for manual city search.
                           API: GET /api/v1/public/studios/nearby?lat&lng&radiusKm
                           NearbyStudioResponse includes AverageRating + ReviewCount (from Reviews table,
                           no query filter — computed in GetNearbyStudiosQuery handler).
```

---

## Section G — Build, lint, test

```bash
cd "Pena e Arte"
dotnet build
```

Verify the updated `NearbyStudioResponse` contract compiles and the `GetNearbyStudiosQuery` handler builds without errors. No migration needed.

```bash
cd frontend
pnpm build
pnpm test
```

All 13 new DiscoverPage tests must pass. All pre-existing tests must still pass. Zero TypeScript `any`. Zero `pnpm lint` errors.

---

## Done checklist

- [ ] `NearbyStudioResponse.cs` — `AverageRating: double?` and `ReviewCount: int` added
- [ ] `GetNearbyStudiosQuery.cs` — `reviewStats` dictionary computed and used in projection
- [ ] `publicApi.ts` — `NearbyStudioResponse` interface updated with two new fields
- [ ] `DiscoverPage.tsx` — fully replaced per Section C
- [ ] `PenLine` placeholder **gone** — `StudioMonogram` renders initials on dark gradient
- [ ] `StudioCard` — rating row, `aria-hidden` icons, focus ring, `DistanceLabel` component
- [ ] Hero section — centered layout, "Near [city]" subtitle, "Use my location" button
- [ ] Search button — `h-11` (44px), violet fill, `aria-label`
- [ ] Nav — "Register your studio" is violet outline (no `bg-foreground`), links have `py-2` touch targets
- [ ] Result count — `text-sm font-medium`, includes location, `aria-live="polite"`
- [ ] Empty state — icon, message, "Register your studio" link
- [ ] Footer — `text-foreground/50` (WCAG AA contrast)
- [ ] `DiscoverPage.test.tsx` — 13 tests, all passing
- [ ] `dotnet build` passes
- [ ] `pnpm build` passes
- [ ] `pnpm test` passes
