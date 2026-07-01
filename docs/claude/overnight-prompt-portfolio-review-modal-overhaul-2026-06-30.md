# Overnight Prompt — Portfolio Lightbox & Review Section Overhaul
**Goal:** Fix all confirmed visual, UX, and accessibility bugs in `ReviewSection.tsx`,
`StarRating.tsx`, and the `PortfolioLightbox` inside `PortfolioFeed.tsx`. Then add
three features: prev/next image navigation in the lightbox, a "Book with artist"
conversion CTA, and a "Verified Client" badge on reviews.

The auth gate, success auto-dismiss, duplicate-review 409 handling, and loading
skeletons are already working correctly — preserve them entirely.

No new npm or NuGet packages. All changes must pass `pnpm tsc --noEmit`, `pnpm lint`,
and `pnpm test --run` before the session ends.

---

## Read First

1. `CLAUDE.md`
2. `docs/claude/frontend.md`
3. `docs/claude/backend.md`
4. `docs/claude/database.md`
5. `docs/claude/architecture.md`
6. `docs/claude/conventions.md`

---

## Source Files to Read Before Starting

Read each file in full before changing anything:

- `frontend/src/features/public/components/ReviewSection.tsx`
- `frontend/src/shared/components/ui/StarRating.tsx`
- `frontend/src/features/public/components/PortfolioFeed.tsx`
- `frontend/src/features/public/publicApi.ts`
- `frontend/src/features/public/__tests__/ReviewSection.test.tsx`
- `frontend/src/features/public/__tests__/PortfolioFeed.test.tsx`
- `Pena_e_Arte.Contracts/Responses/Public/ReviewResponse.cs`
- `Pena_e_Arte.Application/Reviews/Commands/CreateArtistReviewCommand.cs`
- `Pena_e_Arte.Application/Reviews/Commands/CreateStudioReviewCommand.cs`
- `Pena_e_Arte.Application/Reviews/Commands/CreatePortfolioImageReviewCommand.cs`
- Read the `GetArtistReviews`, `GetStudioReviews`, and `GetPortfolioImageReviews`
  query handlers to understand how `ReviewResponse` is mapped.

---

## Files to Change

| File | What changes |
|---|---|
| `Pena_e_Arte.Contracts/Responses/Public/ReviewResponse.cs` | Add `IsVerifiedBooking` |
| `Pena_e_Arte.Application/Reviews/Queries/GetArtistReviewsQuery.cs` | Compute `IsVerifiedBooking` |
| `Pena_e_Arte.Application/Reviews/Queries/GetStudioReviewsQuery.cs` | Compute `IsVerifiedBooking` |
| `Pena_e_Arte.Application/Reviews/Queries/GetPortfolioImageReviewsQuery.cs` | Compute `IsVerifiedBooking` |
| `frontend/src/shared/components/ui/StarRating.tsx` | Full rewrite |
| `frontend/src/features/public/components/ReviewSection.tsx` | Reorder, button, CTA, date |
| `frontend/src/features/public/components/PortfolioFeed.tsx` | Lightbox prev/next, CTA, scroll fade |
| `frontend/src/features/public/publicApi.ts` | Add `isVerifiedBooking` to `ReviewResponse` |
| `frontend/src/features/public/__tests__/ReviewSection.test.tsx` | Update + add tests |
| `frontend/src/features/public/__tests__/PortfolioFeed.test.tsx` | Update + add tests |
| `docs/claude/architecture.md` | Update decisions log |

---

## Section 1 — Backend: "Verified Client" badge on reviews

The `ReviewResponse` currently has: `Id, AuthorName, Rating, Body, CreatedAt`.
It has no way for the frontend to know whether the reviewer actually booked at this
studio or with this artist.

### 1-A: Update `ReviewResponse`

```csharp
public record ReviewResponse(
    Guid     Id,
    string   AuthorName,
    int      Rating,
    string   Body,
    DateTime CreatedAt,
    bool     IsVerifiedBooking);  // ← NEW
```

`IsVerifiedBooking = true` when the reviewer has at least one `Completed` appointment
with the relevant artist/studio. It is computed at query time — not stored. No migration
needed.

### 1-B: Compute `IsVerifiedBooking` in each review query handler

**For artist reviews** (`GetArtistReviewsQuery`):
```csharp
// In the EF Core projection, for each review:
IsVerifiedBooking = await db.Appointments
    .IgnoreQueryFilters()
    .AnyAsync(a =>
        a.ArtistId     == artist.Id             &&
        a.Status       == AppointmentStatus.Completed &&
        /* join via client's user id */
        db.Clients.Any(c => c.Id == a.ClientId && c.UserId == review.AuthorUserId),
        ct)
```

Because this is N+1, do it in a single join. The efficient pattern:

```csharp
// Build a HashSet of AuthorUserIds that have a completed appointment with this artist
var verifiedUserIds = await db.Appointments
    .IgnoreQueryFilters()
    .Where(a => a.ArtistId == artist.Id && a.Status == AppointmentStatus.Completed)
    .Join(db.Clients,
          a => a.ClientId,
          c => c.Id,
          (a, c) => c.UserId)
    .Where(uid => uid != null)
    .Distinct()
    .ToHashSetAsync(ct);

// Then in the projection:
.Select(r => new ReviewResponse(
    r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt,
    IsVerifiedBooking: verifiedUserIds.Contains(r.AuthorUserId)))
```

**For studio reviews** (`GetStudioReviewsQuery`):
Same pattern, but check `a.StudioId == studio.Id` instead of `a.ArtistId`.

**For portfolio image reviews** (`GetPortfolioImageReviewsQuery`):
The tattoo's studio is `image.Artist.StudioId`. Verify the reviewer has a completed
appointment at that studio (the closest proxy to "booked here"):

```csharp
var studioId = portfolioImage.Artist.StudioId;
var verifiedUserIds = await db.Appointments
    .IgnoreQueryFilters()
    .Where(a => a.StudioId == studioId && a.Status == AppointmentStatus.Completed)
    .Join(db.Clients, a => a.ClientId, c => c.Id, (a, c) => c.UserId)
    .Where(uid => uid != null)
    .Distinct()
    .ToHashSetAsync(ct);
```

### 1-C: `IgnoreQueryFilters` justification

The `Appointments.IgnoreQueryFilters()` calls in the review query handlers are
cross-tenant lookups (a client of one studio reviewing an artist from another).
Add entries to the IgnoreQueryFilters Approved Usages table in `architecture.md`:
- `GetArtistReviewsQuery` — cross-tenant verified-booking check
- `GetStudioReviewsQuery` — cross-tenant verified-booking check
- `GetPortfolioImageReviewsQuery` — cross-tenant verified-booking check (studio)

---

## Section 2 — Frontend: `StarRating.tsx` — complete rewrite

The current component has three bugs:
1. Interactive star buttons have no padding — touch target is ~20px (fails WCAG 2.5.5)
2. No hover preview — stars don't light up 1→N when hovering over star N
3. Interactive and display sizes are inconsistent (`h-5 w-5` vs `h-3.5 w-3.5`)
4. Empty star contrast differs slightly between contexts

Replace the entire file:

```tsx
import { Star } from "lucide-react";
import { useState } from "react";

// ── Shared star size tokens ──────────────────────────────────────────────────
// "sm" = display-only star (review cards, tile overlays)
// "md" = interactive stars in the write form
const SIZE = {
  sm: "h-3.5 w-3.5",
  md: "h-5 w-5",
} as const;

// ── Display-only (read) star rating ─────────────────────────────────────────

interface DisplayStarRatingProps {
  value:      number;
  max?:       number;
  size?:      keyof typeof SIZE;
  className?: string;
}

export function StarRating({
  value,
  max = 5,
  size = "sm",
  className = "",
}: DisplayStarRatingProps) {
  return (
    <div
      className={`flex gap-0.5 ${className}`}
      role="img"
      aria-label={`Rated ${value} out of ${max} stars`}
    >
      {Array.from({ length: max }, (_, i) => (
        <Star
          key={i}
          aria-hidden="true"
          className={`${SIZE[size]} shrink-0 ${
            i < value
              ? "text-amber-400 fill-amber-400"
              : "text-muted-foreground/40 fill-none"
          }`}
        />
      ))}
    </div>
  );
}

// ── Interactive (write) star rating ─────────────────────────────────────────

const LABELS = ["Terrible", "Poor", "Okay", "Good", "Excellent"] as const;

interface InteractiveStarRatingProps {
  value:      number;
  max?:       number;
  onChange:   (rating: number) => void;
  className?: string;
}

export function InteractiveStarRating({
  value,
  max = 5,
  onChange,
  className = "",
}: InteractiveStarRatingProps) {
  const [hovered, setHovered] = useState(0);

  // The highlighted count: show hover preview, fall back to selected value
  const highlighted = hovered > 0 ? hovered : value;

  return (
    <div className={`space-y-1 ${className}`}>
      <div
        role="radiogroup"
        aria-label="Star rating"
        className="flex gap-0.5"
        onMouseLeave={() => setHovered(0)}
      >
        {Array.from({ length: max }, (_, i) => {
          const rating = i + 1;
          const isHighlighted = rating <= highlighted;

          return (
            <button
              key={i}
              type="button"
              role="radio"
              aria-checked={value === rating}
              aria-label={`Rate ${rating} of ${max} — ${LABELS[i]}`}
              onClick={() => onChange(rating)}
              onMouseEnter={() => setHovered(rating)}
              onFocus={() => setHovered(rating)}
              onBlur={() => setHovered(0)}
              className={`
                min-w-[44px] min-h-[44px]
                flex items-center justify-center
                rounded-sm
                focus:outline-none focus-visible:ring-2 focus-visible:ring-ring
                transition-transform duration-75
                ${isHighlighted ? "scale-110" : "scale-100"}
              `}
            >
              <Star
                aria-hidden="true"
                className={`${SIZE.md} transition-colors duration-100 ${
                  isHighlighted
                    ? "text-amber-400 fill-amber-400"
                    : "text-muted-foreground/50 fill-none"
                }`}
              />
            </button>
          );
        })}
      </div>

      {/* Live text readout — visible below the stars after selection */}
      <p
        aria-live="polite"
        aria-atomic="true"
        className="h-4 text-xs text-muted-foreground transition-opacity duration-150"
        style={{ opacity: value > 0 ? 1 : 0 }}
      >
        {value > 0 ? `${value} star${value !== 1 ? "s" : ""} — ${LABELS[value - 1]}` : ""}
      </p>
    </div>
  );
}
```

**Important:** This splits the old `StarRating` component into two named exports:
- `StarRating` — display-only (same name, same API — no breaking change)
- `InteractiveStarRating` — the write-form star picker

All existing call sites that used `<StarRating value={x} />` (non-interactive) continue
to work unchanged. All call sites that used `<StarRating interactive onChange={...} />`
must be updated to `<InteractiveStarRating onChange={...} />`.

**Search for all usages:**
```bash
grep -r "StarRating" frontend/src --include="*.tsx" --include="*.ts" -l
```

Update each one:
- In `ReviewSection.tsx` — `ReviewForm` uses `<StarRating interactive onChange={...} />`
  → change to `<InteractiveStarRating onChange={...} />`
- All other `<StarRating value={x} />` usages remain unchanged

---

## Section 3 — Frontend: `ReviewSection.tsx` — full redesign

Read the current file. The issues to fix, in priority order:

### 3-A: Fix section content order (critical)

Current order: `ReviewForm` → `ReviewList` (which contains aggregate)

Correct order:
1. Section heading ("Reviews")
2. Aggregate rating (stars + score + count) — **immediately below heading**
3. Existing review cards (most recent first)
4. Write form **at the bottom** (or collapsed behind a "Write a Review" button)

Refactor `ReviewSection` to:

```tsx
export function ReviewSection({ slug, target, token, imageId }: Props) {
  return (
    <section className="space-y-4" aria-labelledby="reviews-heading">
      <div className="flex items-center gap-2">
        <MessageSquare className="h-4 w-4 text-muted-foreground/70" aria-hidden="true" />
        <h2 id="reviews-heading" className="text-base font-semibold">Reviews</h2>
      </div>

      {/* Reviews list (includes aggregate at the top of the list) */}
      {target === "studio"
        ? <StudioReviewList   slug={slug} />
        : target === "artist"
        ? <ArtistReviewList   slug={slug} />
        : <PortfolioImageReviewList imageId={imageId ?? ""} />}

      {/* Write form is ALWAYS last — users need context before writing */}
      <div className="pt-2 border-t border-border/40">
        <ReviewForm slug={slug} token={token} target={target} imageId={imageId} />
      </div>
    </section>
  );
}
```

### 3-B: Move aggregate rating to top of `ReviewList` with actual stars

Replace the plain-text aggregate:
```tsx
// OLD (text only, buried in the middle):
<p className="text-xs text-muted-foreground">
  {averageRating.toFixed(1)} / 5 · {reviews.length} review{reviews.length !== 1 ? "s" : ""}
</p>

// NEW (stars + score + count, at the TOP of the list section):
{averageRating !== null && reviews && reviews.length > 0 && (
  <div className="flex items-center gap-2 pb-3">
    <StarRating value={Math.round(averageRating)} size="sm" />
    <span className="text-sm font-semibold tabular-nums">
      {averageRating.toFixed(1)}
    </span>
    <span className="text-xs text-muted-foreground">
      · {reviews.length} review{reviews.length !== 1 ? "s" : ""}
    </span>
  </div>
)}
```

### 3-C: Empty state — before the write form

When there are 0 reviews, show a single clean empty state. The current "No reviews yet.
Be the first to leave one." is fine but add a star illustration:

```tsx
{!isLoading && (!reviews || reviews.length === 0) && (
  <div className="py-4 flex flex-col items-center gap-2 text-center">
    <div className="flex gap-0.5 opacity-30">
      {[1,2,3,4,5].map((i) => (
        <Star key={i} className="h-4 w-4 text-amber-400" aria-hidden="true" />
      ))}
    </div>
    <p className="text-sm text-muted-foreground">No reviews yet.</p>
  </div>
)}
```

Import `Star` from `lucide-react` at the top of `ReviewSection.tsx`.

### 3-D: Fix `ReviewForm` — button color, CTA label, star component

**Button color** — Replace the generic `<Button size="sm">` with an explicit violet CTA:

```tsx
<Button
  size="sm"
  onClick={handleSubmit}
  disabled={isSubmitting || rating === 0}
  aria-label="Post review"
  className="bg-violet-600 hover:bg-violet-700 text-white
             disabled:opacity-50 disabled:cursor-not-allowed"
>
  {isSubmitting ? "Posting…" : "Post Review"}
</Button>
```

Note: the button is also **disabled when no star is selected** (`rating === 0`). This
prevents the error flash and tells the user upfront that a rating is required.

**CTA label** — "Submit review" → "Post Review". Update `aria-label` too.

**Star component** — Replace `<StarRating interactive onChange={...} />` with
`<InteractiveStarRating onChange={...} />` (the new split component from Section 2):

```tsx
import { InteractiveStarRating } from "@/shared/components/ui/StarRating";

// In ReviewForm:
<InteractiveStarRating
  value={rating}
  onChange={(r) => { setRating(r); setError(null); }}
/>
```

Remove the `if (rating === 0)` early-return error check from `handleSubmit` — it's
now impossible to submit without a rating (button is disabled). Keep the body-length
check.

### 3-E: Date format — standardize to "Jul 1, 2026"

In `ReviewCard`:

```tsx
// OLD — produces "1 Jul 2026" (UK hybrid):
{new Date(review.createdAt).toLocaleDateString("en-GB", {
  day: "numeric", month: "short", year: "numeric",
})}

// NEW — produces "Jul 1, 2026" (clean, unambiguous):
{new Date(review.createdAt).toLocaleDateString("en-US", {
  month: "short", day: "numeric", year: "numeric",
})}
```

### 3-F: "Verified Client" badge in `ReviewCard`

Update `ReviewResponse` type in `publicApi.ts`:

```typescript
export interface ReviewResponse {
  id:                string;
  authorName:        string;
  rating:            number;
  body:              string;
  createdAt:         string;
  isVerifiedBooking: boolean;   // ← NEW
}
```

Update `ReviewCard` to show a badge when `isVerifiedBooking` is true:

```tsx
import { BadgeCheck } from "lucide-react";

function ReviewCard({ review }: { review: ReviewResponse }) {
  return (
    <div className="py-4 border-b last:border-b-0 space-y-2">
      <div className="flex items-start justify-between gap-2 flex-wrap">
        <div className="space-y-0.5">
          <div className="flex items-center gap-1.5">
            <span className="text-sm font-medium">{review.authorName}</span>
            {review.isVerifiedBooking && (
              <span
                className="inline-flex items-center gap-0.5
                           text-[10px] font-medium text-violet-400
                           px-1.5 py-0.5 rounded-full
                           bg-violet-500/10 border border-violet-500/20"
                title="This reviewer booked at this studio"
              >
                <BadgeCheck className="h-2.5 w-2.5" aria-hidden="true" />
                Verified client
              </span>
            )}
          </div>
          <StarRating value={review.rating} size="sm" />
        </div>
        <span className="text-xs text-muted-foreground shrink-0">
          {new Date(review.createdAt).toLocaleDateString("en-US", {
            month: "short", day: "numeric", year: "numeric",
          })}
        </span>
      </div>
      <p className="text-sm text-muted-foreground whitespace-pre-wrap leading-relaxed">
        {review.body}
      </p>
    </div>
  );
}
```

Add `BadgeCheck` to the lucide-react import.

### 3-G: Textarea placeholder — improve guidance

```tsx
// OLD:
placeholder="Share your experience…"

// NEW:
placeholder="How was the experience? Quality of the work, cleanliness, communication…"
```

---

## Section 4 — Frontend: `PortfolioFeed.tsx` — lightbox improvements

### 4-A: Prev/next image navigation

The lightbox currently shows one image with no way to navigate. Add chevron buttons.

**In `PortfolioFeed`**, change from tracking `lightboxImage` to tracking `lightboxIndex`:

```tsx
// OLD:
const [lightboxImage, setLightboxImage] = useState<PortfolioImageResponse | null>(null);

// NEW:
const [lightboxIndex, setLightboxIndex] = useState<number | null>(null);
const lightboxImage = lightboxIndex !== null ? (allImages[lightboxIndex] ?? null) : null;
```

When opening a tile, find its index:

```tsx
// In MasonryGrid, pass index down to onOpen:
// Change onOpen signature:
onOpen: (image: PortfolioImageResponse) => void
// In PortfolioFeed:
onOpen={(img) => {
  const idx = allImages.findIndex((i) => i.imageId === img.imageId);
  setLightboxIndex(idx >= 0 ? idx : null);
}}
```

Update `PortfolioLightbox` props:

```tsx
interface LightboxProps {
  images:       PortfolioImageResponse[];
  currentIndex: number;
  token:        string | null;
  onClose:      () => void;
  onNavigate:   (index: number) => void;
}

function PortfolioLightbox({ images, currentIndex, token, onClose, onNavigate }: LightboxProps) {
  const image    = images[currentIndex];
  const hasPrev  = currentIndex > 0;
  const hasNext  = currentIndex < images.length - 1;

  if (!image) return null;

  // Keyboard navigation
  useEffect(() => {
    function handleKey(e: KeyboardEvent) {
      if (e.key === "ArrowLeft"  && hasPrev) onNavigate(currentIndex - 1);
      if (e.key === "ArrowRight" && hasNext) onNavigate(currentIndex + 1);
    }
    window.addEventListener("keydown", handleKey);
    return () => window.removeEventListener("keydown", handleKey);
  }, [currentIndex, hasPrev, hasNext, onNavigate]);
```

This `useEffect` is a browser API side-effect (keyboard event), not data fetching.
Document with a comment.

**Prev/next buttons inside the lightbox image panel:**

```tsx
{/* Image panel */}
<div className="bg-black flex items-center justify-center min-h-[280px] relative">
  <img
    src={image.imageUrl}
    alt={`Tattoo by ${image.artistName}`}
    className="w-full h-full object-contain max-h-[70vh]"
  />

  {/* Prev button */}
  {hasPrev && (
    <button
      type="button"
      onClick={() => onNavigate(currentIndex - 1)}
      aria-label="Previous image"
      className="absolute left-2 top-1/2 -translate-y-1/2 z-10
                 rounded-full bg-black/60 p-2 text-white
                 hover:bg-black/80 transition-colors
                 focus-visible:ring-2 focus-visible:ring-white"
    >
      <ChevronLeft className="h-5 w-5" aria-hidden="true" />
    </button>
  )}

  {/* Next button */}
  {hasNext && (
    <button
      type="button"
      onClick={() => onNavigate(currentIndex + 1)}
      aria-label="Next image"
      className="absolute right-2 top-1/2 -translate-y-1/2 z-10
                 rounded-full bg-black/60 p-2 text-white
                 hover:bg-black/80 transition-colors
                 focus-visible:ring-2 focus-visible:ring-white"
    >
      <ChevronRight className="h-5 w-5" aria-hidden="true" />
    </button>
  )}

  {/* Position indicator */}
  <div
    aria-label={`Image ${currentIndex + 1} of ${images.length}`}
    className="absolute bottom-2 left-1/2 -translate-x-1/2
               text-[10px] text-white/60 tabular-nums"
  >
    {currentIndex + 1} / {images.length}
  </div>
</div>
```

Add `ChevronLeft, ChevronRight` to the lucide-react import block.

**Update the lightbox render call** in `PortfolioFeed`:

```tsx
{lightboxIndex !== null && lightboxImage !== null && (
  <PortfolioLightbox
    images={allImages}
    currentIndex={lightboxIndex}
    token={token}
    onClose={() => setLightboxIndex(null)}
    onNavigate={(idx) => setLightboxIndex(idx)}
  />
)}
```

### 4-B: "Book with artist" conversion CTA

Add a CTA section at the bottom of the info panel in the lightbox, below `ReviewSection`.
This is the conversion dead-end fix — users who like what they see need a next step.

```tsx
{/* CTA section — always visible at bottom of info panel */}
<div className="pt-4 border-t border-border/40 space-y-2">
  <Link
    to={`/artist/${image.artistSlug}`}
    onClick={onClose}
    className="flex items-center justify-center gap-2 w-full
               rounded-md bg-violet-600 hover:bg-violet-700
               text-white text-sm font-medium py-2 px-4
               transition-colors"
  >
    Book with {image.artistName}
  </Link>
  <Link
    to={`/artist/${image.artistSlug}`}
    onClick={onClose}
    className="flex items-center justify-center w-full
               text-xs text-muted-foreground hover:text-foreground
               underline underline-offset-4 transition-colors"
  >
    View artist profile
  </Link>
</div>
```

Both links call `onClose` so the lightbox closes cleanly when the user navigates.

### 4-C: Scroll-fade indicator on the info panel

When there's enough content to scroll, the panel doesn't visually communicate that
it's scrollable. Add a `scroll-fade` wrapper.

Replace the info panel `<div>`:

```tsx
{/* Info + reviews panel — scrollable with fade hint */}
<div className="relative flex flex-col max-h-[70vh]">
  <div className="p-5 overflow-y-auto flex-1 space-y-4" id="lightbox-scroll-panel">
    {/* ... all the content ... */}
  </div>
  {/* Fade overlay at the bottom to hint at scrollable content */}
  <div
    aria-hidden="true"
    className="pointer-events-none absolute bottom-0 left-0 right-0 h-8
               bg-gradient-to-t from-background/90 to-transparent
               rounded-br-lg"
  />
</div>
```

The fade disappears naturally when the user scrolls to the bottom (it fades the
background, not content). This is a CSS-only hint — no JS scroll listener needed.

### 4-D: Close button — add `aria-label` and make it a proper icon button

The current close button:
```tsx
<button
  onClick={onClose}
  className="absolute right-3 top-3 z-10 rounded-full bg-black/60 p-1
             text-white hover:bg-black/80 transition-colors"
  aria-label="Close"
>
  <X className="h-4 w-4" />
</button>
```

The `aria-label="Close"` IS already there — confirm it's still present after the
lightbox rewrite. Also add `type="button"` to prevent accidental form submission if
someone wraps the Dialog in a form.

---

## Section 5 — Tests: update and add

### 5-A: `ReviewSection.test.tsx` — breaking changes from reorder

The test for "Submit review" button name must be updated to "Post Review":

```typescript
// OLD — will fail:
const submitBtn = screen.getByRole("button", { name: /submit review/i });

// NEW:
const submitBtn = screen.getByRole("button", { name: /post review/i });
```

Update ALL occurrences of `/submit review/i` in the test file.

The test for success message triggers `aria-label="Submit review"` → update to
`aria-label="Post review"`.

### 5-B: Add tests for `StarRating` components

Create `frontend/src/shared/components/ui/__tests__/StarRating.test.tsx`:

```typescript
import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { StarRating, InteractiveStarRating } from "@/shared/components/ui/StarRating";

describe("StarRating (display)", () => {
  it("renders correct number of stars", () => {
    render(<StarRating value={3} />);
    // role="img" container
    expect(screen.getByRole("img", { name: /rated 3 out of 5/i })).toBeInTheDocument();
  });

  it("aria-label matches value", () => {
    render(<StarRating value={4} max={5} />);
    expect(screen.getByRole("img")).toHaveAttribute("aria-label", "Rated 4 out of 5 stars");
  });
});

describe("InteractiveStarRating", () => {
  it("renders a radiogroup with 5 buttons", () => {
    render(<InteractiveStarRating value={0} onChange={() => {}} />);
    const group = screen.getByRole("radiogroup");
    expect(group).toBeInTheDocument();
    expect(screen.getAllByRole("radio")).toHaveLength(5);
  });

  it("each button has min 44px touch target", () => {
    render(<InteractiveStarRating value={0} onChange={() => {}} />);
    const buttons = screen.getAllByRole("radio");
    buttons.forEach((btn) => {
      expect(btn.className).toMatch(/min-w-\[44px\]/);
      expect(btn.className).toMatch(/min-h-\[44px\]/);
    });
  });

  it("calls onChange with the correct rating on click", () => {
    const onChange = vi.fn();
    render(<InteractiveStarRating value={0} onChange={onChange} />);
    fireEvent.click(screen.getByRole("radio", { name: /rate 3 of 5/i }));
    expect(onChange).toHaveBeenCalledWith(3);
  });

  it("aria-checked is true on selected star", () => {
    render(<InteractiveStarRating value={3} onChange={() => {}} />);
    expect(screen.getByRole("radio", { name: /rate 3 of 5/i }))
      .toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: /rate 4 of 5/i }))
      .toHaveAttribute("aria-checked", "false");
  });

  it("live text readout appears after selection", () => {
    const { rerender } = render(<InteractiveStarRating value={0} onChange={() => {}} />);
    rerender(<InteractiveStarRating value={4} onChange={() => {}} />);
    // The aria-live region should contain the readout
    const live = screen.getByRole("region", { hidden: true }) ??
      document.querySelector("[aria-live='polite']");
    expect(document.querySelector("[aria-live='polite']")?.textContent)
      .toMatch(/4 stars.*good/i);
  });

  it("shows hover preview — aria-checked doesn't change on hover (visual only)", () => {
    render(<InteractiveStarRating value={2} onChange={() => {}} />);
    fireEvent.mouseEnter(screen.getByRole("radio", { name: /rate 5 of 5/i }));
    // Selection doesn't change on hover
    expect(screen.getByRole("radio", { name: /rate 2 of 5/i }))
      .toHaveAttribute("aria-checked", "true");
    expect(screen.getByRole("radio", { name: /rate 5 of 5/i }))
      .toHaveAttribute("aria-checked", "false");
  });
});
```

### 5-C: Update `ReviewSection.test.tsx` — new features

Add these tests:

```typescript
// Post Review button is disabled when no star is selected
it("'Post Review' button is disabled when rating is 0", () => {
  renderSection();
  const btn = screen.getByRole("button", { name: /post review/i });
  expect(btn).toBeDisabled();
});

// Post Review button enables after star selection
it("'Post Review' button enables after selecting a star", async () => {
  const user = userEvent.setup();
  renderSection();
  const btn = screen.getByRole("button", { name: /post review/i });
  expect(btn).toBeDisabled();
  await user.click(screen.getByRole("radio", { name: /rate 3 of 5/i }));
  expect(btn).not.toBeDisabled();
});

// Verified badge — mock a review with isVerifiedBooking: true
it("shows 'Verified client' badge when isVerifiedBooking is true", () => {
  vi.mock("@/features/public/publicApi", () => ({
    // ... (existing mock) ...
    useGetArtistReviewsQuery: () => ({
      data: [{
        id: "r-1", authorName: "Ana Costa", rating: 5,
        body: "Fantastic work", createdAt: "2026-06-01T00:00:00Z",
        isVerifiedBooking: true,
      }],
      isLoading: false,
    }),
    // ... rest of mock ...
  }));
  renderSection();
  expect(screen.getByText(/verified client/i)).toBeInTheDocument();
});

// Review form appears AFTER the reviews list
it("the write form appears after the review list in DOM order", () => {
  renderSection();
  const reviewList = screen.getByLabelText("Loading reviews");  // or skeleton
  // The write form's textarea appears after the skeleton/list
  // We check that 'Write a review' label appears later in the DOM
  // (simple check: it should NOT be the firstChild of the section)
  const section = screen.getByRole("region", { name: /reviews/i });
  const children = Array.from(section.children);
  const headingIdx = children.findIndex((el) =>
    el.querySelector("[id='reviews-heading']") !== null
  );
  const formIdx = children.findIndex((el) =>
    el.querySelector("[aria-label='Write a review']") !== null ||
    el.textContent?.includes("Write a review")
  );
  expect(formIdx).toBeGreaterThan(headingIdx);
});
```

### 5-D: Update `PortfolioFeed.test.tsx` — lightbox navigation

Add to the test file's MSW server:

```typescript
// Add a second image for navigation tests
const IMAGES_NAV: PortfolioImageResponse[] = [
  { ...IMAGES[0], imageId: "img-nav-1" },
  { ...IMAGES[1], imageId: "img-nav-2" },
  { ...IMAGES[0], imageId: "img-nav-3" },
];
```

Add tests:

```typescript
it("lightbox shows prev/next buttons when multiple images exist", async () => {
  server.use(
    http.get("http://localhost/api/v1/public/portfolio/feed", () =>
      HttpResponse.json(IMAGES_NAV),
    ),
  );
  const user = userEvent.setup();
  renderFeed();
  // Click the first tile (not the second one which might be img-nav-2)
  const tiles = await screen.findAllByRole("button", { name: /view tattoo by/i });
  await user.click(tiles[1]); // click the middle tile to have both prev and next
  expect(await screen.findByRole("button", { name: /previous image/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /next image/i })).toBeInTheDocument();
});

it("lightbox shows position indicator", async () => {
  server.use(
    http.get("http://localhost/api/v1/public/portfolio/feed", () =>
      HttpResponse.json(IMAGES_NAV),
    ),
  );
  const user = userEvent.setup();
  renderFeed();
  const tiles = await screen.findAllByRole("button", { name: /view tattoo by/i });
  await user.click(tiles[0]);
  expect(await screen.findByLabelText(/image 1 of 3/i)).toBeInTheDocument();
});

it("next button navigates to the following image", async () => {
  server.use(
    http.get("http://localhost/api/v1/public/portfolio/feed", () =>
      HttpResponse.json(IMAGES_NAV),
    ),
  );
  const user = userEvent.setup();
  renderFeed();
  const tiles = await screen.findAllByRole("button", { name: /view tattoo by/i });
  await user.click(tiles[0]);
  await screen.findByRole("dialog");
  const nextBtn = screen.getByRole("button", { name: /next image/i });
  await user.click(nextBtn);
  // Position should update
  expect(screen.getByLabelText(/image 2 of 3/i)).toBeInTheDocument();
});

it("lightbox close button has aria-label='Close'", async () => {
  const user = userEvent.setup();
  renderFeed();
  await screen.findByLabelText(/Tattoo by Ana Lima/i);
  await user.click(screen.getByLabelText(/Tattoo by Ana Lima/i));
  expect(await screen.findByRole("button", { name: /^close$/i })).toBeInTheDocument();
});

it("lightbox shows 'Book with artist' link", async () => {
  const user = userEvent.setup();
  renderFeed();
  await screen.findByLabelText(/Tattoo by Ana Lima/i);
  await user.click(screen.getByLabelText(/Tattoo by Ana Lima/i));
  expect(await screen.findByRole("link", { name: /book with ana lima/i })).toBeInTheDocument();
});

it("lightbox shows 'View artist profile' link", async () => {
  const user = userEvent.setup();
  renderFeed();
  await screen.findByLabelText(/Tattoo by Ana Lima/i);
  await user.click(screen.getByLabelText(/Tattoo by Ana Lima/i));
  expect(await screen.findByRole("link", { name: /view artist profile/i })).toBeInTheDocument();
});
```

---

## Section 6 — Architecture docs

After all changes, update `docs/claude/architecture.md`:

1. **Decisions Log** — add:
   ```
   | StarRating split into display + interactive | Separate `StarRating` (display) and `InteractiveStarRating` (write form) exports | Touch targets, hover preview, and live readout only needed on interactive variant |
   | ReviewSection order: list before form | Users need to read existing reviews before writing one | Industry trust pattern: aggregate → reviews → form |
   | IsVerifiedBooking on ReviewResponse | Computed at query time via Appointments join, not stored | No migration needed; verified status can change |
   | Lightbox prev/next navigation | Index-based navigation through allImages array | Keyboard arrows (←/→) also supported |
   | "Book with artist" CTA in lightbox | Link to /artist/:slug | Closes modal on navigate |
   ```

2. **IgnoreQueryFilters Approved Usages table** — add entries 19, 20, 21 for:
   - `GetArtistReviewsQuery` — `IsVerifiedBooking` check (cross-tenant completed appointments)
   - `GetStudioReviewsQuery` — same
   - `GetPortfolioImageReviewsQuery` — same

---

## Section 7 — Build checklist

```bash
cd "Pena e Arte"

# 1. Backend build (new ReviewResponse field + query changes)
dotnet build --verbosity minimal

# 2. Backend tests
dotnet test

# 3. Frontend type check
cd frontend && pnpm tsc --noEmit

# 4. Lint
pnpm lint

# 5. All frontend tests must pass (including new StarRating tests)
pnpm test --run
```

All five commands must exit 0.

---

## Summary of Changes

### Bugs fixed:
1. **Section order** — write form moves to bottom; aggregate anchors below heading
2. **Aggregate rendered as text** → rendered with `<StarRating>` + score + count
3. **"Submit review" button** → `bg-violet-600 text-white` (brand color) + renamed "Post Review"
4. **Star touch targets** → `min-w-[44px] min-h-[44px]` on each star button
5. **No hover preview on stars** → `hovered` state + scale animation
6. **Interactive/display star inconsistency** → split into two explicit exports
7. **Date format "1 Jul 2026"** → standardized to "Jul 1, 2026"
8. **Submit enabled without rating** → button disabled when `rating === 0`
9. **No scroll indicator** → CSS gradient fade at bottom of info panel

### New features:
10. **"Verified Client" badge** — backend `IsVerifiedBooking` via Appointments join + frontend badge in `ReviewCard`
11. **Prev/next navigation in lightbox** — chevron buttons + keyboard arrows + position indicator
12. **"Book with artist" CTA** — violet primary button in lightbox info panel
13. **"View artist profile" link** — secondary link below the CTA

---

## Hard Rules Reminder

- No new npm or NuGet packages. `BadgeCheck`, `ChevronLeft`, `ChevronRight` are in
  `lucide-react` (already installed).
- No `useEffect` for data fetching. The keyboard-listener `useEffect` in the lightbox
  is a browser API side-effect, not a fetch.
- No `any`. All new types must be fully typed.
- No default exports on components.
- No TypeScript `enum`.
- All tests green before the session ends.
