# Overnight Prompt — Artist Profile Page Overhaul
**Date:** 2026-06-25
**No new packages.**

---

## Goal

Fix every critical, high, and accessibility issue identified in the UI/UX audit of `/a/{slug}` (the public artist portfolio page). The page currently renders in a ~380px column on a 1400px canvas, has no avatar, a stranded bright-green success message that never dismisses, a ghost-styled primary CTA, no lightbox, and no desktop layout. After this prompt the page should feel like a professional portfolio — trust-building, visually balanced, and immediately compelling.

---

## Step 0 — Read these files first

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/architecture.md
Pena_e_Arte.Domain/Entities/Artist.cs
Pena_e_Arte.Contracts/Responses/Public/PublicArtistResponse.cs
Pena_e_Arte.Application/Public/Queries/GetPublicArtistQuery.cs
Pena_e_Arte.API/Endpoints/PublicEndpoints.cs
frontend/src/features/public/components/ArtistPortfolioPage.tsx
frontend/src/features/public/components/ReviewSection.tsx
frontend/src/features/public/publicApi.ts
frontend/src/shared/components/ui/StarRating.tsx
```

---

## Section A — Backend: add ProfileImageUrl to Artist entity

### A1. `Pena_e_Arte.Domain/Entities/Artist.cs`

Add one property after `Bio`:

```csharp
public string? ProfileImageUrl { get; set; }
```

### A2. Migration

```bash
dotnet ef migrations add AddArtistProfileImageUrl \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
dotnet ef database update \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

No seed data needed — nullable field, defaults to null for all existing artists.

---

## Section B — Backend: expand PublicArtistResponse contract

Replace `Pena_e_Arte.Contracts/Responses/Public/PublicArtistResponse.cs`:

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicArtistResponse(
    Guid                  ArtistId,
    string                Name,
    string                Slug,
    string?               Bio,
    string?               ProfileImageUrl,    // NEW — circular avatar
    IReadOnlyList<string> PortfolioImages,
    string?               Specializations,    // NEW — comma-separated tags e.g. "Blackwork, Mandala"
    decimal?              HourlyRate,         // NEW — base hourly rate in EUR; null = not set
    double?               AverageRating,      // NEW — computed from reviews; null = no reviews
    int                   ReviewCount,        // NEW
    string                StudioName,
    string                StudioSlug,
    bool                  ShowBookingCta,
    bool                  IsOwnProfile);      // NEW — true when the requesting user IS this artist
```

---

## Section C — Backend: update GetPublicArtistQuery

Replace `Pena_e_Arte.Application/Public/Queries/GetPublicArtistQuery.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPublicArtistQuery(string Slug, Guid? CurrentUserId)
    : IRequest<PublicArtistResponse?>;

public class GetPublicArtistHandler(IAppDbContext db)
    : IRequestHandler<GetPublicArtistQuery, PublicArtistResponse?>
{
    public async Task<PublicArtistResponse?> Handle(
        GetPublicArtistQuery query, CancellationToken ct)
    {
        // Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions.
        Artist? artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == query.Slug && a.DeletedAt == null, ct);

        if (artist is null) return null;

        // Approved: public portfolio query.
        Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == artist.StudioId && s.IsActive, ct);

        if (studio is null) return null;

        // Artist-level review aggregate.
        // Approved: public portfolio query.
        var reviewStats = await db.Reviews
            .Where(r => r.ArtistId == artist.Id)
            .GroupBy(_ => 1)
            .Select(g => new { Avg = g.Average(r => (double)r.Rating), Count = g.Count() })
            .FirstOrDefaultAsync(ct);

        bool isOwnProfile = query.CurrentUserId.HasValue
                         && artist.UserId == query.CurrentUserId;

        return new PublicArtistResponse(
            artist.Id,
            $"{artist.FirstName} {artist.LastName}",
            artist.Slug!,
            artist.Bio,
            artist.ProfileImageUrl,
            artist.PortfolioImages,
            artist.Specializations,
            artist.HourlyRate,
            reviewStats is { Count: > 0 } ? Math.Round(reviewStats.Avg, 1) : null,
            reviewStats?.Count ?? 0,
            studio.Name,
            studio.Slug,
            ShowBookingCta: true,
            isOwnProfile);
    }
}
```

---

## Section D — Backend: update PublicEndpoints.cs

In `MapPublicEndpoints`, change the `GetPublicArtist` registration to receive the `ClaimsPrincipal`
(ASP.NET Core resolves this automatically even for `AllowAnonymous` endpoints when a valid JWT is present):

```csharp
group.MapGet("/artists/{slug}", GetPublicArtist).AllowAnonymous();
```

Replace the `GetPublicArtist` private static method:

```csharp
private static async Task<IResult> GetPublicArtist(
    string            slug,
    ISender           mediator,
    ClaimsPrincipal   user,
    CancellationToken ct)
{
    Guid? currentUserId = user.Identity?.IsAuthenticated == true
        ? Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null
        : null;

    PublicArtistResponse? result =
        await mediator.Send(new GetPublicArtistQuery(slug, currentUserId), ct);
    return result is null ? Results.NotFound() : Results.Ok(result);
}
```

Ensure `using System.Security.Claims;` is present (it should already be).

---

## Section E — Frontend: publicApi.ts — update PublicArtistResponse interface

Find the `PublicArtistResponse` interface and replace it:

```typescript
export interface PublicArtistResponse {
  artistId:        string;
  name:            string;
  slug:            string;
  bio:             string | null;
  profileImageUrl: string | null;    // circular avatar; null → show monogram
  portfolioImages: string[];
  specializations: string | null;    // comma-separated; split on render
  hourlyRate:      number | null;    // EUR; null = not disclosed
  averageRating:   number | null;
  reviewCount:     number;
  studioName:      string;
  studioSlug:      string;
  showBookingCta:  boolean;
  isOwnProfile:    boolean;
}
```

---

## Section F — Frontend: ReviewSection.tsx fixes

Two fixes in this file:

### F1. Auto-dismiss success state

In `ReviewForm`, the `success` state currently renders a permanent green string. Replace the
success branch to auto-clear after 4 seconds and use a proper status container:

Replace the entire `success` check and its return with:

```tsx
// Add to imports:
import { useEffect } from "react";
import { CheckCircle } from "lucide-react";

// Inside ReviewForm, after existing useState declarations:
useEffect(() => {
  if (!success) return;
  const id = window.setTimeout(() => setSuccess(false), 4000);
  return () => window.clearTimeout(id);
}, [success]);

// Replace the early return for success:
if (success) {
  return (
    <div
      role="status"
      aria-live="polite"
      className="flex items-center gap-2.5 rounded-lg border border-green-800/60
                 bg-green-950/30 px-4 py-3"
    >
      <CheckCircle className="h-4 w-4 shrink-0 text-green-400" aria-hidden="true" />
      <p className="text-sm text-green-400">
        Review submitted — thank you!
      </p>
    </div>
  );
}
```

### F2. Section heading size

In the `ReviewSection` export, change the `<h2>` element from `text-sm font-medium` to `text-lg font-semibold`:

```tsx
// Old:
<h2 id="reviews-heading" className="text-sm font-medium">Reviews</h2>

// New:
<h2 id="reviews-heading" className="text-lg font-semibold">Reviews</h2>
```

Also change the `MessageSquare` icon from `h-4 w-4 text-muted-foreground` to `h-5 w-5 text-muted-foreground/70` to match the larger heading.

---

## Section G — Frontend: ArtistPortfolioPage.tsx — full replacement

Replace the entire file. This implements:
- Two-column desktop layout (sticky left panel + portfolio right column)
- Circular avatar with monogram fallback
- Rating badge under artist name
- Filled violet "Book" button
- Specializations as badge chips
- Hourly rate display
- Portfolio masonry with zoom cursor
- CSS-columns masonry (no packages)
- Lightbox using shadcn `Dialog`
- "Write a Review" affordance (scroll to reviews section)
- Profile Strength nudge bar (only for `isOwnProfile === true`)
- All accessibility fixes (aria-labels, touch targets, alt text)

```tsx
import { useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  ChevronLeft,
  CheckCircle,
  Images,
  Instagram,
  Star,
  X,
  ZoomIn,
} from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Dialog,
  DialogContent,
} from "@/shared/components/ui/dialog";
import { StarRating }  from "@/shared/components/ui/StarRating";
import { useAppSelector } from "@/app/hooks";
import { useGetPublicArtistQuery } from "../publicApi";
import { useDocumentMeta }         from "@/shared/utils/useDocumentMeta";
import { ReviewSection }           from "./ReviewSection";

// ── Document meta ──────────────────────────────────────────────────────────────

function ArtistMeta({
  name, slug, bio, coverImage,
}: {
  name: string; slug: string; bio: string | null; coverImage?: string;
}) {
  useDocumentMeta({
    title:       `${name} — Tattoo Artist on Pena e Artë`,
    description: bio ?? `View the tattoo portfolio of ${name}.`,
    ogImage:     coverImage,
    canonical:   `https://penaearte.com/a/${slug}`,
  });
  return null;
}

// ── Avatar ─────────────────────────────────────────────────────────────────────

function ArtistAvatar({
  name, profileImageUrl,
}: {
  name: string; profileImageUrl: string | null;
}) {
  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? "")
    .join("");

  if (profileImageUrl) {
    return (
      <img
        src={profileImageUrl}
        alt={`Profile photo of ${name}`}
        className="h-24 w-24 rounded-full object-cover ring-2 ring-border/60"
      />
    );
  }

  return (
    <div
      className="h-24 w-24 rounded-full bg-gradient-to-br from-zinc-700 to-zinc-800
                 ring-2 ring-border/40 flex items-center justify-center"
      aria-hidden="true"
    >
      <span className="text-2xl font-bold text-white/25 select-none">{initials}</span>
    </div>
  );
}

// ── Specialization chips ───────────────────────────────────────────────────────

function SpecializationChips({ value }: { value: string }) {
  const tags = value.split(",").map((s) => s.trim()).filter(Boolean);
  if (tags.length === 0) return null;
  return (
    <div className="flex flex-wrap gap-1.5">
      {tags.map((tag) => (
        <span
          key={tag}
          className="text-xs px-2.5 py-1 rounded-full
                     bg-muted/60 text-muted-foreground/90
                     border border-border/50"
        >
          {tag}
        </span>
      ))}
    </div>
  );
}

// ── Rating summary ─────────────────────────────────────────────────────────────

function RatingSummary({
  averageRating, reviewCount, onWriteReview,
}: {
  averageRating: number | null;
  reviewCount:   number;
  onWriteReview: () => void;
}) {
  if (reviewCount === 0) {
    return (
      <div className="flex items-center gap-2">
        <StarRating value={0} />
        <button
          type="button"
          onClick={onWriteReview}
          className="text-xs text-violet-400 hover:text-violet-300 transition-colors
                     underline underline-offset-2"
        >
          Be the first to review
        </button>
      </div>
    );
  }
  return (
    <div className="flex items-center gap-2 flex-wrap">
      <StarRating value={Math.round(averageRating ?? 0)} />
      <span className="text-sm text-muted-foreground">
        {averageRating?.toFixed(1)} · {reviewCount} review{reviewCount !== 1 ? "s" : ""}
      </span>
      <button
        type="button"
        onClick={onWriteReview}
        className="text-xs text-violet-400 hover:text-violet-300 transition-colors
                   underline underline-offset-2 ml-auto"
      >
        Leave a review
      </button>
    </div>
  );
}

// ── Profile Strength nudge (own profile only) ──────────────────────────────────

function ProfileStrengthNudge({
  hasBio, hasAvatar, hasSpecializations, hasPortfolio, hasRate,
}: {
  hasBio:             boolean;
  hasAvatar:          boolean;
  hasSpecializations: boolean;
  hasPortfolio:       boolean;
  hasRate:            boolean;
}) {
  const items = [hasBio, hasAvatar, hasSpecializations, hasPortfolio, hasRate];
  const done  = items.filter(Boolean).length;
  const total = items.length;
  const pct   = Math.round((done / total) * 100);

  if (pct === 100) return null;

  const missing: string[] = [];
  if (!hasBio)             missing.push("Add a bio");
  if (!hasAvatar)          missing.push("Upload a profile photo");
  if (!hasSpecializations) missing.push("Add your specialties");
  if (!hasPortfolio)       missing.push("Upload at least 1 portfolio image");
  if (!hasRate)            missing.push("Set your hourly rate");

  return (
    <div className="rounded-lg border border-amber-800/50 bg-amber-950/20 p-4 space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-sm font-semibold text-amber-300">
          Profile {pct}% complete
        </p>
        <span className="text-xs text-amber-400/70">{done}/{total} sections</span>
      </div>

      {/* Progress bar */}
      <div className="h-1.5 rounded-full bg-amber-900/40 overflow-hidden">
        <div
          className="h-full rounded-full bg-amber-400 transition-all duration-500"
          style={{ width: `${pct}%` }}
          aria-valuenow={pct}
          aria-valuemin={0}
          aria-valuemax={100}
          role="progressbar"
          aria-label="Profile completion"
        />
      </div>

      {/* Action items */}
      <ul className="space-y-1">
        {missing.map((action) => (
          <li key={action} className="flex items-center gap-1.5 text-xs text-amber-300/70">
            <span aria-hidden="true">·</span>
            {action}
          </li>
        ))}
      </ul>

      <p className="text-xs text-amber-400/60">
        Only visible to you. Complete your profile to attract more clients.
      </p>
    </div>
  );
}

// ── Portfolio masonry ──────────────────────────────────────────────────────────

function PortfolioGrid({
  images, artistName, onImageClick,
}: {
  images:       string[];
  artistName:   string;
  onImageClick: (url: string) => void;
}) {
  if (images.length === 0) {
    return (
      <div className="flex flex-col items-center gap-3 py-16 text-center rounded-lg
                      border border-dashed border-border/50">
        <Images className="h-8 w-8 text-muted-foreground/30" aria-hidden="true" />
        <p className="text-sm text-muted-foreground">No portfolio images yet.</p>
      </div>
    );
  }

  return (
    <div className="columns-2 sm:columns-3 gap-3">
      {images.map((url, i) => (
        <button
          key={url}
          type="button"
          onClick={() => onImageClick(url)}
          className="mb-3 break-inside-avoid block w-full group relative
                     overflow-hidden rounded-lg cursor-zoom-in
                     focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring
                     focus-visible:ring-offset-1"
          aria-label={`View portfolio image ${i + 1} of ${images.length} by ${artistName}`}
        >
          <img
            src={url}
            alt={`Tattoo by ${artistName} — image ${i + 1}`}
            loading={i < 6 ? "eager" : "lazy"}
            decoding="async"
            className="w-full object-cover transition-transform duration-300
                       group-hover:scale-[1.03]"
          />
          {/* Zoom affordance */}
          <div
            className="absolute inset-0 bg-black/0 group-hover:bg-black/25
                       transition-colors duration-200
                       flex items-center justify-center"
          >
            <ZoomIn
              className="h-6 w-6 text-white drop-shadow-lg
                         opacity-0 group-hover:opacity-100 transition-opacity duration-200"
              aria-hidden="true"
            />
          </div>
        </button>
      ))}
    </div>
  );
}

// ── Lightbox ───────────────────────────────────────────────────────────────────

function Lightbox({
  imageUrl, artistName, onClose,
}: {
  imageUrl:   string | null;
  artistName: string;
  onClose:    () => void;
}) {
  return (
    <Dialog open={!!imageUrl} onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent
        className="max-w-5xl w-full p-0 bg-black border-0 overflow-hidden"
        aria-label={`Portfolio image by ${artistName}`}
      >
        {imageUrl && (
          <div className="relative">
            <img
              src={imageUrl}
              alt={`Tattoo portfolio by ${artistName}`}
              className="w-full h-auto max-h-[90vh] object-contain"
            />
            {/* Close button overlay */}
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

// ── Skeleton ───────────────────────────────────────────────────────────────────

function ArtistPageSkeleton() {
  return (
    <div
      className="max-w-6xl mx-auto px-4 py-8"
      aria-label="Loading artist profile"
      aria-busy="true"
    >
      {/* Back link skeleton */}
      <Skeleton className="h-4 w-32 mb-6" />

      <div className="grid grid-cols-1 lg:grid-cols-[340px_1fr] gap-8 lg:gap-12">
        {/* Left col skeleton */}
        <div className="space-y-5">
          <Skeleton className="h-24 w-24 rounded-full" />
          <Skeleton className="h-7 w-48" />
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-16 w-full" />
          <div className="flex gap-1.5">
            <Skeleton className="h-6 w-20 rounded-full" />
            <Skeleton className="h-6 w-24 rounded-full" />
          </div>
          <Skeleton className="h-11 w-full rounded-md" />
        </div>

        {/* Right col skeleton */}
        <div className="columns-2 sm:columns-3 gap-3">
          {["h-52", "h-40", "h-64", "h-48", "h-60", "h-44"].map((h, i) => (
            <div key={i} className={`mb-3 break-inside-avoid ${h}`}>
              <Skeleton className="w-full h-full rounded-lg" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ── Main page ──────────────────────────────────────────────────────────────────

export function ArtistPortfolioPage() {
  const { slug = "" }  = useParams<{ slug: string }>();
  const token          = useAppSelector((s) => s.auth.token);
  const reviewsRef     = useRef<HTMLDivElement>(null);
  const [lightboxUrl, setLightboxUrl] = useState<string | null>(null);

  const { data: artist, isLoading, isError } =
    useGetPublicArtistQuery(slug, { skip: !slug });

  function scrollToReviews() {
    reviewsRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  }

  // ── Loading ──────────────────────────────────────────────────────────────
  if (isLoading) return <ArtistPageSkeleton />;

  // ── Error ────────────────────────────────────────────────────────────────
  if (isError || !artist) {
    return (
      <div className="flex flex-col items-center justify-center min-h-screen gap-4">
        <p className="text-muted-foreground">Artist not found.</p>
        <Button variant="outline" asChild>
          <Link to="/discover">Browse artists</Link>
        </Button>
      </div>
    );
  }

  // ── Book URL ─────────────────────────────────────────────────────────────
  const bookUrl = `/book?studio=${artist.studioSlug}&artist=${artist.slug}`;
  const ctaUrl  = token ? bookUrl : `/login?redirect=${encodeURIComponent(bookUrl)}`;

  // ── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="min-h-screen bg-background flex flex-col">
      <ArtistMeta
        name={artist.name}
        slug={artist.slug}
        bio={artist.bio}
        coverImage={artist.portfolioImages[0] ?? artist.profileImageUrl ?? undefined}
      />

      <Lightbox
        imageUrl={lightboxUrl}
        artistName={artist.name}
        onClose={() => setLightboxUrl(null)}
      />

      <div className="flex-1 max-w-6xl mx-auto w-full px-4 py-8 space-y-6">
        {/* ── Back link ─────────────────────────────────────────────────── */}
        <Link
          to={`/s/${artist.studioSlug}`}
          className="inline-flex items-center gap-1 text-sm text-muted-foreground
                     hover:text-foreground transition-colors
                     py-2 -my-2 min-h-[44px]"
          aria-label={`Back to ${artist.studioName}`}
        >
          <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
          {artist.studioName}
        </Link>

        {/* ── Two-column layout ─────────────────────────────────────────── */}
        <div className="grid grid-cols-1 lg:grid-cols-[340px_1fr] gap-8 lg:gap-12 items-start">

          {/* ── LEFT: sticky profile panel ──────────────────────────────── */}
          <aside className="lg:sticky lg:top-6 space-y-5">

            {/* Profile strength nudge — only for own profile */}
            {artist.isOwnProfile && (
              <ProfileStrengthNudge
                hasBio={!!artist.bio}
                hasAvatar={!!artist.profileImageUrl}
                hasSpecializations={!!artist.specializations}
                hasPortfolio={artist.portfolioImages.length > 0}
                hasRate={artist.hourlyRate != null}
              />
            )}

            {/* Avatar */}
            <ArtistAvatar
              name={artist.name}
              profileImageUrl={artist.profileImageUrl}
            />

            {/* Name */}
            <div className="space-y-1.5">
              <h1 className="text-2xl font-bold tracking-tight">{artist.name}</h1>

              {/* Studio affiliation below the name */}
              <p className="text-sm text-muted-foreground">
                at{" "}
                <Link
                  to={`/s/${artist.studioSlug}`}
                  className="hover:text-foreground underline underline-offset-2 transition-colors"
                >
                  {artist.studioName}
                </Link>
              </p>
            </div>

            {/* Rating summary */}
            <RatingSummary
              averageRating={artist.averageRating}
              reviewCount={artist.reviewCount}
              onWriteReview={scrollToReviews}
            />

            {/* Bio */}
            {artist.bio && (
              <p className="text-sm text-muted-foreground/90 leading-relaxed whitespace-pre-wrap">
                {artist.bio}
              </p>
            )}

            {/* Specializations */}
            {artist.specializations && (
              <SpecializationChips value={artist.specializations} />
            )}

            {/* Hourly rate */}
            {artist.hourlyRate != null && (
              <p className="text-sm text-muted-foreground">
                From{" "}
                <span className="font-semibold text-foreground">
                  €{artist.hourlyRate}/hr
                </span>
              </p>
            )}

            {/* Book CTA */}
            {artist.showBookingCta && (
              <Button
                className="w-full bg-violet-600 hover:bg-violet-700
                           text-white border-0 min-h-[44px] text-sm font-semibold"
                asChild
              >
                <Link to={ctaUrl}>
                  Book an Appointment
                </Link>
              </Button>
            )}

            {/* Divider + studio card */}
            <div className="rounded-lg border bg-muted/20 p-3.5 space-y-1">
              <p className="text-xs text-muted-foreground uppercase tracking-wide font-medium">
                Studio
              </p>
              <Link
                to={`/s/${artist.studioSlug}`}
                className="text-sm font-medium hover:underline underline-offset-2"
              >
                {artist.studioName}
              </Link>
            </div>
          </aside>

          {/* ── RIGHT: portfolio + reviews ───────────────────────────────── */}
          <div className="space-y-12">

            {/* Portfolio section */}
            <section aria-labelledby="portfolio-heading">
              <div className="flex items-center gap-2.5 mb-5">
                <h2 id="portfolio-heading" className="text-lg font-semibold flex items-center gap-2">
                  <Images className="h-5 w-5 text-muted-foreground/70" aria-hidden="true" />
                  Portfolio
                </h2>
                {artist.portfolioImages.length > 0 && (
                  <span className="text-sm text-muted-foreground">
                    ({artist.portfolioImages.length}{" "}
                    {artist.portfolioImages.length === 1 ? "image" : "images"})
                  </span>
                )}
              </div>

              <PortfolioGrid
                images={artist.portfolioImages}
                artistName={artist.name}
                onImageClick={(url) => setLightboxUrl(url)}
              />
            </section>

            {/* Reviews section */}
            <div ref={reviewsRef}>
              <ReviewSection slug={artist.slug} target="artist" token={token} />
            </div>
          </div>
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
| **Critical #1** — No avatar | `ArtistAvatar` component: `ProfileImageUrl` or initials monogram |
| **Critical #2** — "Thanks for your review!" stranded | `ReviewForm`: auto-clear after 4 s via `useEffect` + proper `role="status"` container |
| **Critical #3** — Ghost button for primary CTA | `bg-violet-600 hover:bg-violet-700 text-white border-0`, text changed to "Book an Appointment" |
| **Quick win #1** — Filled book button | Done |
| **Quick win #2** — Rating badge above CTA | `RatingSummary` component with `StarRating` + "N reviews" label |
| **Quick win #3** — "Write a review" CTA | `onWriteReview` callback in `RatingSummary` scrolls to reviews section |
| **Layout** — 380px mobile column on 1400px canvas | `max-w-6xl` + `grid-cols-1 lg:grid-cols-[340px_1fr]` two-column layout |
| **Layout** — dead margins | Left column is `340px` fixed; right is `1fr`; total respects a real design grid |
| **Layout** — footer floating void | `flex flex-col min-h-screen` with `mt-auto` on footer |
| **Typography** — section headings too small | `text-lg font-semibold` on both Portfolio and Reviews |
| **Typography** — rating as plain text | `StarRating` + numeric badge |
| **Color** — vivid green success | Muted `text-green-400` on `bg-green-950/30` with `border-green-800/60` |
| **Spacing** — arbitrary gaps | Consistent `space-y-5` left panel, `space-y-12` right column, `gap-8 lg:gap-12` grid |
| **Icon consistency** — Portfolio had no icon | `Images` icon on Portfolio heading matches `MessageSquare` on Reviews |
| **Lightbox** — images open in new tab only | `PortfolioGrid` with `cursor-zoom-in` + `ZoomIn` hover icon → `Dialog` lightbox |
| **Hover affordance** — images look static | `group-hover:scale-[1.03]` + `ZoomIn` overlay icon |
| **Specializations missing** | `SpecializationChips` renders comma-separated tags as rounded badges |
| **Studio displayed only as back link** | Studio affiliation shown under artist name + separate studio card in panel |
| **Hourly rate missing** | "From €X/hr" shown if `hourlyRate` is set |
| **Empty portfolio state** | `PortfolioGrid` renders bordered dashed empty state with icon |
| **Review section has no container** | `ReviewCard` already has `border-b` separator — preserved |
| **Back link touch target** | `min-h-[44px]` + `py-2 -my-2` on the back `<Link>` |
| **Back link aria** | `aria-label="Back to {studioName}"` |
| **Decorative icon aria** | All icons in this file have `aria-hidden="true"` |
| **Portfolio image alt text** | `alt="Tattoo by {artistName} — image {i+1}"` |
| **Profile completeness nudge** | `ProfileStrengthNudge` shown only when `isOwnProfile === true` |
| **"Book with Elena" redundant copy** | Changed to "Book an Appointment" |
| **Two-column desktop** | `grid-cols-[340px_1fr]` with sticky left panel |

---

## Section I — Tests

### I1. Backend: GetPublicArtistQuery handler

Update or create `tests/Pena_e_Arte.UnitTests/Public/GetPublicArtistHandlerTests.cs`.

The test should verify:
- Returns `null` when artist slug does not exist
- Returns `null` when the artist's studio is inactive
- Returns `IsOwnProfile = true` when `CurrentUserId` matches `artist.UserId`
- Returns `IsOwnProfile = false` when `CurrentUserId` is null or different
- `AverageRating` is `null` when there are no reviews
- `AverageRating` and `ReviewCount` are correctly computed when reviews exist
- All new fields (`ProfileImageUrl`, `Specializations`, `HourlyRate`) are projected correctly

### I2. Frontend: ArtistPortfolioPage tests

Create `frontend/src/features/public/__tests__/ArtistPortfolioPage.test.tsx`:

The test should verify:
- Page title renders the artist name
- Avatar renders `profileImageUrl` when set
- Monogram renders when `profileImageUrl` is null
- Rating badge renders when `reviewCount > 0`
- "Be the first to review" link renders when `reviewCount === 0`
- Specialization chips render when `specializations` is set
- Hourly rate renders when `hourlyRate` is not null
- Book button text is "Book an Appointment" (not "Book with Elena Martins")
- Book button redirects to login when not authenticated
- Portfolio images render as buttons with zoom cursor
- Clicking a portfolio image opens the lightbox (Dialog)
- Profile strength nudge renders only when `isOwnProfile === true`
- Profile strength nudge is hidden when `isOwnProfile === false`
- Empty portfolio state renders when `portfolioImages` is empty
- Back link has `aria-label` containing the studio name

### I3. ReviewSection auto-dismiss test

Add to existing `ReviewSection` tests (or create if not present):
- After a successful review submission, the success message renders with `role="status"`
- After 4 seconds, the success message disappears (use `vitest.useFakeTimers()` + `vi.advanceTimersByTime(4001)`)
- The success message does NOT render a vivid green text on black — it should have the muted container

---

## Section J — Architecture docs

In `docs/claude/architecture.md`, under **Feature Module Map**, update the artist profile entry:

```
ArtistPortfolioPage (/a/{slug})   public/components/ArtistPortfolioPage.tsx
                                  No auth required. ClaimsPrincipal injected for IsOwnProfile.
                                  Two-column desktop layout: sticky left panel (avatar, bio,
                                  specializations, rate, booking) + right col (portfolio masonry,
                                  reviews). Lightbox via shadcn Dialog. Portfolio masonry via
                                  CSS columns (no package).
                                  ProfileImageUrl: DB column on Artist entity.
                                  View tracking: POST /api/v1/public/artists/{slug}/view
                                    (from overnight-prompt-portfolio-feed-2026-06-25.md).
                                  Instagram fields: added by overnight-prompt-instagram-sync
                                    (not yet in contract — add InstagramHandle after that
                                    migration runs).
```

---

## Section K — Build, lint, test

```bash
# Backend
cd "Pena e Arte"
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
dotnet build
```

Verify: `AddArtistProfileImageUrl` migration applied, `GetPublicArtistQuery` handler compiles,
`PublicEndpoints.cs` compiles with the updated handler signature.

```bash
# Frontend
cd frontend
pnpm build
pnpm test
```

All new tests must pass. All pre-existing tests must still pass. Zero TypeScript `any`. Zero `pnpm lint` errors.

---

## Done checklist

- [ ] `Artist.cs` — `ProfileImageUrl string?` added
- [ ] `AddArtistProfileImageUrl` migration created and applied
- [ ] `PublicArtistResponse.cs` — 6 new fields added
- [ ] `GetPublicArtistQuery.cs` — projects all new fields, review aggregate, `IsOwnProfile`
- [ ] `PublicEndpoints.cs` — `GetPublicArtist` passes `ClaimsPrincipal` + `currentUserId` to query
- [ ] `publicApi.ts` — `PublicArtistResponse` interface updated with all 6 new fields
- [ ] `ReviewSection.tsx` — success state auto-clears after 4 s; heading bumped to `text-lg font-semibold`
- [ ] `ArtistPortfolioPage.tsx` — fully replaced per Section G
- [ ] Two-column `lg:grid-cols-[340px_1fr]` layout
- [ ] `ArtistAvatar` — profile photo or initials monogram
- [ ] `RatingSummary` — `StarRating` + review count + "Leave a review" scroll link
- [ ] `SpecializationChips` — badge chips from comma-separated `specializations`
- [ ] Hourly rate displayed when set
- [ ] "Book an Appointment" — `bg-violet-600` filled button (not ghost)
- [ ] Studio affiliation shown under name + studio card in panel
- [ ] `PortfolioGrid` — CSS columns masonry, `cursor-zoom-in`, zoom icon hover
- [ ] `Lightbox` — shadcn `Dialog` with close button
- [ ] `ProfileStrengthNudge` — visible only when `isOwnProfile === true`
- [ ] All icons have `aria-hidden="true"`
- [ ] All portfolio images have descriptive `alt` text
- [ ] Back link has `aria-label` and `min-h-[44px]` touch target
- [ ] Footer uses `text-foreground/50` with `mt-auto` (no floating void)
- [ ] Backend unit tests pass
- [ ] Frontend component tests pass
- [ ] `dotnet build` clean
- [ ] `pnpm build` clean
- [ ] `pnpm test` clean
