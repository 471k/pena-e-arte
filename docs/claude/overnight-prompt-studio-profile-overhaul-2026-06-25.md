# Overnight Prompt — Studio Public Profile Page Overhaul
**Date:** 2026-06-25
**Depends on:** `overnight-prompt-artist-profile-overhaul-2026-06-25.md` must run first
  (adds `Artist.ProfileImageUrl` via migration).
**No new packages.**

---

## Goal

Fix every critical, high, and accessibility issue in the UI/UX audit of `/s/{slug}` (the public
studio profile page). Currently it is a `max-w-2xl` mobile column with a ghost CTA button, no
rating aggregate, no gallery, duplicate artist entries, a double sign-in prompt, an all-caps
form label, and no affordance on artist cards. After this prompt it should read like a professional
booking page — desktop-responsive, visually complete, and trust-building from the first scroll.

---

## Step 0 — Read these files first

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/architecture.md
Pena_e_Arte.Domain/Entities/Studio.cs
Pena_e_Arte.Domain/Entities/Artist.cs
Pena_e_Arte.Contracts/Responses/Public/PublicStudioResponse.cs
Pena_e_Arte.Contracts/Responses/Public/PublicArtistSummary.cs
Pena_e_Arte.Application/Public/Queries/GetPublicStudioQuery.cs
Pena_e_Arte.API/Endpoints/PublicEndpoints.cs
frontend/src/features/public/components/StudioPortfolioPage.tsx
frontend/src/features/public/components/ReviewSection.tsx
frontend/src/features/public/publicApi.ts
frontend/src/features/public/__tests__/StudioPortfolioPage.test.tsx
frontend/src/shared/components/ui/StarRating.tsx
```

---

## Section A — Backend: add contact fields to Studio entity

### A1. `Pena_e_Arte.Domain/Entities/Studio.cs`

Add two nullable properties after `CoverImageUrl`:

```csharp
public string? PhoneNumber      { get; set; }
public string? InstagramHandle  { get; set; }
```

### A2. Migration

```bash
dotnet ef migrations add AddStudioContactInfo \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
dotnet ef database update \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

No seed data — both fields nullable, default null for existing studios.

---

## Section B — Backend: expand PublicArtistSummary contract

Replace `Pena_e_Arte.Contracts/Responses/Public/PublicArtistSummary.cs`:

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicArtistSummary(
    Guid    ArtistId,
    string  Name,
    string  Slug,
    string? Bio,
    string? ProfileImageUrl,   // circular avatar; null → show monogram
    string? Specializations,   // comma-separated e.g. "Blackwork, Mandala"
    double? AverageRating,     // null = no reviews yet
    int     ReviewCount);
```

---

## Section C — Backend: expand PublicStudioResponse contract

Replace `Pena_e_Arte.Contracts/Responses/Public/PublicStudioResponse.cs`:

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicStudioResponse(
    Guid                              StudioId,
    string                            Name,
    string                            Slug,
    string                            City,
    string?                           Description,
    string?                           CoverImageUrl,
    string?                           PhoneNumber,       // NEW
    string?                           InstagramHandle,   // NEW
    double?                           AverageRating,     // NEW — aggregate of studio reviews
    int                               ReviewCount,       // NEW
    IReadOnlyList<string>             GalleryImages,     // NEW — up to 9 images from artists
    IReadOnlyList<PublicArtistSummary> Artists,
    bool                              ShowBookingCta);
```

---

## Section D — Backend: update GetPublicStudioQuery

Replace `Pena_e_Arte.Application/Public/Queries/GetPublicStudioQuery.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicStudioQuery(string Slug) : IRequest<PublicStudioResponse?>;

public class GetPublicStudioHandler(IAppDbContext db)
    : IRequestHandler<GetPublicStudioQuery, PublicStudioResponse?>
{
    public async Task<PublicStudioResponse?> Handle(
        GetPublicStudioQuery query, CancellationToken ct)
    {
        // Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions.
        Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.Slug && s.IsActive, ct);

        if (studio is null) return null;

        // Approved: public portfolio query.
        // DistinctBy guards against data-layer duplicates (e.g., two records with the same Id).
        List<Artist> artists = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == studio.Id && a.DeletedAt == null && a.Slug != null)
            .ToListAsync(ct);

        // De-duplicate by primary key — guards against bad data producing duplicate cards.
        artists = artists.DistinctBy(a => a.Id).ToList();

        List<Guid> artistIds = artists.Select(a => a.Id).ToList();

        // Per-artist review aggregates.
        // Approved: public portfolio query.
        Dictionary<Guid, (double Avg, int Count)> artistReviewStats = await db.Reviews
            .Where(r => r.ArtistId != null && artistIds.Contains(r.ArtistId.Value))
            .GroupBy(r => r.ArtistId!.Value)
            .Select(g => new { ArtistId = g.Key, Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToDictionaryAsync(x => x.ArtistId, x => (x.Avg, x.Count), ct);

        // Studio-level review aggregate.
        // Approved: public portfolio query.
        var studioReviewStats = await db.Reviews
            .Where(r => r.StudioId == studio.Id)
            .GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        // Gallery: up to 3 images per artist, max 9 total, ordered by artist.
        // Round-robin so no single artist dominates the grid.
        List<List<string>> imagesByArtist = artists
            .Select(a => a.PortfolioImages.Take(3).ToList())
            .Where(imgs => imgs.Count > 0)
            .ToList();

        List<string> galleryImages = [];
        int maxSlots = 9;
        for (int i = 0; i < 3 && galleryImages.Count < maxSlots; i++)
        {
            foreach (List<string> imgs in imagesByArtist)
            {
                if (i < imgs.Count && galleryImages.Count < maxSlots)
                    galleryImages.Add(imgs[i]);
            }
        }

        // Build artist summaries.
        IReadOnlyList<PublicArtistSummary> artistSummaries = artists
            .Select(a =>
            {
                (double avg, int count) = artistReviewStats.GetValueOrDefault(a.Id, (0, 0));
                return new PublicArtistSummary(
                    a.Id,
                    $"{a.FirstName} {a.LastName}",
                    a.Slug!,
                    a.Bio,
                    a.ProfileImageUrl,
                    a.Specializations,
                    count > 0 ? Math.Round(avg, 1) : null,
                    count);
            })
            .ToList();

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
            ShowBookingCta: true);
    }
}
```

---

## Section E — Frontend: publicApi.ts

Update both the `PublicArtistSummary` and `PublicStudioResponse` interfaces.

```typescript
export interface PublicArtistSummary {
  artistId:        string;
  name:            string;
  slug:            string;
  bio:             string | null;
  profileImageUrl: string | null;   // circular avatar
  specializations: string | null;   // comma-separated
  averageRating:   number | null;
  reviewCount:     number;
}

export interface PublicStudioResponse {
  studioId:        string;
  name:            string;
  slug:            string;
  city:            string;
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

## Section F — Frontend: ReviewSection.tsx — three targeted fixes

### F1. Remove all-caps from the form label

Find the `<p>` with `uppercase` in `ReviewForm` and remove that class. Also upgrade it from `text-xs` to `text-sm font-medium`:

```tsx
// Old:
<p className="text-xs text-muted-foreground font-medium tracking-wide uppercase">
  Write a review
</p>

// New:
<label htmlFor="review-body" className="text-sm font-medium">
  Write a review
</label>
```

Add `id="review-body"` to the `<textarea>` element so the label association works:

```tsx
<textarea
  id="review-body"
  aria-label="Write a review"
  ...
/>
```

### F2. Gate the form for unauthenticated users — eliminate double sign-in prompt

Replace the existing unauthenticated state (the inline `{!token && <p>Sign in…</p>}` block and the
"Sign in to review" button label) with a clean gate pattern. In `ReviewForm`, add an early return
before the form when `!token`:

```tsx
// Add to imports:
import { Link } from "react-router-dom";

// Add before the main form JSX — place AFTER the success check, BEFORE the form return:
if (!token) {
  const returnUrl = target === "studio" ? `/s/${slug}` : `/a/${slug}`;
  return (
    <div
      className="rounded-lg border bg-muted/20 px-5 py-6
                 flex flex-col items-center gap-3 text-center"
    >
      <p className="text-sm text-muted-foreground">
        Sign in to share your experience with this {target}.
      </p>
      <Button size="sm" asChild>
        <Link to={`/login?redirect=${encodeURIComponent(returnUrl)}`}>
          Sign in to leave a review
        </Link>
      </Button>
    </div>
  );
}
```

Remove the existing `{!token && <p>...</p>}` block inside the main form body entirely. The button
label reverts to always showing "Submit review" (since unauthenticated users never reach the form).

### F3. StarRating accessibility — interactive mode

In `frontend/src/shared/components/ui/StarRating.tsx`, the interactive star buttons need proper
accessible labels. Read the file in full, then for the interactive mode:
- Wrap the stars in a `<div role="radiogroup" aria-label="Rating">` (or `aria-labelledby` pointing to
  the ReviewForm label)
- Each star button should have `aria-label="Rate N star out of 5"` and `aria-pressed={value >= n}`
- Add a visually hidden live region that announces the current selection:
  ```tsx
  <span className="sr-only" aria-live="polite">
    {value === 0 ? "No rating selected" : `${value} star${value !== 1 ? "s" : ""} selected`}
  </span>
  ```

Preserve all existing read-only behaviour. Only the interactive path changes.

---

## Section G — Frontend: StudioPortfolioPage.tsx — full replacement

Replace the entire file. All audit issues addressed:

- `max-w-6xl` full-width layout
- Two-column desktop: main left + sticky right sidebar (CTA, contact, back nav)
- Hero cover image expanded to `h-72` with gradient overlay showing studio name
- Rating aggregate row under studio name
- Enriched `ArtistCard` with avatar, specializations, rating, `ChevronRight` affordance
- Gallery masonry (`columns-2 sm:columns-3`) with lightbox using shadcn `Dialog`
- "Book an Appointment" — filled violet button (not ghost)
- Phone number link and Instagram link in sidebar
- Back navigation to `/discover`
- All section headings at `text-lg font-semibold`
- All decorative icons `aria-hidden="true"`

```tsx
import { useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  ChevronLeft,
  ChevronRight,
  Images,
  Instagram,
  MapPin,
  Phone,
  Users,
  X,
} from "lucide-react";
import { Button }           from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton }          from "@/shared/components/ui/skeleton";
import {
  Dialog,
  DialogContent,
} from "@/shared/components/ui/dialog";
import { StarRating }  from "@/shared/components/ui/StarRating";
import { useAppSelector }          from "@/app/hooks";
import { useGetPublicStudioQuery } from "../publicApi";
import type { PublicArtistSummary } from "../publicApi";
import { useDocumentMeta }          from "@/shared/utils/useDocumentMeta";
import { ReviewSection }            from "./ReviewSection";

// ── Document meta ──────────────────────────────────────────────────────────────

function StudioMeta({
  name, slug, description, coverImageUrl,
}: {
  name: string; slug: string; description: string | null; coverImageUrl: string | null;
}) {
  useDocumentMeta({
    title:       `${name} — Book a Tattoo on Pena e Artë`,
    description: description ?? `Book your next tattoo at ${name}.`,
    ogImage:     coverImageUrl ?? undefined,
    canonical:   `https://penaearte.com/s/${slug}`,
  });
  return null;
}

// ── Artist mini-avatar ─────────────────────────────────────────────────────────

function ArtistAvatar({ name, profileImageUrl }: { name: string; profileImageUrl: string | null }) {
  if (profileImageUrl) {
    return (
      <img
        src={profileImageUrl}
        alt={`Profile photo of ${name}`}
        className="h-10 w-10 rounded-full object-cover shrink-0"
      />
    );
  }
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");
  return (
    <div
      className="h-10 w-10 rounded-full bg-gradient-to-br from-zinc-700 to-zinc-800
                 shrink-0 flex items-center justify-center"
      aria-hidden="true"
    >
      <span className="text-xs font-bold text-white/25 select-none">{initials}</span>
    </div>
  );
}

// ── Artist card ───────────────────────────────────────────────────────────────

function ArtistCard({ artist }: { artist: PublicArtistSummary }) {
  const primarySpec = artist.specializations?.split(",")[0]?.trim() ?? null;

  return (
    <Link
      to={`/a/${artist.slug}`}
      aria-label={`View ${artist.name}'s portfolio`}
      className="block focus-visible:outline-none
                 focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-1
                 rounded-lg"
    >
      <Card
        className="hover:border-border/80 hover:shadow-sm hover:shadow-black/20
                   transition-all cursor-pointer group h-full"
      >
        <CardContent className="p-4 space-y-2.5">
          {/* Top row: avatar + name + chevron */}
          <div className="flex items-center gap-3">
            <ArtistAvatar
              name={artist.name}
              profileImageUrl={artist.profileImageUrl}
            />
            <div className="min-w-0 flex-1">
              <p className="font-semibold text-sm leading-tight truncate">
                {artist.name}
              </p>
              {/* Primary specialty under name */}
              {primarySpec && (
                <p className="text-xs text-muted-foreground truncate mt-0.5">
                  {primarySpec}
                </p>
              )}
            </div>
            {/* Navigation affordance — [AUDIT FIX: artist cards were not obviously clickable] */}
            <ChevronRight
              className="h-4 w-4 text-muted-foreground/40 shrink-0
                         group-hover:text-foreground/70 transition-colors"
              aria-hidden="true"
            />
          </div>

          {/* Rating row */}
          {artist.reviewCount > 0 && (
            <div className="flex items-center gap-1.5">
              <StarRating value={Math.round(artist.averageRating ?? 0)} />
              <span className="text-xs text-muted-foreground">
                ({artist.reviewCount})
              </span>
            </div>
          )}

          {/* Bio fallback when no specialty */}
          {!primarySpec && artist.bio && (
            <p className="text-xs text-muted-foreground line-clamp-2">{artist.bio}</p>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}

// ── Gallery lightbox ──────────────────────────────────────────────────────────

function GalleryLightbox({
  imageUrl, studioName, onClose,
}: {
  imageUrl:   string | null;
  studioName: string;
  onClose:    () => void;
}) {
  return (
    <Dialog open={!!imageUrl} onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent
        className="max-w-5xl w-full p-0 bg-black border-0 overflow-hidden"
        aria-label={`Portfolio image from ${studioName}`}
      >
        {imageUrl && (
          <div className="relative">
            <img
              src={imageUrl}
              alt={`Tattoo portfolio by ${studioName}`}
              className="w-full h-auto max-h-[90vh] object-contain"
            />
            <button
              type="button"
              onClick={onClose}
              aria-label="Close image"
              className="absolute top-3 right-3 rounded-full bg-black/60 backdrop-blur-sm
                         p-1.5 text-white hover:bg-black/80 transition-colors"
            >
              <X className="h-4 w-4" aria-hidden="true" />
            </button>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

// ── Loading skeleton ──────────────────────────────────────────────────────────

function StudioPageSkeleton() {
  return (
    <div className="min-h-screen bg-background" aria-label="Loading studio page" aria-busy="true">
      {/* Hero skeleton */}
      <Skeleton className="h-72 w-full rounded-none" />

      <div className="max-w-6xl mx-auto px-4 py-8">
        <div className="grid grid-cols-1 lg:grid-cols-[1fr_300px] gap-8 lg:gap-12 items-start">
          {/* Left col */}
          <div className="space-y-6">
            <div className="space-y-3">
              <Skeleton className="h-8 w-56" />
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-4 w-20" />
              <Skeleton className="h-16 w-full" />
            </div>
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
              {Array.from({ length: 4 }).map((_, i) => (
                <Skeleton key={i} className="h-20 w-full rounded-lg" />
              ))}
            </div>
          </div>
          {/* Right col */}
          <div className="space-y-3">
            <Skeleton className="h-11 w-full rounded-md" />
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-4 w-32" />
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Main page ──────────────────────────────────────────────────────────────────

export function StudioPortfolioPage() {
  const { slug = "" }  = useParams<{ slug: string }>();
  const token          = useAppSelector((s) => s.auth.token);
  const [lightboxUrl,  setLightboxUrl] = useState<string | null>(null);

  const { data: studio, isLoading, isError } =
    useGetPublicStudioQuery(slug, { skip: !slug });

  // ── Loading ──────────────────────────────────────────────────────────────
  if (isLoading) return <StudioPageSkeleton />;

  // ── Error ────────────────────────────────────────────────────────────────
  if (isError || !studio) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen gap-4">
        <p className="text-muted-foreground">Studio not found.</p>
        <Button variant="outline" asChild>
          <Link to="/discover">Browse studios</Link>
        </Button>
      </div>
    );
  }

  const bookUrl = `/book?studio=${studio.slug}`;
  const ctaUrl  = token ? bookUrl : `/login?redirect=${encodeURIComponent(bookUrl)}`;

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="min-h-screen bg-background flex flex-col">
      <StudioMeta
        name={studio.name}
        slug={studio.slug}
        description={studio.description}
        coverImageUrl={studio.coverImageUrl}
      />

      <GalleryLightbox
        imageUrl={lightboxUrl}
        studioName={studio.name}
        onClose={() => setLightboxUrl(null)}
      />

      {/* ── Hero ──────────────────────────────────────────────────────────── */}
      {/* [AUDIT FIX: cover image expanded from h-48 to h-72; name overlay] */}
      <div className="relative h-72 bg-zinc-900 overflow-hidden">
        {studio.coverImageUrl ? (
          <img
            src={studio.coverImageUrl}
            alt={`${studio.name} cover`}
            className="w-full h-full object-cover"
          />
        ) : (
          // Cover placeholder — gradient with studio initials
          <div
            className="w-full h-full bg-gradient-to-br from-zinc-800 to-zinc-900
                       flex items-center justify-center"
            aria-hidden="true"
          >
            <span className="text-6xl font-bold text-white/10 select-none">
              {studio.name.split(/\s+/).map((w) => w[0]).join("").slice(0, 2).toUpperCase()}
            </span>
          </div>
        )}
        {/* Bottom gradient overlay for name legibility */}
        <div
          className="absolute inset-0 bg-gradient-to-t from-black/70 via-black/20 to-transparent
                     flex flex-col justify-end px-6 py-6"
        >
          <h1 className="text-3xl font-bold text-white tracking-tight drop-shadow-lg">
            {studio.name}
          </h1>
        </div>
      </div>

      {/* ── Content ───────────────────────────────────────────────────────── */}
      <div className="flex-1 max-w-6xl mx-auto w-full px-4 py-8">
        {/* Back nav — [AUDIT FIX: breadcrumb to discovery] */}
        <Link
          to="/discover"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground
                     hover:text-foreground transition-colors mb-6 block
                     py-2 -my-2 min-h-[44px]"
          aria-label="Back to studio discovery"
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
          Browse studios
        </Link>

        <div className="grid grid-cols-1 lg:grid-cols-[1fr_300px] gap-8 lg:gap-12 items-start">

          {/* ── LEFT: main content ────────────────────────────────────────── */}
          <div className="space-y-10">

            {/* Studio info block */}
            <div className="space-y-3">
              {/* Location */}
              <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
                <MapPin className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                <span>{studio.city}</span>
              </div>

              {/* Rating aggregate — [AUDIT FIX: missing on previous version] */}
              {studio.reviewCount > 0 && (
                <div className="flex items-center gap-2">
                  <StarRating value={Math.round(studio.averageRating ?? 0)} />
                  <span className="text-sm text-muted-foreground">
                    {studio.averageRating?.toFixed(1)} · {studio.reviewCount}{" "}
                    review{studio.reviewCount !== 1 ? "s" : ""}
                  </span>
                </div>
              )}

              {/* Description */}
              {studio.description && (
                <p className="text-sm text-muted-foreground/90 leading-relaxed">
                  {studio.description}
                </p>
              )}
            </div>

            {/* Artist grid — [AUDIT FIX: enriched cards with avatar + spec + rating + chevron] */}
            {studio.artists.length > 0 && (
              <section aria-labelledby="artists-heading">
                <div className="flex items-center gap-2.5 mb-4">
                  <h2
                    id="artists-heading"
                    className="text-lg font-semibold flex items-center gap-2"
                  >
                    <Users className="h-5 w-5 text-muted-foreground/70" aria-hidden="true" />
                    Artists
                  </h2>
                  <span className="text-sm text-muted-foreground">
                    ({studio.artists.length})
                  </span>
                </div>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  {studio.artists.map((a) => (
                    <ArtistCard key={a.artistId} artist={a} />
                  ))}
                </div>
              </section>
            )}

            {/* Gallery — [AUDIT FIX: Critical missing element — portfolio images] */}
            {studio.galleryImages.length > 0 && (
              <section aria-labelledby="gallery-heading">
                <div className="flex items-center gap-2.5 mb-4">
                  <h2
                    id="gallery-heading"
                    className="text-lg font-semibold flex items-center gap-2"
                  >
                    <Images className="h-5 w-5 text-muted-foreground/70" aria-hidden="true" />
                    Portfolio
                  </h2>
                  <span className="text-sm text-muted-foreground">
                    ({studio.galleryImages.length})
                  </span>
                </div>
                <div className="columns-2 sm:columns-3 gap-3">
                  {studio.galleryImages.map((url, i) => (
                    <button
                      key={url}
                      type="button"
                      onClick={() => setLightboxUrl(url)}
                      className="mb-3 break-inside-avoid block w-full group relative
                                 overflow-hidden rounded-lg cursor-zoom-in
                                 focus-visible:outline-none focus-visible:ring-2
                                 focus-visible:ring-ring focus-visible:ring-offset-1"
                      aria-label={`View portfolio image ${i + 1} of ${studio.galleryImages.length}`}
                    >
                      <img
                        src={url}
                        alt={`Tattoo portfolio work at ${studio.name} — image ${i + 1}`}
                        loading={i < 6 ? "eager" : "lazy"}
                        decoding="async"
                        className="w-full object-cover transition-transform duration-300
                                   group-hover:scale-[1.03]"
                      />
                      <div
                        className="absolute inset-0 bg-black/0 group-hover:bg-black/20
                                   transition-colors duration-200"
                      />
                    </button>
                  ))}
                </div>
              </section>
            )}

            {/* Reviews */}
            <ReviewSection slug={studio.slug} target="studio" token={token} />
          </div>

          {/* ── RIGHT: sticky sidebar ─────────────────────────────────────── */}
          <aside className="lg:sticky lg:top-6 space-y-4">

            {/* Booking CTA card */}
            <div className="rounded-xl border bg-muted/10 p-5 space-y-4">
              {/* CTA — [AUDIT FIX: ghost button → filled violet; "Book here" → "Book an Appointment"] */}
              {studio.showBookingCta && (
                <Button
                  className="w-full bg-violet-600 hover:bg-violet-700
                             text-white border-0 min-h-[44px] text-sm font-semibold"
                  asChild
                >
                  <Link to={ctaUrl}>Book an Appointment</Link>
                </Button>
              )}

              {/* Phone — [AUDIT FIX: missing contact info] */}
              {studio.phoneNumber && (
                <a
                  href={`tel:${studio.phoneNumber}`}
                  className="flex items-center gap-2 text-sm text-muted-foreground
                             hover:text-foreground transition-colors min-h-[44px]"
                  aria-label={`Call ${studio.name} at ${studio.phoneNumber}`}
                >
                  <Phone className="h-4 w-4 shrink-0" aria-hidden="true" />
                  {studio.phoneNumber}
                </a>
              )}

              {/* Instagram — [AUDIT FIX: missing social link] */}
              {studio.instagramHandle && (
                <a
                  href={`https://instagram.com/${studio.instagramHandle.replace(/^@/, "")}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-2 text-sm text-muted-foreground
                             hover:text-foreground transition-colors min-h-[44px]"
                  aria-label={`${studio.name} on Instagram`}
                >
                  <Instagram className="h-4 w-4 shrink-0" aria-hidden="true" />
                  @{studio.instagramHandle.replace(/^@/, "")}
                </a>
              )}

              {/* Location */}
              <div className="flex items-center gap-2 text-sm text-muted-foreground">
                <MapPin className="h-4 w-4 shrink-0" aria-hidden="true" />
                {studio.city}
              </div>
            </div>

            {/* Microcopy under CTA */}
            <p className="text-xs text-muted-foreground/60 text-center px-1">
              Booking requests go directly to the studio.
            </p>
          </aside>
        </div>
      </div>

      {/* Footer */}
      <footer className="py-4 text-center text-xs text-foreground/50 border-t mt-auto">
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

## Section H — Audit cross-reference

| Audit item | Fix applied |
|---|---|
| **Critical #1** — Ghost "Book here" button | Filled `bg-violet-600` button, copy → "Book an Appointment" |
| **Critical #2** — No studio images / portfolio | `GalleryImages` in contract; masonry gallery with lightbox; hero expanded to `h-72` |
| **Critical #3** — Duplicate artists + test data | `DistinctBy(a => a.Id)` in handler |
| **Layout** — 540px mobile column on 1400px | `max-w-6xl` + `grid-cols-[1fr_300px]` two-column layout |
| **Layout** — no desktop sidebar | Sticky right sidebar with CTA card + contact info |
| **Layout** — empty flanks | Full `max-w-6xl` content grid fills the viewport |
| **Layout** — "No reviews yet" floats outside card | Already handled by `ReviewSection` structure (inside the section) |
| **Typography** — section headings too small | `text-lg font-semibold` on "Artists", "Portfolio", "Reviews" |
| **Typography** — "WRITE A REVIEW" all-caps | Removed `uppercase` CSS class from ReviewForm label; changed to `<label>` |
| **Typography** — "Our artists" generic copy | Changed to "Artists" |
| **Color** — ghost CTA | Filled violet button |
| **Icons** — location pin smaller than section icons | Location pin stays `h-3.5 w-3.5`; section icons changed to `h-5 w-5` |
| **Icons** — all decorative icons** | `aria-hidden="true"` on all Lucide icons that don't carry content |
| **Missing: rating aggregate** | `AverageRating` + `ReviewCount` in contract and displayed with `StarRating` |
| **Missing: studio description** | Already in contract; now displayed with better typography |
| **Missing: contact info** | `PhoneNumber` + `InstagramHandle` in Studio entity + contract + sidebar |
| **Missing: portfolio gallery** | `GalleryImages` built from artist portfolio images (round-robin, max 9) |
| **Missing: artist avatar/rating/specialty** | `PublicArtistSummary` expanded; `ArtistCard` shows all three |
| **Missing: back nav to discover** | `<Link to="/discover">Browse studios</Link>` above the content |
| **Missing: affordance on artist cards** | `ChevronRight` icon + hover shadow + `aria-label` on the Link wrapper |
| **Usability** — double sign-in prompt | Replaced with a clean gate: unauthenticated users see a single "Sign in to leave a review" card |
| **Usability** — textarea editable before realising gate | Unauthenticated gate hides the textarea entirely |
| **Usability** — "Book here" dead-end copy | Changed to "Book an Appointment" |
| **Accessibility** — textarea no `<label>` | `<label htmlFor="review-body">Write a review</label>` + `id="review-body"` on textarea |
| **Accessibility** — StarRating interactive mode | `role="radiogroup"`, `aria-label` on each star, visually hidden live region |
| **Accessibility** — back link touch target | `min-h-[44px]` on back link |
| **Footer** — floating void | `mt-auto` + no excessive padding |
| **Microcopy** — no helper text under CTA | "Booking requests go directly to the studio." |

---

## Section I — Tests

### I1. Backend: GetPublicStudioQuery tests

Update or create `tests/Pena_e_Arte.UnitTests/Public/GetPublicStudioHandlerTests.cs`:

- `DistinctBy` test: if the DB returns two artist rows with the same `Id`, only one appears in the response
- Review aggregate: studio with reviews returns `AverageRating` and `ReviewCount`; studio with no reviews returns null and 0
- Per-artist review aggregate: `PublicArtistSummary.AverageRating` and `ReviewCount` are computed correctly
- Gallery images: max 9 images; round-robin across artists; empty when no artists have portfolio images
- `PhoneNumber` and `InstagramHandle` are projected from the Studio entity

### I2. Frontend: StudioPortfolioPage.test.tsx — update + extend

The existing 10 tests must still pass with these changes:

**Tests that need updating (because button copy changed):**
```tsx
// Old tests reference "Book here" — update to "Book an Appointment"
it("renders 'Book an Appointment' CTA when showBookingCta is true", () => {
  renderPage();
  expect(screen.getByRole("link", { name: "Book an Appointment" })).toBeInTheDocument();
});

it("Book an Appointment links to /login redirect when unauthenticated", () => {
  renderPage(null);
  const link = screen.getByRole("link", { name: "Book an Appointment" });
  expect(link.getAttribute("href")).toMatch(/\/login/);
});

it("Book an Appointment links directly to /book when authenticated", () => {
  renderPage("fake-token");
  const link = screen.getByRole("link", { name: "Book an Appointment" });
  expect(link.getAttribute("href")).toMatch(/\/book/);
  expect(link.getAttribute("href")).not.toMatch(/\/login/);
});
```

**Seed data** — update `STUDIO` constant to include the new fields:
```tsx
const STUDIO: PublicStudioResponse = {
  studioId:        "studio-001",
  name:            "Ink Soul",
  slug:            "test-studio",
  city:            "Porto",
  description:     "Premier tattoo studio in Porto.",
  coverImageUrl:   "https://cdn.example.com/cover.jpg",
  phoneNumber:     "+351 912 345 678",
  instagramHandle: "inksoultattoo",
  averageRating:   4.7,
  reviewCount:     12,
  galleryImages:   [
    "https://cdn.example.com/art1.jpg",
    "https://cdn.example.com/art2.jpg",
    "https://cdn.example.com/art3.jpg",
  ],
  artists: [
    {
      artistId:        "artist-001",
      name:            "Maria Silva",
      slug:            "maria-silva",
      bio:             "Specialises in neo-trad.",
      profileImageUrl: null,
      specializations: "Neo-Traditional, Illustrative",
      averageRating:   4.9,
      reviewCount:     8,
    },
    {
      artistId:        "artist-002",
      name:            "João Costa",
      slug:            "joao-costa",
      bio:             null,
      profileImageUrl: null,
      specializations: null,
      averageRating:   null,
      reviewCount:     0,
    },
  ],
  showBookingCta: true,
};
```

**New tests to add:**
```tsx
it("renders studio rating when reviewCount > 0", () => {
  renderPage();
  expect(screen.getByText(/4\.7/)).toBeInTheDocument();
  expect(screen.getByText(/12 reviews/)).toBeInTheDocument();
});

it("renders phone number link when phoneNumber is set", () => {
  renderPage();
  const phoneLink = screen.getByRole("link", { name: /call ink soul/i });
  expect(phoneLink).toHaveAttribute("href", "tel:+351 912 345 678");
});

it("renders Instagram link when instagramHandle is set", () => {
  renderPage();
  const igLink = screen.getByRole("link", { name: /instagram/i });
  expect(igLink).toHaveAttribute("href", "https://instagram.com/inksoultattoo");
});

it("renders gallery images when galleryImages is not empty", () => {
  renderPage();
  const galleryButtons = screen.getAllByRole("button", { name: /view portfolio image/i });
  expect(galleryButtons).toHaveLength(3);
});

it("gallery section is hidden when galleryImages is empty", () => {
  mockUseGetPublicStudioQuery.mockReturnValue({
    data: { ...STUDIO, galleryImages: [] },
    isLoading: false,
    isError: false,
  });
  renderPage();
  expect(screen.queryByRole("button", { name: /view portfolio image/i })).not.toBeInTheDocument();
});

it("artist cards include ChevronRight affordance via aria-label on the Link", () => {
  renderPage();
  expect(screen.getByRole("link", { name: "View Maria Silva's portfolio" })).toBeInTheDocument();
});

it("renders artist specialization under artist name", () => {
  renderPage();
  expect(screen.getByText("Neo-Traditional")).toBeInTheDocument();
});

it("'Browse studios' back link points to /discover", () => {
  renderPage();
  const backLink = screen.getByRole("link", { name: /back to studio discovery/i });
  expect(backLink).toHaveAttribute("href", "/discover");
});

it("cover image renders with alt text including studio name", () => {
  renderPage();
  const img = screen.getByRole("img", { name: /Ink Soul cover/i });
  expect(img).toHaveAttribute("src", STUDIO.coverImageUrl);
});
```

### I3. ReviewSection tests (unauthenticated gate)

Add to `ReviewSection` test file (create if absent):

```tsx
it("shows sign-in gate instead of form when unauthenticated", () => {
  // Render ReviewSection with token=null
  // Expect: "Sign in to share your experience" text visible
  // Expect: "Sign in to leave a review" button visible
  // Expect: textarea NOT in the document
});

it("shows the form when authenticated", () => {
  // Render ReviewSection with a valid token
  // Expect: textarea present in the document
  // Expect: "Sign in to share your experience" NOT present
});
```

---

## Section J — Architecture docs

In `docs/claude/architecture.md`, under **Feature Module Map**, update the studio profile entry:

```
StudioPortfolioPage (/s/{slug})   public/components/StudioPortfolioPage.tsx
                                  No auth required. Two-column desktop layout:
                                  left = hero + info + artists + gallery + reviews;
                                  right = sticky sidebar with CTA + contact.
                                  Hero: CoverImageUrl or initials monogram, h-72.
                                  Gallery: aggregated from artists' PortfolioImages
                                    (max 3 per artist, max 9 total, round-robin).
                                  Artist cards: enriched with avatar, specializations,
                                    per-artist rating (from PublicArtistSummary).
                                  Lightbox: shadcn Dialog (no package).
                                  Deduplication: DistinctBy(a => a.Id) in handler.
                                  Contact fields: PhoneNumber, InstagramHandle
                                    (AddStudioContactInfo migration).
```

---

## Section K — Build and verify

```bash
cd "Pena e Arte"
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
dotnet build
```

Verify: `AddStudioContactInfo` migration applied, `GetPublicStudioQuery` handler compiles with
all new fields.

```bash
cd frontend
pnpm build
pnpm test
```

All updated and new tests must pass. All 10 pre-existing `StudioPortfolioPage` tests must pass
with the updated button copy. Zero TypeScript `any`. Zero `pnpm lint` errors.

---

## Done checklist

- [ ] `Studio.cs` — `PhoneNumber string?` and `InstagramHandle string?` added
- [ ] `AddStudioContactInfo` migration created and applied
- [ ] `PublicArtistSummary.cs` — `ProfileImageUrl`, `Specializations`, `AverageRating`, `ReviewCount` added
- [ ] `PublicStudioResponse.cs` — `PhoneNumber`, `InstagramHandle`, `AverageRating`, `ReviewCount`, `GalleryImages` added
- [ ] `GetPublicStudioQuery.cs` — full replacement: dedup, per-artist reviews, studio reviews, gallery, new fields
- [ ] `publicApi.ts` — both interfaces updated
- [ ] `ReviewSection.tsx` — form label changed to `<label>`; `uppercase` removed; unauthenticated gate replaces double sign-in prompt
- [ ] `StarRating.tsx` — interactive mode gets `role="radiogroup"`, per-star `aria-label`, live region
- [ ] `StudioPortfolioPage.tsx` — fully replaced per Section G
- [ ] Hero expanded to `h-72` with gradient overlay and studio name
- [ ] Back link "Browse studios" → `/discover` with `min-h-[44px]`
- [ ] Studio rating row rendered when `reviewCount > 0`
- [ ] "Artists" section with enriched cards (avatar, spec, rating, `ChevronRight`)
- [ ] "Portfolio" gallery section with masonry + lightbox
- [ ] Sticky right sidebar with filled violet CTA + phone + Instagram + location
- [ ] "Book an Appointment" — `bg-violet-600` filled button
- [ ] Footer — `text-foreground/50` + `mt-auto`
- [ ] Microcopy under CTA button
- [ ] Backend handler tests pass (dedup, aggregates, gallery)
- [ ] Frontend `StudioPortfolioPage.test.tsx` — all 10 existing tests updated + 9 new tests pass
- [ ] ReviewSection gate tests pass
- [ ] `dotnet build` clean
- [ ] `pnpm build` clean
- [ ] `pnpm test` clean
