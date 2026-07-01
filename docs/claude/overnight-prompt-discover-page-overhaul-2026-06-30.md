# Overnight Prompt — Discover Page & Portfolio Feed Overhaul
**Goal:** Fix every visual, layout, accessibility, copy, and technical defect on the
DiscoverPage and PortfolioFeed, then add the three strategic features: tattoo-style
filtering chips, a value-proposition hero for logged-out visitors, and a
bookmark/save-image feature for authenticated users.

No new npm or NuGet packages. All changes must pass `pnpm tsc --noEmit`, `pnpm lint`,
and `pnpm test --run` before the session is complete.

---

## Read First

1. `CLAUDE.md`
2. `docs/claude/frontend.md`
3. `docs/claude/backend.md`
4. `docs/claude/database.md`
5. `docs/claude/architecture.md`
6. `docs/claude/conventions.md`

---

## Files to edit (primary)

| File | What changes |
|---|---|
| `frontend/src/features/public/components/DiscoverPage.tsx` | Nav, logo, tabs, hero, footer, copy |
| `frontend/src/features/public/components/PortfolioFeed.tsx` | Masonry algorithm, attribution strip, hover, style chips, bookmark button, CLS |
| `frontend/src/features/public/publicApi.ts` | New endpoints for style filter + bookmarks |
| `frontend/src/features/public/__tests__/DiscoverPage.test.tsx` | Update + add tests |
| `frontend/src/features/public/__tests__/PortfolioFeed.test.tsx` | Update + add tests |
| `Pena_e_Arte.Domain/Entities/PortfolioImage.cs` | Add `Style string?` |
| `Pena_e_Arte.Domain/Entities/SavedPortfolioImage.cs` | New entity |
| `Pena_e_Arte.Application/Persistence/IAppDbContext.cs` | Add `SavedPortfolioImages` |
| `Pena_e_Arte.Application/Public/Queries/GetPortfolioFeedQuery.cs` | Add `style` filter |
| `Pena_e_Arte.Application/Saved/Commands/SavePortfolioImageCommand.cs` | New |
| `Pena_e_Arte.Application/Saved/Commands/UnsavePortfolioImageCommand.cs` | New |
| `Pena_e_Arte.Application/Saved/Queries/GetSavedPortfolioImagesQuery.cs` | New |
| `Pena_e_Arte.Contracts/Responses/Public/PortfolioImageResponse.cs` | Add `Style` |
| `Pena_e_Arte.API/Endpoints/SavedImagesEndpoints.cs` | New |
| `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs` | Add `style` query param |
| `docs/claude/architecture.md` | Update decisions log |

---

## Section 1 — Backend: Style field + Saved images

### 1-A: Add `Style` to `PortfolioImage`

Read `Pena_e_Arte.Domain/Entities/PortfolioImage.cs`. Add:

```csharp
/// <summary>
/// Optional tattoo style tag. Values are app-controlled; see TattooStyle constants.
/// Max 50 chars. Null means untagged / "All".
/// </summary>
public string? Style { get; set; }
```

Add a constants file `Pena_e_Arte.Domain/Constants/TattooStyle.cs`:

```csharp
namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Canonical tattoo style identifiers. Used on PortfolioImage.Style and as filter
/// chip values on the DiscoverPage. Keep in sync with STYLES constant in PortfolioFeed.tsx.
/// </summary>
public static class TattooStyle
{
    public const string Traditional    = "traditional";
    public const string Realism        = "realism";
    public const string Blackwork      = "blackwork";
    public const string Geometric      = "geometric";
    public const string Watercolor     = "watercolor";
    public const string Fineline       = "fineline";
    public const string NeoTraditional = "neo-traditional";
    public const string Japanese       = "japanese";

    public static readonly IReadOnlyList<string> All =
        [Traditional, Realism, Blackwork, Geometric, Watercolor, Fineline, NeoTraditional, Japanese];
}
```

Update `PortfolioImageResponse` contract to include `Style`:

```csharp
public record PortfolioImageResponse(
    Guid?   ImageId,
    string  ImageUrl,
    string? Style,           // ← NEW (nullable — untagged images are valid)
    string  ArtistName,
    string  ArtistSlug,
    string  StudioName,
    string  StudioSlug,
    double? AverageRating,
    int     ReviewCount,
    double? ImageAverageRating,
    int     ImageReviewCount,
    double? DistanceKm,
    long    ViewCount);
```

Update the EF Core configuration in `AppDbContext`:
```csharp
b.Property(p => p.Style).HasMaxLength(50).IsRequired(false);
```

### 1-B: Update `GetPortfolioFeedQuery` to accept `style` filter

Read `Pena_e_Arte.Application/Public/Queries/GetPortfolioFeedQuery.cs`.

Add `string? Style` to the query record:

```csharp
public record GetPortfolioFeedQuery(
    double? Lat,
    double? Lng,
    double  RadiusKm,
    int     Page,
    int     PageSize = 24,
    string? Style    = null)   // ← NEW
    : IRequest<List<PortfolioImageResponse>>;
```

In the handler, after the bounding-box filter, add:

```csharp
if (!string.IsNullOrWhiteSpace(query.Style))
    imageQuery = imageQuery.Where(p => p.Style == query.Style);
```

Update `PublicEndpoints.cs` — the `GetPortfolioFeed` handler parameter list:

```csharp
private static async Task<IResult> GetPortfolioFeed(
    double? lat, double? lng,
    double  radiusKm = 50,
    int     page     = 1,
    int     pageSize = 24,
    string? style    = null,           // ← NEW
    ISender mediator = default!,
    CancellationToken ct = default)
{
    var result = await mediator.Send(
        new GetPortfolioFeedQuery(lat, lng, radiusKm, page, pageSize, style), ct);
    return Results.Ok(result);
}
```

### 1-C: Migration

```bash
dotnet ef migrations add AddPortfolioImageStyle \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

### 1-D: New entity `SavedPortfolioImage`

Create `Pena_e_Arte.Domain/Entities/SavedPortfolioImage.cs`:

```csharp
namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A portfolio image that a logged-in user has bookmarked.
/// Not tenant-scoped — the saving user may belong to a different studio.
/// </summary>
public class SavedPortfolioImage
{
    public Guid     Id               { get; set; } = Guid.NewGuid();
    public Guid     UserId           { get; set; }
    public Guid     PortfolioImageId { get; set; }
    public DateTime SavedAt          { get; set; } = DateTime.UtcNow;

    public PortfolioImage PortfolioImage { get; set; } = null!;
}
```

Add to `IAppDbContext.cs` under "Cross-tenant public data":

```csharp
// User-saved portfolio images — cross-tenant (user may belong to any studio)
DbSet<SavedPortfolioImage> SavedPortfolioImages { get; }
```

Add to `AppDbContext` and configure:

```csharp
public DbSet<SavedPortfolioImage> SavedPortfolioImages => Set<SavedPortfolioImage>();
```

```csharp
builder.Entity<SavedPortfolioImage>(b =>
{
    b.ToTable("SavedPortfolioImages");
    b.HasKey(s => s.Id);
    b.HasIndex(s => new { s.UserId, s.PortfolioImageId }).IsUnique(); // one save per user per image
    b.HasOne(s => s.PortfolioImage)
     .WithMany()
     .HasForeignKey(s => s.PortfolioImageId)
     .OnDelete(DeleteBehavior.Cascade);
});
```

No global query filter — saves are by `UserId`, not by tenant.

### 1-E: Application commands + query for saves

**`SavePortfolioImageCommand.cs`**

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Saved.Commands;

public record SavePortfolioImageCommand(Guid UserId, Guid ImageId) : IRequest;

public class SavePortfolioImageHandler(IAppDbContext db)
    : IRequestHandler<SavePortfolioImageCommand>
{
    public async Task Handle(SavePortfolioImageCommand cmd, CancellationToken ct)
    {
        // Approved: cross-tenant public image lookup.
        bool imageExists = await db.PortfolioImages
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == cmd.ImageId, ct);

        if (!imageExists)
            throw new NotFoundException(nameof(PortfolioImage), cmd.ImageId);

        bool alreadySaved = await db.SavedPortfolioImages
            .AnyAsync(s => s.UserId == cmd.UserId && s.PortfolioImageId == cmd.ImageId, ct);

        if (alreadySaved) return; // idempotent — already saved, no error

        db.SavedPortfolioImages.Add(new SavedPortfolioImage
        {
            UserId           = cmd.UserId,
            PortfolioImageId = cmd.ImageId,
        });
        await db.SaveChangesAsync(ct);
    }
}
```

**`UnsavePortfolioImageCommand.cs`**

```csharp
public record UnsavePortfolioImageCommand(Guid UserId, Guid ImageId) : IRequest;

public class UnsavePortfolioImageHandler(IAppDbContext db)
    : IRequestHandler<UnsavePortfolioImageCommand>
{
    public async Task Handle(UnsavePortfolioImageCommand cmd, CancellationToken ct)
    {
        SavedPortfolioImage? saved = await db.SavedPortfolioImages
            .FirstOrDefaultAsync(
                s => s.UserId == cmd.UserId && s.PortfolioImageId == cmd.ImageId, ct);

        if (saved is null) return; // idempotent
        db.SavedPortfolioImages.Remove(saved);
        await db.SaveChangesAsync(ct);
    }
}
```

**`GetSavedPortfolioImagesQuery.cs`**

```csharp
public record GetSavedPortfolioImagesQuery(Guid UserId, int Page = 1, int PageSize = 24)
    : IRequest<List<PortfolioImageResponse>>;

public class GetSavedPortfolioImagesHandler(IAppDbContext db)
    : IRequestHandler<GetSavedPortfolioImagesQuery, List<PortfolioImageResponse>>
{
    public async Task<List<PortfolioImageResponse>> Handle(
        GetSavedPortfolioImagesQuery query, CancellationToken ct)
    {
        // Approved: cross-tenant — user may have saved images from any studio.
        return await db.SavedPortfolioImages
            .IgnoreQueryFilters()
            .Where(s => s.UserId == query.UserId)
            .OrderByDescending(s => s.SavedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(s => new PortfolioImageResponse(
                s.PortfolioImage.Id,
                s.PortfolioImage.ImageUrl,
                s.PortfolioImage.Style,
                // ... join with artist + studio for names
                // Use same projection as GetPortfolioFeedHandler
                ...))
            .ToListAsync(ct);
    }
}
```

**Important:** The projection for `GetSavedPortfolioImagesHandler` must include `.Include(s => s.PortfolioImage.Artist).ThenInclude(a => a.Studio)` to avoid N+1. Write the full query with proper `.Include()` chains matching the pattern in `GetPortfolioFeedQuery`.

**`GetSavedImageIdsQuery.cs`** — returns just the set of saved image IDs for the current user, used by the frontend to render bookmark state:

```csharp
public record GetSavedImageIdsQuery(Guid UserId) : IRequest<HashSet<Guid>>;

public class GetSavedImageIdsHandler(IAppDbContext db)
    : IRequestHandler<GetSavedImageIdsQuery, HashSet<Guid>>
{
    public async Task<HashSet<Guid>> Handle(GetSavedImageIdsQuery query, CancellationToken ct)
    {
        return await db.SavedPortfolioImages
            .Where(s => s.UserId == query.UserId)
            .Select(s => s.PortfolioImageId)
            .ToHashSetAsync(ct);
    }
}
```

### 1-F: API endpoints for saves

Create `Pena_e_Arte.API/Endpoints/SavedImagesEndpoints.cs`:

```csharp
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Pena_e_Arte.Application.Saved.Commands;
using Pena_e_Arte.Application.Saved.Queries;

namespace Pena_e_Arte.API.Endpoints;

public static class SavedImagesEndpoints
{
    public static void MapSavedImagesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/saved-images")
            .RequireAuthorization("ClientAndAbove");

        group.MapGet   ("/",           GetSavedImages);
        group.MapGet   ("/ids",        GetSavedImageIds);
        group.MapPost  ("/{imageId:guid}", SaveImage);
        group.MapDelete("/{imageId:guid}", UnsaveImage);
    }

    private static async Task<IResult> GetSavedImages(
        ClaimsPrincipal user, ISender mediator,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result  = await mediator.Send(new GetSavedPortfolioImagesQuery(userId, page), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetSavedImageIds(
        ClaimsPrincipal user, ISender mediator, CancellationToken ct = default)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result  = await mediator.Send(new GetSavedImageIdsQuery(userId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> SaveImage(
        Guid imageId, ClaimsPrincipal user, ISender mediator, CancellationToken ct = default)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new SavePortfolioImageCommand(userId, imageId), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnsaveImage(
        Guid imageId, ClaimsPrincipal user, ISender mediator, CancellationToken ct = default)
    {
        Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new UnsavePortfolioImageCommand(userId, imageId), ct);
        return Results.NoContent();
    }
}
```

Register in `Program.cs`:
```csharp
app.MapSavedImagesEndpoints();
```

Add `IgnoreQueryFilters` usages to `architecture.md`:
- `SavePortfolioImageHandler` — cross-tenant image lookup before saving
- `GetSavedPortfolioImagesHandler` — cross-tenant user's saved images
- `GetSavedImageIdsHandler` — user's saved image IDs (no tenant scope)

### 1-G: Migration for saved images

```bash
dotnet ef migrations add AddSavedPortfolioImages \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

---

## Section 2 — Frontend: `publicApi.ts` updates

Read the current `publicApi.ts`. Make the following changes:

### 2-A: Update `PortfolioImageResponse` interface

```typescript
export interface PortfolioImageResponse {
  imageId:            string;
  imageUrl:           string;
  style:              string | null;     // ← NEW
  artistName:         string;
  artistSlug:         string;
  studioName:         string;
  studioSlug:         string;
  averageRating:      number | null;
  reviewCount:        number;
  imageAverageRating: number | null;
  imageReviewCount:   number;
  distanceKm:         number | null;
  viewCount:          number;
}
```

### 2-B: Update feed query to accept `style`

```typescript
export interface PortfolioFeedArgs {
  lat?:      number;
  lng?:      number;
  radiusKm:  number;
  page:      number;
  pageSize?: number;
  style?:    string;    // ← NEW
}

// In the endpoint:
getPortfolioFeed: builder.query<PortfolioImageResponse[], PortfolioFeedArgs>({
  query: ({ lat, lng, radiusKm, page, pageSize = 24, style }) => {
    const params = new URLSearchParams();
    params.set("radiusKm", String(radiusKm));
    params.set("page",     String(page));
    params.set("pageSize", String(pageSize));
    if (lat != null) params.set("lat", String(lat));
    if (lng != null) params.set("lng", String(lng));
    if (style)       params.set("style", style);
    return `portfolio/feed?${params.toString()}`;
  },
  providesTags: ["PortfolioFeed"],
}),
```

### 2-C: Add bookmark endpoints to `publicApi.ts`

Add tag type `"SavedImage"` to `tagTypes`.

```typescript
// Saved images
getSavedImageIds: builder.query<string[], void>({
  query:         () => "saved-images/ids",
  providesTags:  ["SavedImage"],
}),
getSavedImages: builder.query<PortfolioImageResponse[], number>({
  query: (page = 1) => `saved-images?page=${page}`,
  providesTags: ["SavedImage"],
}),
saveImage: builder.mutation<void, string>({
  query: (imageId) => ({ url: `saved-images/${imageId}`, method: "POST" }),
  invalidatesTags: ["SavedImage"],
}),
unsaveImage: builder.mutation<void, string>({
  query: (imageId) => ({ url: `saved-images/${imageId}`, method: "DELETE" }),
  invalidatesTags: ["SavedImage"],
}),
```

Export the four new hooks.

---

## Section 3 — Frontend: `PortfolioFeed.tsx` — full rewrite

Read the current file. Replace with the following updated implementation. Each change is
explained inline with comments.

### 3-A: Column-balancing masonry (fix #1 — the most critical visual bug)

**Problem:** `columns-2 md:columns-3 gap-3` CSS columns fill top-to-bottom within
each column. A single tall image in column 2 creates two large voids flanking it.

**Fix:** Render explicit column `<div>` elements in JSX. Use round-robin assignment
(image 0 → col 0, image 1 → col 1, image 2 → col 2, image 3 → col 0, …). This
distributes images evenly, keeping column heights statistically balanced without
needing to know image dimensions.

```tsx
// ── Responsive column count hook ──────────────────────────────────────────────
// Acceptable useEffect: ResizeObserver / resize event — browser API side-effect,
// not data fetching.
function useColumnCount(): 1 | 2 | 3 {
  const getCount = (): 1 | 2 | 3 => {
    if (typeof window === "undefined") return 2;
    if (window.innerWidth >= 1024) return 3;
    if (window.innerWidth >= 640)  return 2;
    return 1;
  };

  const [count, setCount] = useState<1 | 2 | 3>(getCount);

  useEffect(() => {
    const update = () => setCount(getCount());
    window.addEventListener("resize", update);
    return () => window.removeEventListener("resize", update);
  }, []);

  return count;
}

// ── Round-robin column distributor ────────────────────────────────────────────
function distributeToColumns<T>(items: T[], columnCount: number): T[][] {
  const cols: T[][] = Array.from({ length: columnCount }, () => []);
  items.forEach((item, i) => cols[i % columnCount].push(item));
  return cols;
}

// ── Masonry grid ──────────────────────────────────────────────────────────────
interface MasonryGridProps {
  images:   PortfolioImageResponse[];
  onOpen:   (image: PortfolioImageResponse) => void;
  savedIds: ReadonlySet<string>;
  onToggleSave: (imageId: string, isSaved: boolean) => void;
  token: string | null;
}

function MasonryGrid({ images, onOpen, savedIds, onToggleSave, token }: MasonryGridProps) {
  const columnCount = useColumnCount();
  const columns     = distributeToColumns(images, columnCount);

  return (
    <div className="flex gap-3" role="list" aria-label="Portfolio images">
      {columns.map((col, colIdx) => (
        <div key={colIdx} className="flex flex-col gap-3 flex-1 min-w-0">
          {col.map((img) => (
            <PortfolioTile
              key={img.imageId}
              image={img}
              isSaved={savedIds.has(img.imageId)}
              onOpen={onOpen}
              onToggleSave={onToggleSave}
              showBookmark={token !== null}
            />
          ))}
        </div>
      ))}
    </div>
  );
}
```

### 3-B: Always-visible attribution strip (fix #5 — no attribution)

**Problem:** The artist name and studio only appear on hover. First-time visitors have
zero context about who made the piece. The hover overlay is invisible on touch devices.

**Fix:** Add a persistent attribution strip at the bottom of every tile, visible at all
times. The hover overlay remains for additional context (rating, "View tattoo →" CTA).

```tsx
function PortfolioTile({ image, isSaved, onOpen, onToggleSave, showBookmark }: TileProps) {
  const [failed, setFailed] = useState(false);

  if (failed) { /* ...existing fallback unchanged... */ }

  return (
    <div
      role="listitem"
      className="relative rounded-lg overflow-hidden group
                 focus-within:ring-2 focus-within:ring-ring focus-within:ring-offset-2"
    >
      {/* Main clickable image */}
      <button
        type="button"
        className="block w-full text-left focus-visible:outline-none cursor-pointer"
        aria-label={`View tattoo by ${image.artistName} at ${image.studioName}`}
        onClick={() => onOpen(image)}
      >
        {/*
          CLS fix: wrap the image in an aspect-ratio container. We don't know the
          image's natural ratio at render time, so we use padding-top trick with
          `absolute` positioning. The image reveals its natural height once loaded.
          Use min-h to prevent 0-height flash on first paint.
        */}
        <img
          src={image.imageUrl}
          alt={`Tattoo artwork by ${image.artistName}`}
          loading="lazy"
          decoding="async"
          className="w-full object-cover block transition-transform duration-300
                     group-hover:scale-[1.02]"
          onError={() => setFailed(true)}
        />

        {/* Hover overlay — additional context, CTA */}
        <div
          aria-hidden="true"
          className="absolute inset-0
                     bg-gradient-to-t from-black/80 via-black/20 to-transparent
                     opacity-0 group-hover:opacity-100 group-focus-within:opacity-100
                     transition-opacity duration-200
                     flex flex-col justify-end px-3 pb-10 gap-0.5"
        >
          {image.imageReviewCount > 0 && (
            <div className="flex items-center gap-1">
              <StarRating value={Math.round(image.imageAverageRating ?? 0)} />
              <span className="text-white/60 text-[10px]">({image.imageReviewCount})</span>
            </div>
          )}
          <span className="text-violet-300 text-xs font-medium">View tattoo →</span>
        </div>
      </button>

      {/* Distance badge (top-right) */}
      {image.distanceKm !== null && (
        <span
          aria-label={`${image.distanceKm} km away`}
          className="absolute top-2 right-2 z-10
                     bg-black/70 backdrop-blur-sm text-white text-[10px] font-medium
                     px-1.5 py-0.5 rounded-full flex items-center gap-0.5"
        >
          <MapPin className="h-2.5 w-2.5" aria-hidden="true" />
          {image.distanceKm} km
        </span>
      )}

      {/* Bookmark button (top-left) — only when authenticated */}
      {showBookmark && (
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); onToggleSave(image.imageId, isSaved); }}
          aria-label={isSaved ? `Remove ${image.artistName}'s tattoo from saved` : `Save ${image.artistName}'s tattoo`}
          aria-pressed={isSaved}
          className={`absolute top-2 left-2 z-10 p-1.5 rounded-full
                      transition-colors backdrop-blur-sm
                      ${isSaved
                        ? "bg-violet-600 text-white"
                        : "bg-black/60 text-white/70 hover:text-white hover:bg-black/80"
                      }
                      opacity-0 group-hover:opacity-100 group-focus-within:opacity-100
                      focus-visible:opacity-100
                      transition-opacity duration-200`}
        >
          {/* Bookmark icon — use Lucide Bookmark (already in lucide-react) */}
          <Bookmark className={`h-3.5 w-3.5 ${isSaved ? "fill-current" : ""}`} aria-hidden="true" />
        </button>
      )}

      {/* ── Always-visible attribution strip ──────────────────────────────── */}
      {/* This is the key fix: attribution is ALWAYS visible, not just on hover. */}
      <div className="bg-zinc-950/85 backdrop-blur-sm px-2.5 py-1.5 flex items-center gap-2">
        <div className="flex-1 min-w-0">
          <p className="text-white text-[11px] font-medium truncate leading-tight">
            {image.artistName}
          </p>
          <p className="text-white/50 text-[10px] truncate leading-tight">
            {image.studioName}
          </p>
        </div>
        {image.reviewCount > 0 && (
          <div className="flex items-center gap-0.5 shrink-0">
            <span className="text-yellow-400 text-[10px]">★</span>
            <span className="text-white/60 text-[10px]">
              {image.averageRating?.toFixed(1)}
            </span>
          </div>
        )}
      </div>
    </div>
  );
}
```

**TileProps interface:**
```typescript
interface TileProps {
  image:         PortfolioImageResponse;
  isSaved:       boolean;
  onOpen:        (image: PortfolioImageResponse) => void;
  onToggleSave:  (imageId: string, isSaved: boolean) => void;
  showBookmark:  boolean;
}
```

### 3-C: Style filter chips (new feature)

Add a horizontally-scrollable chip row above the masonry grid.
This is the single most-expected control on any portfolio discovery surface.

```tsx
// ── Style constants ───────────────────────────────────────────────────────────
// Keep in sync with TattooStyle.cs constants on the backend.
const STYLES: ReadonlyArray<{ value: string; label: string }> = [
  { value: "",               label: "All"            },
  { value: "blackwork",      label: "Blackwork"      },
  { value: "realism",        label: "Realism"        },
  { value: "traditional",    label: "Traditional"    },
  { value: "geometric",      label: "Geometric"      },
  { value: "fineline",       label: "Fineline"       },
  { value: "watercolor",     label: "Watercolor"     },
  { value: "neo-traditional", label: "Neo-Traditional" },
  { value: "japanese",       label: "Japanese"       },
];

// ── StyleChips component ──────────────────────────────────────────────────────
interface StyleChipsProps {
  activeStyle:  string;
  onChange:     (style: string) => void;
}

function StyleChips({ activeStyle, onChange }: StyleChipsProps) {
  return (
    <div
      role="group"
      aria-label="Filter by tattoo style"
      className="flex items-center gap-1.5 overflow-x-auto scrollbar-none pb-1
                 -mx-4 px-4 sm:mx-0 sm:px-0"
    >
      {STYLES.map(({ value, label }) => {
        const isActive = activeStyle === value;
        return (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={isActive}
            onClick={() => onChange(value)}
            className={`shrink-0 px-3 py-1 rounded-full text-xs font-medium
                        border transition-colors whitespace-nowrap
                        ${isActive
                          ? "bg-violet-600 border-violet-500 text-white"
                          : "border-border text-muted-foreground hover:text-foreground hover:border-border/80"
                        }`}
          >
            {label}
          </button>
        );
      })}
    </div>
  );
}
```

### 3-D: Updated `PortfolioFeed` main component

Bring together the masonry fix, style chips, bookmark feature, and "load more":

```tsx
export function PortfolioFeed({ lat, lng, radiusKm, nearOnly }: PortfolioFeedProps) {
  const [page,          setPage]        = useState(1);
  const [activeStyle,   setActiveStyle] = useState("");
  const [lightboxImage, setLightboxImage] = useState<PortfolioImageResponse | null>(null);
  const [allImages,     setAllImages]   = useState<PortfolioImageResponse[]>([]);

  const token  = useAppSelector((s) => s.auth.token);
  const userId = useAppSelector((s) => s.auth.user?.id);

  const feedArgs: PortfolioFeedArgs = {
    lat:      nearOnly && lat != null ? lat : undefined,
    lng:      nearOnly && lng != null ? lng : undefined,
    radiusKm: nearOnly ? radiusKm : 50,
    page,
    style:    activeStyle || undefined,
  };

  const { data: images, isLoading, isFetching } = useGetPortfolioFeedQuery(feedArgs);

  // Saved image IDs — only fetch when logged in
  const { data: savedIds = [] } = useGetSavedImageIdsQuery(undefined, { skip: !token });
  const [saveImage]   = useSaveImageMutation();
  const [unsaveImage] = useUnsaveImageMutation();

  const savedSet = useMemo(
    () => new Set(savedIds),
    [savedIds],
  );

  // Accumulate pages (infinite scroll append)
  // Reset when style or nearOnly changes
  useEffect(() => {
    if (images) {
      setAllImages((prev) => page === 1 ? images : [...prev, ...images]);
    }
  }, [images, page]);
  // Note: This useEffect accumulates API results into local state. It is NOT
  // data fetching — RTK Query fetches the data. This effect only merges pages
  // into the accumulated list. This is a client-side accumulation side-effect.

  // Reset to page 1 when filter changes
  function handleStyleChange(style: string) {
    setActiveStyle(style);
    setPage(1);
    setAllImages([]);
  }

  function handleToggleSave(imageId: string, isSaved: boolean) {
    if (!userId) return;
    if (isSaved) unsaveImage(imageId);
    else saveImage(imageId);
  }

  if (isLoading && page === 1) return <PortfolioSkeleton />;

  if (allImages.length === 0 && !isLoading) {
    return (
      <div className="space-y-4">
        <StyleChips activeStyle={activeStyle} onChange={handleStyleChange} />
        {/* ...existing empty state JSX... */}
      </div>
    );
  }

  const hasMore = (images?.length ?? 0) >= 24;

  return (
    <div className="space-y-4">
      <StyleChips activeStyle={activeStyle} onChange={handleStyleChange} />

      <MasonryGrid
        images={allImages}
        onOpen={setLightboxImage}
        savedIds={savedSet}
        onToggleSave={handleToggleSave}
        token={token}
      />

      {/* Load more */}
      {hasMore && (
        <div className="flex justify-center pt-2 pb-6">
          <Button
            variant="outline"
            onClick={() => setPage((p) => p + 1)}
            disabled={isFetching}
            aria-label={isFetching ? "Loading more images" : "Load more portfolio images"}
            className="min-w-[140px]"
          >
            {isFetching ? "Loading…" : "Load more"}
          </Button>
        </div>
      )}

      {lightboxImage !== null && (
        <PortfolioLightbox
          image={lightboxImage}
          token={token}
          onClose={() => setLightboxImage(null)}
        />
      )}
    </div>
  );
}
```

**Important about the `useEffect` for page accumulation:** This is the one approved
pattern where `useEffect` interacts with API data — it is accumulating paged results into
a client-side list, not fetching data. RTK Query does the fetching. Document this with
the comment above. If a future version of RTK Query supports cursor pagination natively,
migrate to that.

### 3-E: Update `PortfolioLightbox` — add style badge

In the lightbox info panel, show the image's style tag if present:

```tsx
{image.style && (
  <span className="inline-block text-[10px] font-medium uppercase tracking-wider
                   px-2 py-0.5 rounded-full bg-zinc-800 text-zinc-300 border border-zinc-700">
    {image.style}
  </span>
)}
```

---

## Section 4 — Frontend: `DiscoverPage.tsx` — targeted fixes

Read the current file. Apply each fix in order. Do not rewrite the entire file —
edit only what is listed here.

### 4-A: Logo icon — replace generic pen with tattoo needle

The current SVG is a generic pencil/edit glyph identical to text-editor toolbars.
Replace with a simple abstract mark — a stylised needle silhouette:

```tsx
{/* Replace the existing <svg> element for the logo icon */}
<svg
  aria-hidden="true"
  viewBox="0 0 24 24"
  className="h-5 w-5"
  fill="none"
  stroke="currentColor"
  strokeWidth="1.75"
  strokeLinecap="round"
  strokeLinejoin="round"
>
  {/* Needle body */}
  <line x1="12" y1="2" x2="12" y2="18" />
  {/* Needle tip */}
  <path d="M10 16 L12 22 L14 16" />
  {/* Ink drop at top */}
  <circle cx="12" cy="5" r="2" fill="currentColor" stroke="none" />
  {/* Cross-guard */}
  <line x1="8" y1="9" x2="16" y2="9" />
</svg>
```

Add a screenreader-visible site name adjacent to the visual:
```tsx
<span className="font-semibold tracking-tight text-sm">Pena e Artë</span>
{/* aria-label on the wrapping Link */}
<Link to="/discover" aria-label="Pena e Artë — Home" className="flex items-center gap-2">
  {/* logo SVG + name */}
</Link>
```

### 4-B: Active tab state — pronounce the active tab (fix #4)

**Problem:** Active tab uses `bg-background shadow-sm text-foreground` which is barely
distinguishable from the inactive `text-muted-foreground` at a glance.

**Fix:** Add a violet bottom border + `font-semibold` to the active tab. Change the
container from a floating pill to an underline-style tablist:

```tsx
{/* Replace the existing tablist div */}
<div
  role="tablist"
  aria-label="Content type"
  className="flex items-center gap-0 border-b border-border/40"
>
  {(["portfolio", "studios"] as const).map((tab) => (
    <button
      key={tab}
      role="tab"
      id={`tab-${tab}`}
      aria-selected={activeTab === tab}
      aria-controls={`panel-${tab}`}
      onClick={() => setActiveTab(tab)}
      className={`px-4 py-2 text-xs font-medium transition-colors capitalize
                  border-b-2 -mb-px
                  ${activeTab === tab
                    ? "border-violet-500 text-foreground font-semibold"
                    : "border-transparent text-muted-foreground hover:text-foreground"
                  }`}
    >
      {tab === "portfolio" ? "Portfolio" : "Studios"}
    </button>
  ))}
</div>
```

Add `id` and `aria-controls` attributes matching the content panel `id`:
```tsx
<main id="panel-portfolio" role="tabpanel" aria-labelledby="tab-portfolio" ...>
  {activeTab === "portfolio" && <PortfolioFeed ... />}
</main>
```

### 4-C: "Register studio" button — fix WCAG contrast (fix #6 / accessibility)

**Problem:** `text-violet-400` on dark background = ~7.4:1 (passes). But the border
`border-violet-500/60` with `60%` opacity lowers contrast of the border itself.
More importantly, make the button visually stronger as a CTA.

**Fix:** Increase border to `border-violet-500` (full opacity) at 2px, add a very subtle
background tint:

```tsx
<Link to="/register"
  className="text-xs font-medium px-3 py-2 rounded-md
             border-2 border-violet-500 text-violet-400
             bg-violet-500/5
             hover:bg-violet-500/15 hover:text-violet-300
             transition-colors
             focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-violet-500">
  Register studio
</Link>
```

### 4-D: "Near me" pill — replace implicit `|` separator (fix #6 visuals)

Looking at the existing code, the `Near me` button and `locationName` span are separate
elements. But they appear adjacent without a clear visual separator. Add an explicit
`·` (middle dot) separator between them:

```tsx
{/* Inside the location state section, when both nearOnly-toggle and locationName are shown */}
{activeTab === "portfolio" && lat !== null && (
  <button ...>
    <MapPin ... />
    Near me
  </button>
)}
{locationName && (
  <>
    <span aria-hidden="true" className="text-border select-none">·</span>
    <span className="text-xs text-muted-foreground hidden sm:block truncate max-w-[140px]"
          aria-label={`Current location: ${locationName}`}>
      {locationName}
    </span>
  </>
)}
```

### 4-E: Search placeholder — improve copy (fix #7 content)

```tsx
// Change placeholder from "Search city…" to:
placeholder="Find artists in a city…"
// And the aria-label:
aria-label="Search for a city to discover artists"
```

### 4-F: "View on map" microcopy (fix #7 content)

```tsx
<Link to="/map" ...>
  View studios on map
</Link>
```

### 4-G: Value proposition hero for logged-out users (new feature)

Add a conditional hero block rendered above the search row in the header, only when
`token === null` (user is not logged in). This is the acquisition funnel for first-time
visitors.

```tsx
// At the top of DiscoverPage component, extract token from Redux:
const token = useAppSelector((s) => s.auth.token);

// In the header JSX, add before the bottom row (search + tabs row):
{!token && (
  <div className="px-4 pt-1 pb-2.5 border-b border-border/40">
    <p className="text-xs text-muted-foreground max-w-sm">
      Discover tattoo artists and studios near you. Browse portfolios, read
      reviews, and book your next session.
    </p>
  </div>
)}
```

For the page itself — below the header and above the content area — add a hero `<section>`
visible only to logged-out visitors, rendered before the tab panels:

```tsx
{/* In <main>, before the tab panels */}
{!token && !locationName && (
  <section aria-labelledby="hero-heading" className="py-10 text-center space-y-3">
    <h1 id="hero-heading" className="text-2xl font-bold tracking-tight">
      Discover tattoo artists near you
    </h1>
    <p className="text-sm text-muted-foreground max-w-md mx-auto">
      Browse portfolios from studios worldwide, filter by style, and find the
      artist who matches your vision.
    </p>
    <div className="flex items-center justify-center gap-3 pt-1">
      <Link to="/login"
        className="text-sm text-violet-400 hover:text-violet-300 underline
                   underline-offset-4 transition-colors">
        Sign in
      </Link>
      <span aria-hidden="true" className="text-border">·</span>
      <Link to="/register"
        className="text-sm font-medium px-4 py-1.5 rounded-md
                   bg-violet-600 hover:bg-violet-700 text-white transition-colors">
        Register your studio
      </Link>
    </div>
  </section>
)}
```

Show the hero only when no location has been detected yet (no `locationName`) so it
doesn't compete with the feed once content loads.

### 4-H: Footer — replace redundant "Powered by" with useful content

```tsx
{/* Replace the existing footer */}
<footer className="py-5 border-t border-border/40">
  <div className="max-w-6xl mx-auto px-4 flex flex-col sm:flex-row items-center
                  justify-between gap-3 text-xs text-foreground/50">
    <span>© {new Date().getFullYear()} Pena e Artë. All rights reserved.</span>
    <nav aria-label="Footer links" className="flex items-center gap-4">
      <Link to="/discover" className="hover:text-foreground/80 transition-colors">
        Discover
      </Link>
      <Link to="/map" className="hover:text-foreground/80 transition-colors">
        Map
      </Link>
      <Link to="/register" className="hover:text-foreground/80 transition-colors">
        Register studio
      </Link>
    </nav>
  </div>
</footer>
```

### 4-I: Tab transition — visual feedback on switch

Add a brief fade transition when switching tabs so the user knows the click registered:

```tsx
{/* Wrap tab panel content in a fade div */}
<div
  key={activeTab}          {/* key change forces remount → CSS transition plays */}
  className="animate-in fade-in duration-150"
>
  {activeTab === "portfolio" && <PortfolioFeed ... />}
  {activeTab === "studios"  && <div className="space-y-4">...</div>}
</div>
```

`animate-in` and `fade-in` are Tailwind CSS v3 animation utilities. If they are not
in the current Tailwind config, add this custom animation to `tailwind.config.ts`:

```typescript
// In the existing theme.extend block:
keyframes: {
  "fade-in": { from: { opacity: "0" }, to: { opacity: "1" } },
},
animation: {
  "fade-in": "fade-in 0.15s ease-out",
},
```

---

## Section 5 — Tests

### 5-A: `DiscoverPage.test.tsx` — update and add tests

All existing tests must continue to pass. Add:

```typescript
it("renders value-proposition hero when not authenticated and no location", () => {
  renderPage(); // store has no auth token by default
  expect(screen.getByRole("heading", {
    name: /discover tattoo artists near you/i
  })).toBeInTheDocument();
});

it("renders 'Portfolio' tab with bottom-border active indicator", () => {
  renderPage();
  const portfolioTab = screen.getByRole("tab", { name: /portfolio/i });
  expect(portfolioTab.className).toMatch(/border-violet/);
  expect(portfolioTab).toHaveAttribute("aria-selected", "true");
});

it("active tab has aria-selected=true, inactive has aria-selected=false", async () => {
  const user = userEvent.setup();
  renderPage();
  const studiosTab   = screen.getByRole("tab", { name: /studios/i });
  const portfolioTab = screen.getByRole("tab", { name: /portfolio/i });
  expect(portfolioTab).toHaveAttribute("aria-selected", "true");
  expect(studiosTab).toHaveAttribute("aria-selected", "false");
  await user.click(studiosTab);
  expect(studiosTab).toHaveAttribute("aria-selected", "true");
  expect(portfolioTab).toHaveAttribute("aria-selected", "false");
});

it("footer contains copyright notice, not just 'Powered by' branding", () => {
  renderPage();
  expect(screen.getByText(/all rights reserved/i)).toBeInTheDocument();
  expect(screen.queryByText(/powered by pena/i)).not.toBeInTheDocument();
});

it("search placeholder reads 'Find artists in a city'", () => {
  renderPage();
  expect(screen.getByPlaceholderText(/find artists in a city/i)).toBeInTheDocument();
});
```

Update `it("renders 'View on map'...")` to match the new copy:
```typescript
it("renders 'View studios on map' nav link", () => {
  renderPage();
  expect(screen.getByRole("link", { name: /view studios on map/i })).toBeInTheDocument();
});
```

### 5-B: `PortfolioFeed.test.tsx` — update and add tests

Add to the MSW server in the test file:
```typescript
http.get("http://localhost/api/v1/saved-images/ids", () => HttpResponse.json([])),
```

(This prevents unhandled-request warnings when `getSavedImageIds` fires.)

Add tests:

```typescript
it("renders style chip row with 'All' chip as default selected", async () => {
  renderFeed();
  await screen.findByLabelText(/Tattoo by Ana Lima/i);
  const allChip = screen.getByRole("radio", { name: /^all$/i });
  expect(allChip).toHaveAttribute("aria-checked", "true");
});

it("each style chip is accessible as a radio button", async () => {
  renderFeed();
  await screen.findByLabelText(/Tattoo by Ana Lima/i);
  expect(screen.getByRole("radio", { name: /blackwork/i })).toBeInTheDocument();
  expect(screen.getByRole("radio", { name: /realism/i })).toBeInTheDocument();
});

it("clicking a style chip sends the style query param to the API", async () => {
  const user = userEvent.setup();
  let capturedStyle: string | null = null;
  server.use(
    http.get("http://localhost/api/v1/public/portfolio/feed", ({ request }) => {
      capturedStyle = new URL(request.url).searchParams.get("style");
      return HttpResponse.json(IMAGES);
    }),
  );
  renderFeed();
  await screen.findByLabelText(/Tattoo by Ana Lima/i);
  await user.click(screen.getByRole("radio", { name: /blackwork/i }));
  // Wait for refetch
  await vi.waitFor(() => expect(capturedStyle).toBe("blackwork"));
});

it("attribution strip shows artist name below each image (always visible)", async () => {
  renderFeed();
  // The attribution strip is always visible (not just on hover), so it's in the DOM
  expect(await screen.findByText("Ana Lima")).toBeInTheDocument();
  expect(await screen.findByText("João Costa")).toBeInTheDocument();
});

it("tile is wrapped in a listitem role", async () => {
  renderFeed();
  await screen.findByLabelText(/Tattoo by Ana Lima/i);
  const listitems = screen.getAllByRole("listitem");
  expect(listitems.length).toBeGreaterThanOrEqual(2);
});
```

---

## Section 6 — Accessibility audit

After all JSX changes, run this mental checklist on both components:

| Check | Where | Required fix |
|---|---|---|
| `<button>` with only icon child has `aria-label` | Bookmark button, close button | ✅ Already added |
| Tab has `aria-selected`, `role="tab"`, `aria-controls` | Portfolio/Studios tabs | ✅ Added in 4-B |
| Tab panel has `role="tabpanel"`, `aria-labelledby` | Main content panel | Add in 4-B |
| Star rating has accessible label | `StarRating` component | Verify existing impl |
| Search input has `aria-label` | City search input | ✅ Updated in 4-E |
| "Register studio" button text contrast ≥ 4.5:1 | Nav CTA | ✅ Fixed in 4-C |
| Empty state has meaningful copy | Empty feed + empty studios | ✅ Already there |
| Logo link has `aria-label` | Brand link | ✅ Added in 4-A |
| Loading region has `aria-busy` and `aria-label` | Skeleton components | ✅ Already there |
| "Near me" toggle has accessible pressed state | Near me button | Add `aria-pressed={nearOnly}` |

Add `aria-pressed={nearOnly}` to the "Near me" toggle button:
```tsx
<button
  type="button"
  aria-pressed={nearOnly}
  aria-label={nearOnly ? "Location filter active — click to show all" : "Filter to near me"}
  onClick={() => setNearOnly((v) => !v)}
  ...
>
```

---

## Section 7 — Build checklist

Run in order. Fix every error before moving to the next step.

```bash
cd "Pena e Arte"

# 1. Backend build (for new entities, commands, endpoints)
dotnet build --verbosity minimal

# 2. Migrations
dotnet ef database update --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API

# 3. Backend tests
dotnet test

# 4. Frontend type check
cd frontend && pnpm tsc --noEmit

# 5. Lint
pnpm lint

# 6. Frontend tests — all must pass
pnpm test --run
```

All six must exit 0.

---

## Architecture docs update

After completing all changes, update `docs/claude/architecture.md`:

1. **IgnoreQueryFilters Approved Usages table** — add entries #16, #17, #18 for the
   three new save-image handlers.

2. **Decisions Log** — add:
   ```
   | Masonry layout algorithm | Round-robin JS column distribution (not CSS columns) |
   | Avoided CSS `columns-*` masonry — causes unbalanced column heights with varied image sizes |
   | Style filter | `style` string field on PortfolioImage; chip UI in PortfolioFeed |
   | Bookmark feature | SavedPortfolioImage entity — cross-tenant, UserId-scoped |
   | Portfolio attribution | Always-visible name strip on tile + hover overlay for extra context |
   ```

3. **Feature Module Map** — add Feature 21: Bookmark / Saved Images.

---

## Hard rules reminder

- **No new npm or NuGet packages.** `Bookmark` icon from `lucide-react` (already installed).
  `animate-in`/`fade-in` via Tailwind config extension (no new package).
- **No `useEffect` for data fetching** — the page-accumulation `useEffect` in
  `PortfolioFeed` is approved because RTK Query does the fetch; the effect only merges
  pages into local state. Document it with a comment.
- **No `any`.** All new props and state must be typed.
- **No default exports on components.**
- **No TypeScript `enum`.** Use `as const` objects.
- **Every new endpoint has a FluentValidation validator** — `SavePortfolioImageCommand`
  has no user-supplied text, so its validator only checks `ImageId != Guid.Empty` and
  `UserId != Guid.Empty`. Create `SavePortfolioImageValidator` anyway.
- **Tests run green** before the session ends.
