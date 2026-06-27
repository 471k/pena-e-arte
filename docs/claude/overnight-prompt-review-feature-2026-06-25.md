# Overnight Prompt — Review Feature
**Goal:** Allow authenticated users to leave reviews on (1) individual portfolio tattoo images and
(2) tattoo artists. Artist reviews already exist in the codebase — this prompt extends the system
to cover per-image reviews and makes both review surfaces prominent and accessible.

---

## Read First

1. `CLAUDE.md`
2. `docs/claude/backend.md`
3. `docs/claude/frontend.md`
4. `docs/claude/database.md`
5. `docs/claude/architecture.md`
6. `docs/claude/conventions.md`

---

## Current State

| What exists | Status |
|---|---|
| `Review` entity with `StudioId?` / `ArtistId?` | ✅ Done |
| `CreateArtistReviewCommand` + handler + validator | ✅ Done |
| `CreateStudioReviewCommand` + handler + validator | ✅ Done |
| `GetArtistReviewsQuery` / `GetStudioReviewsQuery` | ✅ Done |
| Public endpoints: `POST /artists/{slug}/reviews`, `GET /artists/{slug}/reviews` | ✅ Done |
| `ReviewSection.tsx` component (supports `target: "studio" \| "artist"`) | ✅ Done |
| `publicApi.ts` RTK Query hooks for studio + artist reviews | ✅ Done |
| Reviews on individual portfolio images | ❌ Does not exist |
| `PortfolioImage` as a first-class entity with its own ID | ❌ Does not exist |

**Core gap:** `Artist.PortfolioImages` is a `List<string>` (raw image URLs stored as JSON column).
Individual images have no identity, so they cannot receive reviews. This prompt adds a
`PortfolioImage` entity that gives each image a stable `Guid`, then layers reviews on top.

---

## Architecture Decisions

1. **`PortfolioImage` entity** — a `TenantEntity` subclass with its own `Id`. Replaces the raw
   `List<string>` on `Artist`. All new portfolio image uploads will create a `PortfolioImage` row.

2. **Backward compatibility** — existing images stored in `Artist.PortfolioImages` are migrated
   to `PortfolioImage` rows during the EF Core migration via a data migration script embedded in
   the migration file. Any image that existed before this migration will receive a generated `Id`
   and can immediately receive reviews.

3. **`Artist.PortfolioImages`** — remove this `List<string>` property after migration. All
   portfolio image access goes through the `PortfolioImage` entity.

4. **Review entity** — add `PortfolioImageId?: Guid?` and `ForPortfolioImage()` factory. One of
   `StudioId`, `ArtistId`, or `PortfolioImageId` must be set. Add a check constraint or domain
   guard.

5. **One review per user per target** — already enforced for studios and artists. Same rule for
   portfolio images.

6. **Who can review?** — `ClientAndAbove` (authenticated). Anonymous visitors see reviews but
   cannot post.

---

## Phase A — Domain Layer

### A-1: New entity `PortfolioImage`

Create `Pena_e_Arte.Domain/Entities/PortfolioImage.cs`:

```csharp
namespace Pena_e_Arte.Domain.Entities;

public class PortfolioImage : TenantEntity
{
    public Guid     ArtistId  { get; set; }
    public string   ImageUrl  { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Artist Artist { get; set; } = null!;
}
```

### A-2: Update `Review.cs`

Read the current file. Add `PortfolioImageId` and a new factory method:

```csharp
public Guid? PortfolioImageId { get; private set; }

public static Review ForPortfolioImage(
    Guid imageId, Guid authorUserId, string authorName, int rating, string body)
    => new()
    {
        PortfolioImageId = imageId,
        AuthorUserId     = authorUserId,
        AuthorName       = authorName,
        Rating           = rating,
        Body             = body.Trim(),
    };
```

Also add a domain guard to the constructor to prevent reviews with no target:

```csharp
// Private constructor used by factory methods only.
private Review()
{
    // Guard applied after object construction via Validate().
}

private void Validate()
{
    int targets = (StudioId.HasValue ? 1 : 0)
                + (ArtistId.HasValue ? 1 : 0)
                + (PortfolioImageId.HasValue ? 1 : 0);

    if (targets != 1)
        throw new InvalidOperationException(
            "A Review must target exactly one of StudioId, ArtistId, or PortfolioImageId.");
}
```

Call `Validate()` at the end of each factory method.

### A-3: Update `Artist.cs`

Read the current file. Remove `public List<string> PortfolioImages { get; set; } = [];`
and add a navigation property:

```csharp
public ICollection<PortfolioImage> Portfolio { get; set; } = [];
```

---

## Phase B — Persistence Layer

### B-1: Update `IAppDbContext.cs`

Read the current file. Add `PortfolioImage` under the "Tenant-scoped" section:

```csharp
DbSet<PortfolioImage> PortfolioImages { get; }
```

### B-2: Update `AppDbContext.cs`

Read the implementation file. Add:

```csharp
public DbSet<PortfolioImage> PortfolioImages => Set<PortfolioImage>();
```

Add EF Core configuration in `OnModelCreating`:

```csharp
builder.Entity<PortfolioImage>(b =>
{
    b.ToTable("PortfolioImages");
    b.HasKey(p => p.Id);
    b.Property(p => p.ImageUrl).HasMaxLength(2048).IsRequired();
    b.Property(p => p.CreatedAt).HasDefaultValueSql("UTC_TIMESTAMP()");
    b.HasQueryFilter(p => p.TenantId == _currentTenant.StudioId);

    b.HasOne(p => p.Artist)
     .WithMany(a => a.Portfolio)
     .HasForeignKey(p => p.ArtistId)
     .OnDelete(DeleteBehavior.Cascade);
});
```

Add the `PortfolioImageId` FK configuration on the `Review` entity:

```csharp
builder.Entity<Review>(b =>
{
    // ... existing config ...
    b.HasOne<PortfolioImage>()
     .WithMany()
     .HasForeignKey(r => r.PortfolioImageId)
     .OnDelete(DeleteBehavior.Cascade)
     .IsRequired(false);
});
```

Remove the `Artist.PortfolioImages` JSON column configuration (the `List<string>` property is gone).

### B-3: EF Core Migration

```bash
cd "Pena e Arte"
dotnet ef migrations add AddPortfolioImageEntity \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

The generated migration will create the `PortfolioImages` table and add `PortfolioImageId` to
`Reviews`. Before running `database update`, edit the generated migration's `Up` method to add a
**data migration** that seeds `PortfolioImage` rows from the existing JSON data on `Artists`:

```csharp
// After creating the PortfolioImages table, migrate existing artist portfolio URLs.
migrationBuilder.Sql(@"
    INSERT INTO PortfolioImages (Id, TenantId, ArtistId, ImageUrl, CreatedAt)
    SELECT
        UNHEX(REPLACE(UUID(), '-', '')),
        a.TenantId,
        a.Id,
        img.value,
        UTC_TIMESTAMP()
    FROM Artists a
    CROSS JOIN JSON_TABLE(
        COALESCE(a.PortfolioImages, '[]'),
        '$[*]' COLUMNS (value VARCHAR(2048) PATH '$')
    ) AS img
    WHERE a.PortfolioImages IS NOT NULL
      AND JSON_LENGTH(a.PortfolioImages) > 0;
");

// Drop the now-redundant JSON column (data is safe in PortfolioImages table).
migrationBuilder.DropColumn(table: "Artists", name: "PortfolioImages");
```

And in `Down`:

```csharp
migrationBuilder.AddColumn<string>(
    name:      "PortfolioImages",
    table:     "Artists",
    type:      "json",
    nullable:  true);

migrationBuilder.Sql(@"
    UPDATE Artists a
    SET a.PortfolioImages = (
        SELECT JSON_ARRAYAGG(pi.ImageUrl)
        FROM PortfolioImages pi
        WHERE pi.ArtistId = a.Id
    );
");

migrationBuilder.DropTable(name: "PortfolioImages");
```

Run the migration:

```bash
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API
```

---

## Phase C — Application Layer

### C-1: `CreatePortfolioImageReviewCommand`

Create `Pena_e_Arte.Application/Reviews/Commands/CreatePortfolioImageReviewCommand.cs`:

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record CreatePortfolioImageReviewCommand(
    Guid   ImageId,
    Guid   AuthorUserId,
    string AuthorName,
    int    Rating,
    string Body) : IRequest;

public class CreatePortfolioImageReviewValidator
    : AbstractValidator<CreatePortfolioImageReviewCommand>
{
    public CreatePortfolioImageReviewValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Body)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(2000);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(200);
    }
}

public class CreatePortfolioImageReviewHandler(IAppDbContext db)
    : IRequestHandler<CreatePortfolioImageReviewCommand>
{
    public async Task Handle(CreatePortfolioImageReviewCommand command, CancellationToken ct)
    {
        // Approved: public portfolio lookup — images are visible cross-tenant.
        bool imageExists = await db.PortfolioImages
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == command.ImageId, ct);

        if (!imageExists)
            throw new NotFoundException(nameof(PortfolioImage), command.ImageId);

        bool alreadyReviewed = await db.Reviews
            .AnyAsync(r => r.PortfolioImageId == command.ImageId
                        && r.AuthorUserId     == command.AuthorUserId, ct);

        if (alreadyReviewed)
            throw new ConflictException("You have already reviewed this tattoo.");

        Review review = Review.ForPortfolioImage(
            command.ImageId,
            command.AuthorUserId,
            command.AuthorName,
            command.Rating,
            command.Body);

        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
    }
}
```

### C-2: `GetPortfolioImageReviewsQuery`

Create `Pena_e_Arte.Application/Public/Queries/GetPortfolioImageReviewsQuery.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetPortfolioImageReviewsQuery(Guid ImageId) : IRequest<List<ReviewResponse>>;

public class GetPortfolioImageReviewsHandler(IAppDbContext db)
    : IRequestHandler<GetPortfolioImageReviewsQuery, List<ReviewResponse>>
{
    public async Task<List<ReviewResponse>> Handle(
        GetPortfolioImageReviewsQuery query, CancellationToken ct)
    {
        // Approved: public review read — cross-tenant.
        bool imageExists = await db.PortfolioImages
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == query.ImageId, ct);

        if (!imageExists) return [];

        return await db.Reviews
            .Where(r => r.PortfolioImageId == query.ImageId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50)
            .Select(r => new ReviewResponse(r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt))
            .ToListAsync(ct);
    }
}
```

### C-3: Update `PortfolioImageResponse` contract

Read `Pena_e_Arte.Contracts/Responses/Public/PortfolioImageResponse.cs`.
Add `Guid? ImageId` as the first field and `double? ImageAverageRating` + `int ImageReviewCount`:

```csharp
public record PortfolioImageResponse(
    Guid?   ImageId,           // null for legacy images pre-migration (should not occur after migration)
    string  ImageUrl,
    string  ArtistName,
    string  ArtistSlug,
    string  StudioName,
    string  StudioSlug,
    double? AverageRating,     // artist-level rating
    int     ReviewCount,       // artist-level review count
    double? ImageAverageRating,// rating for this specific image
    int     ImageReviewCount,  // review count for this specific image
    double? DistanceKm,
    long    ViewCount);
```

### C-4: Update `GetPortfolioFeedQuery` handler

Read the existing handler. Update the query to join `PortfolioImages` instead of reading from
`Artist.PortfolioImages` JSON, and include per-image review aggregates.

The handler must:

1. Query `PortfolioImages` table (joined with `Artists`, `Studios`), not the JSON column.
2. For each image, compute `ImageAverageRating` and `ImageReviewCount` from `Reviews` where
   `PortfolioImageId == image.Id`.
3. The Bayesian score used for ranking should now factor in both artist-level rating AND
   per-image rating (prefer images with good per-image reviews).
4. Return `ImageId` in the response.

Revised ranking formula (Bayesian, same constants m=5, C=3.5):

```csharp
// Bayesian score for the image: blend image-level reviews with artist-level as prior.
double imageCount = imageReviews.TryGetValue(image.Id, out var ir) ? ir.Count : 0;
double imageAvg   = imageCount > 0 ? ir.Sum / imageCount : 3.5;
double artistCount = artistReviews.TryGetValue(artistId, out var ar) ? ar.Count : 0;
double artistAvg   = artistCount > 0 ? ar.Sum / artistCount : 3.5;

// Blend: 60% image rating, 40% artist rating when both have reviews
double blendedAvg = imageCount > 0
    ? (imageAvg * 0.6 + artistAvg * 0.4)
    : artistAvg;

double bayesianScore = (imageCount * blendedAvg + 5 * 3.5) / (imageCount + 5);
long views = viewCounts.TryGetValue(artistId, out long v) ? v : 0;
double finalScore = bayesianScore * 0.7 + Math.Log10(views + 1) * 0.3;
```

**Full handler skeleton** (replace the existing `GetPortfolioFeedHandler.Handle`):

```csharp
public async Task<List<PortfolioImageResponse>> Handle(
    GetPortfolioFeedQuery query, CancellationToken ct)
{
    // 1. Bounding box filter (if lat/lng provided)
    // ... same as existing ...

    // 2. Load portfolio images with artist + studio info
    IQueryable<PortfolioImage> imageQuery = db.PortfolioImages
        .IgnoreQueryFilters()
        .Include(p => p.Artist)
        .Where(p => p.Artist.DeletedAt == null);

    if (filteredStudioIds is not null)
        imageQuery = imageQuery.Where(p => p.Artist.StudioId != null
                                        && filteredStudioIds.Contains(p.Artist.StudioId!.Value));

    List<PortfolioImage> images = await imageQuery
        .OrderByDescending(p => p.CreatedAt)
        .Skip((query.Page - 1) * query.PageSize)
        .Take(query.PageSize)
        .ToListAsync(ct);

    if (images.Count == 0) return [];

    IReadOnlyList<Guid> imageIds  = images.Select(p => p.Id).ToList();
    IReadOnlyList<Guid> artistIds = images.Select(p => p.ArtistId).Distinct().ToList();

    // 3. Per-image review aggregates
    Dictionary<Guid, (double Sum, int Count)> imageReviews = await db.Reviews
        .Where(r => r.PortfolioImageId.HasValue && imageIds.Contains(r.PortfolioImageId!.Value))
        .GroupBy(r => r.PortfolioImageId!.Value)
        .Select(g => new { Id = g.Key, Sum = g.Sum(r => (double)r.Rating), Count = g.Count() })
        .ToDictionaryAsync(x => x.Id, x => (x.Sum, x.Count), ct);

    // 4. Artist-level review aggregates
    Dictionary<Guid, (double Sum, int Count)> artistReviews = await db.Reviews
        .Where(r => r.ArtistId.HasValue && artistIds.Contains(r.ArtistId!.Value))
        .GroupBy(r => r.ArtistId!.Value)
        .Select(g => new { Id = g.Key, Sum = g.Sum(r => (double)r.Rating), Count = g.Count() })
        .ToDictionaryAsync(x => x.Id, x => (x.Sum, x.Count), ct);

    // 5. Redis view counts (batch MGET by artistId — same as existing)
    // ... same as existing ...

    // 6. Studio names (lookup by ArtistId via Artist.StudioId)
    // Load studio names for the artists involved
    // ... same as existing, already loading via Include or dictionary ...

    // 7. Score, sort, project
    return images
        .Select(img =>
        {
            imageReviews.TryGetValue(img.Id, out (double Sum, int Count) ir);
            artistReviews.TryGetValue(img.ArtistId, out (double Sum, int Count) ar);

            double imageAvg  = ir.Count > 0 ? ir.Sum / ir.Count : 3.5;
            double artistAvg = ar.Count > 0 ? ar.Sum / ar.Count : 3.5;
            double blended   = ir.Count > 0 ? imageAvg * 0.6 + artistAvg * 0.4 : artistAvg;
            double bayesian  = (ir.Count * blended + 5 * 3.5) / (ir.Count + 5);
            long   views     = viewCounts.TryGetValue(img.ArtistId, out long v) ? v : 0;
            double score     = bayesian * 0.7 + Math.Log10(views + 1) * 0.3;

            return (Image: img, Score: score, Ir: ir, Ar: ar, Views: views);
        })
        .OrderByDescending(x => x.Score)
        .Select(x =>
        {
            Artist a = x.Image.Artist;
            // Resolve studio info from the artist's cached Studio navigation (from Include)
            return new PortfolioImageResponse(
                ImageId:            x.Image.Id,
                ImageUrl:           x.Image.ImageUrl,
                ArtistName:         $"{a.FirstName} {a.LastName}".Trim(),
                ArtistSlug:         a.Slug ?? "",
                StudioName:         a.Studio?.Name ?? "",
                StudioSlug:         a.Studio?.Slug ?? "",
                AverageRating:      x.Ar.Count > 0 ? x.Ar.Sum / x.Ar.Count : null,
                ReviewCount:        x.Ar.Count,
                ImageAverageRating: x.Ir.Count > 0 ? x.Ir.Sum / x.Ir.Count : null,
                ImageReviewCount:   x.Ir.Count,
                DistanceKm:         /* Haversine if lat/lng provided, else null */,
                ViewCount:          x.Views);
        })
        .ToList();
}
```

**Note:** The `Artist` entity needs a navigation property to `Studio`. Check if it exists; if not,
add `public Studio? Studio { get; set; }` to `Artist.cs` and configure the FK in `AppDbContext`
(`Artist.StudioId → Studio.Id`).

### C-5: Update artist portfolio upload / delete

Read `Pena_e_Arte.API/Endpoints/ArtistEndpoints.cs` and the corresponding commands.

**On upload (adding an image to the portfolio):** Instead of appending a URL to
`Artist.PortfolioImages`, create a `PortfolioImage` row and return its `Id` alongside the URL.

**Return type update:** The artist portfolio upload response must now include:

```csharp
public record ArtistPortfolioImageResponse(Guid ImageId, string ImageUrl);
```

**On delete:** Delete the `PortfolioImage` row (cascade will clean up reviews). Do not update a
JSON column.

---

## Phase D — API Layer

### D-1: New routes in `PublicEndpoints.cs`

Read the current file. Add two new routes in `MapPublicEndpoints`:

```csharp
group.MapGet ("/portfolio/{imageId:guid}/reviews", GetPortfolioImageReviews).AllowAnonymous();
group.MapPost("/portfolio/{imageId:guid}/reviews", CreatePortfolioImageReview)
     .RequireAuthorization("ClientAndAbove");
```

Add the handler methods (private static):

```csharp
private static async Task<IResult> GetPortfolioImageReviews(
    Guid              imageId,
    ISender           mediator,
    CancellationToken ct)
{
    List<ReviewResponse> result =
        await mediator.Send(new GetPortfolioImageReviewsQuery(imageId), ct);
    return Results.Ok(result);
}

private static async Task<IResult> CreatePortfolioImageReview(
    Guid                imageId,
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
        new CreatePortfolioImageReviewCommand(imageId, authorId, authorName, body.Rating, body.Body),
        ct);
    return Results.NoContent();
}
```

---

## Phase E — Frontend

### E-1: Update `publicApi.ts`

Read the current file. Make these changes:

**Update `PortfolioImageResponse` interface:**

```typescript
export interface PortfolioImageResponse {
  imageId:            string;         // Guid — stable ID for review linking
  imageUrl:           string;
  artistName:         string;
  artistSlug:         string;
  studioName:         string;
  studioSlug:         string;
  averageRating:      number | null;  // artist-level
  reviewCount:        number;         // artist-level
  imageAverageRating: number | null;  // per-image
  imageReviewCount:   number;         // per-image
  distanceKm:         number | null;
  viewCount:          number;
}
```

**Add `PortfolioImageReviewArgs`:**

```typescript
export interface PortfolioImageReviewArgs {
  imageId: string;
  rating:  number;
  body:    string;
}
```

**Add tag type and endpoints:**

```typescript
tagTypes: [
  "PublicStudio", "PublicArtist", "SharedDesign", "NearbyStudios",
  "StudioReviews", "ArtistReviews",
  "PortfolioImageReviews",  // ← NEW
],
```

```typescript
getPortfolioImageReviews: builder.query<ReviewResponse[], string>({
  query: (imageId) => `portfolio/${imageId}/reviews`,
  providesTags: (_result, _err, imageId) => [{ type: "PortfolioImageReviews", id: imageId }],
}),
createPortfolioImageReview: builder.mutation<void, PortfolioImageReviewArgs>({
  query: ({ imageId, rating, body }) => ({
    url:    `portfolio/${imageId}/reviews`,
    method: "POST",
    body:   { rating, body },
  }),
  invalidatesTags: (_result, _err, { imageId }) => [
    { type: "PortfolioImageReviews", id: imageId },
  ],
}),
```

Export the new hooks in the `export const { ... }` block.

### E-2: Update `ReviewSection.tsx`

Read the current file. Extend the `target` discriminant to support `"tattoo"` and add an `imageId`
prop. Follow the exact same pattern already used for `"artist"` and `"studio"`.

**Updated prop types:**

```typescript
// ── Review form props ─────────────────────────────────────────────────────────

interface ReviewFormProps {
  slug?:    string;
  imageId?: string;
  token:    string | null;
  target:   "studio" | "artist" | "tattoo";
}
```

**In `ReviewForm`, import the new mutation and query hooks:**

```typescript
import {
  useGetStudioReviewsQuery,
  useGetArtistReviewsQuery,
  useGetPortfolioImageReviewsQuery,
  useCreateStudioReviewMutation,
  useCreateArtistReviewMutation,
  useCreatePortfolioImageReviewMutation,
  type ReviewResponse,
} from "../publicApi";
```

**Update mutation selection in `ReviewForm`:**

```typescript
const [createPortfolioReview, { isLoading: isTattooSubmitting }] =
  useCreatePortfolioImageReviewMutation();

const isSubmitting =
  target === "studio" ? isStudioSubmitting
  : target === "artist" ? isArtistSubmitting
  : isTattooSubmitting;

function handleSubmit() {
  // ...existing validation...
  if (target === "studio" && slug)
    createStudioReview({ slug, rating, body: body.trim() })
      .unwrap().then(onSuccess).catch(onError);
  else if (target === "artist" && slug)
    createArtistReview({ slug, rating, body: body.trim() })
      .unwrap().then(onSuccess).catch(onError);
  else if (target === "tattoo" && imageId)
    createPortfolioReview({ imageId, rating, body: body.trim() })
      .unwrap().then(onSuccess).catch(onError);
}
```

**Add `PortfolioImageReviewList` component:**

```typescript
function PortfolioImageReviewList({ imageId }: { imageId: string }) {
  const { data: reviews, isLoading } = useGetPortfolioImageReviewsQuery(imageId);
  const averageRating = reviews && reviews.length > 0
    ? reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length
    : null;
  return <ReviewList reviews={reviews} isLoading={isLoading} averageRating={averageRating} />;
}
```

**Update the top-level `ReviewSection`:**

```typescript
interface Props {
  slug?:    string;    // required when target === "studio" | "artist"
  imageId?: string;   // required when target === "tattoo"
  target:   "studio" | "artist" | "tattoo";
  token:    string | null;
}

export function ReviewSection({ slug, imageId, target, token }: Props) {
  return (
    <section className="space-y-5" aria-labelledby="reviews-heading">
      <div className="flex items-center gap-2">
        <MessageSquare className="h-5 w-5 text-muted-foreground/70" aria-hidden="true" />
        <h2 id="reviews-heading" className="text-lg font-semibold">
          {target === "tattoo" ? "Reviews for this piece" : "Reviews"}
        </h2>
      </div>

      <ReviewForm slug={slug} imageId={imageId} token={token} target={target} />

      {target === "studio" && slug  && <StudioReviewList slug={slug} />}
      {target === "artist" && slug  && <ArtistReviewList slug={slug} />}
      {target === "tattoo" && imageId && <PortfolioImageReviewList imageId={imageId} />}
    </section>
  );
}
```

**Unauthenticated gate — update `returnUrl` for tattoo target:**

```typescript
const returnUrl =
  target === "studio" ? `/s/${slug}`
  : target === "artist" ? `/artist/${slug}`
  : `/discover`;
```

### E-3: Portfolio feed lightbox — add review section

Read `frontend/src/features/public/components/PortfolioFeed.tsx` (created by the portfolio-feed
overnight prompt).

The lightbox `Dialog` that opens when a user clicks a portfolio image currently shows only the
image and artist info. Add the `ReviewSection` below the image in the dialog:

```tsx
import { useAppSelector } from "@/app/hooks";
import { ReviewSection } from "./ReviewSection";

// Inside the Dialog content, after the image and artist info:
{selectedImage && (
  <ReviewSection
    imageId={selectedImage.imageId}
    target="tattoo"
    token={useAppSelector((s) => s.auth.token)}
  />
)}
```

**Do not call `useAppSelector` inside JSX** — extract the token before the return:

```tsx
const token = useAppSelector((s) => s.auth.token);

// Then in JSX:
{selectedImage && (
  <div className="mt-4 border-t pt-4">
    <ReviewSection
      imageId={selectedImage.imageId}
      target="tattoo"
      token={token}
    />
  </div>
)}
```

### E-4: Artist profile page — portfolio image reviews

Read `frontend/src/features/public/components/ArtistPortfolioPage.tsx`.

The masonry grid already has a `Dialog` lightbox (from the artist-profile-overhaul prompt). Inside
the lightbox Dialog, after the full-size image, add:

```tsx
{lightboxImage && (
  <div className="mt-4 border-t pt-4">
    <ReviewSection
      imageId={lightboxImage.imageId}
      target="tattoo"
      token={token}
    />
  </div>
)}
```

The lightbox currently tracks `lightboxImage` as a URL string. Change it to track the full
`{ imageId: string; imageUrl: string }` object so the review section has access to `imageId`.

Update the `PortfolioGrid` component's click handler accordingly:

```tsx
// Instead of:
const [lightboxUrl, setLightboxUrl] = useState<string | null>(null);

// Use:
interface LightboxEntry { imageId: string; imageUrl: string; }
const [lightboxImage, setLightboxImage] = useState<LightboxEntry | null>(null);
```

**Note:** If `ArtistPortfolioPage` renders from `PublicArtistResponse.portfolioImages: string[]`
(raw URLs without IDs), this will not work until the artist profile endpoint returns
`PortfolioImage` objects with IDs. Update `PublicArtistResponse` to include:

```typescript
// In publicApi.ts
export interface ArtistPortfolioImage {
  imageId: string;
  imageUrl: string;
}

export interface PublicArtistResponse {
  // ... existing fields ...
  portfolioImages: ArtistPortfolioImage[];  // ← changed from string[]
}
```

And update `GetPublicArtistQuery` handler to project from `Artist.Portfolio` instead of
`Artist.PortfolioImages` JSON:

```csharp
PortfolioImages = artist.Portfolio
    .OrderByDescending(p => p.CreatedAt)
    .Select(p => new ArtistPortfolioImageResponse(p.Id, p.ImageUrl))
    .ToList()
```

Add `ArtistPortfolioImageResponse` to contracts:

```csharp
// Pena_e_Arte.Contracts/Responses/Public/ArtistPortfolioImageResponse.cs
public record ArtistPortfolioImageResponse(Guid ImageId, string ImageUrl);
```

And update `PublicArtistResponse` to use it:

```csharp
public record PublicArtistResponse(
    Guid                                 ArtistId,
    string                               Name,
    string                               Slug,
    string?                              Bio,
    string?                              ProfileImageUrl,
    IReadOnlyList<ArtistPortfolioImageResponse> PortfolioImages,  // ← changed type
    string?                              Specializations,
    decimal?                             HourlyRate,
    double?                              AverageRating,
    int                                  ReviewCount,
    string                               StudioName,
    string                               StudioSlug,
    bool                                 ShowBookingCta,
    bool                                 IsOwnProfile);
```

Update `publicApi.ts` `PublicArtistResponse` accordingly.

### E-5: Image review count display in feed

In `PortfolioFeed.tsx`, update the image card overlay to show the per-image review count
alongside the artist-level rating that's already displayed. The `PortfolioImageResponse` now
has `imageAverageRating` and `imageReviewCount`. Display these in the image hover overlay:

```tsx
{image.imageReviewCount > 0 && (
  <span className="text-xs">
    ★ {image.imageAverageRating?.toFixed(1)} ({image.imageReviewCount})
  </span>
)}
```

---

## Phase F — Tests

### F-1: `CreatePortfolioImageReviewHandlerTests.cs`

Create `tests/Pena_e_Arte.UnitTests/Reviews/CreatePortfolioImageReviewHandlerTests.cs`.
Follow the exact same pattern as `CreateStudioReviewHandlerTests.cs`:

```csharp
using FluentAssertions;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reviews;

public class CreatePortfolioImageReviewHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private CreatePortfolioImageReviewHandler CreateSut() => new(_db);

    private async Task<PortfolioImage> SeedImage(Guid? artistId = null)
    {
        Artist artist = new() { FirstName = "Test", LastName = "Artist", Email = "t@ink.test" };
        _db.Artists.Add(artist);

        PortfolioImage image = new()
        {
            ArtistId = artistId ?? artist.Id,
            ImageUrl = "https://cdn.example.com/tattoo.jpg",
        };
        _db.PortfolioImages.Add(image);
        await _db.SaveChangesAsync();
        return image;
    }

    [Fact]
    public async Task Creates_review_when_image_exists_and_no_prior_review()
    {
        PortfolioImage image    = await SeedImage();
        Guid           authorId = Guid.NewGuid();

        await CreateSut().Handle(
            new CreatePortfolioImageReviewCommand(
                image.Id, authorId, "Maria C.", 5, "Stunning piece, love the detail!"),
            CancellationToken.None);

        _db.Reviews.Should().ContainSingle(r =>
            r.PortfolioImageId == image.Id && r.Rating == 5);
    }

    [Fact]
    public async Task Throws_NotFoundException_when_image_not_found()
    {
        Func<Task> act = () => CreateSut().Handle(
            new CreatePortfolioImageReviewCommand(
                Guid.NewGuid(), Guid.NewGuid(), "Ana", 4, "This is a long enough review body"),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_ConflictException_when_user_already_reviewed_image()
    {
        PortfolioImage image    = await SeedImage();
        Guid           authorId = Guid.NewGuid();

        Review existing = Review.ForPortfolioImage(
            image.Id, authorId, "Ana", 4, "First review for this tattoo piece");
        _db.Reviews.Add(existing);
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(
            new CreatePortfolioImageReviewCommand(
                image.Id, authorId, "Ana", 5, "Trying to review again here!"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*already reviewed*");
    }

    [Fact]
    public void Validator_rejects_rating_above_5()
    {
        CreatePortfolioImageReviewValidator validator = new();
        CreatePortfolioImageReviewCommand command = new(
            Guid.NewGuid(), Guid.NewGuid(), "Ana", 6, "Some valid review body text here");

        validator.ShouldFailOn(command, nameof(command.Rating));
    }

    [Fact]
    public void Validator_rejects_body_shorter_than_10_chars()
    {
        CreatePortfolioImageReviewValidator validator = new();
        CreatePortfolioImageReviewCommand command = new(
            Guid.NewGuid(), Guid.NewGuid(), "Ana", 4, "short");

        validator.ShouldFailOn(command, nameof(command.Body));
    }
}
```

**Note:** `FakeDbContext.Create()` must include `DbSet<PortfolioImage>`. Read
`tests/Pena_e_Arte.UnitTests/Helpers/FakeDbContext.cs` and add it.

### F-2: Update `ReviewSection.test.tsx`

Read the existing test file. Add tests for the `"tattoo"` target:

```typescript
describe("ReviewSection — tattoo target", () => {
  beforeEach(() => {
    vi.mock("@/features/public/publicApi", () => ({
      useGetPortfolioImageReviewsQuery: () => ({ data: [], isLoading: false }),
      useCreatePortfolioImageReviewMutation: () => [vi.fn(), { isLoading: false }],
      // ... existing mocks for studio/artist ...
    }));
  });

  it("renders 'Reviews for this piece' heading for tattoo target", () => {
    render(
      <MemoryRouter>
        <ReviewSection imageId="img-001" target="tattoo" token="tok" />
      </MemoryRouter>,
    );
    expect(screen.getByRole("heading", { name: /reviews for this piece/i })).toBeInTheDocument();
  });

  it("shows sign-in gate for unauthenticated user on tattoo target", () => {
    render(
      <MemoryRouter>
        <ReviewSection imageId="img-001" target="tattoo" token={null} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/sign in to share your experience/i)).toBeInTheDocument();
  });

  it("shows the review form for authenticated user on tattoo target", () => {
    render(
      <MemoryRouter>
        <ReviewSection imageId="img-001" target="tattoo" token="tok" />
      </MemoryRouter>,
    );
    expect(screen.getByRole("textbox", { name: /write a review/i })).toBeInTheDocument();
  });
});
```

### F-3: Update `FakeDbContext` for `PortfolioImages`

Read `tests/Pena_e_Arte.UnitTests/Helpers/FakeDbContext.cs`. Add:

```csharp
public DbSet<PortfolioImage> PortfolioImages => Set<PortfolioImage>();
```

And register the entity in the in-memory model builder.

---

## Phase G — Architecture Docs

Update `docs/claude/architecture.md`:

Under **Decisions Log**, add:

```
### Decision: PortfolioImage entity (2026-06-25)
Replaced Artist.PortfolioImages: List<string> with a PortfolioImage entity (TenantEntity)
so that individual portfolio photos have stable GUIDs and can receive per-image reviews.
Migration: AddPortfolioImageEntity seeds existing images via MySQL JSON_TABLE extraction.
Artist upload endpoint now creates PortfolioImage rows; delete cascades reviews.
```

Under **Feature Checklist**, check off: Portfolio Image Reviews.

---

## Phase H — Build Checklist

Run in order. Fix any failure before proceeding to the next step.

```bash
cd "Pena e Arte"

# 1. Build backend
dotnet build --verbosity minimal

# 2. Run migration
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API

# 3. Run backend tests
dotnet test

# 4. Frontend type check
cd frontend
pnpm tsc --noEmit

# 5. Lint
pnpm lint

# 6. Frontend tests
pnpm test --run
```

All six must exit 0 before this prompt is considered complete.

---

## Hard Rules

1. **No new NuGet or npm packages.**
2. **No business logic in endpoints** — all three review endpoints call MediatR only.
3. **`IgnoreQueryFilters()` only in approved locations** — both new public handlers qualify
   (add `// Approved: public portfolio lookup — cross-tenant.` comment).
4. **No PII in logs** — `authorName` must never appear in a Serilog statement.
5. **TypeScript strict mode** — no `any`, no default exports on components.
6. **`useAppSelector` hook must not be called inside JSX** — extract tokens before the return.
7. **`ReviewSection` must remain a single component** — do not create three separate components
   for studio/artist/tattoo; extend the existing one with the `"tattoo"` target.
