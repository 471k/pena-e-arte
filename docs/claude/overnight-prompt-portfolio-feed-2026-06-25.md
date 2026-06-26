# Overnight Prompt — Portfolio Discovery Feed
**Date:** 2026-06-25
**Depends on:** `overnight-prompt-discover-page-polish-2026-06-25.md` must run first and pass all checks.
**No new packages.**

---

## Goal

The moment a user opens `/discover`, they see a full-viewport masonry grid of the highest-rated, most-reviewed tattoo images from artists across the platform — zero friction, no location required to start. This is the product's front door and it must grab attention instantly, like opening Behance or Instagram for tattoos.

The studio card list is still available as a secondary tab. Location, if granted, is used to show distance badges and optionally filter the feed — but it is never a prerequisite for seeing content.

---

## Step 0 — Read these files first

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/architecture.md
Pena_e_Arte.Contracts/Responses/Public/PublicArtistResponse.cs
Pena_e_Arte.Domain/Entities/Artist.cs
Pena_e_Arte.Domain/Entities/Review.cs
Pena_e_Arte.API/Endpoints/PublicEndpoints.cs
Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs
frontend/src/features/public/publicApi.ts
frontend/src/features/public/components/DiscoverPage.tsx
frontend/src/features/public/components/ArtistPortfolioPage.tsx
frontend/src/shared/components/ui/StarRating.tsx
```

---

## Section A — New contract: PortfolioImageResponse

Create `Pena_e_Arte.Contracts/Responses/Public/PortfolioImageResponse.cs`:

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record PortfolioImageResponse(
    string  ImageUrl,
    string  ArtistName,
    string  ArtistSlug,
    string  StudioName,
    string  StudioSlug,
    double? AverageRating,   // null = no artist reviews yet
    int     ReviewCount,
    double? DistanceKm,      // null when no location context provided
    long    ViewCount);      // from Redis; 0 when not yet viewed
```

---

## Section B — Redis view-count convention

View counts live in Redis (already configured — `IConnectionMultiplexer` is registered as singleton).

Key format: `portfolio:views:{artistId}` (e.g. `portfolio:views:3f2504e0-4fdb-11d1-9a48-0800200c9a66`)

- **Increment:** called by `POST /api/v1/public/artists/{slug}/view` when any user views an artist profile.
- **Read:** batch `MGET` inside `GetPortfolioFeedHandler` for all candidate artist IDs.
- **Persistence:** Redis data survives restarts if AOF/RDB is configured (standard setup). View counts do not need to be exact — approximate is fine.

---

## Section C — New query: GetPortfolioFeedQuery

Create `Pena_e_Arte.Application/Public/Queries/GetPortfolioFeedQuery.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using StackExchange.Redis;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPortfolioFeedQuery(
    double? Lat,
    double? Lng,
    double  RadiusKm,
    int     Page,
    int     PageSize = 24) : IRequest<List<PortfolioImageResponse>>;

public class GetPortfolioFeedHandler(IAppDbContext db, IConnectionMultiplexer redis)
    : IRequestHandler<GetPortfolioFeedQuery, List<PortfolioImageResponse>>
{
    public async Task<List<PortfolioImageResponse>> Handle(
        GetPortfolioFeedQuery query, CancellationToken ct)
    {
        // Approved: public portfolio discovery — no tenant scope required.
        // All IgnoreQueryFilters calls below are intentional (cross-tenant public data).

        // 1. Fetch all active artists that have at least one portfolio image.
        List<Artist> artists = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.DeletedAt == null && a.Slug != null && a.PortfolioImages.Count > 0)
            .ToListAsync(ct);

        if (artists.Count == 0) return [];

        List<Guid> artistIds  = artists.Select(a => a.Id).ToList();
        List<Guid> studioIds  = artists.Select(a => a.StudioId).Distinct().ToList();

        // 2. Load studios (active only).
        Dictionary<Guid, Studio> studiosById = await db.Studios
            .IgnoreQueryFilters()
            .Where(s => studioIds.Contains(s.Id) && s.IsActive)
            .ToDictionaryAsync(s => s.Id, ct);

        // 3. Artist-level review aggregates.
        Dictionary<Guid, (double Avg, int Count)> reviewStats = await db.Reviews
            .Where(r => r.ArtistId != null && artistIds.Contains(r.ArtistId.Value))
            .GroupBy(r => r.ArtistId!.Value)
            .Select(g => new { ArtistId = g.Key, Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.ArtistId, x => (x.Avg, x.Count), ct);

        // 4. View counts — batch MGET from Redis.
        IDatabase redisDb  = redis.GetDatabase();
        RedisKey[] redisKeys = artistIds.Select(id => (RedisKey)$"portfolio:views:{id}").ToArray();
        RedisValue[] redisValues = await redisDb.StringGetAsync(redisKeys);
        Dictionary<Guid, long> viewCounts = artistIds
            .Zip(redisValues, (id, v) => (id, count: v.HasValue ? (long)v : 0L))
            .ToDictionary(x => x.id, x => x.count);

        // 5. Score artists.
        // Bayesian average: pulls low-count artists toward the global mean (3.5)
        // so one 5-star review does not outrank an artist with 50 genuine reviews.
        const double m = 5.0;   // minimum review threshold
        const double C = 3.5;   // prior mean (global average)

        static double BayesianScore(double avg, int count) =>
            (count * avg + m * C) / (count + m);

        // 6. Haversine for distance filter (in-memory; candidate set is already small).
        static double Haversine(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        // 7. Score, filter by radius (if location provided), sort.
        List<(Artist Artist, Studio Studio, double? DistanceKm, double Score)> scored =
            artists
                .Where(a => studiosById.ContainsKey(a.StudioId))
                .Select(a =>
                {
                    Studio studio = studiosById[a.StudioId];

                    double? dist = (query.Lat.HasValue && query.Lng.HasValue)
                        ? Haversine(query.Lat.Value, query.Lng.Value,
                                    studio.Latitude, studio.Longitude)
                        : (double?)null;

                    // When location provided, exclude artists outside radius.
                    if (dist.HasValue && dist.Value > query.RadiusKm)
                        return default;

                    (double avg, int count) = reviewStats.GetValueOrDefault(a.Id, (0, 0));
                    long views = viewCounts.GetValueOrDefault(a.Id, 0L);

                    double score = BayesianScore(avg, count)
                                 + Math.Log10(views + 1) * 0.5;

                    return (Artist: a, Studio: studio, DistanceKm: dist, Score: score);
                })
                .Where(x => x.Artist != null)
                .OrderByDescending(x => x.Score)
                .ToList();

        // 8. Explode: take up to 3 images per artist, interleaved by artist rank.
        // Round-robin across artists so the feed doesn't cluster one artist's images together.
        // e.g. artist1-img1, artist2-img1, artist3-img1, artist1-img2, artist2-img2 ...
        int maxImagesPerArtist = 3;
        List<List<PortfolioImageResponse>> columns = scored
            .Select(x =>
            {
                (double avg, int count) = reviewStats.GetValueOrDefault(x.Artist.Id, (0, 0));
                long views = viewCounts.GetValueOrDefault(x.Artist.Id, 0L);

                return x.Artist.PortfolioImages
                    .Take(maxImagesPerArtist)
                    .Select(url => new PortfolioImageResponse(
                        url,
                        $"{x.Artist.FirstName} {x.Artist.LastName}",
                        x.Artist.Slug!,
                        x.Studio.Name,
                        x.Studio.Slug,
                        count > 0 ? Math.Round(avg, 1) : null,
                        count,
                        x.DistanceKm.HasValue ? Math.Round(x.DistanceKm.Value, 1) : null,
                        views))
                    .ToList();
            })
            .ToList();

        // Interleave: take one image from each artist column in order.
        List<PortfolioImageResponse> interleaved = [];
        int maxImages = columns.Max(c => c.Count);
        for (int i = 0; i < maxImages; i++)
        {
            foreach (List<PortfolioImageResponse> col in columns)
            {
                if (i < col.Count) interleaved.Add(col[i]);
            }
        }

        int skip = (query.Page - 1) * query.PageSize;
        return interleaved.Skip(skip).Take(query.PageSize).ToList();
    }
}
```

---

## Section D — Updated PublicEndpoints.cs

Add two new private static methods and register them in `MapPublicEndpoints`.

In `MapPublicEndpoints`, add to the group:

```csharp
group.MapPost("/artists/{slug}/view",  RecordArtistView).AllowAnonymous();
group.MapGet ("/portfolio/feed",       GetPortfolioFeed).AllowAnonymous();
```

Add the two private static methods at the bottom of `PublicEndpoints`:

```csharp
private static async Task<IResult> RecordArtistView(
    string               slug,
    IAppDbContext        db,
    IConnectionMultiplexer redis,
    CancellationToken    ct)
{
    // Fire-and-forget view counter. No MediatR needed — no domain invariants.
    // Approved: public, anonymous, write-only to Redis — not business data.
    Guid? artistId = await db.Artists
        .IgnoreQueryFilters()
        .Where(a => a.Slug == slug && a.DeletedAt == null)
        .Select(a => (Guid?)a.Id)
        .FirstOrDefaultAsync(ct);

    if (artistId is null) return Results.NotFound();

    IDatabase redisDb = redis.GetDatabase();
    await redisDb.StringIncrementAsync($"portfolio:views:{artistId}");
    return Results.NoContent();
}

private static async Task<IResult> GetPortfolioFeed(
    double?           lat,
    double?           lng,
    double            radiusKm,
    int               page,
    ISender           mediator,
    CancellationToken ct,
    int               pageSize = 24)
{
    List<PortfolioImageResponse> result = await mediator.Send(
        new GetPortfolioFeedQuery(lat, lng, radiusKm, page, pageSize), ct);
    return Results.Ok(result);
}
```

Add the missing using for `PortfolioImageResponse` and `GetPortfolioFeedQuery` if not already present.

---

## Section E — Frontend: publicApi.ts additions

Add to the `publicApi` endpoint builder. Place these after the existing `getNearbyStudios` endpoint.

### New interface

```typescript
export interface PortfolioImageResponse {
  imageUrl:      string;
  artistName:    string;
  artistSlug:    string;
  studioName:    string;
  studioSlug:    string;
  averageRating: number | null;
  reviewCount:   number;
  distanceKm:    number | null;
  viewCount:     number;
}
```

### New RTK Query endpoints

Use the `serializeQueryArgs` + `merge` pattern so "Load more" appends to the same cache slot rather than replacing it. `forceRefetch` triggers a real network request when the page number changes.

```typescript
getPortfolioFeed: builder.query<
  PortfolioImageResponse[],
  { lat?: number; lng?: number; radiusKm?: number; page: number }
>({
  query: ({ lat, lng, radiusKm = 50, page }) => {
    const params = new URLSearchParams({ radiusKm: String(radiusKm), page: String(page) });
    if (lat != null) params.set("lat", String(lat));
    if (lng != null) params.set("lng", String(lng));
    return `public/portfolio/feed?${params.toString()}`;
  },
  // Location changes or radiusKm changes should start a fresh cache entry.
  // Page changes should append to the existing entry.
  serializeQueryArgs: ({ queryArgs: { lat, lng, radiusKm } }) =>
    `portfolio-feed:${lat ?? ""}:${lng ?? ""}:${radiusKm ?? 50}`,
  merge: (currentCache, newItems) => {
    currentCache.push(...newItems);
  },
  forceRefetch: ({ currentArg, previousArg }) =>
    currentArg?.page !== previousArg?.page,
}),

recordArtistView: builder.mutation<void, string>({
  query: (slug) => ({ url: `public/artists/${slug}/view`, method: "POST" }),
}),
```

Export `useGetPortfolioFeedQuery` and `useRecordArtistViewMutation` from `publicApi.ts` (they are auto-generated by RTK Query — just ensure the endpoints are added and the api object is exported correctly).

---

## Section F — New component: PortfolioFeed.tsx

Create `frontend/src/features/public/components/PortfolioFeed.tsx`.

This is a standalone component that owns the masonry grid. `DiscoverPage` will import it.

```tsx
import { useState } from "react";
import { Link } from "react-router-dom";
import { MapPin } from "lucide-react";
import { Button }    from "@/shared/components/ui/button";
import { Skeleton }  from "@/shared/components/ui/skeleton";
import { StarRating } from "@/shared/components/ui/StarRating";
import {
  useGetPortfolioFeedQuery,
  type PortfolioImageResponse,
} from "../publicApi";

// ── Props ─────────────────────────────────────────────────────────────────────

interface PortfolioFeedProps {
  lat:      number | null;
  lng:      number | null;
  radiusKm: number;
  nearOnly: boolean; // when true, pass lat/lng to filter by distance
}

// ── Skeleton ──────────────────────────────────────────────────────────────────

// Varying heights simulate the real masonry grid while content loads.
const SKELETON_HEIGHTS = [
  "h-52", "h-72", "h-64",
  "h-80", "h-48", "h-56",
  "h-60", "h-76", "h-44",
  "h-68", "h-52", "h-80",
] as const;

function PortfolioSkeleton() {
  return (
    <div
      className="columns-2 md:columns-3 gap-3"
      aria-label="Loading portfolio"
      aria-busy="true"
    >
      {SKELETON_HEIGHTS.map((h, i) => (
        <div key={i} className={`mb-3 break-inside-avoid ${h}`}>
          <Skeleton className="w-full h-full rounded-lg" />
        </div>
      ))}
    </div>
  );
}

// ── Individual image tile ─────────────────────────────────────────────────────

function PortfolioTile({ image }: { image: PortfolioImageResponse }) {
  return (
    <Link
      to={`/a/${image.artistSlug}`}
      className="mb-3 break-inside-avoid block relative group rounded-lg overflow-hidden
                 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring
                 focus-visible:ring-offset-2"
      aria-label={`Tattoo by ${image.artistName} at ${image.studioName}`}
    >
      {/* Image */}
      <img
        src={image.imageUrl}
        alt={`Tattoo by ${image.artistName}`}
        loading="lazy"
        decoding="async"
        className="w-full object-cover transition-transform duration-300
                   group-hover:scale-[1.03]"
        onError={(e) => {
          // Hide broken images silently — don't break the grid layout
          (e.currentTarget as HTMLImageElement).style.display = "none";
        }}
      />

      {/* Distance badge — top right corner */}
      {image.distanceKm !== null && (
        <span
          className="absolute top-2 right-2
                     bg-black/60 backdrop-blur-sm
                     text-white text-[10px] font-medium
                     px-1.5 py-0.5 rounded-full
                     flex items-center gap-0.5"
        >
          <MapPin className="h-2.5 w-2.5" aria-hidden="true" />
          {image.distanceKm} km
        </span>
      )}

      {/* Hover overlay — gradient from bottom */}
      <div
        className="absolute inset-0
                   bg-gradient-to-t from-black/85 via-black/25 to-transparent
                   opacity-0 group-hover:opacity-100 group-focus-visible:opacity-100
                   transition-opacity duration-200
                   flex flex-col justify-end p-3 gap-1"
      >
        <p className="text-white font-semibold text-sm leading-snug truncate">
          {image.artistName}
        </p>
        <p className="text-white/65 text-xs truncate">{image.studioName}</p>

        {image.reviewCount > 0 && (
          <div className="flex items-center gap-1.5">
            <StarRating value={Math.round(image.averageRating ?? 0)} />
            <span className="text-white/55 text-xs">({image.reviewCount})</span>
          </div>
        )}

        <span className="text-violet-300 text-xs font-medium mt-0.5">
          View artist →
        </span>
      </div>
    </Link>
  );
}

// ── Main feed component ───────────────────────────────────────────────────────

export function PortfolioFeed({ lat, lng, radiusKm, nearOnly }: PortfolioFeedProps) {
  const [page, setPage] = useState(1);

  const feedArgs = {
    lat:      nearOnly && lat != null ? lat : undefined,
    lng:      nearOnly && lng != null ? lng : undefined,
    radiusKm: nearOnly ? radiusKm : 50,
    page,
  };

  const { data: images, isLoading, isFetching } =
    useGetPortfolioFeedQuery(feedArgs);

  if (isLoading) return <PortfolioSkeleton />;

  if (!images || images.length === 0) {
    return (
      <div className="flex flex-col items-center gap-4 py-24 text-center">
        <div className="rounded-full bg-muted/40 p-6">
          {/* Decorative SVG tattoo needle icon — inline, no import */}
          <svg
            aria-hidden="true"
            viewBox="0 0 24 24"
            className="h-9 w-9 stroke-current fill-none stroke-[1.5] text-muted-foreground/40"
          >
            <path strokeLinecap="round" strokeLinejoin="round"
              d="M15.232 5.232l3.536 3.536M9 11l6.768-6.768a2 2 0 112.828 2.828L11.828
                 13.828A2 2 0 0110 14.414l-2.828.414.414-2.828A2 2 0 019
                 10.172V11z" />
          </svg>
        </div>
        <div className="space-y-1.5">
          <p className="text-base font-semibold">No portfolio work yet</p>
          <p className="text-sm text-muted-foreground max-w-xs">
            {nearOnly
              ? "No artists with portfolio images found nearby. Try a larger radius or turn off the location filter."
              : "Be among the first artists to show your work here."}
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
    );
  }

  const hasMore = images.length >= page * 24;

  return (
    <div className="space-y-6">
      {/* Masonry grid — CSS columns, no package */}
      <div className="columns-2 md:columns-3 gap-3">
        {images.map((img, i) => (
          <PortfolioTile
            // Key must be stable per item — use slug + url, not index alone
            key={`${img.artistSlug}::${img.imageUrl}`}
            image={img}
          />
        ))}
      </div>

      {/* Load more */}
      {hasMore && (
        <div className="flex justify-center pt-2 pb-6">
          <Button
            variant="outline"
            onClick={() => setPage((p) => p + 1)}
            disabled={isFetching}
            className="min-w-[140px]"
          >
            {isFetching ? "Loading…" : "Load more"}
          </Button>
        </div>
      )}
    </div>
  );
}
```

---

## Section G — Updated DiscoverPage.tsx

Replace the entire `DiscoverPage.tsx` with the version below. This supersedes the version written in `overnight-prompt-discover-page-polish-2026-06-25.md` (which should have run before this prompt). All the polish from that prompt is preserved; this version adds the Portfolio tab as the default view and compresses the hero into a slim sticky header.

```tsx
import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Image, Locate, MapPin, Search, Users } from "lucide-react";
import { Button }         from "@/shared/components/ui/button";
import { Skeleton }       from "@/shared/components/ui/skeleton";
import { Card, CardContent } from "@/shared/components/ui/card";
import { StarRating }     from "@/shared/components/ui/StarRating";
import { PortfolioFeed }  from "./PortfolioFeed";
import {
  useGetNearbyStudiosQuery,
  type NearbyStudioResponse,
} from "../publicApi";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

// ── Constants ─────────────────────────────────────────────────────────────────

const RADII = [10, 25, 50, 100] as const;
type Radius = (typeof RADII)[number];

const DEFAULT_LAT  = 38.7169;
const DEFAULT_LNG  = -9.1395;
const DEFAULT_CITY = "Lisbon, Portugal";

type ActiveTab = "portfolio" | "studios";

// ── Types ─────────────────────────────────────────────────────────────────────

interface NominatimResult {
  lat:          string;
  lon:          string;
  display_name: string;
}

// ── Helpers ───────────────────────────────────────────────────────────────────

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

// ── StudioCard ────────────────────────────────────────────────────────────────

function StudioCard({ studio }: { studio: NearbyStudioResponse }) {
  return (
    <Link
      to={`/s/${studio.slug}`}
      className="block rounded-lg focus-visible:outline-none
                 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1"
    >
      <Card className="hover:border-border/80 hover:shadow-md hover:shadow-black/20
                       transition-all cursor-pointer h-full">
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
          <StudioMonogram name={studio.name} />
        )}
        <CardContent className="p-4 space-y-2">
          <p className="font-semibold text-sm leading-tight line-clamp-1">{studio.name}</p>

          <div className="flex items-center gap-1.5">
            {studio.reviewCount > 0 ? (
              <>
                <StarRating value={Math.round(studio.averageRating ?? 0)} />
                <span className="text-xs text-muted-foreground">({studio.reviewCount})</span>
              </>
            ) : (
              <span className="text-xs text-muted-foreground/60 italic">No reviews yet</span>
            )}
          </div>

          <div className="flex items-center gap-1 text-xs text-muted-foreground">
            <MapPin className="h-3 w-3 shrink-0" aria-hidden="true" />
            <span className="truncate">{studio.city}</span>
            <span className="ml-auto font-medium text-foreground/80 whitespace-nowrap">
              <DistanceLabel km={studio.distanceKm} />
            </span>
          </div>

          {studio.artistCount > 0 && (
            <div className="flex items-center gap-1 text-xs text-muted-foreground">
              <Users className="h-3 w-3 shrink-0" aria-hidden="true" />
              <span>{studio.artistCount} artist{studio.artistCount !== 1 ? "s" : ""}</span>
            </div>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}

// ── Studio grid skeleton ───────────────────────────────────────────────────────

function StudioSkeleton() {
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
         aria-label="Loading studios" aria-busy="true">
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

// ── Meta ──────────────────────────────────────────────────────────────────────

function DiscoverMeta() {
  useDocumentMeta({
    title:       "Discover Tattoo Art Near You — Pena e Artë",
    description: "Browse tattoo portfolios and studios near your location.",
    canonical:   "https://penaearte.com/discover",
  });
  return null;
}

// ── Main page ─────────────────────────────────────────────────────────────────

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
  const [activeTab,     setActiveTab]     = useState<ActiveTab>("portfolio");
  // nearOnly: when true, portfolio feed is filtered to the user's radius
  const [nearOnly,      setNearOnly]      = useState(false);

  const inputRef = useRef<HTMLInputElement>(null);

  // ── Reverse geocode ───────────────────────────────────────────────────────
  const reverseGeocode = useCallback(async (latitude: number, longitude: number) => {
    try {
      const res  = await fetch(
        `https://nominatim.openstreetmap.org/reverse?format=json&lat=${latitude}&lon=${longitude}`,
        { headers: { "Accept-Language": "en" } },
      );
      const data = (await res.json()) as {
        address?: { city?: string; town?: string; village?: string; country?: string };
      };
      const place   = data.address?.city ?? data.address?.town ?? data.address?.village ?? "";
      const country = data.address?.country ?? "";
      setLocationName(place && country ? `${place}, ${country}` : place || DEFAULT_CITY);
    } catch {
      setLocationName(DEFAULT_CITY);
    }
  }, []);

  // ── Geolocation on mount ──────────────────────────────────────────────────
  // Acceptable useEffect: browser API side-effect, not data fetching.
  useEffect(() => {
    if (!hasGeo) return;
    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        setLat(pos.coords.latitude);
        setLng(pos.coords.longitude);
        setIsGeoLocating(false);
        await reverseGeocode(pos.coords.latitude, pos.coords.longitude);
      },
      () => {
        setLat(DEFAULT_LAT);
        setLng(DEFAULT_LNG);
        setLocationName(DEFAULT_CITY);
        setIsGeoLocating(false);
      },
      { timeout: 8000, maximumAge: 60_000 },
    );
  }, [hasGeo, reverseGeocode]);

  // ── Re-trigger geolocation ────────────────────────────────────────────────
  function handleUseMyLocation() {
    if (!hasGeo) return;
    setIsGeoLocating(true);
    setLocationName("");
    navigator.geolocation.getCurrentPosition(
      async (pos) => {
        setLat(pos.coords.latitude);
        setLng(pos.coords.longitude);
        setIsGeoLocating(false);
        await reverseGeocode(pos.coords.latitude, pos.coords.longitude);
      },
      () => {
        setLat(DEFAULT_LAT);
        setLng(DEFAULT_LNG);
        setLocationName(DEFAULT_CITY);
        setIsGeoLocating(false);
      },
      { timeout: 8000, maximumAge: 0 },
    );
  }

  // ── Forward geocode ───────────────────────────────────────────────────────
  async function handleLocationSearch() {
    const q = searchInput.trim();
    if (!q) return;
    setIsGeocoding(true);
    setSearchError(null);
    try {
      const res     = await fetch(
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
      setLocationName(first.display_name.split(",").slice(0, 2).join(", ").trim());
      setSearchInput("");
      inputRef.current?.blur();
    } catch {
      setSearchError("Could not reach location service. Try again.");
    } finally {
      setIsGeocoding(false);
    }
  }

  // ── Studios query (only runs on Studios tab) ──────────────────────────────
  const { data: studios, isLoading: isStudiosLoading, isFetching: isStudiosFetching } =
    useGetNearbyStudiosQuery(
      { lat: lat!, lng: lng!, radiusKm },
      { skip: activeTab !== "studios" || lat === null || lng === null },
    );

  const isLoadingStudios = lat === null || isStudiosLoading || isStudiosFetching;

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="min-h-screen bg-background flex flex-col">
      <DiscoverMeta />

      {/* ── Sticky header ───────────────────────────────────────────────── */}
      <header className="sticky top-0 z-[100] border-b bg-background/95 backdrop-blur-sm">
        {/* Top row: brand + nav */}
        <div className="flex items-center justify-between px-4 py-2.5">
          <div className="flex items-center gap-2">
            <svg aria-hidden="true" viewBox="0 0 24 24"
                 className="h-5 w-5 fill-none stroke-current stroke-2">
              <path strokeLinecap="round" strokeLinejoin="round"
                d="M15.232 5.232l3.536 3.536M9 11l6.768-6.768a2 2 0 112.828 2.828
                   L11.828 13.828A2 2 0 0110 14.414l-2.828.414.414-2.828A2 2 0
                   019 10.172V11z" />
            </svg>
            <span className="font-semibold tracking-tight text-sm">Pena e Artë</span>
          </div>

          <nav className="flex items-center gap-1" aria-label="Site navigation">
            <Link to="/map"
              className="text-xs text-muted-foreground hover:text-foreground
                         transition-colors px-3 py-2 rounded-md hover:bg-muted/40">
              View on map
            </Link>
            <Link to="/login"
              className="text-xs text-muted-foreground hover:text-foreground
                         transition-colors px-3 py-2 rounded-md hover:bg-muted/40">
              Sign in
            </Link>
            <Link to="/register"
              className="text-xs font-medium px-3 py-2 rounded-md border
                         border-violet-500/60 text-violet-400
                         hover:bg-violet-500/10 hover:border-violet-400 transition-colors">
              Register studio
            </Link>
          </nav>
        </div>

        {/* Bottom row: search + tabs + location toggle */}
        <div className="flex items-center gap-2 px-4 pb-2.5">
          {/* Search input */}
          <div className="flex flex-1 items-center gap-2 max-w-sm">
            <div className="relative flex-1">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5
                                 text-muted-foreground pointer-events-none" aria-hidden="true" />
              <input
                ref={inputRef}
                type="search"
                placeholder="Search city…"
                value={searchInput}
                onChange={(e) => setSearchInput(e.target.value)}
                onKeyDown={(e) => { if (e.key === "Enter") void handleLocationSearch(); }}
                aria-label="Search for a city"
                className="w-full h-9 pl-8 pr-3 rounded-md border bg-background text-xs
                           focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-1
                           placeholder:text-muted-foreground"
              />
            </div>
            <Button
              size="sm"
              onClick={() => void handleLocationSearch()}
              disabled={isGeocoding || !searchInput.trim()}
              aria-label="Search"
              className="h-9 px-3 bg-violet-600 hover:bg-violet-700 text-white border-0"
            >
              <Search className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          </div>

          {/* Tab toggle */}
          <div
            role="tablist"
            className="flex items-center rounded-md border bg-muted/30 p-0.5 gap-0.5"
          >
            <button
              role="tab"
              aria-selected={activeTab === "portfolio"}
              onClick={() => setActiveTab("portfolio")}
              className={`px-3 py-1.5 rounded text-xs font-medium transition-colors ${
                activeTab === "portfolio"
                  ? "bg-background shadow-sm text-foreground"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              Portfolio
            </button>
            <button
              role="tab"
              aria-selected={activeTab === "studios"}
              onClick={() => setActiveTab("studios")}
              className={`px-3 py-1.5 rounded text-xs font-medium transition-colors ${
                activeTab === "studios"
                  ? "bg-background shadow-sm text-foreground"
                  : "text-muted-foreground hover:text-foreground"
              }`}
            >
              Studios
            </button>
          </div>

          {/* Location state */}
          <div className="flex items-center gap-1.5 ml-auto">
            {isGeoLocating ? (
              <span className="text-xs text-muted-foreground animate-pulse flex items-center gap-1">
                <Locate className="h-3 w-3" aria-hidden="true" />
                Detecting…
              </span>
            ) : locationName ? (
              <>
                {/* "Near me" toggle — shown only on portfolio tab */}
                {activeTab === "portfolio" && lat !== null && (
                  <button
                    type="button"
                    onClick={() => setNearOnly((v) => !v)}
                    className={`flex items-center gap-1 text-xs px-2 py-1 rounded-full border
                                transition-colors ${
                                  nearOnly
                                    ? "bg-violet-600/20 border-violet-500/60 text-violet-300"
                                    : "border-border text-muted-foreground hover:text-foreground"
                                }`}
                  >
                    <MapPin className="h-3 w-3" aria-hidden="true" />
                    Near me
                  </button>
                )}
                <span className="text-xs text-muted-foreground hidden sm:block truncate max-w-[140px]">
                  {locationName}
                </span>
              </>
            ) : hasGeo ? (
              <button
                type="button"
                onClick={handleUseMyLocation}
                className="flex items-center gap-1 text-xs text-violet-400
                           hover:text-violet-300 transition-colors"
              >
                <Locate className="h-3 w-3" aria-hidden="true" />
                Use my location
              </button>
            ) : null}
          </div>
        </div>

        {searchError && (
          <p className="px-4 pb-2 text-xs text-destructive" role="alert">{searchError}</p>
        )}
      </header>

      {/* ── Content area ─────────────────────────────────────────────────── */}
      <main className="flex-1 px-4 py-5 max-w-6xl mx-auto w-full">

        {/* Portfolio tab */}
        {activeTab === "portfolio" && (
          <PortfolioFeed
            lat={lat}
            lng={lng}
            radiusKm={radiusKm}
            nearOnly={nearOnly}
          />
        )}

        {/* Studios tab */}
        {activeTab === "studios" && (
          <div className="space-y-4">
            {/* Radius selector */}
            <div className="flex items-center gap-2">
              <span className="text-xs text-muted-foreground">Within</span>
              <select
                value={radiusKm}
                onChange={(e) => setRadiusKm(parseInt(e.target.value, 10) as Radius)}
                aria-label="Search radius"
                className="h-9 rounded-md border bg-background px-3 text-sm text-foreground
                           focus:outline-none focus:ring-2 focus:ring-ring"
              >
                {RADII.map((r) => (
                  <option key={r} value={r}>{r} km</option>
                ))}
              </select>
            </div>

            {isLoadingStudios ? (
              <StudioSkeleton />
            ) : !studios || studios.length === 0 ? (
              <div className="flex flex-col items-center gap-4 py-20 text-center">
                <MapPin className="h-8 w-8 text-muted-foreground/40" aria-hidden="true" />
                <div className="space-y-1">
                  <p className="text-base font-semibold">No studios found nearby</p>
                  <p className="text-sm text-muted-foreground">Try a larger radius.</p>
                </div>
                <Link to="/register"
                  className="text-sm text-violet-400 hover:text-violet-300 underline underline-offset-4">
                  Register your studio →
                </Link>
              </div>
            ) : (
              <>
                <p
                  className="text-sm font-medium"
                  aria-live="polite"
                  aria-atomic="true"
                >
                  {studios.length} studio{studios.length !== 1 ? "s" : ""} near{" "}
                  <span className="text-foreground/70">{locationName}</span>{" "}
                  within {radiusKm} km
                </p>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                  {studios.map((s) => (
                    <StudioCard key={s.studioId} studio={s} />
                  ))}
                </div>
              </>
            )}
          </div>
        )}
      </main>

      {/* ── Footer ───────────────────────────────────────────────────────── */}
      <footer className="py-4 text-center text-xs text-foreground/50 border-t">
        <a href="https://penaearte.com" target="_blank" rel="noopener noreferrer"
           className="hover:text-foreground/80 hover:underline transition-colors">
          Powered by Pena e Artë
        </a>
      </footer>
    </div>
  );
}
```

---

## Section H — ArtistPortfolioPage.tsx: add view tracking

In `ArtistPortfolioPage.tsx`, import `useRecordArtistViewMutation` and fire it when the page loads.

Add the import at the top alongside the existing `publicApi` import:

```typescript
import { useGetPublicArtistQuery, useRecordArtistViewMutation } from "../publicApi";
```

Inside `ArtistPortfolioPage`, add the mutation hook and a `useEffect` that fires once when the slug is known. Place this immediately after the existing `useGetPublicArtistQuery` call:

```typescript
const [recordView] = useRecordArtistViewMutation();

// Track portfolio view for discovery feed ranking.
// Acceptable useEffect: browser-side-effect (fire-and-forget), not data fetching.
useEffect(() => {
  if (!slug) return;
  // Best-effort — ignore failures silently; view count is non-critical
  void recordView(slug);
}, [slug, recordView]);
```

No other changes to `ArtistPortfolioPage.tsx`.

---

## Section I — Architecture docs

In `docs/claude/architecture.md`, under **Feature Module Map**, add or update the Portfolio Feed entry:

```
Portfolio Feed            GET /api/v1/public/portfolio/feed
                          Handler: GetPortfolioFeedQuery (Application/Public/Queries/)
                          No auth. Approved AllowAnonymous exception — public discovery.
                          Scoring: Bayesian avg rating + log10(views+1)*0.5
                          View counts: Redis, key = portfolio:views:{artistId}
                          Max 3 images per artist; interleaved by artist rank.
                          Pagination: page/pageSize; RTK Query merge for infinite scroll.
                          Frontend: PortfolioFeed.tsx + DiscoverPage.tsx (Portfolio tab, default)

Artist View Counter       POST /api/v1/public/artists/{slug}/view
                          No auth. Fires from ArtistPortfolioPage on mount.
                          Redis INCR only — no DB write, no MediatR.
                          Approved: non-domain, non-PII write.
```

---

## Section J — Tests

### J1. Backend: PortfolioFeed handler unit test

Create `tests/Pena_e_Arte.UnitTests/Public/GetPortfolioFeedHandlerTests.cs`:

The test should:
- Mock `IAppDbContext` with in-memory artist and studio data (two artists, each with 2 portfolio images)
- Mock `IConnectionMultiplexer` / `IDatabase` to return fixed view counts
- Assert the handler returns images, respects max 3 per artist, and orders by score
- Assert that an artist with no studio (studio not active) is excluded
- Assert that pagination skips correctly

### J2. Frontend: PortfolioFeed component tests

Create `frontend/src/features/public/__tests__/PortfolioFeed.test.tsx`:

```tsx
import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter }    from "react-router-dom";
import { Provider }        from "react-redux";
import { configureStore }  from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer }     from "msw/node";
import { publicApi }       from "@/features/public/publicApi";
import { PortfolioFeed }   from "@/features/public/components/PortfolioFeed";

function makeStore() {
  return configureStore({
    reducer: { [publicApi.reducerPath]: publicApi.reducer },
    middleware: (gd) => gd().concat(publicApi.middleware),
  });
}

function renderFeed(props = {}) {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <PortfolioFeed lat={null} lng={null} radiusKm={50} nearOnly={false} {...props} />
      </MemoryRouter>
    </Provider>,
  );
}

const IMAGES = [
  {
    imageUrl:      "https://example.com/tattoo1.jpg",
    artistName:    "Ana Lima",
    artistSlug:    "ana-lima",
    studioName:    "Black Ink Lisbon",
    studioSlug:    "black-ink-lisbon",
    averageRating: 4.8,
    reviewCount:   22,
    distanceKm:    null,
    viewCount:     150,
  },
  {
    imageUrl:      "https://example.com/tattoo2.jpg",
    artistName:    "João Costa",
    artistSlug:    "joao-costa",
    studioName:    "Dark Arts Porto",
    studioSlug:    "dark-arts-porto",
    averageRating: null,
    reviewCount:   0,
    distanceKm:    3.2,
    viewCount:     40,
  },
];

const server = setupServer(
  http.get("http://localhost/api/v1/public/portfolio/feed", () =>
    HttpResponse.json(IMAGES),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "warn" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

describe("PortfolioFeed", () => {
  it("renders artist name in each tile overlay area (accessible via aria-label)", async () => {
    renderFeed();
    expect(await screen.findByLabelText(/Tattoo by Ana Lima/i)).toBeInTheDocument();
    expect(await screen.findByLabelText(/Tattoo by João Costa/i)).toBeInTheDocument();
  });

  it("shows distance badge when distanceKm is not null", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by João Costa/i);
    expect(screen.getByText(/3\.2/)).toBeInTheDocument();
  });

  it("does NOT show distance badge when distanceKm is null", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    // Ana Lima has distanceKm: null — no km badge in her tile
    const anaLink = screen.getByLabelText(/Tattoo by Ana Lima/i);
    expect(anaLink.querySelector("[aria-hidden]")?.textContent).not.toMatch(/km/);
  });

  it("shows rating and review count when reviewCount > 0", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    expect(screen.getByText("(22)")).toBeInTheDocument();
  });

  it("does NOT show rating row when reviewCount is 0", async () => {
    renderFeed();
    await screen.findByLabelText(/Tattoo by João Costa/i);
    expect(screen.queryByText("(0)")).not.toBeInTheDocument();
  });

  it("shows empty state with register link when feed is empty", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", () =>
        HttpResponse.json([]),
      ),
    );
    renderFeed();
    expect(await screen.findByText(/no portfolio work yet/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /register your studio/i })).toBeInTheDocument();
  });

  it("shows empty state for nearOnly with specific message", async () => {
    server.use(
      http.get("http://localhost/api/v1/public/portfolio/feed", () =>
        HttpResponse.json([]),
      ),
    );
    renderFeed({ nearOnly: true, lat: 38.7, lng: -9.1 });
    expect(await screen.findByText(/no artists with portfolio images found nearby/i))
      .toBeInTheDocument();
  });

  it("each tile links to the artist profile page", async () => {
    renderFeed();
    const tile = await screen.findByLabelText(/Tattoo by Ana Lima/i);
    expect(tile).toHaveAttribute("href", "/a/ana-lima");
  });

  it("'Load more' button is hidden when fewer than 24 images returned", async () => {
    renderFeed(); // only 2 images
    await screen.findByLabelText(/Tattoo by Ana Lima/i);
    expect(screen.queryByRole("button", { name: /load more/i })).not.toBeInTheDocument();
  });
});
```

### J3. DiscoverPage: tab switching test

Add to the existing `DiscoverPage.test.tsx` (created in the previous prompt):

```tsx
it("defaults to Portfolio tab", () => {
  renderPage();
  const portfolioTab = screen.getByRole("tab", { name: /portfolio/i });
  expect(portfolioTab).toHaveAttribute("aria-selected", "true");
});

it("switches to Studios tab on click", async () => {
  const user = userEvent.setup();
  renderPage();
  const studiosTab = screen.getByRole("tab", { name: /studios/i });
  await user.click(studiosTab);
  expect(studiosTab).toHaveAttribute("aria-selected", "true");
});
```

---

## Section K — Build and verify

```bash
cd "Pena e Arte"
dotnet build
```

Verify `GetPortfolioFeedQuery`, `PortfolioImageResponse`, and the two new `PublicEndpoints` methods all compile without errors. No migration needed.

```bash
cd frontend
pnpm build
pnpm test
```

All new tests must pass. All pre-existing tests must still pass. Zero TypeScript `any`. Zero `pnpm lint` errors.

---

## Done checklist

- [ ] `PortfolioImageResponse.cs` created
- [ ] `GetPortfolioFeedQuery.cs` created — scoring, distance filter, interleaving, pagination
- [ ] `PublicEndpoints.cs` — `RecordArtistView` and `GetPortfolioFeed` methods added and registered
- [ ] `publicApi.ts` — `PortfolioImageResponse`, `getPortfolioFeed` (with `merge`), `recordArtistView` added
- [ ] `PortfolioFeed.tsx` created — masonry grid, hover overlays, distance badges, load more
- [ ] `DiscoverPage.tsx` replaced — slim sticky header, Portfolio/Studios tab toggle, Portfolio as default
- [ ] `PortfolioFeed` integrated into `DiscoverPage` via the Portfolio tab
- [ ] `nearOnly` toggle shows in header after geo resolves (Portfolio tab only)
- [ ] `ArtistPortfolioPage.tsx` — `useRecordArtistViewMutation` + `useEffect` added
- [ ] `GetPortfolioFeedHandlerTests.cs` — backend unit test written
- [ ] `PortfolioFeed.test.tsx` — 8 tests, all passing
- [ ] `DiscoverPage.test.tsx` — 2 tab-switching tests added, all 15 tests pass
- [ ] `dotnet build` clean
- [ ] `pnpm build` clean
- [ ] `pnpm test` clean
