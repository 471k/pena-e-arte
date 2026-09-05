# Overnight Prompt — Portfolio Image Categories (Fresh / Healed / Design)

> Date: 2026-08-27
> Target: `Pena_e_Arte.Domain`, `Pena_e_Arte.Contracts`, `Pena_e_Arte.Application` (Artists, Public,
> Saved), `Pena_e_Arte.Infrastructure` (one EF migration), `Pena_e_Arte.API`,
> `frontend/src/features/artists`, `frontend/src/features/public`, backend + frontend tests,
> Help Menu (`helpContent.ts`), standalone user manual (`index.html`).
> One new EF Core migration (nullable column — zero-downtime, no data backfill). No new npm or
> NuGet packages — `DropdownMenu` and `Badge` already exist in `shared/components/ui`. No
> onboarding-tour changes needed (see Part 9c — verified, not assumed).

---

## Pre-flight

1. Read `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/frontend.md`, `docs/claude/database.md`,
   `docs/claude/conventions.md` before making any changes.
2. Baseline, before touching anything:
   - `dotnet build`
   - `dotnet test` — note the current pass count; pre-existing failures are not this prompt's
     problem, but do not introduce new ones.
   - `pnpm tsc --noEmit`
   - `pnpm test src/features/artists src/features/public` — confirm the current suite is green
     first.
3. Read `Pena_e_Arte.Domain/Constants/TattooStyle.cs`,
   `Pena_e_Arte.Application/Artists/Commands/UpdateArtistPortfolioCommand.cs`,
   `Pena_e_Arte.Application/Public/Queries/GetPortfolioFeedQuery.cs`, and
   `frontend/src/features/public/components/PortfolioFeed.tsx` in full before starting. `Style` is
   the exact, already-shipped precedent this prompt's new `Category` field must mirror end to end —
   same nullable-string-plus-constants-class shape, same "no backfill" migration philosophy, same
   client-side vs. server-side filtering split between the artist's own portfolio page and the
   public discovery feed. Every deviation from that precedent in this prompt is called out
   explicitly in the Decisions table below; anywhere it isn't called out, follow `Style` exactly.

---

## Context — current state (verified against live source, 2026-08-27)

- The artist portfolio upload feature already exists and is fully built: `PortfolioImage` (a
  `TenantEntity`, `Pena_e_Arte.Domain/Entities/PortfolioImage.cs`) has `ArtistId`, `ImageUrl`, and
  a nullable `Style` (max 50 chars, tagged via `TattooStyle` constants). Artists (and owners
  managing an artist) upload via `ArtistDetailPage.tsx`'s "Portfolio" tab — `openImagePicker()`
  opens a native file picker, uploads through `usePresignedUpload()` to Cloudflare R2 under
  `portfolio/{artistId}/...`, then calls `useUpdateArtistPortfolioMutation()` (backed by
  `UpdateArtistPortfolioCommand`) to persist the new image row with `style: null`. Each image
  already gets a **post-hoc** style `Select` dropdown (`STYLE_OPTIONS`, synced with `TattooStyle.cs`)
  so it can be tagged or retagged at any time after upload.
- **There is no concept of "what stage of the work this photo shows" anywhere in the schema today.**
  Every uploaded photo is treated identically — a freshly-finished tattoo, a healed result weeks
  later, and a flash/stencil design that hasn't been tattooed yet are all just rows in the same
  `PortfolioImages` table with no field distinguishing them. This prompt adds exactly that one new
  field — `Category` — modeled as a direct sibling of `Style`, not a replacement for it. The two are
  independent, orthogonal tags on the same image (a photo can be both "blackwork" *and* "healed").
- **This is unrelated to the existing `Design`/`DesignApproval`/`DesignRevision` entities.** The
  codebase already has a `Design` entity (`Pena_e_Arte.Domain/Entities/Design.cs`) — a specific
  client's commissioned tattoo-design proposal, tied to one `ClientId`/`ArtistId` pair, with its own
  approval/revision workflow (`frontend/src/features/designs/`). That is a private, per-client
  booking artifact. What this prompt adds is a public *portfolio* label — "this photo in my public
  gallery is a design/flash piece, not a finished tattoo" — with no relationship to the `Design`
  entity or table whatsoever. Do not touch `Design`, `DesignApproval`, `DesignRevision`, or anything
  under `Pena_e_Arte.Application/Designs/` or `frontend/src/features/designs/` in this prompt. To
  keep this unambiguous in code, the new constants class is named `PortfolioImageCategory`, not
  `DesignCategory` or anything that could be confused with the unrelated `Design` entity.
- `PortfolioImageResponse` (`Pena_e_Arte.Contracts/Responses/Public/PortfolioImageResponse.cs`) is
  shared by three different handlers: `GetPortfolioFeedHandler` (public Discover feed),
  `GetSavedPortfolioImagesHandler` (a logged-in user's bookmarked images), and indirectly nothing
  else. All three must be checked when this DTO's shape changes — this prompt updates the field and
  both handler call sites explicitly (Parts 3d/3f); do not assume the DTO change alone is enough.
- `ArtistPortfolioImageResponse` (`Pena_e_Arte.Contracts/Responses/Public/ArtistPortfolioImageResponse.cs`)
  is separately constructed in two places: `CreateArtistHandler.Map` (used by every `ArtistResponse`
  — create, update, get, list, and the portfolio-update return value) and `GetPublicArtistHandler`
  (the public `/artist/{slug}` page). Both are updated explicitly in Part 3.
- `StudioPortfolioPage`'s highlight gallery (`GetPublicStudioHandler`,
  `Pena_e_Arte.Application/Public/Queries/GetPublicStudioQuery.cs`) currently does
  `a.Portfolio.Select(p => p.ImageUrl).Take(3).ToList()` per artist with no ordering and no filtering
  at all — whatever order EF returns `Portfolio` in (unspecified; no `OrderBy`) is what a visitor
  sees. See Decision #6 below for the one behavior change this prompt makes here.

---

## Decisions (already made with the product owner — do not re-litigate)

| # | Decision | Rationale |
|---|---|---|
| 1 | New field `PortfolioImage.Category` — nullable `string`, max 20 chars, values from a new `PortfolioImageCategory` constants class: `fresh` ("Fresh Tattoo"), `healed` ("Healed Tattoo"), `design` ("Design"). Independent of `Style`, not a replacement. | Mirrors `TattooStyle`/`PortfolioImage.Style` exactly — same shape, same validation pattern, same "app-controlled string, not a DB enum" rationale already recorded in `architecture.md`'s Decisions Log for `Style` (avoids a migration on every future category addition). |
| 2 | The DB column is **nullable**, and **existing portfolio images are not backfilled** — every row uploaded before this migration keeps `Category = null` ("Uncategorized") until an artist or owner relabels it. | Follows the exact same precedent as `Style`'s own rollout (`AddPortfolioImageStyle` migration added a nullable column with no backfill) and the more recent `Client.ArtistId` precedent (`AddArtistIdToClient`, 2026-08-20): there is no reliable way to infer which of three categories an already-uploaded photo belongs to, and a wrong guess (e.g. auto-labeling every legacy photo "fresh") is worse than a visible, correctable "Uncategorized" state. Zero-downtime migration — no data migration step. |
| 3 | **New uploads are categorized at upload time, not only after the fact.** The single "Add image" button on `ArtistDetailPage.tsx`'s Portfolio tab becomes a `DropdownMenu` with three explicit actions — "Fresh Tattoo", "Healed Tattoo", "Design" — each opens the same file picker/upload flow as today but tags the resulting image with that category immediately. Each image **also** keeps a per-image Category `Select` (placed directly above the existing Style `Select`, same component, same interaction pattern) so a category can still be corrected or added after the fact — for both new uploads and legacy "Uncategorized" ones. | This is the literal ask — "he should be able to label **and/or** upload them categorized" — and the existing single-button, tag-after upload flow (which is all `Style` has) doesn't fully cover the "upload... categorized as" half. Keeping the post-hoc `Select` too (rather than making category upload-time-only) preserves the ability to fix a mis-clicked category or tag old photos, exactly like `Style` already allows. |
| 4 | Category is exposed as a **segmented three-tab filter** (All / Fresh Tattoos / Healed Tattoos / Designs) on the public Discover feed and on an artist's public portfolio page, rendered **above** the existing Style chip row as a visually distinct control — not merged into the Style chips as more chips. | `Style` is an open, growing taxonomy (8 values today, more likely later) best suited to a scrollable chip row. `Category` is a small, fixed, coarser partition of the same gallery — closer to how Vagaro/Fresha/Boulevard-tier competitors already split an artist's work into "Portfolio / Healed / Flash"-style tabs (CLAUDE.md rule 6 — current category standard). Keeping it visually separate from Style avoids implying it's part of the same open list, and the two filters combine independently (e.g. "Healed" + "Blackwork" together), exactly like Style already combines with the existing radius/keyword filters on the Discover feed. |
| 5 | Filtering behavior mirrors `Style`'s existing (and already slightly asymmetric) split exactly: the public Discover feed (`PortfolioFeed.tsx` / `GetPortfolioFeedQuery`) filters **server-side** via a new `category` query parameter; an artist's own public portfolio page (`ArtistPortfolioPage.tsx`) filters **client-side** over the already-fully-loaded `portfolioImages` array, same as its existing `activeStyle` filter. No change to `DiscoverPage.tsx` — it does not own any style/category state today (`PortfolioFeed.tsx` manages `activeStyle` internally), and category follows the identical pattern, verified by reading the component, not assumed. | Consistency over introducing a second filtering strategy. The Discover feed is paginated and cross-tenant (must filter in the DB query); an artist's own portfolio page is a single already-loaded array (client-side filtering is strictly simpler and matches the existing `availableStyles`/`activeStyle` code exactly). |
| 6 | `StudioPortfolioPage`'s per-artist highlight gallery (`GetPublicStudioHandler`, currently `a.Portfolio.Select(p => p.ImageUrl).Take(3)`, max 9 total) now **prefers Fresh/Healed tattoo photos over Design images**, ordered by `CreatedAt` descending within each group, falling back to Design images only when an artist has fewer than 3 non-Design images. | Flagged explicitly per CLAUDE.md rule 6: a studio's public highlight strip is the first thing a prospective client sees, and letting it be dominated by flash/stencil sketches instead of finished work (today's `Take(3)` has no ordering or filtering at all — whatever EF happens to return) falls behind how every competitor in this category curates a studio profile's hero gallery. This is the one place this prompt changes existing ranking behavior rather than purely adding a new field; kept as its own isolated part (Part 7) so it can be reviewed/reverted independently of the rest of this prompt if the product owner disagrees. |
| 7 | No per-category cap. The existing studio-wide 50-image total cap in `UpdateArtistPortfolioValidator` is unchanged. | Nothing in the request asked for per-category limits, and inventing one is scope creep the product owner didn't ask for. |
| 8 | `PortfolioImageResponse` and `ArtistPortfolioImageResponse` gain `Category` as a genuinely new positional record parameter (no default value) — every construction call site, production and test, must be updated explicitly and the compiler used to find stragglers. | Matches this codebase's own established methodology for exactly this class of change (see the `Client.ArtistId`/`ClientResponse` precedent, 2026-08-20: "grep the whole repo... run `dotnet build`... fix whatever the type checker... surfaces, do not try to enumerate every file by hand up front"). A silent default would let a call site compile while quietly always sending `Category: null`. |

**Explicitly out of scope, flagged and not touched here:** `SavedImagesPage`/the saved-images UI —
`useGetSavedImagesQuery`/`savedImagesApi.ts` exist and are fully wired on the backend
(`GetSavedPortfolioImagesQuery`, `POST/DELETE /api/v1/saved-images/{id}`), but a grep of
`frontend/src/features` for any consumer of `useGetSavedImagesQuery` (as opposed to the
`useGetSavedImageIdsQuery` bookmark-toggle check, which **is** wired into `PortfolioFeed.tsx`) found
none — there appears to be no page that actually renders a user's saved-images list yet. That's a
pre-existing gap unrelated to this prompt; `Category` still flows through
`GetSavedPortfolioImagesHandler`'s response correctly (Part 3f) so nothing regresses, but building a
saved-images page is not part of this prompt.

---

## Part 1 — Domain + EF Core

### 1a. New file — `Pena_e_Arte.Domain/Constants/PortfolioImageCategory.cs`

```csharp
namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Canonical portfolio-image category identifiers — what stage of work a portfolio photo shows.
/// Used on PortfolioImage.Category. Independent of TattooStyle (Style is the tattoo's artistic
/// style; Category is fresh/healed/design). Not related to the Design/DesignApproval/DesignRevision
/// entities (a per-client commissioned-tattoo workflow) — this is a public portfolio label only.
/// Keep in sync with CATEGORY_OPTIONS in ArtistDetailPage.tsx and CATEGORIES in PortfolioFeed.tsx.
/// </summary>
public static class PortfolioImageCategory
{
    public const string FreshTattoo = "fresh";
    public const string HealedTattoo = "healed";
    public const string Design = "design";

    public static readonly IReadOnlyList<string> All = [FreshTattoo, HealedTattoo, Design];
}
```

### 1b. `Pena_e_Arte.Domain/Entities/PortfolioImage.cs`

Add, directly after `Style`:

```csharp
/// <summary>
/// Optional portfolio category tag. Values are app-controlled; see PortfolioImageCategory
/// constants. Max 20 chars. Null means uncategorized. Independent of Style.
/// </summary>
public string? Category { get; set; }
```

### 1c. `Pena_e_Arte.Infrastructure/Persistence/Configurations/PortfolioImageConfiguration.cs`

Add, directly after the existing `Style` property config:

```csharp
builder.Property(p => p.Category).HasMaxLength(20).IsRequired(false);
```

### 1d. Migration

```bash
dotnet ef migrations add AddPortfolioImageCategory \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

Verify the generated migration adds a single **nullable** `varchar(20)` `Category` column to
`PortfolioImages` — nothing else. Apply it locally (`dotnet ef database update ...`) and confirm the
app still boots before moving on.

---

## Part 2 — Contracts

### 2a. `Pena_e_Arte.Contracts/Requests/UpdateArtistPortfolioRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record UpdateArtistPortfolioRequest(List<PortfolioImageInput> Images);

public record PortfolioImageInput(string ImageUrl, string? Style, string? Category);
```

### 2b. `Pena_e_Arte.Contracts/Responses/Public/ArtistPortfolioImageResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses.Public;

public record ArtistPortfolioImageResponse(Guid ImageId, string ImageUrl, string? Style, string? Category);
```

### 2c. `Pena_e_Arte.Contracts/Responses/Public/PortfolioImageResponse.cs`

Add `Category` directly after `Style`:

```csharp
public record PortfolioImageResponse(
    Guid ImageId,
    string ImageUrl,
    string? Style,              // nullable — untagged images are valid
    string? Category,           // nullable — uncategorized images are valid; fresh/healed/design
    string ArtistName,
    string ArtistSlug,
    string StudioName,
    string StudioSlug,
    double? AverageRating,      // artist-level rating; null = no reviews yet
    int ReviewCount,        // artist-level
    double? ImageAverageRating, // rating for this specific image; null = no reviews
    int ImageReviewCount,   // review count for this specific image
    double? DistanceKm,         // null when no location context provided
    long ViewCount);         // from Redis; 0 when not yet viewed
```

This is a breaking positional-record change (two new params, no defaults, per Decision #8). Grep the
whole repo for `new PortfolioImageInput(`, `new ArtistPortfolioImageResponse(`, and
`new PortfolioImageResponse(` — the two production call sites for each are updated explicitly in
Part 3 below; everything else (test fixtures) needs the same treatment. Run `dotnet build` on the
test projects afterward and fix whatever the compiler flags — do not try to enumerate every test
file by hand up front.

---

## Part 3 — Application layer

### 3a. `Pena_e_Arte.Application/Artists/Commands/UpdateArtistPortfolioCommand.cs` — handler replacement

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Commands;

public record UpdateArtistPortfolioCommand(Guid Id, UpdateArtistPortfolioRequest Request) : IRequest<ArtistResponse>;

public class UpdateArtistPortfolioHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<UpdateArtistPortfolioCommand, ArtistResponse>
{
    public async Task<ArtistResponse> Handle(UpdateArtistPortfolioCommand command, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == command.Id, ct);
        if (artist is null)
            throw new NotFoundException(nameof(Artist), command.Id);

        if (currentUser.Role == "artist" && artist.UserId != currentUser.UserId)
            throw new ForbiddenException();

        // Sync PortfolioImage rows: preserve existing (to keep their reviews and pick
        // up any style/category change), add new, remove stale.
        List<PortfolioImage> existing = await db.PortfolioImages
            .Where(p => p.ArtistId == command.Id)
            .ToListAsync(ct);

        Dictionary<string, PortfolioImageInput> incomingByUrl = command.Request.Images
            .ToDictionary(i => i.ImageUrl, i => i);

        // Delete removed images — cascade removes their reviews.
        List<PortfolioImage> toRemove = existing.Where(p => !incomingByUrl.ContainsKey(p.ImageUrl)).ToList();
        db.PortfolioImages.RemoveRange(toRemove);

        Dictionary<string, PortfolioImage> existingByUrl = existing.ToDictionary(p => p.ImageUrl);
        foreach (PortfolioImageInput input in command.Request.Images)
        {
            if (existingByUrl.TryGetValue(input.ImageUrl, out PortfolioImage? kept))
            {
                kept.Style = input.Style;
                kept.Category = input.Category;
            }
            else
            {
                db.PortfolioImages.Add(new PortfolioImage
                {
                    ArtistId = artist.Id,
                    StudioId = artist.StudioId,
                    ImageUrl = input.ImageUrl,
                    Style = input.Style,
                    Category = input.Category,
                });
            }
        }

        artist.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Reload with portfolio for mapping.
        Artist updated = await db.Artists
            .Include(a => a.Portfolio)
            .FirstAsync(a => a.Id == command.Id, ct);

        return CreateArtistHandler.Map(updated);
    }
}
```

Only two lines changed from the current handler (`kept.Category = input.Category;` and
`Category = input.Category,`) — everything else is unchanged, shown in full to keep the handler
copy-pasteable.

### 3b. `Pena_e_Arte.Application/Artists/Validators/UpdateArtistPortfolioValidator.cs`

```csharp
using FluentValidation;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Domain.Constants;

namespace Pena_e_Arte.Application.Artists.Validators;

public class UpdateArtistPortfolioValidator : AbstractValidator<UpdateArtistPortfolioCommand>
{
    public UpdateArtistPortfolioValidator()
    {
        RuleFor(x => x.Request.Images).NotNull();
        RuleForEach(x => x.Request.Images).ChildRules(image =>
        {
            image.RuleFor(i => i.ImageUrl)
                .NotEmpty()
                .MaximumLength(2048)
                .Must(url => Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                    && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                .WithMessage("Each image must be a valid absolute URL.");
            image.RuleFor(i => i.Style)
                .Must(s => s is null || TattooStyle.All.Contains(s))
                .WithMessage($"Style must be one of: {string.Join(", ", TattooStyle.All)}.");
            image.RuleFor(i => i.Category)
                .Must(c => c is null || PortfolioImageCategory.All.Contains(c))
                .WithMessage($"Category must be one of: {string.Join(", ", PortfolioImageCategory.All)}.");
        });
        RuleFor(x => x.Request.Images.Count).LessThanOrEqualTo(50)
            .WithMessage("A maximum of 50 portfolio images are allowed.");
    }
}
```

### 3c. `Pena_e_Arte.Application/Artists/Commands/CreateArtistCommand.cs`

Update the `Map` method's `ArtistPortfolioImageResponse` projection:

```csharp
internal static ArtistResponse Map(Artist a) =>
    new(a.Id, a.StudioId, a.UserId, a.FirstName, a.LastName, a.Email, a.Specializations, a.HourlyRate,
        a.IsActive, a.AvatarUrl,
        a.Portfolio.OrderByDescending(p => p.CreatedAt)
            .Select(p => new ArtistPortfolioImageResponse(p.Id, p.ImageUrl, p.Style, p.Category))
            .ToList(),
        a.Slug, a.CreatedAt, a.UpdatedAt);
```

(Only the `Select` line changes — adds `, p.Category` to the constructor call.) This is the one
`Map` function used by every `ArtistResponse`-returning handler (create, update, get-by-id,
list, and `UpdateArtistPortfolioHandler`'s return value), so this single change covers all of them.

### 3d. `Pena_e_Arte.Application/Public/Queries/GetPortfolioFeedQuery.cs`

Add `Category` to the query record and filter, then include it in the response projection:

```csharp
public record GetPortfolioFeedQuery(
    double? Lat,
    double? Lng,
    double RadiusKm,
    int Page,
    int PageSize = 24,
    string? Style = null,
    string? Category = null,
    string? Search = null) : IRequest<List<PortfolioImageResponse>>;
```

After the existing `Style` filter block:

```csharp
if (!string.IsNullOrWhiteSpace(query.Style))
    imageQuery = imageQuery.Where(p => p.Style == query.Style);

if (!string.IsNullOrWhiteSpace(query.Category))
    imageQuery = imageQuery.Where(p => p.Category == query.Category);
```

In the final projection's `PortfolioImageResponse` construction, add `Category: x.Image.Category`
directly after `Style: x.Image.Style`.

The existing `Search` block already matches `p.Style` — leave it unchanged; searching by category
keyword (e.g. typing "healed" in the search box) is not part of this prompt's scope and the search
box's placeholder text does not claim to cover it.

### 3e. `Pena_e_Arte.Application/Public/Queries/GetPublicArtistQuery.cs`

Update the `ArtistPortfolioImageResponse` projection (same shape as 3c):

```csharp
artist.Portfolio
    .OrderByDescending(p => p.CreatedAt)
    .Select(p => new ArtistPortfolioImageResponse(p.Id, p.ImageUrl, p.Style, p.Category))
    .ToList(),
```

### 3f. `Pena_e_Arte.Application/Saved/Queries/GetSavedPortfolioImagesQuery.cs`

In the `PortfolioImageResponse` construction inside `GetSavedPortfolioImagesHandler.Handle`, add
`Category: img.Category` directly after `Style: img.Style`.

---

## Part 4 — API endpoint

### `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs`

Update the `GetPortfolioFeed` handler to accept and forward the new query parameter:

```csharp
private static async Task<IResult> GetPortfolioFeed(
    double? lat,
    double? lng,
    double radiusKm,
    int page,
    int pageSize,
    ISender mediator,
    CancellationToken ct,
    string? style = null,
    string? category = null,
    string? search = null)
{
    List<PortfolioImageResponse> result = await mediator.Send(
        new GetPortfolioFeedQuery(lat, lng, radiusKm, page, pageSize, style, category, search), ct);
    return Results.Ok(result);
}
```

(Only `string? category = null` added as a parameter, threaded into the `GetPortfolioFeedQuery`
constructor call in its correct position — matching the record's new parameter order from Part 3d.)
No route or `RequireRateLimiting`/`AllowAnonymous` change needed.

---

## Part 5 — Frontend: artist-side upload + labeling (`frontend/src/features/artists`)

### 5a. `artistsApi.ts`

Update `ArtistPortfolioImage`:

```typescript
export interface ArtistPortfolioImage {
  imageId:  string;
  imageUrl: string;
  style:    string | null;
  category: string | null;
}
```

Update the `updateArtistPortfolio` mutation's body type:

```typescript
updateArtistPortfolio: builder.mutation<
  ArtistResponse,
  { id: string; images: { imageUrl: string; style: string | null; category: string | null }[] }
>({
```

(No other change to this mutation — same query/invalidation/optimistic-update logic as today.)

### 5b. `components/ArtistDetailPage.tsx`

**Imports** — add to the existing `lucide-react` import line: nothing new needed there (`ImagePlus`,
`Loader2`, `X` already imported). Add:

```tsx
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from "@/shared/components/ui/dropdown-menu";
import { Badge } from "@/shared/components/ui/badge";
```

**New constant**, placed directly after `STYLE_OPTIONS`:

```tsx
// Keep in sync with PortfolioImageCategory.cs constants on the backend.
const CATEGORY_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "fresh",  label: "Fresh Tattoo"  },
  { value: "healed", label: "Healed Tattoo" },
  { value: "design", label: "Design"        },
];
```

**`openImagePicker` — replace with a category-aware version:**

```tsx
function openImagePicker(category: string) {
  if (!id || !artist) return;
  const input = document.createElement("input");
  input.type = "file";
  input.accept = "image/*";
  input.onchange = async () => {
    const file = input.files?.[0];
    input.remove();
    if (!file) return;
    const objectKey = `portfolio/${id}/${Date.now()}-${file.name.replace(/\s+/g, "_")}`;
    const publicUrl = await upload(file, objectKey);
    if (!publicUrl) {
      toast.error("Image upload failed.");
      return;
    }
    const images = [
      ...artist.portfolioImages.map((p) => ({ imageUrl: p.imageUrl, style: p.style, category: p.category })),
      { imageUrl: publicUrl, style: null, category },
    ];
    const result = await updatePortfolio({ id, images });
    if ("error" in result) {
      toast.error("Failed to save portfolio image.");
    } else {
      const label = CATEGORY_OPTIONS.find((c) => c.value === category)?.label ?? category;
      toast.success(`${label} added to portfolio.`);
    }
  };
  document.body.appendChild(input);
  input.click();
}
```

Note the `.map()` in the images array now carries `category: p.category` through — this is not
optional boilerplate. `removePortfolioImage` and `updateImageStyle` below rebuild the **entire**
images array from `artist.portfolioImages` on every call; if either omits `category` from its
`.map()`, every image's category is silently wiped on the next removal or style edit. Update both:

```tsx
async function removePortfolioImage(imageId: string) {
  if (!id || !artist) return;
  const images = artist.portfolioImages
    .filter((p) => p.imageId !== imageId)
    .map((p) => ({ imageUrl: p.imageUrl, style: p.style, category: p.category }));
  const result = await updatePortfolio({ id, images });
  if ("error" in result) {
    toast.error("Failed to remove image.");
  }
}

async function updateImageStyle(imageId: string, style: string | null) {
  if (!id || !artist) return;
  const images = artist.portfolioImages.map((p) => ({
    imageUrl: p.imageUrl,
    style:    p.imageId === imageId ? style : p.style,
    category: p.category,
  }));
  const result = await updatePortfolio({ id, images });
  if ("error" in result) {
    toast.error("Failed to update style.");
  }
}
```

**New function**, placed directly after `updateImageStyle`:

```tsx
async function updateImageCategory(imageId: string, category: string | null) {
  if (!id || !artist) return;
  const images = artist.portfolioImages.map((p) => ({
    imageUrl: p.imageUrl,
    style:    p.style,
    category: p.imageId === imageId ? category : p.category,
  }));
  const result = await updatePortfolio({ id, images });
  if ("error" in result) {
    toast.error("Failed to update category.");
  }
}
```

**"Add image" button → `DropdownMenu` with three category actions.** Replace the existing button
block in the Portfolio tab:

```tsx
{canManagePortfolio && (
  <div className="flex justify-end mb-3">
    <DropdownMenu modal={false}>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          size="sm"
          className="gap-1.5"
          disabled={isUploading || isSavingPf}
        >
          {isUploading || isSavingPf ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" />
          ) : (
            <ImagePlus className="h-3.5 w-3.5" />
          )}
          Add image
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        {CATEGORY_OPTIONS.map(({ value, label }) => (
          <DropdownMenuItem
            key={value}
            onSelect={() => {
              // Deferred (not prevented — a prevented onSelect keeps the dropdown open
              // indefinitely): opening the native file picker synchronously from a
              // DropdownMenuItem select races the menu's own close/focus-return behavior,
              // the same class of issue documented for Dialog-based overlays in
              // MyStudiosPage.tsx's "Manage notifications" item.
              setTimeout(() => openImagePicker(value), 0);
            }}
          >
            {label}
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  </div>
)}
```

**Empty-state copy** — update the helper text:

```tsx
{artist.portfolioImages.length === 0 ? (
  <div className="flex flex-col items-center justify-center py-12 gap-2 text-center">
    <p className="text-sm font-medium">No portfolio images yet</p>
    {canManagePortfolio && (
      <p className="text-xs text-muted-foreground">
        Upload fresh tattoos, healed results, or designs, and tag each with a style so they
        appear on the public discover feed and its filters.
      </p>
    )}
  </div>
) : (
```

**Per-image controls** — add a Category `Select` above the existing Style `Select`/label, and a
read-only Category `Badge` in the non-manager branch. Replace the per-image controls block:

```tsx
{canManagePortfolio ? (
  <>
    <Select
      value={category ?? "none"}
      onValueChange={(v) => void updateImageCategory(imageId, v === "none" ? null : v)}
    >
      <SelectTrigger
        aria-label="Portfolio category"
        className={cn("h-7 text-xs", !category && "text-muted-foreground")}
      >
        <SelectValue placeholder="Uncategorized" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="none">Uncategorized</SelectItem>
        {CATEGORY_OPTIONS.map(({ value, label }) => (
          <SelectItem key={value} value={value}>{label}</SelectItem>
        ))}
      </SelectContent>
    </Select>
    <Select
      value={style ?? "none"}
      onValueChange={(v) => void updateImageStyle(imageId, v === "none" ? null : v)}
    >
      <SelectTrigger
        aria-label="Tattoo style"
        className={cn("h-7 text-xs", !style && "text-muted-foreground")}
      >
        <SelectValue placeholder="No style" />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="none">No style</SelectItem>
        {STYLE_OPTIONS.map(({ value, label }) => (
          <SelectItem key={value} value={value}>{label}</SelectItem>
        ))}
      </SelectContent>
    </Select>
  </>
) : (
  <div className="space-y-0.5 px-1">
    {category && (
      <Badge variant="secondary" className="text-[10px]">
        {CATEGORY_OPTIONS.find((c) => c.value === category)?.label ?? category}
      </Badge>
    )}
    {style && (
      <p className="text-xs text-muted-foreground">
        {STYLE_OPTIONS.find((s) => s.value === style)?.label ?? style}
      </p>
    )}
  </div>
)}
```

This replaces the current `canManagePortfolio ? <Select ...style.../> : style ? <p>...</p> : null`
ternary — note the destructured `category` must be added to the `.map(({ imageId, imageUrl, style })`
line just above this block: `artist.portfolioImages.map(({ imageId, imageUrl, style, category }) => (`.

---

## Part 6 — Frontend: public discovery filter (`frontend/src/features/public`)

### 6a. `publicApi.ts`

Add `category` to `PortfolioImageResponse` directly after `style`:

```typescript
export interface PortfolioImageResponse {
  imageId:             string;
  imageUrl:            string;
  style:               string | null;   // nullable — untagged images are valid
  category:            string | null;   // nullable — uncategorized images are valid; fresh/healed/design
  artistName:          string;
  artistSlug:          string;
  studioName:          string;
  studioSlug:          string;
  averageRating:       number | null;
  reviewCount:         number;
  imageAverageRating:  number | null;
  imageReviewCount:    number;
  distanceKm:          number | null;
  viewCount:           number;
}
```

Add `category` to `PortfolioFeedArgs`:

```typescript
export interface PortfolioFeedArgs {
  lat?:      number;
  lng?:      number;
  radiusKm:  number;
  page:      number;
  pageSize?: number;
  style?:    string;
  category?: string;
  search?:   string;
}
```

Update `getPortfolioFeed`'s `query` builder:

```typescript
getPortfolioFeed: builder.query<PortfolioImageResponse[], PortfolioFeedArgs>({
  query: ({ lat, lng, radiusKm, page, pageSize = 24, style, category, search }) => {
    const params = new URLSearchParams();
    params.set("radiusKm", String(radiusKm));
    params.set("page",     String(page));
    params.set("pageSize", String(pageSize));
    if (lat != null) params.set("lat", String(lat));
    if (lng != null) params.set("lng", String(lng));
    if (style)        params.set("style",    style);
    if (category)      params.set("category", category);
    if (search)       params.set("search", search);
    return `portfolio/feed?${params.toString()}`;
  },
  providesTags: ["PortfolioFeed"],
  keepUnusedDataFor: 0,
}),
```

### 6b. `components/PortfolioFeed.tsx`

**New constant**, placed directly after the `STYLES` array, before `StyleChips`:

```tsx
// ── Category tabs ────────────────────────────────────────────────────────────

// Keep in sync with PortfolioImageCategory.cs constants on the backend.
const CATEGORIES: ReadonlyArray<{ value: string; label: string }> = [
  { value: "",       label: "All"            },
  { value: "fresh",  label: "Fresh Tattoos"  },
  { value: "healed", label: "Healed Tattoos" },
  { value: "design", label: "Designs"        },
];

interface CategoryTabsProps {
  activeCategory: string;
  onChange:       (category: string) => void;
}

function CategoryTabs({ activeCategory, onChange }: CategoryTabsProps) {
  return (
    <div
      role="group"
      aria-label="Filter by portfolio category"
      className="flex items-center gap-1 rounded-lg border border-border bg-muted/40 p-1 w-fit"
    >
      {CATEGORIES.map(({ value, label }) => {
        const isActive = activeCategory === value;
        return (
          <button
            key={value}
            type="button"
            role="radio"
            aria-checked={isActive}
            onClick={() => onChange(value)}
            className={`px-3 py-1.5 min-h-[36px] rounded-md text-xs font-medium
                        transition-colors whitespace-nowrap
                        ${isActive
                          ? "bg-background text-foreground shadow-sm"
                          : "text-muted-foreground hover:text-foreground"
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

This is deliberately a different visual shape from `StyleChips` (a single bordered segmented
control, not individually-pilled scrollable chips) per Decision #4 — it reads as one small,
fixed-size control, not an open, scrollable list like styles.

**State + wiring**, inside the main `PortfolioFeed` component:

```tsx
const [activeCategory, setActiveCategory] = useState("");
```

placed directly after `const [activeStyle, setActiveStyle] = useState("");`.

Add to `feedArgs`:

```tsx
const feedArgs: PortfolioFeedArgs = {
  lat:      effectiveLat,
  lng:      effectiveLng,
  radiusKm: effectiveRadiusKm,
  page,
  style:    activeStyle || undefined,
  category: activeCategory || undefined,
  search:   keyword.trim() || undefined,
};
```

Add a handler, directly after `handleStyleChange`:

```tsx
function handleCategoryChange(category: string) {
  setActiveCategory(category);
  setPage(1);
  setAllImages([]);
}
```

Do **not** add `activeCategory` to the automatic reset `useEffect`'s dependency array (the one
keyed on `[effectiveLat, effectiveLng, effectiveRadiusKm, keyword]`) — exactly like `activeStyle`,
category resets are handled entirely by its own explicit handler, matching the existing style
precedent exactly (see the comment already above that effect explaining why).

**Render** — place `<CategoryTabs .../>` directly above the existing `<StyleChips .../>` render
(same container, stacked with a small gap — match whatever spacing wrapper already holds the style
chip row and keyword/location controls):

```tsx
<CategoryTabs activeCategory={activeCategory} onChange={handleCategoryChange} />
<StyleChips activeStyle={activeStyle} onChange={handleStyleChange} />
```

**Lightbox badge** — in the info panel, add a category `Badge` directly before the existing style
pill:

```tsx
{image.category && (
  <Badge variant="secondary" className="text-[10px]">
    {CATEGORIES.find((c) => c.value === image.category)?.label ?? image.category}
  </Badge>
)}

{image.style && (
  <span className="inline-block text-[10px] font-medium uppercase tracking-wider
                   px-2 py-0.5 rounded-full bg-zinc-800 text-zinc-300 border border-zinc-700">
    {image.style}
  </span>
)}
```

Add the `Badge` import: `import { Badge } from "@/shared/components/ui/badge";`.

**Empty-state message** — the existing `` `No ${activeStyle} tattoos found...` `` message should
account for category too when style is unset but category is active:

```tsx
emptyMessage={
  activeStyle
    ? `No ${activeStyle} tattoos found. Try a different style or browse all.`
    : activeCategory
      ? `No ${CATEGORIES.find((c) => c.value === activeCategory)?.label.toLowerCase()} found. Try a different filter or browse all.`
      : undefined
}
```

(Locate the exact prop/usage this message is passed into and adapt precisely — it's the existing
conditional string currently keyed on `activeStyle` alone.)

### 6c. `components/ArtistPortfolioPage.tsx`

**New constant**, placed directly after the existing `STYLES` array (keep the two visually distinct
per Decision #4, same as PortfolioFeed):

```tsx
// Keep in sync with PortfolioImageCategory.cs on the backend and CATEGORIES in PortfolioFeed.tsx.
const CATEGORIES: ReadonlyArray<{ value: string; label: string }> = [
  { value: "fresh",  label: "Fresh Tattoos"  },
  { value: "healed", label: "Healed Tattoos" },
  { value: "design", label: "Designs"        },
];
```

**State**, directly after `const [activeStyle, setActiveStyle] = useState<string>("");`:

```tsx
const [activeCategory, setActiveCategory] = useState<string>("");
```

**Derived categories**, directly after the existing `availableStyles` memo:

```tsx
const availableCategories = useMemo(() => {
  if (!artist) return [];
  const seen = new Set(artist.portfolioImages.map((p) => p.category).filter(Boolean));
  return CATEGORIES.filter(({ value }) => seen.has(value));
}, [artist]);
```

**Combined filtering** — replace the existing `visibleImages` derivation:

```tsx
const visibleImages = artist.portfolioImages.filter((p) =>
  (!activeStyle || p.style === activeStyle) &&
  (!activeCategory || p.category === activeCategory)
);
```

**Render** — add a category tab row using the same bordered-segmented-control markup as
`CategoryTabs` in `PortfolioFeed.tsx` (either extract `CategoryTabs` into a small shared component
under `frontend/src/features/public/components/` and import it in both files, or duplicate the
~25-line component — given both files already independently duplicate `STYLES`/style-chip styling
today rather than sharing it, duplicate `CategoryTabs` here too for consistency with that existing
pattern, including an "All" option identical to `PortfolioFeed`'s). Place it directly above the
existing style-chips block:

```tsx
{availableCategories.length > 1 && (
  <CategoryTabs
    activeCategory={activeCategory}
    onChange={(c) => setActiveCategory(c)}
    categories={[{ value: "", label: "All" }, ...CATEGORIES]}
  />
)}
{availableStyles.length > 1 && (
  // ... existing style chip row, unchanged
)}
```

(`CategoryTabs` here takes an extra `categories` prop since this page needs the "All" option
prepended only when rendering — adjust the component signature accordingly if duplicating it, or
just inline the "All" entry into a page-local `CATEGORIES` constant that already includes it, mirroring
how `PortfolioFeed.tsx`'s own `CATEGORIES` bakes "All" in as `{ value: "", label: "All" }`. Either is
fine; keep it consistent with whichever choice `PortfolioFeed.tsx` ends up using in Part 6b.)

Update the empty-message logic (`activeStyleLabel`/the "No {x} images yet" message near
`emptyMessage={activeStyle ? ...}`) the same way as Part 6b, accounting for `activeCategory` too.

---

## Part 7 — Backend: studio highlight gallery category preference

### `Pena_e_Arte.Application/Public/Queries/GetPublicStudioQuery.cs`

Replace the gallery-building block:

```csharp
// Gallery: up to 3 images per artist, max 9 total, round-robin so no single artist dominates.
// Prefer Fresh/Healed tattoo photos over Design images — a design/flash sketch only fills a
// slot when an artist has fewer than 3 non-Design images. Newest first within each group.
List<List<string>> imagesByArtist = artists
    .Select(a =>
    {
        List<PortfolioImage> ordered = a.Portfolio.OrderByDescending(p => p.CreatedAt).ToList();
        List<PortfolioImage> nonDesign = ordered.Where(p => p.Category != PortfolioImageCategory.Design).ToList();
        List<PortfolioImage> designs = ordered.Where(p => p.Category == PortfolioImageCategory.Design).ToList();
        return nonDesign.Concat(designs).Take(3).Select(p => p.ImageUrl).ToList();
    })
    .Where(imgs => imgs.Count > 0)
    .ToList();
```

Add `using Pena_e_Arte.Domain.Constants;` to this file's imports if not already present.

---

## Part 8 — Tests

### Backend

- `tests/Pena_e_Arte.UnitTests/Artists/UpdateArtistPortfolioHandlerTests.cs` — update every existing
  test's `PortfolioImageInput` construction to supply `Category` (or `null`). Add new cases:
  - Adding an image with a category persists `PortfolioImage.Category` correctly for each of
    `fresh`/`healed`/`design`.
  - Updating an existing image's category (without touching style) leaves `Style` unchanged, and
    vice versa — the two fields are independent.
  - Category omitted (`null`) on an existing tagged image clears it back to uncategorized.
- `tests/Pena_e_Arte.UnitTests/Artists/UpdateArtistPortfolioValidatorTests.cs` — add cases:
  invalid category string fails validation with the expected message; `null` category passes;
  each of the three valid values passes.
- `tests/Pena_e_Arte.UnitTests/Public/GetPortfolioFeedHandlerTests.cs` — add cases: `Category`
  filter returns only matching images; `Category` and `Style` filters combine (AND, not OR);
  no `Category` filter returns all categories including uncategorized (`null`) rows.
- `tests/Pena_e_Arte.UnitTests/Public/GetPublicArtistHandlerTests.cs` /
  `GetPublicStudioHandlerTests.cs` (or wherever their existing coverage lives — verify the actual
  file names before assuming) — update fixtures to include `Category`; add a
  `GetPublicStudioHandler` case asserting an artist with 2 fresh + 2 design images returns the 2
  fresh images plus 1 design (not 3 designs) in the highlight gallery, and another asserting an
  artist with only design images still fills their 3 slots from those designs (the fallback path).
- `tests/Pena_e_Arte.UnitTests/Saved/GetSavedPortfolioImagesHandlerTests.cs` — update fixtures/
  assertions to cover `Category` passing through unchanged.
- Grep the whole `tests/` tree for `new PortfolioImageInput(`, `new ArtistPortfolioImageResponse(`,
  and `new PortfolioImageResponse(`, and fix any remaining call sites the compiler flags by running
  `dotnet build` on the test projects and working through the errors — do not try to enumerate every
  occurrence by hand up front.

### Frontend

- `__tests__/artists.test.tsx` / `ArtistDetailPage`-related test file (verify the actual file
  covering the Portfolio tab before assuming — likely `artists.test.tsx` per the existing file
  list) — update `ArtistPortfolioImage`/`ArtistResponse` fixtures to include `category`. Add tests:
  - The "Add image" button opens a dropdown with three category options; selecting one uploads and
    tags the resulting image with that category (assert the `updatePortfolio` mutation call/request
    body includes the right `category`).
  - The per-image Category `Select` updates an image's category and preserves its existing `style`
    (assert the mutation body's `style` for that image is unchanged).
  - Removing an image preserves the remaining images' `category` values (regression test for the
    "silently wiped on removal" failure mode called out in Part 5b).
  - A non-manager viewer (client role, or another artist without edit rights) sees a read-only
    category `Badge` instead of the `Select`.
- `__tests__/PortfolioFeed.test.tsx` — add MSW/fixture coverage for `category` on
  `PortfolioImageResponse`; add tests: selecting a category tab calls `useGetPortfolioFeedQuery`
  with the right `category` arg and resets `page`/accumulated images; combining a category tab and
  a style chip sends both params; the lightbox shows the category badge when present.
- `__tests__/ArtistPortfolioPage.test.tsx` — add fixture coverage for `category`; add tests: the
  category tab row only renders when the artist has ≥ 2 distinct categories among their images
  (mirroring the existing `availableStyles.length > 1` test, if one exists — check first); selecting
  a category filters `visibleImages` correctly in combination with an active style filter.
- Run `pnpm tsc --noEmit` and the full `pnpm test` after all of the above. `PortfolioImageResponse`
  and `ArtistPortfolioImage` both gained a new required field — check every other test file across
  the frontend that constructs a `PortfolioImageResponse`- or `ArtistPortfolioImage`-shaped fixture
  (`StudioPortfolioPage`, `DiscoverPage`, `ReviewSection`, `CreateArtistPage`, `InstagramTab` tests
  are all candidates per the earlier "Portfolio" file search — verify which actually construct these
  shapes rather than just referencing the feature) and fix whatever the type checker and test run
  surface.

---

## Part 9 — Help Menu, user manual, onboarding tour

Per `CLAUDE.md` rule 7, this feature is not done until all three surfaces reflect it.

### 9a. `frontend/src/features/help/helpContent.ts`

Update the existing `owner-artist-portfolio` article:

```typescript
{
  id: "owner-artist-portfolio",
  // ...
  title: "Add portfolio images and tag their category and style",
  keywords: [
    "portfolio", "upload image", "tattoo style", "style tag", "discover filter",
    "no portfolio work", "keyword search",
    "fresh tattoo", "healed tattoo", "design", "portfolio category",
  ],
  summary: "Upload work to an artist's portfolio as a fresh tattoo, healed tattoo, or design, and tag each image with a tattoo style — both show up in the public Discover page's filters.",
  steps: [
    "Open an artist's profile and click the \"Portfolio\" tab.",
    "Click \"Add image\" and choose Fresh Tattoo, Healed Tattoo, or Design — this uploads the photo and tags its category in one step.",
    "For each image, use the category dropdown to relabel it later if needed, and the style dropdown to pick a tattoo style (e.g. Traditional, Realism, Blackwork).",
    "Click either dropdown again at any time to change or clear an image's category or style.",
    "The Discover page's category tabs (All / Fresh Tattoos / Healed Tattoos / Designs) and style filter chips only match an image's own tags — they combine, so a client can filter to \"Healed Tattoos\" and \"Blackwork\" at once.",
    "An image left uncategorized or without a style still shows in the artist's portfolio, but it won't appear when a client filters Discover by that category or style. The separate keyword search box (\"Search styles, artists…\") also matches an artist's name and their \"Specializations\" text, so a well-filled-in Specializations field helps your work surface there even for untagged images.",
  ],
},
```

(Adapt to the article's actual current field layout — verify the exact object shape in the file
before editing; the above reflects the content already read during research, restructured to cover
category.)

### 9b. `frontend/public/user-manual/index.html`

- `#artist-profile` section (the "My portfolio profile" article, ~line 1720–1748): update the
  Portfolio-tab bullet:

  > `<li><span class="step-title">Portfolio</span> tab — add or remove photos shown on your public
  > artist page. Click "Add image" and choose Fresh Tattoo, Healed Tattoo, or Design to upload and
  > tag a photo's category in one step, or use the category dropdown on any existing image to
  > relabel it. Each image also has its own style dropdown (Traditional, Realism, Blackwork, etc.)
  > — tag it so the photo appears when a client filters the public Discover page by that style.</li>`

  Update the tip callout directly below it to mention both filters:

  > `<div class="callout callout-tip"><strong>Tip:</strong> Discover's category tabs and style
  > filters both match an image's own tags, not the "Specializations" text on your profile — an
  > uncategorized or untagged image stays in your portfolio but won't show up under either filter.</div>`

- `#guest-discover` section: update the Portfolio-tab wireframe/steps to mention the new category
  tabs alongside the existing style filter chip row (`<li><span class="step-title">Filter by
  category</span> using the Fresh Tattoos / Healed Tattoos / Designs tabs above the style chips —
  combine with a style filter for a narrower result.</li>`, added next to the existing
  `<li><span class="step-title">Filter by style</span>...</li>` bullet).
- `#guest-artist-portfolio` section: update the "filterable portfolio grid" description and the
  figcaption ("style filter chips reflect only styles present in that artist's own work") to also
  mention category tabs when ≥ 2 categories are present.
- `#guest-portfolio-feed` section: same treatment as `#guest-discover` — the "Filter by style"
  bullet gets a sibling "Filter by category" bullet.
- Do **not** touch `#guest-studio-portfolio`'s highlight-gallery description with implementation
  detail about the Fresh/Healed-over-Design ordering from Part 7 — that's an internal ranking
  heuristic, not a control the visitor interacts with, consistent with how the existing
  round-robin/max-9 gallery logic is also left undocumented in the manual today (verified: the
  manual describes the gallery's *appearance*, never its selection algorithm).

### 9c. Onboarding tours

Checked `frontend/src/features/help/tours/artistTour.ts` and `ownerTour.ts` — neither has any step
referencing portfolio, style, or category today (confirmed via grep — no matches in either file).
**No tour changes are needed for this prompt** — stated explicitly here rather than left ambiguous,
per this project's own convention of recording genuine no-op findings rather than silently skipping
them.

---

## Definition of done

- [ ] Migration applied cleanly; `dotnet ef database update` succeeds; app boots.
- [ ] `dotnet build` — zero errors.
- [ ] `dotnet test` — all green (pre-existing failures noted at pre-flight excluded), including the
      new/updated `UpdateArtistPortfolioHandlerTests`, `UpdateArtistPortfolioValidatorTests`,
      `GetPortfolioFeedHandlerTests`, `GetPublicStudioHandlerTests`, `GetSavedPortfolioImagesHandlerTests`.
- [ ] `pnpm tsc --noEmit` — zero errors.
- [ ] `pnpm test` — all green, including the updated Artists/Public suites and any other suite
      touched by `PortfolioImageResponse`'s/`ArtistPortfolioImage`'s new field.
- [ ] Manual smoke check (or an added integration/component test covering it): an artist/owner can
      upload a new portfolio image via each of the three category options from the "Add image"
      dropdown and see it correctly tagged; relabeling an image's category via its dropdown doesn't
      touch its style (and vice versa); removing one image doesn't wipe the categories of the
      remaining images; the Discover page's category tabs and an artist's own public portfolio page
      both filter correctly, including in combination with an active style filter; a studio's public
      highlight gallery prefers Fresh/Healed images over Designs, falling back to Designs only when
      an artist has fewer than 3 non-Design images.
- [ ] `helpContent.ts` and `user-manual/index.html` updated per Part 9; onboarding tours confirmed
      (not just assumed) to need no change.
