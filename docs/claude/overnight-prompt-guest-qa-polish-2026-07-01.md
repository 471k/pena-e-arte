# Overnight Prompt — Guest/Visitor: Autonomous QA → Bug Fix → Polish Loop
**Date:** 2026-07-01
**Mode:** Fully autonomous. No user present. Run until every loop exits clean.

---

## Your Mission

You are the first potential client who has never heard of this tattoo studio platform.
You found it on social media, or a friend shared a design link, or you walked past a
studio whose website had the booking widget. You have no account. You are browsing.

The public-facing pages are the platform's shopfront. They load for every visitor,
including search engine bots. They must be fast, visually correct, semantically sound,
and guide the visitor naturally toward booking.

Two phases, run in order. Do not skip to Phase 2 until Phase 1 is fully green.

**Phase 1 — Bug Hunt:** Walk every unauthenticated-accessible page and endpoint
systematically. Fix each bug immediately, re-test, keep looping until the suite is green.

**Phase 2 — Polish:** Evaluate every public page as a product manager who wants
maximum bookings from first-time visitors. Implement what's missing.

---

## Constraints (apply everywhere)

- No new npm or NuGet packages.
- No `useEffect` for data fetching. Approved uses: geolocation callbacks, timer
  side-effects (debounce, auto-dismiss), scroll/resize/outside-click event listeners,
  analytics recording on mount (view tracking), URL/search-param reads on mount.
- TypeScript strict mode. No `any`. No default exports on components.
- Public endpoints must be `[AllowAnonymous]` — no auth required.
- Public endpoints must still enforce tenant isolation: a `/public/studio/{slug}`
  endpoint must not leak data from inactive or suspended studios.
- Review-creation endpoints: require auth (`ClientAndAbove`).
- Rate limiting must be applied on all public write endpoints (review, register).
- Never log PII. Serilog logs must include `request_id` (no `tenant_id`/`user_id` on
  truly public anonymous requests — these may legitimately be absent).
- No secrets in source. `VITE_PUBLIC_URL` for all public-facing URLs.

---

## Required Reading (do before touching any file)

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/architecture.md
docs/claude/conventions.md
```

---

## Guest/Visitor Surface Map

Unauthenticated visitors can access these routes without any login:

| Route | Component | Purpose |
|---|---|---|
| `/` | `IndexRedirect` | → `/discover` for unauthenticated users |
| `/discover` | `DiscoverPage` | Platform landing: portfolio feed + nearby studios |
| `/map` | `StudioMapPage` | Full-screen studio map |
| `/s/:slug` | `StudioPortfolioPage` | Public studio profile |
| `/artist/:slug` | `ArtistPortfolioPage` | Public artist portfolio |
| `/share/:token` | `SharedDesignPage` | Design preview via share link |
| `/embed/:studioSlug` | `EmbedPage` | Embeddable booking widget (for iframe) |
| `/login` | `LoginPage` | Login form |
| `/forgot-password` | `ForgotPasswordPage` | Password reset request |
| `/reset-password` | `ResetPasswordPage` | Password reset confirm |
| `/client-register` | `ClientRegisterPage` | Client signup (requires `?studioId=`) |
| `/register` | `RegisterStudioPage` | Studio owner signup |
| `/verify-email` | `VerifyEmailPage` | Email verification callback |

**The visitor journey (primary path):**
```
/discover
  ↓ search for studios / browse portfolio feed
/s/:studioSlug
  ↓ view studio profile, click "Book with us"
/client-register?studioId={id}&redirect=/book
  ↓ register + auto-login
/book
```

**Secondary paths:**
```
Friend shares design:
  /share/:token → view design → /s/:studioSlug → register/login

Studio website embed:
  /embed/:slug (iframe) → "Book an Appointment" → opens /s/:slug in new tab

Direct link to artist:
  /artist/:slug → view portfolio → "Book now" → /client-register?studioId={id}

Search engine:
  /s/:slug (SEO-optimised studio page) → register/book flow
```

**Backend public endpoints (AllowAnonymous):**
```
GET  /api/v1/studios/nearby?lat=&lng=&radiusKm=   → GetNearbyStudiosQuery
GET  /api/v1/studios/public/{slug}                → GetPublicStudioQuery
GET  /api/v1/artists/public/{slug}                → GetPublicArtistQuery
POST /api/v1/artists/public/{slug}/view           → RecordArtistViewCommand (analytics)
GET  /api/v1/portfolio-feed?cursor=&pageSize=     → GetPortfolioFeedQuery
GET  /api/v1/public/design-share/{token}          → GetSharedDesignQuery
GET  /api/v1/reviews/studio/{slug}                → GetStudioReviewsQuery
GET  /api/v1/reviews/artist/{slug}                → GetArtistReviewsQuery
GET  /api/v1/reviews/portfolio-image/{imageId}    → GetPortfolioImageReviewsQuery

POST /api/v1/auth/register                        → RegisterUserCommand (AllowAnonymous)
POST /api/v1/auth/login                           → LoginCommand (AllowAnonymous)
POST /api/v1/auth/forgot-password                 → ForgotPasswordCommand (AllowAnonymous)
POST /api/v1/auth/reset-password                  → ResetPasswordCommand (AllowAnonymous)
GET  /api/v1/auth/verify-email?token=             → VerifyEmailCommand (AllowAnonymous)

POST /api/v1/reviews/studio     (ClientAndAbove — auth required)
POST /api/v1/reviews/artist     (ClientAndAbove — auth required)
POST /api/v1/reviews/portfolio-image (ClientAndAbove — auth required)
```

---

# PHASE 1 — BUG HUNT

## The Loop Algorithm

```
LOOP:
  1. Build:
       cd "Pena e Arte" && dotnet build
       cd frontend && pnpm build      (TypeScript errors surface here)
  2. Test:
       dotnet test --no-build
       pnpm test
  3. Collect every failure.
  4. For each failure:
       a. Read the source file in full.
       b. Diagnose the exact root cause.
       c. Fix only what is broken.
       d. Re-run just that test file to confirm the fix.
       e. If still failing: diagnose from scratch. Fix differently. Re-run.
       f. Repeat until that test is green.
  5. After all fixes: re-run the full suite.
  6. If new failures appeared: go to step 4.
  7. All green → EXIT PHASE 1, ENTER PHASE 2.
```

---

## Audit Checklist — work through in order while fixing failures

### Layer A — Backend: Public Endpoint Correctness

#### A1. GetPublicStudio — suspended / inactive studio handling

File: `GetPublicStudioQuery.cs` (or equivalent)
Route: `GET /api/v1/studios/public/{slug}`

A suspended or deregistered studio must NOT be visible on the public site. Verify:
- Returns 404 when `studio.IsActive == false` — not the full studio record.
- Returns 404 when the studio has no `slug` set.
- Returns 404 when `studio.IsPublic == false` (or equivalent flag).
- Response shape includes: `name`, `slug`, `description`, `city`, `address`,
  `coverImageUrl`, `logoUrl`, `phone`, `instagramHandle`, `artists` (summary list),
  `averageRating`, `reviewCount`, `showBookingCta`, `showPlatformBranding`.
- `artists` list: only include artists who are active (not soft-deleted, not archived).
  Each artist summary: `artistId`, `slug`, `name`, `bio`, `profileImageUrl`,
  `specializations`, `averageRating`, `reviewCount`.
- `showBookingCta`: owner-configured toggle. If `false`, no "Book" button shown on
  the studio page. Verify this field is in the response.

**Security check:** Verify `[AllowAnonymous]` is on this endpoint. Also verify there
is NO sensitive data in the response: no `stripeSubscriptionId`, no `userId`, no
internal IDs beyond what's needed.

#### A2. GetPublicArtist — same hardening

File: `GetPublicArtistQuery.cs`
Route: `GET /api/v1/artists/public/{slug}`

Verify:
- Returns 404 when artist is archived or has no public `slug`.
- Returns 404 when the artist's studio is inactive.
- Response includes: `id` (for internal use), `slug`, `name`, `bio`, `avatarUrl`,
  `specializations`, `hourlyRate`, `instagramHandle`, `studioName`, `studioSlug`,
  `portfolioImages` (public-facing array of `{ imageId, imageUrl, style }`),
  `averageRating`, `reviewCount`.
- `portfolioImages`: only include images that are NOT flagged as private. If there is
  no privacy flag, all images are public by default — document this.
- `[AllowAnonymous]` on the endpoint.

**Bug to check:** `ArtistPortfolioPage` sets the canonical URL as:
```ts
canonical: `https://penaearte.com/a/${slug}`
```
But the router uses `/artist/:slug`, NOT `/a/:slug`. The canonical is wrong.
Fix: `canonical: `https://penaearte.com/artist/${slug}``

Also: `useDocumentMeta` is a hook — if it accepts `canonical` in a `{ title, description, ogImage, canonical }` shape, ensure the `<link rel="canonical">` is actually rendered in the DOM. Read `useDocumentMeta.ts` and verify. If it only sets `document.title`, the canonical tag is silently dropped — add the `<link>` to the head via:
```ts
let link = document.querySelector<HTMLLinkElement>("link[rel='canonical']");
if (!link) {
  link = document.createElement("link");
  link.rel = "canonical";
  document.head.appendChild(link);
}
link.href = canonical;
```

#### A3. RecordArtistView — analytics without leaking data

File: `RecordArtistViewCommand.cs`
Route: `POST /api/v1/artists/public/{slug}/view`

`ArtistPortfolioPage` fires a view-tracking event on mount via:
```ts
const [recordView] = useRecordArtistViewMutation();
useEffect(() => { void recordView(slug); }, [slug, recordView]);
```

Verify:
- `[AllowAnonymous]` — anonymous visitors should be tracked.
- Does NOT crash if `slug` doesn't exist (returns 200 or 204 silently — don't 404).
- Stores `ViewedAt`, `ArtistId`, and optionally an anonymous session token.
- Rate-limited: max 1 view per IP per artist per hour to prevent artificial inflation.
- Never logs the visitor's IP in the application log (log `request_id` only).
- If Hangfire/background job is used: the job must not crash on a missing artist.

#### A4. GetNearbyStudios — geospatial query correctness

File: `GetNearbyStudiosQuery.cs`
Route: `GET /api/v1/studios/nearby?lat=&lng=&radiusKm=`

Verify:
- Accepts `lat` (double), `lng` (double), `radiusKm` (int from `[10, 25, 50, 100]`).
- Validates: `lat` in [-90, 90], `lng` in [-180, 180].
- Validates: `radiusKm` is one of the four valid values (or any positive int ≤ 200).
  Do NOT accept `radiusKm=999999`.
- Returns only ACTIVE studios (`IsActive == true`, `IsPublic == true`).
- Returns studios within the radius using haversine formula or database spatial index.
  Verify: the query doesn't do a full-table scan on every request (add spatial index
  on `lat`/`lng` columns if missing).
- Response per studio: `slug`, `name`, `city`, `distanceKm` (rounded to 1 decimal),
  `coverImageUrl`, `averageRating`, `reviewCount`, `artistCount`.
- `[AllowAnonymous]`.
- Rate-limited: this endpoint is expensive — max 30 requests/min per IP.

#### A5. GetPortfolioFeed — cursor pagination

File: `GetPortfolioFeedQuery.cs`
Route: `GET /api/v1/portfolio-feed?cursor=&pageSize=`

The `DiscoverPage` `PortfolioFeed` component fetches public portfolio images from
across ALL studios on the platform (cross-tenant discovery feed).

Verify:
- Returns portfolio images from ACTIVE studios only.
- Supports cursor-based pagination (`cursor` = last seen `imageId`).
- Default `pageSize`: 20. Max `pageSize`: 50 (reject values > 50).
- Response shape: `{ items: PortfolioFeedItem[], nextCursor: string | null }`.
- Each `PortfolioFeedItem`: `{ imageId, imageUrl, style, artistName, artistSlug,
  studioName, studioSlug }`.
- Does NOT include deleted or private images.
- `[AllowAnonymous]`.
- No tenant filter — this is a cross-platform feed (intentionally cross-tenant).
  Verify it does NOT call `IgnoreQueryFilters()` unnecessarily — the query should
  join on `Studio.IsActive` to filter, not bypass global filters.

#### A6. GetSharedDesign — token validation

File: `GetSharedDesignQuery.cs`
Route: `GET /api/v1/public/design-share/{token}`

Verify:
- Returns 404 when the token does not exist.
- Returns 404 when the token has `ExpiresAt < DateTime.UtcNow` (expired).
- Returns 404 when the token has `RevokedAt != null` (revoked).
- Response: `{ title, imageUrl, studioName, studioSlug, expiresAt }`.
- Does NOT include client PII (the design belongs to a client — their name must NOT
  appear on this public page).
- `[AllowAnonymous]`.
- Rate-limited: 60 requests/min per IP (preview images can be large).

#### A7. Review endpoints — creation correctness

Files: `CreateStudioReviewCommand.cs`, `CreateArtistReviewCommand.cs`,
       `CreatePortfolioImageReviewCommand.cs`

All three follow the same pattern. Verify for each:
- Requires `ClientAndAbove` policy (NOT `AllowAnonymous`).
- `rating` must be 1–5 (validate server-side, don't trust the UI).
- `body` must be min 10 chars, max 2000 chars.
- Duplicate check: one review per (client, target) pair.
  - Studio review: one per `(clientId, studioId)`.
  - Artist review: one per `(clientId, artistId)`.
  - Portfolio image review: one per `(clientId, imageId)`.
  - On duplicate: 409 Conflict with message "You have already left a review."
- `isVerifiedBooking`: set to `true` if the reviewing client has a `Completed`
  appointment at this studio/with this artist. Set by the handler, NOT trusted from
  the request body.
- The review is associated to the correct tenant even though the GET endpoints are
  cross-tenant (reviews belong to the studio/artist's tenant).
- Rate-limited: max 5 review submissions per IP per hour.

#### A8. Auth endpoints — registration hardening

Files: `RegisterUserCommand.cs`, `LoginCommand.cs`, `ForgotPasswordCommand.cs`,
       `ResetPasswordCommand.cs`

Verify for `RegisterUserCommand` (client registration path):
- `role` param from the request is locked to `"client"` for the public endpoint.
  An attacker must NOT be able to register an `owner` or `issuer` account via this endpoint.
- `studioId` is validated: the studio must exist and be active. If not: 400 with
  "Studio not found."
- `email` uniqueness: if email already exists: 409 with "An account with this email already exists."
- Password: min 8 chars (enforced server-side, not just Zod).
- Rate-limited: max 5 registrations per IP per 10 minutes.

Verify for `ForgotPasswordCommand`:
- Always returns 200 (even if email not found) — prevents user enumeration.
  Response body: "If an account with that email exists, a password reset link has been sent."
- Rate-limited: max 3 requests per email per hour.

Verify for `ResetPasswordCommand`:
- Token from the URL is single-use. After use, mark as consumed.
- Token has a TTL (e.g., 1 hour). Expired tokens: 400 "Reset link has expired."
- Invalid tokens: 400 "Invalid or expired reset link."
- On success: the user's all existing sessions/refresh tokens are invalidated.

---

### Layer B — Frontend: Public Pages

#### B1. IndexRedirect — unauthenticated visitor

`IndexRedirect` in `router.tsx`:
```ts
function IndexRedirect() {
  const role = useAppSelector((s) => s.auth.role);
  if (!role) return <Navigate to="/discover" replace />;
  return <Navigate to={getRoleRedirectPath(role)} replace />;
}
```

Verify:
- Unauthenticated user at `/` → `/discover`. ✓
- Authenticated user at `/` → role-specific home. ✓
- The redirect is `replace: true` (no history entry for `/`). ✓

Also verify `CatchAllRedirect`:
- Unauthenticated user at an unknown route → `/discover`. ✓
- Authenticated user at an unknown route → role-specific home. ✓

#### B2. DiscoverPage — geolocation + Nominatim search

Read `DiscoverPage.tsx` in full. Verify:

**Geolocation flow:**
- "Locate me" button calls `navigator.geolocation.getCurrentPosition`.
- If permission denied: shows a user-facing message (not a crash or silent failure).
  Add error handling:
  ```ts
  navigator.geolocation.getCurrentPosition(
    (pos) => { setLat(pos.coords.latitude); setLng(pos.coords.longitude); },
    (err) => {
      if (err.code === err.PERMISSION_DENIED) {
        toast.error("Location access denied. Enter your city manually.");
      }
    }
  );
  ```
- If geolocation is unavailable (HTTP context, old browser): button should not appear,
  OR clicking it shows "Location not available in your browser."
- Default location (Lisbon, Portugal): shown in the location input when no geolocation
  is available.

**Nominatim geocoding:**
- Search by city name uses `https://nominatim.openstreetmap.org/search?q=...&format=json`.
- Nominatim requires a `User-Agent` header — browser fetch without it may be rate-limited.
  Since this runs in-browser, the browser sends its own User-Agent (not the app's).
  Verify: Nominatim is not being proxied through the backend (it should be fetched
  directly from the browser — a backend proxy would require User-Agent policy setup).
- On empty results: shows "No locations found for '{query}'. Try a different city."
- On network error: shows "Location search unavailable. Try again."
- Debounce: the search should not fire on every keystroke — add 400ms debounce or fire
  only on submit/Enter.

**Studio list:**
- `useGetNearbyStudiosQuery({ lat, lng, radiusKm })` fetches nearby studios.
- Loading: skeletons shown while fetching. Verify number of skeleton items matches
  typical result count (3–5 skeletons is enough).
- Empty: "No studios found within {radiusKm} km. Try a larger radius."
- Error: "Failed to load nearby studios. Try again." with retry button.
- Each studio card: links to `/s/${studio.slug}`. ✓
- `StarRating` shown when `studio.averageRating !== null`. ✓
- `StudioMonogram` (initials fallback) shown when no `coverImageUrl`. ✓
- Studio cards are links with `aria-label`. ✓

**Portfolio feed (PortfolioFeed component):**
- Fetches `GET /api/v1/portfolio-feed` with cursor pagination.
- Initial load shows 20 items.
- "Load more" button triggers next page.
- Loading state: image grid skeleton.
- Empty state: "No portfolio images yet. Studios are warming up!"
- Each image: links to `/artist/${image.artistSlug}`. Verify the link is correct
  (NOT `/artists/${id}` — that's the authenticated route).
- Lightbox: clicking an image opens a full-screen dialog. ✓
  Verify prev/next navigation in the lightbox works (keyboard arrow keys + buttons).
  Verify lightbox closes on Escape key or clicking outside.
- Broken images (expired R2 URLs): `onError` handler hides the broken image and shows
  a placeholder tile.

#### B3. StudioPortfolioPage

The studio's public profile — the most visited page. Read `StudioPortfolioPage.tsx` in full.

Verify:
- `useDocumentMeta({ title, description, ogImage, canonical })` called. ✓
  Canonical: `https://penaearte.com/s/${slug}`. ✓
- Loading: full-page skeleton shown while `isLoading`. ✓
- Error: 404 slug → `"Studio not found"` state with a link to `/discover`. ✓
- Cover image: when null, a gradient placeholder (`StudioMonogram` or background color). ✓
- Description: rendered as `<p>` — if it contains line breaks, use `whitespace-pre-wrap`.
- Contact info: phone number rendered as `<a href="tel:...">`, Instagram as
  `<a href="https://instagram.com/...">`.
- "Book an appointment" button: only shown when `studio.showBookingCta == true`.
  When clicked → navigates to `/client-register?studioId={studio.id}` (or `/login?redirect=...`
  for returning users).
  **Bug to check:** Verify the "Book" button takes visitors to the right page.
  Unauthenticated visitors → `/client-register?studioId=...`.
  Authenticated clients → `/book`.
  The button should check `role` from Redux state and route accordingly.

- Artist cards: each links to `/artist/${artist.slug}`. ✓
  Avatar fallback (initials) when `profileImageUrl` is null. ✓
- Portfolio gallery (`Dialog` lightbox): prev/next navigation buttons. ✓
  Keyboard arrow key navigation. Verify.
  `X` button (or outside click) closes the dialog. ✓
  Broken image fallback (`ImageOff` icon). ✓
- **Review section:**
  - Loads reviews via `useGetStudioReviewsQuery({ slug })`.
  - `ReviewSection` shows "Sign in to leave a review" when `token` is null/falsy.
    **Bug to check:** What IS `token`? Read how `ReviewSection` receives `token`.
    It appears to be a JWT-style auth token. Verify that `token` is correctly obtained
    from the auth state (`useAppSelector(s => s.auth.accessToken)`) and passed down.
    If `token` is always null on `StudioPortfolioPage` (because the page doesn't read
    auth state), authenticated users will see "Sign in" instead of the review form.
  - Review form: `InteractiveStarRating` + body textarea. ✓
  - 409 on duplicate: shows "You have already left a review." ✓
  - Auto-dismiss success message after 4 seconds. ✓

#### B4. ArtistPortfolioPage

Verify the full artist portfolio experience.

- `useDocumentMeta` called with correct `canonical`. **Fix: `/artist/` not `/a/`** (see A2).
- View tracking: `useEffect(() => { void recordView(slug); }, [slug, recordView])` — fires once per slug. ✓
- Loading: skeleton. ✓
- Error (404 or invalid slug): fallback with link to `/discover`. ✓
- Avatar: large (96×96) rounded. Shows `profileImageUrl` or initials fallback. ✓
- Specialization chips: comma-split display tags. ✓
- Bio: shown when non-empty. ✓
- Instagram link: `href="https://instagram.com/{handle}"`. Strip leading `@` from handle if present.
- Hourly rate: shown as "€X/hr" when non-null.
- Rating summary: `averageRating` + `reviewCount` displayed. "Write a review" link scrolls to review section.
- Portfolio grid: masonry or uniform grid of images.
  - Click → opens lightbox (`Dialog`). ✓
  - `ZoomIn` cursor on hover. ✓
  - Lightbox: `DialogTitle` present for accessibility (see shadcn Dialog requirement). ✓
  - Prev/next navigation: verify clicking prev/next cycles through images correctly.
    **Bug to check:** Is the lightbox index managed correctly? If `currentIndex` starts
    at -1 or undefined when first opened, the prev button may be clickable on the first
    image (it should be disabled or hidden).
  - Broken image: `onError` fallback to placeholder.
- "Book with this artist" CTA → `/client-register?studioId={studio.id}` for guests,
  `/book` for authenticated clients.
  **Bug:** The studio's `id` must be included in the registration URL so the client
  is linked to the correct studio. Verify `PublicArtistResponse` includes `studioId`
  (or that the registration link uses `studioSlug` → resolves on the backend).
- Review section: same verification as B3 for review form with `token`.

#### B5. SharedDesignPage

The simplest public page. Verify:
- `isLoading`: full-screen `Loader2` spinner on black background. ✓
- Error / expired token: "This link has expired" message + "Go home" link. ✓
- Image rendered with `alt={design.title}`. ✓
- Title, studio name, expiry date shown. ✓
- "Book your own tattoo" CTA → `/s/${design.studioSlug}`. ✓
- **Missing:** `useDocumentMeta` not called. Add:
  ```tsx
  useDocumentMeta({
    title: design ? `${design.title} — Shared Design by ${design.studioName}` : "Design Preview",
  });
  ```
- **Missing:** If the image URL is expired/broken, there's no `onError` handler.
  Add an `onError` fallback showing the `ImageOff` icon.
- **Missing:** The expiry date is formatted as `new Date(design.expiresAt).toLocaleDateString()`.
  If the design has ALREADY expired but was loaded from cache, the page would still show
  the image. The backend returns 404 for expired tokens — but the frontend caches the
  response. Verify: RTK Query does NOT cache the shared design response with a long TTL.
  Add `keepUnusedDataFor: 0` to prevent stale display of expired designs.

#### B6. EmbedPage

Designed to be embedded as an `<iframe>` on third-party studio websites.

Verify:
- `isEmbedded()` detects if inside iframe (`window.self !== window.top`). ✓
- `EMBED_BASE = VITE_PUBLIC_URL ?? window.location.origin` — used in `studioPageUrl`. ✓
- Cover image: shown when `studio.coverImageUrl` is set. ✓
- Studio name, city, description shown. ✓
- "Book an Appointment" button:
  - When embedded: `window.open(studioPageUrl, "_blank")` opens studio page in new tab. ✓
  - When NOT embedded (direct URL visit): navigates to studio page in same tab. ✓
  - Only shown when `studio.showBookingCta == true`. ✓
- Artists list shown as pills with first initial. ✓
- "Powered by Pena e Artë" footer. ✓
- Loading: `EmbedSkeleton`. ✓
- Error: "Studio not found." message. ✓

**Bug to check:** The `EmbedPage` has no `<meta name="X-Frame-Options">` concerns —
but the backend must NOT send `X-Frame-Options: DENY` or `X-Frame-Options: SAMEORIGIN`
for the `/embed/*` route, or the iframe will be blocked.

Read `Program.cs` / security middleware. Verify:
- `X-Frame-Options` header is NOT set to `DENY` globally.
- For the `/embed/` path specifically: the response does NOT include `X-Frame-Options`.
- `Content-Security-Policy: frame-ancestors` should allow the studio's domain.
  Since the studio's domain is unknown at build time, use:
  `Content-Security-Policy: frame-ancestors *` for the embed route only.
  (For all OTHER routes: `frame-ancestors 'self'` to prevent clickjacking.)

**Missing:** The `EmbedPage` has no `data-testid` attributes and the test file exists.
Read `EmbedPage.test.tsx` and ensure all tested elements have the right structure.

#### B7. ClientRegisterPage

The signup flow for new clients. This is the critical conversion point.

Verify:
- Without `?studioId=`: shows "Browse studios" interstitial, NOT a broken form. ✓
- With `?studioId=`: shows the full registration form. ✓
- Already logged in: `useEffect` redirects away. ✓ (but check for flash of the form
  before the redirect — add a loading state while `existingRole` is being read).
- Form validation:
  - `firstName`: required, max 100. ✓
  - `email`: required, valid email format. ✓
  - `password`: min 8. ✓
  - `confirmPassword`: matches password. ✓
  - All errors show inline below the field with `role="alert"`. ✓
- Submit flow:
  1. `registerUser({...})` called.
  2. `.unwrap()` — if `registerUser` fails: server error shown. ✓
  3. `login({email, password})` called.
  4. `.unwrap()` — **Bug:** if `registerUser` succeeds but `login` fails, the user is
     created but not logged in. The current code would throw an unhandled error (the
     `.unwrap()` on the login mutation would throw). Fix:
     ```ts
     try {
       await registerUser({...}).unwrap();
       const { accessToken } = await login({...}).unwrap();
       dispatch(setCredentials(decodeToken(accessToken)));
       navigate(redirectTo, { replace: true });
     } catch (err) {
       // registerUser error handled via registerError state above
       // login error after successful register: show "Account created but login failed"
       if (registerError) return; // handled by serverError display
       toast.error("Account created. Please sign in manually.");
       navigate(`/login?redirect=${encodeURIComponent(redirectTo)}`);
     }
     ```
  5. After login: `navigate(redirectTo, { replace: true })`. ✓
- 429 rate limit: shows "Too many attempts. Please try again in a few minutes." ✓
- 409 duplicate email: the current code shows `registerError.data?.message ?? "Registration failed."`.
  Verify the backend returns `message: "An account with this email already exists."` on 409.
- "Already have an account? Sign in" link: `href` correctly includes `?redirect=...`
  when `redirectTo !== "/book"`. ✓
- "Registering a studio instead?" link → `/register`. ✓

**Missing:** No `lastName` field in the registration form. But the backend's
`RegisterUserCommand` may require a `lastName`. Check the backend command:
- If `lastName` is required: add it to the form.
- If `lastName` is optional: verify the form can omit it without a server error.

**Missing:** No email verification step shown after registration. The `VerifyEmailPage`
exists. Verify the registration flow sends a verification email and communicates this
to the new user:
```tsx
// In the success path (after login), if email is not yet verified, show:
toast("Check your email to verify your account. You can still book in the meantime.");
```

#### B8. StudioMapPage

`GET /map` — full-screen studio map. Read `StudioMapPage.tsx`.

Verify:
- Map library used (likely Leaflet or Mapbox). Verify the library is correctly
  configured with the right tiles.
- Studio markers render on the map.
- Clicking a marker shows a popup with studio name + link to `/s/${slug}`. ✓
- Initial viewport: centered on the user's location (if geolocation available) or
  a sensible default.
- Loading state while studios are fetched.
- Error state when map tiles fail to load.
- Mobile: full-screen map with proper touch pan/zoom.
- `[AllowAnonymous]` on the studio list endpoint used here.

---

### Layer C — Test Suite Completeness (Public Pages)

#### C1. DiscoverPage.test.tsx

Required tests:
- Renders portfolio feed section
- Renders studio search section
- "Locate me" button triggers geolocation
- Geolocation denied: shows error message
- Nominatim search: shows results list
- Nominatim search: shows "no results" on empty response
- Radius filter chips change the search radius
- Studio cards link to `/s/${slug}`
- Studio card shows average rating when non-null
- Loading skeletons shown while `isGetNearbyStudiosQuery` loading
- Error state with retry button

#### C2. StudioPortfolioPage.test.tsx

Required tests:
- Renders studio name and description
- Cover image rendered (or monogram fallback)
- Artist cards link to `/artist/${artist.slug}`
- "Book" button links to `/client-register?studioId=...` for guests
- "Book" button links to `/book` for authenticated clients
- "Book" button hidden when `showBookingCta` is false
- Portfolio lightbox opens on image click
- Lightbox prev/next navigation works
- Lightbox closes on `X` click and outside click
- Review section: shows "Sign in" when `token` is null
- Review section: shows review form when `token` is present
- Loading skeleton renders
- Error state: "Studio not found" with link to /discover
- `document.title` is set correctly

#### C3. ArtistPortfolioPage.test.tsx

Required tests:
- Renders artist name, bio, specializations
- Instagram link opens `https://instagram.com/{handle}`
- Portfolio grid renders images
- Click on image opens lightbox
- Lightbox shows artist name in DialogTitle
- Lightbox prev button disabled/hidden on first image
- Lightbox next button disabled/hidden on last image
- Lightbox keyboard prev/next navigation
- View tracking: `recordView` called on mount with artist slug
- View tracking: NOT called again on re-render (only on slug change)
- Broken image: `onError` fires and shows fallback
- "Book with this artist" button → `/client-register?studioId=...`
- Canonical URL is `/artist/${slug}` not `/a/${slug}`
- Review section shows "Sign in" when unauthenticated
- Loading skeleton renders
- Error state with link to /discover

#### C4. SharedDesignPage.test.tsx

Required tests:
- Loading state: spinner on dark background
- Error state: "This link has expired" + Go home link
- Design rendered: image, title, studio name, expiry date
- `alt` attribute set to design title
- Broken image: `onError` fallback shown
- `document.title` set to design title
- RTK Query cache: `keepUnusedDataFor: 0` — expired designs not served from cache
- "Book your own tattoo" button links to `/s/${design.studioSlug}`

#### C5. EmbedPage.test.tsx

Required tests:
- Loading: EmbedSkeleton renders
- Error: "Studio not found" when `isError`
- Cover image renders when present
- Studio name, city, description render
- "Book Appointment" button hidden when `showBookingCta` is false
- "Book Appointment" button: `window.open` called when embedded
- "Book Appointment" button: `window.location.href` set when not embedded
- Artists list renders as pills
- "Powered by Pena e Artë" footer link present
- `EMBED_BASE` uses `VITE_PUBLIC_URL` env var

#### C6. ClientRegisterPage.test.tsx

Required tests:
- Without `studioId`: shows interstitial with "Browse studios" button
- Without `studioId`: "Browse studios" links to /discover
- With `studioId`: shows registration form
- firstName required validation
- email format validation
- password min 8 chars validation
- confirmPassword must match password
- Submit calls registerUser mutation
- registerUser success: calls login mutation with same credentials
- Auto-login success: navigates to redirectTo
- registerUser 409: shows "email already exists" error
- registerUser 429: shows rate limit message
- login failure after successful register: toast + redirect to /login
- Submit button disabled during loading
- "Already have an account? Sign in" link
- Authenticated user: redirected away from this page

#### C7. ReviewSection.test.tsx

Required tests:
- Unauthenticated (`token` null): shows "Sign in to leave a review" prompt
- Sign in link has correct `?redirect=...` param
- Authenticated: shows star rating + textarea form
- Zero rating: shows validation error "Please select a star rating"
- Body under 10 chars: shows validation error
- Body over 2000 chars: rejected (if max enforced)
- Submit calls correct mutation (studio / artist / tattoo)
- Success state auto-dismisses after 4 seconds
- 409 error: shows "You have already left a review"
- Other error: shows generic failure message

#### C8. PortfolioFeed.test.tsx

Required tests:
- Renders initial image grid
- "Load more" button fetches next cursor
- Loading state shows skeletons
- Empty state: "No portfolio images yet"
- Error state with retry
- Image click opens lightbox
- Lightbox shows artist name + slug link
- Broken image: `onError` fires fallback

---

## Phase 1 Exit Condition

```
dotnet build   → 0 errors, 0 warnings
pnpm build     → 0 TypeScript errors
dotnet test    → All green
pnpm test      → All green
```

---

# PHASE 2 — POLISH TO FINISHED PRODUCT

The guest visitor has no account and no loyalty yet. They are comparing this platform
to Instagram, Google Maps, and competitor booking sites. Every second of load time,
every confusing label, every missing piece of information is a reason to leave.

Evaluate every public page as a conversion-focused product designer.

---

## P1. SEO + Meta Tags

### P1.1 Open Graph tags for all public pages

`useDocumentMeta` currently sets `document.title`. Extend it to also set OG tags:

```ts
// useDocumentMeta.ts
export function useDocumentMeta({
  title,
  description,
  ogImage,
  canonical,
}: {
  title:        string;
  description?: string;
  ogImage?:     string;
  canonical?:   string;
}) {
  useEffect(() => {
    document.title = title;

    setMeta("og:title",       title);
    setMeta("og:type",        "website");
    if (description) setMeta("og:description", description);
    if (ogImage)     setMeta("og:image",        ogImage);
    if (description) setMeta("description",     description);

    if (canonical) {
      let link = document.querySelector<HTMLLinkElement>("link[rel='canonical']");
      if (!link) {
        link = document.createElement("link");
        link.rel = "canonical";
        document.head.appendChild(link);
      }
      link.href = canonical;
    }
  }, [title, description, ogImage, canonical]);
}

function setMeta(property: string, content: string) {
  const isOg = property.startsWith("og:");
  const selector = isOg
    ? `meta[property="${property}"]`
    : `meta[name="${property}"]`;
  let tag = document.querySelector<HTMLMetaElement>(selector);
  if (!tag) {
    tag = document.createElement("meta");
    if (isOg) tag.setAttribute("property", property);
    else       tag.setAttribute("name",     property);
    document.head.appendChild(tag);
  }
  tag.content = content;
}
```

Apply to every public page:
- `DiscoverPage`: `title = "Find Tattoo Studios — Pena e Artë"`, description = "Browse the best tattoo artists near you."
- `StudioPortfolioPage`: title = `"{name} — Book a Tattoo"`, ogImage = `coverImageUrl`.
- `ArtistPortfolioPage`: title = `"{name} — Tattoo Artist"`, ogImage = first portfolio image.
- `SharedDesignPage`: title = `"{design.title} — Design Preview"`.
- `EmbedPage`: no OG tags needed (embedded context).

### P1.2 Structured data (JSON-LD) for studios and artists

Add `<script type="application/ld+json">` to studio and artist public pages for rich
search results.

For `StudioPortfolioPage`:
```tsx
useEffect(() => {
  if (!studio) return;
  const schema = {
    "@context": "https://schema.org",
    "@type": "TattooParlor",
    "name": studio.name,
    "description": studio.description,
    "url": `https://penaearte.com/s/${studio.slug}`,
    "image": studio.coverImageUrl,
    "address": { "@type": "PostalAddress", "addressLocality": studio.city },
    "aggregateRating": studio.reviewCount > 0 ? {
      "@type": "AggregateRating",
      "ratingValue": studio.averageRating,
      "reviewCount": studio.reviewCount,
    } : undefined,
  };
  const script = document.createElement("script");
  script.type = "application/ld+json";
  script.text = JSON.stringify(schema);
  document.head.appendChild(script);
  return () => { document.head.removeChild(script); };
}, [studio]);
```

For `ArtistPortfolioPage`:
```tsx
"@type": "Person",
"jobTitle": "Tattoo Artist",
"name": artist.name,
"description": artist.bio,
"url": `https://penaearte.com/artist/${artist.slug}`,
```

---

## P2. DiscoverPage Polish

### P2.1 Two-tab layout: "Nearby Studios" + "Portfolio Feed"

The `DiscoverPage` already has an `activeTab: "portfolio" | "studios"` state.
Verify that the tab switch is visually distinct (underline or pill tab) and
that the active tab is retained in the URL as `?tab=portfolio` or `?tab=studios`
so sharing the link opens the right tab.

### P2.2 Search input: Enter key triggers search

Verify that pressing Enter in the location input triggers the Nominatim geocoding search.
Currently, the user may need to click a "Search" button. If there's no "Search" button,
add one OR ensure the `keydown` event on the input triggers the search.

### P2.3 Radius filter: visual feedback

When the user changes the radius, the studio list should immediately show a loading
skeleton while the new results load. Verify RTK Query's `isFetching` flag is used
(not just `isLoading` — `isFetching` is `true` on re-fetches too).

### P2.4 Studio card: artist count + styles

The `StudioCard` currently shows: studio name, city, distance, star rating.
Add: `"X artists"` label and top 3 style tags (e.g., "Japanese, Blackwork, Realism").

Source: `studio.artistCount` from the API response. `studio.styles` (if available from
the backend) or computed from top artist specializations.

### P2.5 Empty portfolio feed: animated placeholder

When `PortfolioFeed` has no items, instead of a plain text empty state, show
a subtle animated grid of placeholder tiles to communicate what the feed will look like:
```tsx
{Array.from({ length: 6 }).map((_, i) => (
  <div key={i} className="aspect-square rounded-lg bg-muted/30 animate-pulse" />
))}
<p className="col-span-full text-center text-sm text-muted-foreground mt-2">
  Studios are warming up — check back soon.
</p>
```

---

## P3. Studio Portfolio Page Polish

### P3.1 "Book now" button — authenticated vs guest routing

The "Book" button on `StudioPortfolioPage` must route correctly:
- **Guest:** → `/client-register?studioId={studio.id}&redirect=/book`
- **Authenticated client of THIS studio:** → `/book`
- **Authenticated client of A DIFFERENT studio:** → show a message "You're already
  registered with another studio. To book here, please contact the studio directly."
  OR create a cross-tenant booking flow.
- **Authenticated artist/owner:** → show a message or hide the button.

Read the current implementation. If it doesn't handle these cases, implement them:
```tsx
const role    = useAppSelector((s) => s.auth.role);
const tenantId = useAppSelector((s) => s.auth.tenantId);

function handleBook() {
  if (!role) {
    navigate(`/client-register?studioId=${studio.id}&redirect=/book`);
    return;
  }
  if (role === Role.Client && tenantId === studio.tenantId) {
    navigate("/book");
    return;
  }
  if (role === Role.Client) {
    toast("You're registered with a different studio. Contact this studio to book.");
    return;
  }
  // Owner/Artist/Issuer visiting — don't show the book button
}
```

### P3.2 Artist cards — "from €X/hr" price label

When the artist has an `hourlyRate`, show it on their card in the studio page:
```tsx
{artist.hourlyRate && (
  <p className="text-xs text-muted-foreground mt-0.5">from €{artist.hourlyRate}/hr</p>
)}
```

### P3.3 Gallery image count badge

When the studio has portfolio images, show a count badge on the gallery section:
```tsx
<h2 className="text-sm font-semibold">
  Gallery
  {images.length > 0 && (
    <span className="ml-2 text-xs text-muted-foreground font-normal">
      {images.length} {images.length === 1 ? "photo" : "photos"}
    </span>
  )}
</h2>
```

### P3.4 "Respond to reviews" — owner response display

If the review response feature exists (`review.ownerResponse`), render it below the
review body:
```tsx
{review.ownerResponse && (
  <div className="mt-2 pl-3 border-l-2 border-border/50 text-sm text-muted-foreground">
    <p className="text-xs font-medium text-foreground/70 mb-0.5">Studio response:</p>
    <p>{review.ownerResponse}</p>
  </div>
)}
```

### P3.5 Review pagination

If a studio has > 10 reviews, paginate them. Show "Load more reviews" button.
Use RTK Query `cursor` param if the backend supports it, or page-based pagination.

---

## P4. Artist Portfolio Page Polish

### P4.1 Portfolio image styles as filter chips

When portfolio images have `style` metadata, add filter chips above the grid:
```tsx
const styles = [...new Set(images.map((img) => img.style).filter(Boolean))];

{styles.length > 1 && (
  <div className="flex flex-wrap gap-2 mb-4">
    <button onClick={() => setFilter(null)} className={cn("chip", !filter && "chip-active")}>
      All ({images.length})
    </button>
    {styles.map((style) => (
      <button key={style} onClick={() => setFilter(style)} className={cn("chip", filter === style && "chip-active")}>
        {style} ({images.filter((img) => img.style === style).length})
      </button>
    ))}
  </div>
)}
```

### P4.2 Booking CTA on every artist page

A prominent, sticky "Book with {artist.name}" button at the bottom of the artist page:
```tsx
<div className="fixed bottom-0 inset-x-0 p-4 bg-background/80 backdrop-blur-sm border-t z-30">
  <Button className="w-full max-w-md mx-auto block" onClick={handleBook}>
    Book with {artist.firstName ?? artist.name}
  </Button>
</div>
```

Where `handleBook` follows the same routing logic as P3.1.

### P4.3 "From this studio" breadcrumb

Add a "← {studioName}" link at the top of the artist page linking back to `/s/${artist.studioSlug}`:
```tsx
<Link
  to={`/s/${artist.studioSlug}`}
  className="text-xs text-muted-foreground hover:text-foreground flex items-center gap-1 mb-4"
>
  <ChevronLeft className="h-3.5 w-3.5" />
  {artist.studioName}
</Link>
```

---

## P5. Registration + Login Flow Polish

### P5.1 LastName field on ClientRegisterPage

If the backend requires `lastName` (check `RegisterUserCommand.cs`):
- Add a `lastName` field between `firstName` and `email`.
- Update the Zod schema: `lastName: z.string().min(1, "Last name is required").max(100)`.

If the backend makes `lastName` optional — still add the field as optional (it improves
the client record from the start).

### P5.2 Password strength indicator

Below the password field, show a simple strength indicator:
```tsx
function PasswordStrength({ value }: { value: string }) {
  const strength =
    value.length === 0 ? 0 :
    value.length < 8    ? 1 :
    /[A-Z]/.test(value) && /[0-9]/.test(value) && value.length >= 12 ? 3 : 2;

  const labels = ["", "Weak", "Fair", "Strong"];
  const colors = ["", "bg-destructive", "bg-amber-500", "bg-green-500"];
  if (strength === 0) return null;

  return (
    <div className="space-y-1">
      <div className="flex gap-1">
        {[1, 2, 3].map((i) => (
          <div key={i} className={cn("h-1 flex-1 rounded-full", i <= strength ? colors[strength] : "bg-muted")} />
        ))}
      </div>
      <p className="text-xs text-muted-foreground">{labels[strength]}</p>
    </div>
  );
}
```

### P5.3 Email verification banner after registration

After registering and auto-logging in, if the user's email is not yet verified, show
a persistent banner on `/book`:
```tsx
{!user?.emailVerified && (
  <div className="w-full bg-amber-500/10 border-b border-amber-500/30 px-6 py-2.5 text-xs text-amber-700 dark:text-amber-400 flex items-center gap-2">
    <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
    Check your email to verify your account. Some features may be limited until verified.
    <button className="ml-auto underline" onClick={resendVerification}>Resend email</button>
  </div>
)}
```

### P5.4 Login page: redirect param preserved

`LoginPage` accepts `?redirect=...` query param. After successful login, it navigates
to the redirect URL. Verify:
- The redirect URL is validated to be a relative path (prevent open redirect to external URLs):
  ```ts
  const safeRedirect = redirectTo?.startsWith("/") ? redirectTo : "/";
  ```
- When clicking "Sign in to leave a review" from `StudioPortfolioPage`, the `?redirect`
  includes the studio's slug path, so after login the user is returned to the studio page.

---

## P6. SharedDesignPage Polish

### P6.1 Artist attribution on shared design

The shared design page shows "By {studioName}". Add the artist's name if available:
```tsx
<p className="text-sm text-muted-foreground">
  Design by {design.artistName ?? design.studioName}
  {design.artistSlug && (
    <> ·{" "}
      <Link to={`/artist/${design.artistSlug}`} className="underline">
        View portfolio
      </Link>
    </>
  )}
</p>
```
Add `artistName` and `artistSlug` to `SharedDesignResponse` on the backend.

### P6.2 Design expiry countdown

If the share link expires in < 24 hours, show an urgency message:
```tsx
const hoursLeft = Math.floor((new Date(design.expiresAt).getTime() - Date.now()) / 3_600_000);
{hoursLeft < 24 && hoursLeft > 0 && (
  <p className="text-xs text-amber-600 dark:text-amber-400">
    This link expires in {hoursLeft} hour{hoursLeft !== 1 ? "s" : ""}
  </p>
)}
```

---

## P7. EmbedPage Polish

### P7.1 Embed preview on StudioProfilePage (owner-facing)

On the `StudioProfilePage` (owner's studio settings), add an "Embed" section that:
1. Shows the `<iframe>` code the studio owner can copy-paste onto their website.
2. Shows a live preview of the embed.

```
Embed code:
<iframe
  src="https://app.penaearte.com/embed/{slug}"
  width="400"
  height="600"
  frameborder="0"
></iframe>
```

This is an owner-facing feature (Phase 2 for studio settings), but the embed itself
is a guest-accessible page — ensure it renders correctly for all states.

### P7.2 Embed: "No artists" state

When the studio has `artists.length === 0`, the embed currently shows nothing in the
"Our artists" section. Add an empty state:
```tsx
{studio.artists.length === 0 && (
  <p className="text-xs text-muted-foreground">Artists being added soon.</p>
)}
```

---

## P8. Security Hardening (Public Routes)

### P8.1 CSP for embed route

Ensure the backend adds these headers ONLY for `/embed/*` responses:
```http
Content-Security-Policy: frame-ancestors *
X-Frame-Options: ALLOWALL
```

And for ALL OTHER routes:
```http
Content-Security-Policy: frame-ancestors 'self'
X-Frame-Options: SAMEORIGIN
```

Implement as a custom ASP.NET Core middleware that inspects the request path.

### P8.2 Rate limiting on all public endpoints

Verify `IpRateLimitingMiddleware` (or ASP.NET Core's built-in rate limiting) is
configured for all AllowAnonymous endpoints. Recommended limits:
```
GET  /api/v1/studios/nearby         30 req/min per IP
GET  /api/v1/studios/public/*       120 req/min per IP
GET  /api/v1/artists/public/*       120 req/min per IP
GET  /api/v1/portfolio-feed         60 req/min per IP
GET  /api/v1/public/design-share/*  60 req/min per IP
GET  /api/v1/reviews/*              120 req/min per IP
POST /api/v1/reviews/*              5 req/min per IP
POST /api/v1/auth/register          5 per 10 min per IP
POST /api/v1/auth/forgot-password   3 per hour per email
```

### P8.3 CORS for public endpoints

Verify CORS policy allows the embed page to be loaded from any origin (`*`) for
the public GET endpoints that the embed widget calls. Studio-internal endpoints
should use a more restrictive CORS policy.

---

## Phase 2 Exit Condition

After all polish items:

1. `pnpm test` — all green.
2. `dotnet test` — all green.
3. `pnpm build` — no TypeScript errors.
4. `dotnet build` — no warnings.
5. Self-review checklist — visit every public page as a guest and confirm:
   - Page title set? (`document.title`)
   - OG tags present? (`og:title`, `og:description`, `og:image`)
   - Canonical URL correct?
   - Loading skeleton on every page?
   - Error state with recovery on every page?
   - Broken images handled with `onError` fallback?
   - "Book" button routes guests to `/client-register?studioId=...`?
   - "Book" button routes authenticated clients to `/book`?
   - Review form shows "Sign in" for guests, form for authenticated clients?
   - `SharedDesignPage` has `keepUnusedDataFor: 0`?
   - `EmbedPage` is embeddable (no `X-Frame-Options: DENY`)?
   - CSP `frame-ancestors *` ONLY on `/embed/` routes?
   - Rate limiting on all public write endpoints?
   - Canonical URL for artist portfolio is `/artist/` not `/a/`?
   - `ClientRegisterPage` handles login failure after successful registration?
   - JSON-LD structured data present on studio and artist pages?
   - `useDocumentMeta` sets OG tags AND canonical link tag?

---

## Final Deliverable

When both phases exit cleanly, append to `docs/claude/architecture.md`:

```markdown
## Guest/Visitor QA Pass — 2026-07-01

### Bugs fixed
- [list each bug: file → root cause → fix]

### Polish implemented
- [list each item: component → what was added]

### Architecture decisions
- [any decisions made → copy to Decisions Log table]

### Deferred items
- [anything not done and why]
```
