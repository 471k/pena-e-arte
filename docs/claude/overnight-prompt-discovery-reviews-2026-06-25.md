# Overnight Prompt — Public Discovery & Reviews
**Date:** 2026-06-25  
**Scope:** Full-stack — new `Review` entity, location-based studio discovery endpoint,
            public reviews (read anonymous, write requires auth), `DiscoverPage` with
            geo-detect + location search, `ReviewSection` embedded in portfolio pages.

---

## Context

Read `CLAUDE.md` and `docs/claude/architecture.md` before starting.

### What already exists
- `/map` → `StudioMapPage.tsx` — Leaflet map showing ALL studios (owner-only data, authenticated).
- `/s/:slug` → `StudioPortfolioPage.tsx` — public studio portfolio. No reviews yet.
- `/artist/:slug` → `ArtistPortfolioPage.tsx` — public artist portfolio. No reviews yet.
- `frontend/src/features/public/publicApi.ts` — `publicApi` RTK Query slice, no nearby or review endpoints.
- `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs` — only `GET /studios/{slug}` and `GET /artists/{slug}`.
- `GetPublicStudioQuery.cs` — uses `IgnoreQueryFilters()` with comment `// Approved: public portfolio query`.
- `Studio` entity — has `Latitude`, `Longitude`, `City`, `IsActive`, `Slug`, `CoverImageUrl`, `Description`.
- `Artist` entity — has `StudioId`, `DeletedAt`, `Slug`, `Bio`, `FirstName`, `LastName`.

### What does NOT exist yet
- `Review` domain entity — create it fresh.
- Nearby-studios query or endpoint.
- Any review endpoints (GET or POST).
- `DiscoverPage.tsx` — create at `/discover`.
- `ReviewSection.tsx` — create shared component.
- `StarRating.tsx` — create shared UI component.

### Hard rules (non-negotiable)
1. No new npm or NuGet packages.
2. All data fetching via RTK Query. `useEffect` only for browser API side-effects
   (geolocation, `document.title`) — never for data fetching.
3. TypeScript strict mode. No `any`. No default exports on components.
4. Reviews have NO `TenantId` — they are cross-tenant public data. Omit the
   global query filter on `DbSet<Review>`.
5. All public GET endpoints: `.AllowAnonymous()`.
6. Review POST endpoint: `.RequireAuthorization("ClientAndAbove")`.
7. Never log PII (names, emails, content). Log `tenant_id` / `user_id` / `request_id`.
8. No business logic in endpoints — MediatR only.
9. Write tests alongside every backend command and query.

---

## Part 1 — Domain: `Review` entity

**File:** `Pena_e_Arte.Domain/Entities/Review.cs` (create new)

```csharp
namespace Pena_e_Arte.Domain.Entities;

public class Review
{
    private Review() { }   // EF Core constructor

    public Guid      Id           { get; private set; } = Guid.NewGuid();
    public Guid?     StudioId     { get; private set; }  // set for studio reviews
    public Guid?     ArtistId     { get; private set; }  // set for artist reviews
    public Guid      AuthorUserId { get; private set; }
    public string    AuthorName   { get; private set; } = "";
    public int       Rating       { get; private set; }  // 1–5
    public string    Body         { get; private set; } = "";
    public DateTime  CreatedAt    { get; private set; } = DateTime.UtcNow;

    public static Review ForStudio(
        Guid studioId, Guid authorUserId, string authorName, int rating, string body)
        => new()
        {
            StudioId     = studioId,
            AuthorUserId = authorUserId,
            AuthorName   = authorName,
            Rating       = rating,
            Body         = body.Trim(),
        };

    public static Review ForArtist(
        Guid artistId, Guid authorUserId, string authorName, int rating, string body)
        => new()
        {
            ArtistId     = artistId,
            AuthorUserId = authorUserId,
            AuthorName   = authorName,
            Rating       = rating,
            Body         = body.Trim(),
        };
}
```

> **No `TenantId`.** Reviews are cross-tenant public records. Do NOT add
> `HasQueryFilter` on this entity in `AppDbContext`.

---

## Part 2 — EF Core: DbContext + configuration + migration

### 2a — `IAppDbContext.cs`
Add to the interface:
```csharp
DbSet<Review> Reviews { get; }
```

### 2b — `AppDbContext.cs`
Add the DbSet and entity configuration inside `OnModelCreating`:

```csharp
public DbSet<Review> Reviews => Set<Review>();
```

Inside `OnModelCreating`, **after** the existing tenant filter registrations:
```csharp
modelBuilder.Entity<Review>(entity =>
{
    entity.HasKey(r => r.Id);

    entity.Property(r => r.AuthorName).HasMaxLength(200).IsRequired();
    entity.Property(r => r.Body).HasMaxLength(2000).IsRequired();
    entity.Property(r => r.Rating).IsRequired();

    // No HasQueryFilter — reviews are public cross-tenant data.
    // StudioId and ArtistId are foreign keys but not navigation properties
    // (to avoid accidental tenant-scoped includes).
    entity.HasIndex(r => r.StudioId);
    entity.HasIndex(r => r.ArtistId);
    entity.HasIndex(r => new { r.AuthorUserId, r.StudioId }).IsUnique();   // one review per user per studio
    entity.HasIndex(r => new { r.AuthorUserId, r.ArtistId }).IsUnique();   // one review per user per artist
});
```

### 2c — Migration
Run:
```bash
dotnet ef migrations add AddReviews --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```

---

## Part 3 — Contracts: new DTOs

### 3a — `Pena_e_Arte.Contracts/Responses/Public/NearbyStudioResponse.cs` (create new)
```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record NearbyStudioResponse(
    Guid    StudioId,
    string  Name,
    string  Slug,
    string  City,
    string? CoverImageUrl,
    double  DistanceKm,
    int     ArtistCount);
```

### 3b — `Pena_e_Arte.Contracts/Responses/Public/ReviewResponse.cs` (create new)
```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record ReviewResponse(
    Guid     Id,
    string   AuthorName,
    int      Rating,
    string   Body,
    DateTime CreatedAt);
```

### 3c — `Pena_e_Arte.Contracts/Requests/CreateReviewRequest.cs` (create new)
```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record CreateReviewRequest(int Rating, string Body);
```

---

## Part 4 — Application: `GetNearbyStudiosQuery`

**File:** `Pena_e_Arte.Application/Public/Queries/GetNearbyStudiosQuery.cs` (create new)

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetNearbyStudiosQuery(double Lat, double Lng, double RadiusKm)
    : IRequest<List<NearbyStudioResponse>>;

public class GetNearbyStudiosHandler(IAppDbContext db)
    : IRequestHandler<GetNearbyStudiosQuery, List<NearbyStudioResponse>>
{
    public async Task<List<NearbyStudioResponse>> Handle(
        GetNearbyStudiosQuery query, CancellationToken ct)
    {
        // Bounding-box pre-filter then exact Haversine in memory.
        // Approved: public discovery query — see architecture.md AllowAnonymous Exceptions.
        double latDelta = query.RadiusKm / 111.0;
        double lngDelta = query.RadiusKm / (111.0 * Math.Cos(query.Lat * Math.PI / 180.0));

        List<Studio> candidates = await db.Studios
            .IgnoreQueryFilters()
            .Where(s =>
                s.IsActive &&
                s.Latitude  >= query.Lat - latDelta && s.Latitude  <= query.Lat + latDelta &&
                s.Longitude >= query.Lng - lngDelta && s.Longitude <= query.Lng + lngDelta)
            .ToListAsync(ct);

        // Count artists per studio (published, not deleted).
        // Approved: public discovery query.
        List<Guid> studioIds = candidates.Select(s => s.Id).ToList();
        Dictionary<Guid, int> artistCounts = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => studioIds.Contains(a.StudioId) && a.DeletedAt == null && a.Slug != null)
            .GroupBy(a => a.StudioId)
            .Select(g => new { StudioId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StudioId, x => x.Count, ct);

        return candidates
            .Select(s => new
            {
                Studio   = s,
                Distance = Haversine(query.Lat, query.Lng, s.Latitude, s.Longitude),
            })
            .Where(x => x.Distance <= query.RadiusKm)
            .OrderBy(x => x.Distance)
            .Take(40)
            .Select(x => new NearbyStudioResponse(
                x.Studio.Id,
                x.Studio.Name,
                x.Studio.Slug,
                x.Studio.City,
                x.Studio.CoverImageUrl,
                Math.Round(x.Distance, 1),
                artistCounts.GetValueOrDefault(x.Studio.Id, 0)))
            .ToList();
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371.0;
        double dLat = (lat2 - lat1) * Math.PI / 180;
        double dLon = (lon2 - lon1) * Math.PI / 180;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
```

---

## Part 5 — Application: `GetStudioReviewsQuery`

**File:** `Pena_e_Arte.Application/Public/Queries/GetStudioReviewsQuery.cs` (create new)

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetStudioReviewsQuery(string Slug) : IRequest<List<ReviewResponse>>;

public class GetStudioReviewsHandler(IAppDbContext db)
    : IRequestHandler<GetStudioReviewsQuery, List<ReviewResponse>>
{
    public async Task<List<ReviewResponse>> Handle(
        GetStudioReviewsQuery query, CancellationToken ct)
    {
        // Approved: public review read — see architecture.md AllowAnonymous Exceptions.
        Domain.Entities.Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.Slug && s.IsActive, ct);

        if (studio is null) return [];

        // Reviews have no TenantId — no IgnoreQueryFilters needed.
        return await db.Reviews
            .Where(r => r.StudioId == studio.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ReviewResponse(r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt))
            .ToListAsync(ct);
    }
}
```

---

## Part 6 — Application: `GetArtistReviewsQuery`

**File:** `Pena_e_Arte.Application/Public/Queries/GetArtistReviewsQuery.cs` (create new)

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetArtistReviewsQuery(string Slug) : IRequest<List<ReviewResponse>>;

public class GetArtistReviewsHandler(IAppDbContext db)
    : IRequestHandler<GetArtistReviewsQuery, List<ReviewResponse>>
{
    public async Task<List<ReviewResponse>> Handle(
        GetArtistReviewsQuery query, CancellationToken ct)
    {
        // Approved: public review read — see architecture.md AllowAnonymous Exceptions.
        Domain.Entities.Artist? artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == query.Slug && a.DeletedAt == null, ct);

        if (artist is null) return [];

        return await db.Reviews
            .Where(r => r.ArtistId == artist.Id)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ReviewResponse(r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt))
            .ToListAsync(ct);
    }
}
```

---

## Part 7 — Application: `CreateStudioReviewCommand` + validator

**File:** `Pena_e_Arte.Application/Reviews/Commands/CreateStudioReviewCommand.cs` (create new)

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record CreateStudioReviewCommand(
    string Slug,
    Guid   AuthorUserId,
    string AuthorName,
    int    Rating,
    string Body) : IRequest;

public class CreateStudioReviewValidator : AbstractValidator<CreateStudioReviewCommand>
{
    public CreateStudioReviewValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Body)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(2000);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(200);
    }
}

public class CreateStudioReviewHandler(IAppDbContext db)
    : IRequestHandler<CreateStudioReviewCommand>
{
    public async Task Handle(CreateStudioReviewCommand command, CancellationToken ct)
    {
        // Approved: public portfolio lookup — see architecture.md AllowAnonymous Exceptions.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == command.Slug && s.IsActive, ct)
            ?? throw new NotFoundException(nameof(Studio), command.Slug);

        bool alreadyReviewed = await db.Reviews
            .AnyAsync(r => r.StudioId == studio.Id && r.AuthorUserId == command.AuthorUserId, ct);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this studio.");

        Review review = Review.ForStudio(
            studio.Id,
            command.AuthorUserId,
            command.AuthorName,
            command.Rating,
            command.Body);

        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
    }
}
```

> **Note:** If `ConflictException` does not exist in `Pena_e_Arte.Domain.Exceptions`,
> create it:
> ```csharp
> namespace Pena_e_Arte.Domain.Exceptions;
> public sealed class ConflictException(string message) : Exception(message);
> ```
> Then in `Pena_e_Arte.API` (Program.cs or exception handler middleware), map it to
> HTTP 409. If a global exception handler already exists, add the mapping there.

---

## Part 8 — Application: `CreateArtistReviewCommand` + validator

**File:** `Pena_e_Arte.Application/Reviews/Commands/CreateArtistReviewCommand.cs` (create new)

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record CreateArtistReviewCommand(
    string Slug,
    Guid   AuthorUserId,
    string AuthorName,
    int    Rating,
    string Body) : IRequest;

public class CreateArtistReviewValidator : AbstractValidator<CreateArtistReviewCommand>
{
    public CreateArtistReviewValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Body)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(2000);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(200);
    }
}

public class CreateArtistReviewHandler(IAppDbContext db)
    : IRequestHandler<CreateArtistReviewCommand>
{
    public async Task Handle(CreateArtistReviewCommand command, CancellationToken ct)
    {
        // Approved: public portfolio lookup.
        Artist artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == command.Slug && a.DeletedAt == null, ct)
            ?? throw new NotFoundException(nameof(Artist), command.Slug);

        bool alreadyReviewed = await db.Reviews
            .AnyAsync(r => r.ArtistId == artist.Id && r.AuthorUserId == command.AuthorUserId, ct);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this artist.");

        Review review = Review.ForArtist(
            artist.Id,
            command.AuthorUserId,
            command.AuthorName,
            command.Rating,
            command.Body);

        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
    }
}
```

---

## Part 9 — API: `PublicEndpoints.cs` additions

**File:** `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs` — MODIFY existing file

Add the following three endpoints inside `MapPublicEndpoints`, after the existing two:

```csharp
group.MapGet("/studios/nearby",           GetNearbyStudios).AllowAnonymous();
group.MapGet("/studios/{slug}/reviews",   GetStudioReviews).AllowAnonymous();
group.MapGet("/artists/{slug}/reviews",   GetArtistReviews).AllowAnonymous();
group.MapPost("/studios/{slug}/reviews",  CreateStudioReview).RequireAuthorization("ClientAndAbove");
group.MapPost("/artists/{slug}/reviews",  CreateArtistReview).RequireAuthorization("ClientAndAbove");
```

Add the handler methods (still inside the `PublicEndpoints` static class):

```csharp
private static async Task<IResult> GetNearbyStudios(
    double            lat,
    double            lng,
    double            radiusKm,
    ISender           mediator,
    CancellationToken ct)
{
    List<NearbyStudioResponse> result =
        await mediator.Send(new GetNearbyStudiosQuery(lat, lng, radiusKm), ct);
    return Results.Ok(result);
}

private static async Task<IResult> GetStudioReviews(
    string            slug,
    ISender           mediator,
    CancellationToken ct)
{
    List<ReviewResponse> result =
        await mediator.Send(new GetStudioReviewsQuery(slug), ct);
    return Results.Ok(result);
}

private static async Task<IResult> GetArtistReviews(
    string            slug,
    ISender           mediator,
    CancellationToken ct)
{
    List<ReviewResponse> result =
        await mediator.Send(new GetArtistReviewsQuery(slug), ct);
    return Results.Ok(result);
}

private static async Task<IResult> CreateStudioReview(
    string              slug,
    CreateReviewRequest body,
    ClaimsPrincipal     user,
    ISender             mediator,
    CancellationToken   ct)
{
    Guid   authorId   = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    string authorName = user.FindFirstValue(ClaimTypes.Name)
                     ?? user.FindFirstValue(ClaimTypes.GivenName)
                     ?? "Anonymous";

    await mediator.Send(
        new CreateStudioReviewCommand(slug, authorId, authorName, body.Rating, body.Body), ct);
    return Results.NoContent();
}

private static async Task<IResult> CreateArtistReview(
    string              slug,
    CreateReviewRequest body,
    ClaimsPrincipal     user,
    ISender             mediator,
    CancellationToken   ct)
{
    Guid   authorId   = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    string authorName = user.FindFirstValue(ClaimTypes.Name)
                     ?? user.FindFirstValue(ClaimTypes.GivenName)
                     ?? "Anonymous";

    await mediator.Send(
        new CreateArtistReviewCommand(slug, authorId, authorName, body.Rating, body.Body), ct);
    return Results.NoContent();
}
```

Add required `using` statements at the top of the file:
```csharp
using System.Security.Claims;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Contracts.Requests;
```

---

## Part 10 — Frontend: `publicApi.ts` additions

**File:** `frontend/src/features/public/publicApi.ts` — MODIFY existing file

### 10a — New interfaces (add after existing interfaces)

```ts
export interface NearbyStudioResponse {
  studioId:     string;
  name:         string;
  slug:         string;
  city:         string;
  coverImageUrl: string | null;
  distanceKm:   number;
  artistCount:  number;
}

export interface ReviewResponse {
  id:         string;
  authorName: string;
  rating:     number;
  body:       string;
  createdAt:  string;
}

export interface NearbyStudiosArgs {
  lat:      number;
  lng:      number;
  radiusKm: number;
}

export interface CreateReviewArgs {
  slug:   string;
  rating: number;
  body:   string;
}
```

### 10b — Add tag types

In the `createApi` call, add `"NearbyStudios"`, `"StudioReviews"`, `"ArtistReviews"` to `tagTypes`.

### 10c — New endpoints

Add inside the `endpoints` builder:
```ts
getNearbyStudios: builder.query<NearbyStudioResponse[], NearbyStudiosArgs>({
  query: ({ lat, lng, radiusKm }) =>
    `studios/nearby?lat=${lat}&lng=${lng}&radiusKm=${radiusKm}`,
  providesTags: ["NearbyStudios"],
}),
getStudioReviews: builder.query<ReviewResponse[], string>({
  query: (slug) => `studios/${slug}/reviews`,
  providesTags: (_result, _err, slug) => [{ type: "StudioReviews", id: slug }],
}),
getArtistReviews: builder.query<ReviewResponse[], string>({
  query: (slug) => `artists/${slug}/reviews`,
  providesTags: (_result, _err, slug) => [{ type: "ArtistReviews", id: slug }],
}),
createStudioReview: builder.mutation<void, CreateReviewArgs>({
  query: ({ slug, rating, body }) => ({
    url:    `studios/${slug}/reviews`,
    method: "POST",
    body:   { rating, body },
  }),
  invalidatesTags: (_result, _err, { slug }) => [{ type: "StudioReviews", id: slug }],
}),
createArtistReview: builder.mutation<void, CreateReviewArgs>({
  query: ({ slug, rating, body }) => ({
    url:    `artists/${slug}/reviews`,
    method: "POST",
    body:   { rating, body },
  }),
  invalidatesTags: (_result, _err, { slug }) => [{ type: "ArtistReviews", id: slug }],
}),
```

### 10d — Export new hooks

Add to the existing export block:
```ts
export const {
  useGetPublicStudioQuery,
  useGetPublicArtistQuery,
  useGetSharedDesignQuery,
  useGetNearbyStudiosQuery,
  useGetStudioReviewsQuery,
  useGetArtistReviewsQuery,
  useCreateStudioReviewMutation,
  useCreateArtistReviewMutation,
} = publicApi;
```

> **Important:** `publicApi` uses `baseUrl: "/api/v1/public/"`. The review POST
> endpoints are also mapped under `/api/v1/public/`, so the mutation URLs are correct.

---

## Part 11 — Frontend: `StarRating.tsx` shared component

**File:** `frontend/src/shared/components/ui/StarRating.tsx` (create new)

```tsx
import { Star } from "lucide-react";

interface StarRatingProps {
  value:       number;
  max?:        number;
  interactive?: false;
  className?:  string;
}

interface InteractiveStarRatingProps {
  value:        number;
  max?:         number;
  interactive:  true;
  onChange:     (rating: number) => void;
  className?:   string;
}

type Props = StarRatingProps | InteractiveStarRatingProps;

export function StarRating(props: Props) {
  const { value, max = 5, className = "" } = props;

  return (
    <div
      className={`flex gap-0.5 ${className}`}
      aria-label={`Rating: ${value} out of ${max}`}
      role={props.interactive ? "radiogroup" : "img"}
    >
      {Array.from({ length: max }, (_, i) => {
        const filled = i < value;
        if (props.interactive) {
          const rating = i + 1;
          return (
            <button
              key={i}
              type="button"
              aria-label={`Rate ${rating} out of ${max}`}
              aria-pressed={filled}
              onClick={() => props.onChange(rating)}
              className="focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-sm"
            >
              <Star
                className={`h-5 w-5 transition-colors ${
                  filled
                    ? "text-amber-400 fill-amber-400"
                    : "text-muted-foreground hover:text-amber-300"
                }`}
              />
            </button>
          );
        }

        return (
          <Star
            key={i}
            className={`h-3.5 w-3.5 ${
              filled ? "text-amber-400 fill-amber-400" : "text-muted-foreground/40"
            }`}
          />
        );
      })}
    </div>
  );
}
```

---

## Part 12 — Frontend: `ReviewSection.tsx`

**File:** `frontend/src/features/public/components/ReviewSection.tsx` (create new)

Accepts `slug`, `target` (`"studio"` | `"artist"`), and `token` (from Redux state).
On submit: if `token` is falsy, redirect to `/login?redirect=...` — never actually
submit anonymously.

```tsx
import { useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { MessageSquare } from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { StarRating } from "@/shared/components/ui/StarRating";
import {
  useGetStudioReviewsQuery,
  useGetArtistReviewsQuery,
  useCreateStudioReviewMutation,
  useCreateArtistReviewMutation,
  type ReviewResponse,
} from "../publicApi";

function ReviewCard({ review }: { review: ReviewResponse }) {
  return (
    <div className="py-4 border-b last:border-b-0 space-y-1.5">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <span className="text-sm font-medium">{review.authorName}</span>
        <div className="flex items-center gap-2">
          <StarRating value={review.rating} />
          <span className="text-xs text-muted-foreground">
            {new Date(review.createdAt).toLocaleDateString("en-GB", {
              day: "numeric", month: "short", year: "numeric",
            })}
          </span>
        </div>
      </div>
      <p className="text-sm text-muted-foreground whitespace-pre-wrap">{review.body}</p>
    </div>
  );
}

function ReviewsSkeleton() {
  return (
    <div className="space-y-4" aria-label="Loading reviews">
      {Array.from({ length: 3 }).map((_, i) => (
        <div key={i} className="py-4 border-b space-y-2">
          <div className="flex items-center justify-between">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-3.5 w-20" />
          </div>
          <Skeleton className="h-12 w-full" />
        </div>
      ))}
    </div>
  );
}

interface Props {
  slug:   string;
  target: "studio" | "artist";
  token:  string | null;
}

export function ReviewSection({ slug, target, token }: Props) {
  const navigate = useNavigate();

  const [rating,  setRating]  = useState(0);
  const [body,    setBody]    = useState("");
  const [error,   setError]   = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  const isStudio = target === "studio";

  const { data: reviews, isLoading } = isStudio
    ? useGetStudioReviewsQuery(slug)                     // eslint-disable-line react-hooks/rules-of-hooks
    : useGetArtistReviewsQuery(slug);                    // eslint-disable-line react-hooks/rules-of-hooks

  // Note: conditional hook call above is safe here because `target` is a stable
  // prop that never changes for a given component mount.
  // If linting objects, extract into two separate wrapper components.

  const [createStudioReview, { isLoading: isStudioSubmitting }] =
    useCreateStudioReviewMutation();
  const [createArtistReview, { isLoading: isArtistSubmitting }] =
    useCreateArtistReviewMutation();

  const isSubmitting = isStudio ? isStudioSubmitting : isArtistSubmitting;

  const averageRating = reviews && reviews.length > 0
    ? reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length
    : null;

  function handleSubmit() {
    if (!token) {
      const returnUrl = isStudio ? `/s/${slug}` : `/artist/${slug}`;
      navigate(`/login?redirect=${encodeURIComponent(returnUrl)}`);
      return;
    }

    if (rating === 0) { setError("Please select a star rating."); return; }
    if (body.trim().length < 10) { setError("Review must be at least 10 characters."); return; }

    setError(null);
    const mutation = isStudio ? createStudioReview : createArtistReview;
    mutation({ slug, rating, body: body.trim() })
      .unwrap()
      .then(() => {
        setSuccess(true);
        setBody("");
        setRating(0);
      })
      .catch((err: { status?: number }) => {
        if (err.status === 409) {
          setError("You have already left a review.");
        } else {
          setError("Failed to submit review. Please try again.");
        }
      });
  }

  return (
    <section className="space-y-5" aria-labelledby="reviews-heading">
      <div className="flex items-center gap-2">
        <MessageSquare className="h-4 w-4 text-muted-foreground" />
        <h2 id="reviews-heading" className="text-sm font-medium">
          Reviews
          {averageRating !== null && (
            <span className="ml-2 text-muted-foreground font-normal">
              {averageRating.toFixed(1)} / 5 · {reviews!.length} review{reviews!.length !== 1 ? "s" : ""}
            </span>
          )}
        </h2>
      </div>

      {/* Write-a-review box */}
      {success ? (
        <p className="text-sm text-green-600 dark:text-green-400 py-2">
          Thanks for your review!
        </p>
      ) : (
        <div className="rounded-lg border p-4 space-y-3 bg-muted/30">
          <p className="text-xs text-muted-foreground font-medium tracking-wide uppercase">
            Write a review
          </p>

          <StarRating
            value={rating}
            interactive
            onChange={(r) => { setRating(r); setError(null); }}
          />

          <textarea
            className="w-full min-h-[80px] resize-none rounded-md border bg-background px-3 py-2 text-sm
                       focus:outline-none focus:ring-1 focus:ring-ring placeholder:text-muted-foreground"
            placeholder="Share your experience…"
            maxLength={2000}
            value={body}
            onChange={(e) => setBody(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                handleSubmit();
              }
            }}
            aria-label="Review text"
          />

          {error && (
            <p className="text-xs text-destructive" role="alert">{error}</p>
          )}

          {!token && (
            <p className="text-xs text-muted-foreground">
              <Link to="/login" className="underline underline-offset-2">Sign in</Link>
              {" "}to post your review.
            </p>
          )}

          <Button
            size="sm"
            onClick={handleSubmit}
            disabled={isSubmitting}
            aria-label="Submit review"
          >
            {isSubmitting ? "Submitting…" : token ? "Submit review" : "Sign in to review"}
          </Button>
        </div>
      )}

      {/* Reviews list */}
      {isLoading ? (
        <ReviewsSkeleton />
      ) : !reviews || reviews.length === 0 ? (
        <p className="text-sm text-muted-foreground py-4">
          No reviews yet. Be the first to leave one.
        </p>
      ) : (
        <div>
          {reviews.map((r) => (
            <ReviewCard key={r.id} review={r} />
          ))}
        </div>
      )}
    </section>
  );
}
```

> **Conditional hook note:** The `useGetStudioReviewsQuery` / `useGetArtistReviewsQuery`
> pattern above violates the rules-of-hooks if `target` is genuinely conditional.
> If the linter objects, refactor: create two sub-components `StudioReviewSection` and
> `ArtistReviewSection` that each call the correct hook unconditionally, then have
> `ReviewSection` render one or the other based on `target`. Prefer whichever passes
> lint cleanly.

---

## Part 13 — Frontend: `DiscoverPage.tsx`

**File:** `frontend/src/features/public/components/DiscoverPage.tsx` (create new)

```tsx
import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { MapPin, PenLine, Search, Users } from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent } from "@/shared/components/ui/card";
import { useGetNearbyStudiosQuery, type NearbyStudioResponse } from "../publicApi";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

const RADII = [10, 25, 50, 100] as const;
type Radius = (typeof RADII)[number];

// Default: Lisbon — shown only if geolocation is denied or timed out.
const DEFAULT_LAT = 38.7169;
const DEFAULT_LNG = -9.1395;

interface NominatimResult {
  lat:          string;
  lon:          string;
  display_name: string;
}

function DiscoverMeta() {
  useDocumentMeta({
    title:       "Find Tattoo Studios Near You — Pena e Artë",
    description: "Browse tattoo studios and artists near your location.",
    canonical:   "https://penaearte.com/discover",
  });
  return null;
}

function StudioCard({ studio }: { studio: NearbyStudioResponse }) {
  return (
    <Link to={`/s/${studio.slug}`}>
      <Card className="hover:border-ring transition-colors cursor-pointer h-full">
        {studio.coverImageUrl ? (
          <div className="h-32 bg-muted overflow-hidden rounded-t-lg">
            <img
              src={studio.coverImageUrl}
              alt={studio.name}
              className="w-full h-full object-cover"
              loading="lazy"
            />
          </div>
        ) : (
          <div className="h-32 bg-muted rounded-t-lg flex items-center justify-center">
            <PenLine className="h-8 w-8 text-muted-foreground/30" />
          </div>
        )}
        <CardContent className="p-4 space-y-1.5">
          <p className="font-medium text-sm leading-tight">{studio.name}</p>
          <div className="flex items-center gap-1 text-xs text-muted-foreground">
            <MapPin className="h-3 w-3 shrink-0" />
            <span>{studio.city}</span>
            <span className="ml-auto text-primary font-medium">
              {studio.distanceKm < 1
                ? `${Math.round(studio.distanceKm * 1000)} m`
                : `${studio.distanceKm} km`}
            </span>
          </div>
          {studio.artistCount > 0 && (
            <div className="flex items-center gap-1 text-xs text-muted-foreground">
              <Users className="h-3 w-3 shrink-0" />
              <span>{studio.artistCount} artist{studio.artistCount !== 1 ? "s" : ""}</span>
            </div>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}

function DiscoverSkeleton() {
  return (
    <div
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
      aria-label="Loading studios"
    >
      {Array.from({ length: 6 }).map((_, i) => (
        <div key={i} className="space-y-0">
          <Skeleton className="h-32 w-full rounded-t-lg" />
          <div className="border border-t-0 rounded-b-lg p-4 space-y-2">
            <Skeleton className="h-4 w-32" />
            <Skeleton className="h-3 w-24" />
          </div>
        </div>
      ))}
    </div>
  );
}

export function DiscoverPage() {
  const [lat,          setLat]          = useState<number | null>(null);
  const [lng,          setLng]          = useState<number | null>(null);
  const [locationName, setLocationName] = useState<string>("Detecting location…");
  const [radiusKm,     setRadiusKm]     = useState<Radius>(50);
  const [searchInput,  setSearchInput]  = useState<string>("");
  const [searchError,  setSearchError]  = useState<string | null>(null);
  const [isGeocoding,  setIsGeocoding]  = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  // Geolocation — browser API side-effect (not data fetching).
  useEffect(() => {
    if (!("geolocation" in navigator)) {
      setLat(DEFAULT_LAT);
      setLng(DEFAULT_LNG);
      setLocationName("Lisbon, Portugal");
      return;
    }

    navigator.geolocation.getCurrentPosition(
      (pos) => {
        setLat(pos.coords.latitude);
        setLng(pos.coords.longitude);
        setLocationName("Your location");
      },
      () => {
        setLat(DEFAULT_LAT);
        setLng(DEFAULT_LNG);
        setLocationName("Lisbon, Portugal");
      },
      { timeout: 8000, maximumAge: 60_000 },
    );
  }, []);

  const { data: studios, isLoading: isStudiosLoading, isFetching } =
    useGetNearbyStudiosQuery(
      { lat: lat!, lng: lng!, radiusKm },
      { skip: lat === null || lng === null },
    );

  // Nominatim forward geocoding — inside an event handler (not useEffect).
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
      const results: NominatimResult[] = await res.json() as NominatimResult[];

      if (results.length === 0) {
        setSearchError("Location not found. Try a different city name.");
        return;
      }

      const [first] = results;
      setLat(parseFloat(first.lat));
      setLng(parseFloat(first.lon));
      setLocationName(first.display_name.split(",").slice(0, 2).join(", "));
      setSearchInput("");
    } catch {
      setSearchError("Could not reach location service. Try again.");
    } finally {
      setIsGeocoding(false);
    }
  }

  const isLoading = lat === null || isStudiosLoading || isFetching;

  return (
    <div className="min-h-screen bg-background">
      <DiscoverMeta />

      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-[100]">
        <div className="flex items-center gap-2">
          <PenLine className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Pena e Artë</span>
        </div>
        <nav className="flex items-center gap-3">
          <Link
            to="/map"
            className="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            Map view
          </Link>
          <Link
            to="/login"
            className="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            Sign in
          </Link>
          <Link
            to="/register"
            className="text-sm font-medium bg-foreground text-background px-3 py-1.5 rounded-md hover:opacity-90 transition-opacity"
          >
            Register your studio
          </Link>
        </nav>
      </header>

      <main className="max-w-5xl mx-auto px-4 py-8 space-y-6">
        <div className="space-y-1">
          <h1 className="text-2xl font-bold tracking-tight">Find tattoo studios near you</h1>
          {lat !== null && (
            <p className="text-sm text-muted-foreground flex items-center gap-1">
              <MapPin className="h-3.5 w-3.5" />
              {locationName}
            </p>
          )}
        </div>

        {/* Location search + radius */}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <div className="flex flex-1 gap-2">
            <input
              ref={inputRef}
              type="text"
              placeholder="Search a city or address…"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") void handleLocationSearch(); }}
              className="flex-1 h-9 rounded-md border bg-background px-3 text-sm
                         focus:outline-none focus:ring-1 focus:ring-ring
                         placeholder:text-muted-foreground"
              aria-label="Search location"
            />
            <Button
              size="sm"
              variant="outline"
              onClick={() => void handleLocationSearch()}
              disabled={isGeocoding || !searchInput.trim()}
              aria-label="Search"
            >
              <Search className="h-4 w-4" />
            </Button>
          </div>

          <div className="flex items-center gap-2">
            <label htmlFor="radius-select" className="text-xs text-muted-foreground whitespace-nowrap">
              Within
            </label>
            <select
              id="radius-select"
              value={radiusKm}
              onChange={(e) => setRadiusKm(parseInt(e.target.value, 10) as Radius)}
              className="h-9 rounded-md border bg-background px-2 text-sm text-foreground
                         focus:outline-none focus:ring-1 focus:ring-ring"
            >
              {RADII.map((r) => (
                <option key={r} value={r}>{r} km</option>
              ))}
            </select>
          </div>
        </div>

        {searchError && (
          <p className="text-xs text-destructive" role="alert">{searchError}</p>
        )}

        {/* Results */}
        {isLoading ? (
          <DiscoverSkeleton />
        ) : !studios || studios.length === 0 ? (
          <div className="flex flex-col items-center gap-3 py-16 text-center">
            <MapPin className="h-9 w-9 text-muted-foreground/40" />
            <p className="text-sm font-medium">No studios found nearby</p>
            <p className="text-xs text-muted-foreground">
              Try increasing the search radius or searching a different location.
            </p>
          </div>
        ) : (
          <>
            <p className="text-xs text-muted-foreground">
              {studios.length} studio{studios.length !== 1 ? "s" : ""} within {radiusKm} km
            </p>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {studios.map((s) => (
                <StudioCard key={s.studioId} studio={s} />
              ))}
            </div>
          </>
        )}
      </main>

      <footer className="py-3 text-center text-xs text-muted-foreground border-t mt-8">
        <a
          href="https://penaearte.com"
          target="_blank"
          rel="noopener noreferrer"
          className="hover:underline"
        >
          Powered by Pena e Artë
        </a>
      </footer>
    </div>
  );
}
```

---

## Part 14 — Frontend: Update `StudioPortfolioPage.tsx`

**File:** `frontend/src/features/public/components/StudioPortfolioPage.tsx` — MODIFY

### 14a — Import additions
```tsx
import { useAppSelector } from "@/app/hooks";   // already present
import { ReviewSection }  from "./ReviewSection";
```

`useAppSelector` is already imported. Just add `ReviewSection`.

### 14b — Token access
`token` is already computed on line 42: `const token = useAppSelector((s) => s.auth.token);`

### 14c — Add `ReviewSection` before `</div>` that closes `max-w-2xl` main content

Inside the `max-w-2xl` container, after the artists grid section:
```tsx
<ReviewSection slug={studio.slug} target="studio" token={token} />
```

Place this as the last child of `<div className="max-w-2xl mx-auto px-4 py-8 space-y-6">`.

---

## Part 15 — Frontend: Update `ArtistPortfolioPage.tsx`

**File:** `frontend/src/features/public/components/ArtistPortfolioPage.tsx` — MODIFY

### 15a — Import additions
```tsx
import { ReviewSection } from "./ReviewSection";
```

### 15b — Add `ReviewSection` inside the `max-w-2xl` content div, after the portfolio images section:
```tsx
<ReviewSection slug={artist.slug} target="artist" token={token} />
```

`token` is already declared: `const token = useAppSelector((s) => s.auth.token);`

---

## Part 16 — Frontend: Router + exports

### 16a — `frontend/src/app/router.tsx` — MODIFY

Add the import at the top:
```tsx
import { DiscoverPage } from "@/features/public";
```

Add the route (alongside the other standalone public routes, before `AppRoot`):
```tsx
{ path: "/discover", element: <DiscoverPage /> },
```

### 16b — `frontend/src/features/public/index.ts` — MODIFY

Verify this file exports everything. Add:
```ts
export { DiscoverPage } from "./components/DiscoverPage";
export { ReviewSection } from "./components/ReviewSection";
```

### 16c — `StudioMapPage.tsx` — add "List view" link

In `frontend/src/features/map/components/StudioMapPage.tsx`, inside the `<nav>` in the
header, add before the "Sign in" link:
```tsx
<Link
  to="/discover"
  className="text-sm text-muted-foreground hover:text-foreground transition-colors"
>
  List view
</Link>
```

---

## Part 17 — Tests

### 17a — `GetNearbyStudiosHandlerTests.cs` (create new)

**File:** `tests/Pena_e_Arte.UnitTests/Public/GetNearbyStudiosHandlerTests.cs`

```csharp
using FluentAssertions;
using MockQueryable;
using Moq;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.UnitTests.Public;

public class GetNearbyStudiosHandlerTests
{
    private static Studio MakeStudio(Guid id, double lat, double lng, bool isActive = true) =>
        // Use whatever factory/reflection pattern the project uses for creating test entities.
        // If the project uses object initializers on public setters, use those.
        // Otherwise, see existing handler tests for the established pattern.
        new()
        {
            // Set all required properties. If Studio's setters are private,
            // read an existing handler test in the project to find the mock pattern used.
        };

    [Fact]
    public async Task Returns_studios_within_radius()
    {
        // Arrange
        Guid id1 = Guid.NewGuid();
        Guid id2 = Guid.NewGuid();

        // Lisbon lat/lng; studio 1 is ~0 km away, studio 2 is ~1000 km away.
        List<Studio> studios =
        [
            MakeStudio(id1, 38.7169, -9.1395),   // Lisbon — inside 50 km
            MakeStudio(id2, 52.5200,  13.4050),  // Berlin — outside 50 km
        ];

        Mock<IAppDbContext> dbMock = new();
        dbMock.Setup(d => d.Studios).Returns(studios.AsQueryable().BuildMockDbSet().Object);
        dbMock.Setup(d => d.Artists).Returns(new List<Artist>().AsQueryable().BuildMockDbSet().Object);

        GetNearbyStudiosHandler sut = new(dbMock.Object);

        // Act
        List<NearbyStudioResponse> result = await sut.Handle(
            new GetNearbyStudiosQuery(38.7169, -9.1395, 50), CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].StudioId.Should().Be(id1);
        result[0].DistanceKm.Should().BeLessThan(1);
    }

    [Fact]
    public async Task Returns_empty_when_no_studios_in_radius()
    {
        Mock<IAppDbContext> dbMock = new();
        dbMock.Setup(d => d.Studios).Returns(new List<Studio>().AsQueryable().BuildMockDbSet().Object);
        dbMock.Setup(d => d.Artists).Returns(new List<Artist>().AsQueryable().BuildMockDbSet().Object);

        GetNearbyStudiosHandler sut = new(dbMock.Object);

        List<NearbyStudioResponse> result = await sut.Handle(
            new GetNearbyStudiosQuery(38.7169, -9.1395, 50), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Inactive_studios_excluded()
    {
        List<Studio> studios = [ MakeStudio(Guid.NewGuid(), 38.7169, -9.1395, isActive: false) ];

        Mock<IAppDbContext> dbMock = new();
        dbMock.Setup(d => d.Studios).Returns(studios.AsQueryable().BuildMockDbSet().Object);
        dbMock.Setup(d => d.Artists).Returns(new List<Artist>().AsQueryable().BuildMockDbSet().Object);

        GetNearbyStudiosHandler sut = new(dbMock.Object);

        List<NearbyStudioResponse> result = await sut.Handle(
            new GetNearbyStudiosQuery(38.7169, -9.1395, 50), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
```

> **Note:** Read an existing handler test (e.g. `ReviewDesignHandlerTests.cs`) to find
> how the project creates domain entities in tests (public setters vs. factory methods
> vs. reflection). Replicate that pattern for `MakeStudio` above.

### 17b — `CreateStudioReviewHandlerTests.cs` (create new)

**File:** `tests/Pena_e_Arte.UnitTests/Reviews/CreateStudioReviewHandlerTests.cs`

Tests to write (read `ReviewDesignHandlerTests.cs` for established mock patterns):

1. **`Creates_review_when_studio_exists_and_no_prior_review`** — happy path, verifies
   `db.Reviews.Add` was called once with correct `StudioId`, `Rating`, `Body`.

2. **`Throws_NotFoundException_when_studio_not_found`** — `GetPublicStudioQuery` returns
   null (no matching studio); handler must throw `NotFoundException`.

3. **`Throws_ConflictException_when_user_already_reviewed`** — reviews table already has
   one entry with matching `StudioId` + `AuthorUserId`; handler must throw
   `ConflictException`.

4. **`Validator_rejects_rating_below_1`** — `CreateStudioReviewValidator` with `Rating=0`.

5. **`Validator_rejects_body_shorter_than_10_chars`** — `CreateStudioReviewValidator`
   with `Body="short"`.

### 17c — Frontend: `DiscoverPage.test.tsx` (create new)

**File:** `frontend/src/features/public/__tests__/DiscoverPage.test.tsx`

Read `StudioPortfolioPage.test.tsx` and `ArtistPortfolioPage.test.tsx` to understand the
mock and render pattern used. Then write these three tests:

1. **`Shows skeleton while geolocation is pending`** — mock `navigator.geolocation` so the
   `getCurrentPosition` callback is never called; the RTK Query skip condition keeps the
   query from firing. Assert `aria-label="Loading studios"` is present.

2. **`Renders studio cards after data loads`** — supply geolocation coordinates immediately,
   mock `useGetNearbyStudiosQuery` to return two studio records, assert both studio names
   appear in the document.

3. **`Shows empty state when no studios found`** — geolocation resolves, query returns `[]`,
   assert "No studios found nearby" text is visible.

---

## Part 18 — Verification checklist

After all code is written and the migration is applied, verify:

- [ ] `dotnet build` — zero errors, zero warnings.
- [ ] `dotnet test` — all tests green including the new ones.
- [ ] `pnpm build` — zero TypeScript errors.
- [ ] `pnpm lint` — zero lint errors.
- [ ] `GET /api/v1/public/studios/nearby?lat=38.7&lng=-9.1&radiusKm=50` returns JSON.
- [ ] `GET /api/v1/public/studios/{slug}/reviews` returns `[]` for a new studio.
- [ ] `POST /api/v1/public/studios/{slug}/reviews` without auth returns 401.
- [ ] `/discover` page renders without console errors.
- [ ] `/s/{slug}` page shows the `ReviewSection` component.
- [ ] `/artist/{slug}` page shows the `ReviewSection` component.
- [ ] Submitting the review textarea while logged out calls `navigate("/login?redirect=...")`.
- [ ] Submitting a second review for the same studio returns 409 Conflict and shows
      "You have already left a review." in the UI.
- [ ] `StarRating` interactive mode: clicking star 3 then star 5 updates the rating
      display correctly.

---

## Summary of new/modified files

### New backend files
```
Pena_e_Arte.Domain/Entities/Review.cs
Pena_e_Arte.Domain/Exceptions/ConflictException.cs          ← only if it doesn't exist
Pena_e_Arte.Application/Public/Queries/GetNearbyStudiosQuery.cs
Pena_e_Arte.Application/Public/Queries/GetStudioReviewsQuery.cs
Pena_e_Arte.Application/Public/Queries/GetArtistReviewsQuery.cs
Pena_e_Arte.Application/Reviews/Commands/CreateStudioReviewCommand.cs
Pena_e_Arte.Application/Reviews/Commands/CreateArtistReviewCommand.cs
Pena_e_Arte.Contracts/Responses/Public/NearbyStudioResponse.cs
Pena_e_Arte.Contracts/Responses/Public/ReviewResponse.cs
Pena_e_Arte.Contracts/Requests/CreateReviewRequest.cs
Pena_e_Arte.Infrastructure/Persistence/Migrations/<timestamp>_AddReviews.cs  ← generated
tests/Pena_e_Arte.UnitTests/Public/GetNearbyStudiosHandlerTests.cs
tests/Pena_e_Arte.UnitTests/Reviews/CreateStudioReviewHandlerTests.cs
```

### Modified backend files
```
Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs      ← DbSet<Review>, entity config
Pena_e_Arte.Application/Persistence/IAppDbContext.cs        ← DbSet<Review>
Pena_e_Arte.API/Endpoints/PublicEndpoints.cs                ← 5 new routes + handlers
Pena_e_Arte.API/Program.cs or exception middleware           ← map ConflictException → 409
```

### New frontend files
```
frontend/src/features/public/components/DiscoverPage.tsx
frontend/src/features/public/components/ReviewSection.tsx
frontend/src/shared/components/ui/StarRating.tsx
frontend/src/features/public/__tests__/DiscoverPage.test.tsx
```

### Modified frontend files
```
frontend/src/features/public/publicApi.ts          ← 5 new endpoints + types
frontend/src/features/public/index.ts              ← export DiscoverPage, ReviewSection
frontend/src/features/public/components/StudioPortfolioPage.tsx  ← add ReviewSection
frontend/src/features/public/components/ArtistPortfolioPage.tsx  ← add ReviewSection
frontend/src/app/router.tsx                         ← add /discover route
frontend/src/features/map/components/StudioMapPage.tsx  ← add "List view" link
```
