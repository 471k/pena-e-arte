# Overnight Prompt — Guest QA Deferred UI Items
**Date:** 2026-07-04
**Scope:** Six deferred items from the guest QA pass.

---

## Required Reading

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/database.md
docs/claude/architecture.md
```

Then read these specific source files **before starting any work** — the exact signatures and
patterns matter for every item below:

```
Pena_e_Arte.Domain/Entities/Review.cs
Pena_e_Arte.Contracts/Responses/Public/ReviewResponse.cs
Pena_e_Arte.Contracts/Responses/Public/ArtistPortfolioImageResponse.cs
Pena_e_Arte.Contracts/Responses/Public/PublicArtistResponse.cs
Pena_e_Arte.Application/Public/Queries/GetPublicArtistQuery.cs
Pena_e_Arte.Application/Public/Queries/GetStudioReviewsQuery.cs
Pena_e_Arte.Application/Public/Queries/GetArtistReviewsQuery.cs
Pena_e_Arte.Application/Public/Queries/GetPortfolioImageReviewsQuery.cs
Pena_e_Arte.API/Endpoints/PublicEndpoints.cs
Pena_e_Arte.API/Endpoints/StudioEndpoints.cs
Pena_e_Arte.Infrastructure/Services/IdentityService.cs                ← GenerateJwt method
frontend/src/features/public/components/ArtistPortfolioPage.tsx
frontend/src/features/public/components/ReviewSection.tsx
frontend/src/features/public/publicApi.ts
frontend/src/features/public/components/PortfolioFeed.tsx             ← STYLES array + StyleChips pattern
frontend/src/features/appointments/components/BookPage.tsx
frontend/src/features/auth/components/ClientRegisterPage.tsx
frontend/src/features/auth/components/VerifyEmailPage.tsx             ← understand email flow
frontend/src/features/auth/authApi.ts                                  ← resendVerificationEmail already exists
frontend/src/features/auth/authSlice.ts
frontend/src/shared/types/roles.ts
frontend/src/shared/utils/jwt.ts
```

---

## Item 1 — Artist Portfolio Style Filter Chips

### Problem
`ArtistPortfolioPage` shows all portfolio images in one unsorted masonry grid. The
`PortfolioImage.Style` field exists and is already used in the global `PortfolioFeed`,
but `ArtistPortfolioImageResponse` (the per-artist projection) omits it, so the artist
profile page cannot filter by style.

### Backend Changes

**File:** `Pena_e_Arte.Contracts/Responses/Public/ArtistPortfolioImageResponse.cs`

Add `Style`:
```csharp
public record ArtistPortfolioImageResponse(Guid ImageId, string ImageUrl, string? Style);
```

**File:** `Pena_e_Arte.Application/Public/Queries/GetPublicArtistQuery.cs`

Update the portfolio projection (inside `GetPublicArtistHandler.Handle`):
```csharp
artist.Portfolio
    .OrderByDescending(p => p.CreatedAt)
    .Select(p => new ArtistPortfolioImageResponse(p.Id, p.ImageUrl, p.Style))
    .ToList(),
```

**File:** `tests/Pena_e_Arte.UnitTests/Public/GetPublicArtistHandlerTests.cs`

Add a test verifying `Style` is correctly included in the response projection (follow the
existing test patterns in this file).

### Frontend Changes

**File:** `frontend/src/features/public/publicApi.ts`

Update `ArtistPortfolioImage`:
```ts
export interface ArtistPortfolioImage {
  imageId:  string;
  imageUrl: string;
  style:    string | null;  // ← add
}
```

**File:** `frontend/src/features/public/components/ArtistPortfolioPage.tsx`

Copy the `STYLES` array (without "All" at index 0) from `PortfolioFeed.tsx` — keep in sync
as a local constant. Then:

1. Add `const [activeStyle, setActiveStyle] = useState<string>("")` to `ArtistPortfolioPage`.

2. Compute the styles that actually appear in this artist's images (derive from loaded data,
   not from the global list, so the chips only show styles that have at least one image):
   ```ts
   const availableStyles = useMemo(() => {
     if (!artist) return [];
     const seen = new Set(artist.portfolioImages.map((p) => p.style).filter(Boolean));
     return STYLES.filter(({ value }) => seen.has(value));
   }, [artist]);
   ```

3. Render filter chips **above** the `PortfolioGrid`, only when `availableStyles.length > 1`:
   ```tsx
   {availableStyles.length > 1 && (
     <div
       role="group"
       aria-label="Filter by tattoo style"
       className="flex items-center gap-1.5 overflow-x-auto scrollbar-none pb-1
                  -mx-4 px-4 sm:mx-0 sm:px-0"
     >
       {/* "All" chip */}
       <button
         type="button" role="radio" aria-checked={activeStyle === ""}
         onClick={() => setActiveStyle("")}
         className={chipClass(activeStyle === "")}
       >All</button>
       {availableStyles.map(({ value, label }) => (
         <button key={value} type="button" role="radio"
           aria-checked={activeStyle === value}
           onClick={() => setActiveStyle(value)}
           className={chipClass(activeStyle === value)}
         >{label}</button>
       ))}
     </div>
   )}
   ```

   Where `chipClass(active: boolean)` returns the same Tailwind string used in `PortfolioFeed.tsx`.

4. Filter images passed to `PortfolioGrid`:
   ```ts
   const visibleImages = activeStyle
     ? artist.portfolioImages.filter((p) => p.style === activeStyle)
     : artist.portfolioImages;
   ```

5. Pass `visibleImages` (not `artist.portfolioImages`) to `<PortfolioGrid images={visibleImages} ... />`.

6. When `visibleImages.length === 0` after filtering, show a short "No {label} images yet" empty
   state inside `PortfolioGrid` instead of the generic empty state.

---

## Item 2 — Sticky "Book with Artist" CTA on Mobile

### Problem
On desktop, the "Book an Appointment" button lives in the sticky left sidebar. On mobile the
sidebar stacks above the portfolio grid and the CTA immediately scrolls out of view. A guest
arriving from Instagram has to scroll to the top to find it.

### Frontend Changes

**File:** `frontend/src/features/public/components/ArtistPortfolioPage.tsx`

Add a fixed bottom bar for mobile — render it only when `artist.showBookingCta` is true.
Place it immediately **before the closing `</div>` of the root element** (after the footer):

```tsx
{/* ── Mobile sticky Book CTA ──────────────────────────────────────────── */}
{artist.showBookingCta && (
  <div
    className="fixed bottom-0 inset-x-0 z-[90] lg:hidden
               border-t bg-background/95 backdrop-blur-sm px-4 py-3
               safe-area-inset-bottom"
    aria-label="Quick book bar"
  >
    <Button
      className="w-full bg-violet-600 hover:bg-violet-700
                 text-white border-0 min-h-[44px] text-sm font-semibold"
      asChild
    >
      <Link to={ctaUrl}>Book with {artist.name.split(" ")[0]}</Link>
    </Button>
  </div>
)}
```

Add bottom padding to the page's content area so the bar never obscures portfolio images.
On the root `<div className="min-h-screen bg-background flex flex-col">`, the content div
that wraps the masonry grid needs `pb-20 lg:pb-0` to account for the fixed bar height:

```tsx
<div className="flex-1 max-w-6xl mx-auto w-full px-4 py-8 space-y-6 pb-20 lg:pb-8">
```

The `lg:hidden` class ensures it never renders at desktop widths (the sidebar is already there).

---

## Item 3 — Review Pagination Past 10

### Problem
`ReviewList` in `ReviewSection.tsx` renders all reviews returned by the API (up to 50) at once.
Long review lists bury the write-a-review form and overwhelm the page.

### Frontend Changes

**File:** `frontend/src/features/public/components/ReviewSection.tsx`

In the `ReviewList` component, add client-side slicing:

```ts
const PAGE_SIZE = 10;

function ReviewList({
  reviews, isLoading, averageRating,
}: {
  reviews:       ReviewResponse[] | undefined;
  isLoading:     boolean;
  averageRating: number | null;
}) {
  const [showAll, setShowAll] = useState(false);

  const visible = !reviews
    ? []
    : showAll
    ? reviews
    : reviews.slice(0, PAGE_SIZE);

  const hiddenCount = (reviews?.length ?? 0) - visible.length;

  return (
    <>
      {/* ... existing average-rating header unchanged ... */}
      {isLoading ? (
        <ReviewsSkeleton />
      ) : !reviews || reviews.length === 0 ? (
        /* ... existing empty state unchanged ... */
      ) : (
        <div>
          {visible.map((r) => <ReviewCard key={r.id} review={r} />)}

          {!showAll && hiddenCount > 0 && (
            <button
              type="button"
              onClick={() => setShowAll(true)}
              className="mt-3 w-full py-2.5 text-xs text-muted-foreground
                         hover:text-foreground border border-border/40
                         rounded-md transition-colors"
            >
              Show {hiddenCount} more review{hiddenCount !== 1 ? "s" : ""}
            </button>
          )}
        </div>
      )}
    </>
  );
}
```

Reset `showAll` back to `false` whenever the `slug` prop changes. Since `ReviewList` is a
child component that doesn't receive `slug`, the reset happens naturally — the parent
`StudioReviewList`/`ArtistReviewList` components are re-mounted when the slug changes because
they're keyed by the RTK Query cache key.

---

## Item 4 — Owner Review-Response Display

### Problem
`Review` has no `OwnerResponse` or `OwnerResponseAt` fields. The domain, contracts, query
projections, and frontend all need updating. A new protected endpoint must exist so studio
owners can submit responses. Once a response exists, it must display on the public review page
as an indented reply bubble.

### Step 4a — Domain entity

**File:** `Pena_e_Arte.Domain/Entities/Review.cs`

Add two new properties and a `Respond` method (private setter pattern follows the existing style):

```csharp
public string?   OwnerResponse   { get; private set; }
public DateTime? OwnerResponseAt { get; private set; }

/// <summary>
/// Records the studio/artist owner's public reply to this review.
/// Idempotent — calling again overwrites an existing response.
/// </summary>
/// <param name="response">The owner's reply text (1–2000 characters, trimmed).</param>
public void Respond(string response)
{
    if (string.IsNullOrWhiteSpace(response))
        throw new ArgumentException("Owner response cannot be blank.", nameof(response));

    OwnerResponse   = response.Trim();
    OwnerResponseAt = DateTime.UtcNow;
}
```

### Step 4b — Migration

```bash
cd "Pena e Arte"
dotnet ef migrations add AddOwnerResponseToReview --project Pena_e_Arte.Infrastructure
dotnet ef database update --project Pena_e_Arte.Infrastructure
```

Verify the generated migration adds two nullable columns:
- `OwnerResponse   LONGTEXT     NULL`
- `OwnerResponseAt DATETIME(6)  NULL`

### Step 4c — Contracts

**File:** `Pena_e_Arte.Contracts/Responses/Public/ReviewResponse.cs`

Add the two new fields (nullable — a review may not have an owner response):

```csharp
public record ReviewResponse(
    Guid      Id,
    string    AuthorName,
    int       Rating,
    string    Body,
    DateTime  CreatedAt,
    bool      IsVerifiedBooking,
    string?   OwnerResponse,      // ← new
    DateTime? OwnerResponseAt);   // ← new
```

### Step 4d — Update query projections

**All three** review query handlers select `ReviewResponse` by positional constructor. Update
the `Select()` call in each to include the two new fields:

```csharp
// In GetStudioReviewsQuery, GetArtistReviewsQuery, GetPortfolioImageReviewsQuery:
.Select(r => new ReviewResponse(
    r.Id, r.AuthorName, r.Rating, r.Body, r.CreatedAt,
    verifiedUserIds.Contains(r.AuthorUserId),
    r.OwnerResponse,      // ← new
    r.OwnerResponseAt))   // ← new
```

### Step 4e — RespondToReviewCommand

**File:** `Pena_e_Arte.Application/Reviews/Commands/RespondToReviewCommand.cs` (NEW)

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Reviews.Commands;

public record RespondToReviewRequest(string Response);

public record RespondToReviewCommand(Guid ReviewId, string Response)
    : IRequest<Unit>;

public class RespondToReviewHandler(IAppDbContext db, ICurrentTenant currentTenant)
    : IRequestHandler<RespondToReviewCommand, Unit>
{
    public async Task<Unit> Handle(RespondToReviewCommand command, CancellationToken ct)
    {
        Domain.Entities.Review? review = await db.Reviews
            .FirstOrDefaultAsync(r => r.Id == command.ReviewId, ct);

        if (review is null) throw new NotFoundException(nameof(Domain.Entities.Review), command.ReviewId);

        // The review must belong to this owner's studio (directly or via their artist).
        // db.Reviews uses the tenant query filter, so EF already scoped it — if the
        // review came back, it's either a StudioId review for this tenant OR an ArtistId
        // review for an artist in this tenant. Confirm both cases cover ownership.
        bool isForThisStudio  = review.StudioId  == currentTenant.StudioId;
        bool isForThisArtist  = review.ArtistId.HasValue
            && await db.Artists.AnyAsync(a => a.Id == review.ArtistId && a.StudioId == currentTenant.StudioId, ct);
        bool isForThisImage   = review.PortfolioImageId.HasValue
            && await db.PortfolioImages.AnyAsync(pi => pi.Id == review.PortfolioImageId && pi.StudioId == currentTenant.StudioId, ct);

        if (!isForThisStudio && !isForThisArtist && !isForThisImage)
            throw new ForbiddenException("You cannot respond to this review.");

        review.Respond(command.Response);
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}

public class RespondToReviewValidator : AbstractValidator<RespondToReviewCommand>
{
    public RespondToReviewValidator()
    {
        RuleFor(x => x.ReviewId).NotEmpty();
        RuleFor(x => x.Response)
            .NotEmpty().WithMessage("Response cannot be blank.")
            .MaximumLength(2000).WithMessage("Response must be 2000 characters or fewer.");
    }
}
```

**Note:** Check whether `ForbiddenException` exists in the Domain exceptions — if not, throw
`UnauthorizedAccessException` or create it following the pattern of `NotFoundException`.

### Step 4f — New endpoint

**File:** `Pena_e_Arte.API/Endpoints/ReviewEndpoints.cs` (NEW)

```csharp
using MediatR;
using Pena_e_Arte.Application.Reviews.Commands;

namespace Pena_e_Arte.API.Endpoints;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/reviews");

        group.MapPost("{reviewId:guid}/respond", RespondToReview)
             .RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> RespondToReview(
        Guid                      reviewId,
        RespondToReviewRequest    request,
        ISender                   mediator,
        CancellationToken         ct)
    {
        await mediator.Send(new RespondToReviewCommand(reviewId, request.Response), ct);
        return Results.NoContent();
    }
}
```

Register it in `Program.cs` alongside the other `Map*Endpoints()` calls:
```csharp
app.MapReviewEndpoints();
```

### Step 4g — Frontend: update `publicApi.ts`

Add `ownerResponse` and `ownerResponseAt` to `ReviewResponse`:

```ts
export interface ReviewResponse {
  id:                string;
  authorName:        string;
  rating:            number;
  body:              string;
  createdAt:         string;
  isVerifiedBooking: boolean;
  ownerResponse:     string | null;    // ← new
  ownerResponseAt:   string | null;    // ← new
}
```

Add the respond mutation and `RespondToReviewRequest` to `publicApi`:

```ts
interface RespondToReviewRequest {
  response: string;
}

// In publicApi endpoints:
respondToReview: builder.mutation<void, { reviewId: string } & RespondToReviewRequest>({
  query: ({ reviewId, response }) => ({
    url:    `/reviews/${reviewId}/respond`,
    method: "POST",
    body:   { response },
  }),
  // Invalidate all review tags so the public view refreshes.
  invalidatesTags: ["StudioReviews", "ArtistReviews", "PortfolioImageReviews"],
}),
```

**Important:** `respondToReview` must use the **authenticated** base query (not `publicBaseQuery`)
because the endpoint is not under `/api/v1/public/`. Check how other non-public mutations
(e.g., in `authApi.ts`) set up `baseQuery` and mirror that approach.

If `publicApi` uses `publicBaseQuery` exclusively, create a new slice or move this mutation
to an appropriate authenticated API slice. The simplest path: create a minimal
`frontend/src/features/reviews/reviewsApi.ts` that uses the authenticated `baseQuery` from
`@/shared/api/baseQuery` for this single mutation.

Export the hook:
```ts
export const { useRespondToReviewMutation } = reviewsApi;
```

### Step 4h — Frontend: update `ReviewCard` and `ReviewSection`

**File:** `frontend/src/features/public/components/ReviewSection.tsx`

Update `ReviewCard` to show the owner response bubble when present:

```tsx
function ReviewCard({ review }: { review: ReviewResponse }) {
  return (
    <div className="py-4 border-b last:border-b-0 space-y-2">
      {/* ... existing header and body unchanged ... */}

      {review.ownerResponse && (
        <div className="ml-4 mt-2 pl-3 border-l-2 border-border/50 space-y-1">
          <p className="text-[11px] font-medium text-muted-foreground uppercase tracking-wide">
            Studio response
            {review.ownerResponseAt && (
              <span className="font-normal ml-1">
                · {new Date(review.ownerResponseAt).toLocaleDateString("en-US", {
                    month: "short", day: "numeric", year: "numeric",
                  })}
              </span>
            )}
          </p>
          <p className="text-sm text-muted-foreground/90 leading-relaxed whitespace-pre-wrap">
            {review.ownerResponse}
          </p>
        </div>
      )}
    </div>
  );
}
```

Add `canRespond?: boolean` prop to `ReviewSection` interface and pass it through to
`ReviewCard`. When `canRespond` is true and a review has no `ownerResponse`, show an inline
reply form (collapsible — hidden by default, toggle with a "Reply" link).

The inline reply form in `ReviewCard` (only rendered when `canRespond && !review.ownerResponse`):

```tsx
{canRespond && !review.ownerResponse && (
  <OwnerReplyForm reviewId={review.id} />
)}
```

`OwnerReplyForm` is a small inline component that:
- Has a "Reply" toggle button (text link, not a full button)
- Expands to a `<textarea>` + "Post reply" button when clicked
- Calls `useRespondToReviewMutation` on submit
- Collapses back on success

**File:** `frontend/src/features/public/components/StudioPortfolioPage.tsx`

Pass `canRespond` to `ReviewSection` when the authenticated user is the studio owner:

```tsx
// Near top of StudioPortfolioPage:
const role     = useAppSelector((s) => s.auth.role);
const tenantId = useAppSelector((s) => s.auth.tenantId);
const canRespond = role === "owner" && tenantId === studio.studioId;

// In the JSX:
<ReviewSection
  slug={studio.slug}
  target="studio"
  token={token}
  canRespond={canRespond}          // ← add
/>
```

Update the `ReviewSection` `Props` interface to include:
```ts
interface Props {
  slug:        string;
  target:      "studio" | "artist" | "tattoo";
  token:       string | null;
  imageId?:    string;
  canRespond?: boolean;             // ← add
}
```

Pass `canRespond` down through `StudioReviewList` / `ArtistReviewList` / `PortfolioImageReviewList`
→ `ReviewList` → `ReviewCard`.

### Step 4i — Unit tests

Add to the existing handler test files or create new ones:

```
GetStudioReviewsHandlerTests  — verify OwnerResponse and OwnerResponseAt appear in projection
GetArtistReviewsHandlerTests  — same
RespondToReviewHandlerTests   — new file:
  1. Responds successfully when reviewer's StudioId matches
  2. Responds successfully when review is for an artist in this studio
  3. Throws NotFoundException when review does not exist
  4. Throws ForbiddenException when review belongs to a different studio
  5. Respond is idempotent — calling twice updates OwnerResponseAt
```

---

## Item 5 — Password Strength Indicator

### Problem
`ClientRegisterPage` and `RegisterStudioPage` have no password strength feedback.
Users set weak passwords (min 8 chars, no other guidance) without realising it.

### Step 5a — Create `PasswordStrengthMeter`

**File:** `frontend/src/shared/components/ui/PasswordStrengthMeter.tsx` (NEW)

```tsx
interface Props {
  password: string;
}

type Strength = "weak" | "fair" | "good" | "strong";

function getStrength(pw: string): Strength | null {
  if (!pw) return null;
  const len       = pw.length;
  const hasUpper  = /[A-Z]/.test(pw);
  const hasLower  = /[a-z]/.test(pw);
  const hasDigit  = /\d/.test(pw);
  const hasSpecial = /[^A-Za-z0-9]/.test(pw);

  if (len < 8)                                           return "weak";
  if (len < 10 || !(hasUpper && hasLower && hasDigit))   return "fair";
  if (len < 12 || !hasSpecial)                           return "good";
  return "strong";
}

const LABELS: Record<Strength, string> = {
  weak:   "Weak",
  fair:   "Fair",
  good:   "Good",
  strong: "Strong",
};

const COLORS: Record<Strength, string> = {
  weak:   "bg-destructive",
  fair:   "bg-amber-500",
  good:   "bg-emerald-400",
  strong: "bg-emerald-500",
};

const WIDTHS: Record<Strength, string> = {
  weak:   "w-1/4",
  fair:   "w-2/4",
  good:   "w-3/4",
  strong: "w-full",
};

export function PasswordStrengthMeter({ password }: Props) {
  const strength = getStrength(password);

  if (!strength) return null;

  return (
    <div className="space-y-1" aria-live="polite" aria-label={`Password strength: ${LABELS[strength]}`}>
      <div className="h-1 rounded-full bg-border overflow-hidden">
        <div
          className={`h-full rounded-full transition-all duration-300 ${COLORS[strength]} ${WIDTHS[strength]}`}
        />
      </div>
      <p className="text-[11px] text-muted-foreground">
        Strength:{" "}
        <span className={`font-medium ${strength === "weak" ? "text-destructive" : ""}`}>
          {LABELS[strength]}
        </span>
        {strength === "weak" && " — use at least 8 characters"}
        {strength === "fair" && " — add uppercase, lowercase, and a number"}
        {strength === "good" && " — add a symbol (!@#…) to make it strong"}
      </p>
    </div>
  );
}
```

### Step 5b — Add to `ClientRegisterPage`

**File:** `frontend/src/features/auth/components/ClientRegisterPage.tsx`

1. Import `PasswordStrengthMeter` and `useWatch`:
   ```ts
   import { PasswordStrengthMeter } from "@/shared/components/ui/PasswordStrengthMeter";
   // useWatch is already available from react-hook-form
   ```

2. After `useForm` setup, watch the password field:
   ```ts
   const { register, handleSubmit, formState: { errors }, watch } = useForm<FormValues>({ ... });
   const passwordValue = watch("password");
   ```

3. Add the meter immediately after the `<PasswordInput id="password" ...>` and its error `<p>`:
   ```tsx
   <PasswordStrengthMeter password={passwordValue} />
   ```

### Step 5c — Add to `RegisterStudioPage`

**File:** `frontend/src/features/studios/components/RegisterStudioPage.tsx`

The password field there uses a plain `<Input type="password">`. While you're here:
- Swap it to `<PasswordInput>` (same component used in `ClientRegisterPage`) for the
  show/hide toggle.
- Add `watch("password")` with `useWatch` and render `<PasswordStrengthMeter>` below the field.
- Only show the meter when the password fields are visible (i.e., when not on the OAuth path —
  `watch("password") !== "" || watch("confirmPassword") !== ""`).

---

## Item 6 — Email-Verification Banner on /book

### Problem
Clients who registered but haven't verified their email can book appointments without any
reminder. The JWT contains no `email_verified` claim today, so the frontend has no way to
detect the unverified state.

### Step 6a — Add `email_verified` claim to JWT

**File:** `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`

In `GenerateJwt`, after the `JwtRegisteredClaimNames.Jti` claim, add:

```csharp
new("email_verified", user.EmailConfirmed ? "true" : "false"),
```

This is a standard OIDC claim name. All existing tokens will naturally refresh; new logins
will carry the claim immediately.

### Step 6b — Decode on the frontend

**File:** `frontend/src/shared/types/roles.ts`

Add `emailVerified?: boolean` to the `User` interface:

```ts
export interface User {
  id:             string;
  email:          string;
  name?:          string;
  emailVerified?: boolean;    // ← add
}
```

**File:** `frontend/src/shared/utils/jwt.ts`

Update `JwtClaims`:
```ts
interface JwtClaims {
  sub:             string;
  email:           string;
  given_name?:     string;
  exp?:            number;
  tenant_id?:      string;
  email_verified?: string | boolean;  // ← add
  [ROLE_CLAIM]?:   string;
}
```

Update `decodeToken` to populate `user.emailVerified`:
```ts
const user: User = {
  id:            claims.sub,
  email:         claims.email,
  name:          claims.given_name,
  emailVerified: claims.email_verified === true || claims.email_verified === "true",  // ← add
};
```

### Step 6c — Add verification banner to `BookPage`

**File:** `frontend/src/features/appointments/components/BookPage.tsx`

```tsx
import { AlertTriangle, X }              from "lucide-react";
import { useAppSelector }                from "@/app/hooks";
import { useResendVerificationEmailMutation } from "@/features/auth/authApi";
import { useState }                      from "react";
```

Inside `BookPage`:
```tsx
export function BookPage() {
  useDocumentMeta({ title: "Book — Pena e Artë", canonical: "/book" });

  const user          = useAppSelector((s) => s.auth.user);
  const needsVerify   = user != null && user.emailVerified === false;
  const [dismissed, setDismissed]   = useState(false);
  const [resend, { isLoading: isResending, isSuccess: resentOk }] =
    useResendVerificationEmailMutation();

  return (
    <BookingWidget>
      <div className="bg-background flex items-start justify-center px-4 py-12">
        <div className="w-full max-w-md space-y-6">

          {/* Email-verification banner */}
          {needsVerify && !dismissed && (
            <div
              role="alert"
              className="relative rounded-lg border border-amber-800/50 bg-amber-950/20
                         px-4 py-3 text-sm flex items-start gap-3"
            >
              <AlertTriangle
                className="h-4 w-4 mt-0.5 shrink-0 text-amber-400"
                aria-hidden="true"
              />
              <div className="flex-1 space-y-1">
                {resentOk ? (
                  <p className="text-amber-300">
                    Verification email sent — check your inbox.
                  </p>
                ) : (
                  <>
                    <p className="text-amber-300">
                      Please verify your email address to complete bookings.
                    </p>
                    <button
                      type="button"
                      onClick={() => void resend()}
                      disabled={isResending}
                      className="text-xs text-amber-400 hover:text-amber-300 underline
                                 underline-offset-2 transition-colors disabled:opacity-60"
                    >
                      {isResending ? "Sending…" : "Resend verification email"}
                    </button>
                  </>
                )}
              </div>
              <button
                type="button"
                onClick={() => setDismissed(true)}
                aria-label="Dismiss email verification reminder"
                className="text-amber-400/60 hover:text-amber-400 transition-colors"
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
          )}

          {/* ... rest of BookPage JSX unchanged ... */}
```

**Note on `emailVerified === false` vs `emailVerified === undefined`:**
The check is `user.emailVerified === false` (strict equality). If the user has an older token
without the claim, `emailVerified` will be `undefined` (not `false`), so no banner appears for
existing sessions — they get it on next login.

---

## Verification

Run in order. Fix every failure before moving to the next.

```bash
cd "Pena e Arte"

# 1. Build after domain + contract + query changes
dotnet build

# 2. Run migration (generates migration file — check it before updating)
dotnet ef migrations add AddOwnerResponseToReview --project Pena_e_Arte.Infrastructure
# Inspect the generated file, then:
dotnet ef database update --project Pena_e_Arte.Infrastructure

# 3. Backend tests (new + existing)
dotnet test --no-build

# 4. Frontend type-check (catches ArtistPortfolioImage missing style, etc.)
cd frontend && pnpm tsc --noEmit

# 5. Frontend unit tests
pnpm test
```

All commands must exit 0.

---

## Exit Condition

All 5 commands green. Then append to `docs/claude/architecture.md`:

```markdown
## Guest QA Deferred UI Items — 2026-07-04

### Item 1: Artist portfolio style filter chips
- `ArtistPortfolioImageResponse` now includes `Style?: string`
- `GetPublicArtistQuery` projects `p.Style` into the response
- `ArtistPortfolioPage` shows filter chips when ≥ 2 distinct styles exist in the artist's
  images. Chips derived from the loaded data, not the global STYLES list — no dead chips.

### Item 2: Sticky Book CTA on mobile
- Fixed bottom bar (`lg:hidden`) added to `ArtistPortfolioPage`
- Content area gets `pb-20 lg:pb-8` to prevent overlap
- No JS visibility logic — pure CSS responsive hiding

### Item 3: Review pagination
- `ReviewList` shows first 10 reviews, "Show N more" button reveals the rest
- No backend change — backend already returns up to 50; slicing is client-side

### Item 4: Owner review-response
- `Review.Respond(string)` method added to domain entity
- Migration: `AddOwnerResponseToReview` adds `OwnerResponse` (nullable LONGTEXT) and
  `OwnerResponseAt` (nullable DATETIME)
- `ReviewResponse` contract updated with both fields
- All three review query projections updated
- `RespondToReviewCommand` + handler + validator added (Application layer)
- `ReviewEndpoints.cs` — `POST /api/v1/reviews/{reviewId}/respond` (OwnerOnly)
- `ReviewCard` shows owner response as indented border-left quote block
- `StudioPortfolioPage` passes `canRespond` when `role === "owner" && tenantId === studioId`
- Inline `OwnerReplyForm` rendered per unanswered review when `canRespond` is true

### Item 5: Password strength meter
- `PasswordStrengthMeter` shared component — 4-level (weak/fair/good/strong), no external deps
- Added to `ClientRegisterPage` and `RegisterStudioPage`
- `RegisterStudioPage` password field upgraded from plain `<Input type="password">` to `<PasswordInput>`

### Item 6: Email-verification banner on /book
- `email_verified` JWT claim added in `GenerateJwt` (based on Identity `EmailConfirmed`)
- `User.emailVerified?: boolean` added to `roles.ts`; decoded in `decodeToken`
- `BookPage` shows an amber banner with "Resend verification email" action when
  `user.emailVerified === false` (strict — undefined = old token = no banner)

### Files added/changed
Backend:
- `Pena_e_Arte.Domain/Entities/Review.cs` — OwnerResponse, OwnerResponseAt, Respond()
- `Pena_e_Arte.Contracts/Responses/Public/ReviewResponse.cs` — two new fields
- `Pena_e_Arte.Contracts/Responses/Public/ArtistPortfolioImageResponse.cs` — Style
- `Pena_e_Arte.Application/Public/Queries/GetPublicArtistQuery.cs` — Style in projection
- `Pena_e_Arte.Application/Public/Queries/GetStudioReviewsQuery.cs` — owner response fields
- `Pena_e_Arte.Application/Public/Queries/GetArtistReviewsQuery.cs` — owner response fields
- `Pena_e_Arte.Application/Public/Queries/GetPortfolioImageReviewsQuery.cs` — owner response fields
- `Pena_e_Arte.Application/Reviews/Commands/RespondToReviewCommand.cs` (NEW)
- `Pena_e_Arte.API/Endpoints/ReviewEndpoints.cs` (NEW)
- `Pena_e_Arte.Infrastructure/Services/IdentityService.cs` — email_verified JWT claim
- Migration: AddOwnerResponseToReview

Frontend:
- `frontend/src/shared/types/roles.ts` — User.emailVerified
- `frontend/src/shared/utils/jwt.ts` — decode email_verified
- `frontend/src/shared/components/ui/PasswordStrengthMeter.tsx` (NEW)
- `frontend/src/features/public/publicApi.ts` — ArtistPortfolioImage.style, ReviewResponse fields
- `frontend/src/features/public/components/ArtistPortfolioPage.tsx` — style chips + mobile CTA
- `frontend/src/features/public/components/ReviewSection.tsx` — pagination, owner response, canRespond
- `frontend/src/features/appointments/components/BookPage.tsx` — email verification banner
- `frontend/src/features/auth/components/ClientRegisterPage.tsx` — strength meter
- `frontend/src/features/studios/components/RegisterStudioPage.tsx` — PasswordInput + strength meter
- `frontend/src/features/reviews/reviewsApi.ts` (NEW, if created separately)
```
