# Overnight Master Prompt — Artist Profile "Social" Tab, Profile Page Chrome & Adjacent UI Hygiene

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact files,
> exact current code, exact target code, exact tests, exact docs to sync. Read the whole file
> before writing anything — later phases depend on decisions made in §2 and file paths verified
> in §5 of each phase.

**Date logged:** 2026-09-20
**Requested by:** Phi
**Origin:** `docs/claude/feature-spec-artist-social-tab-and-profile-chrome-2026-09-20.md` — a
UI/UX audit of a single staging screenshot (`staging.tattooos.co`, artist account, `My Portfolio`
→ `Social` tab), corrected and verified against live source, then re-verified a second time (this
document) directly against `main` at commit `27406083` before being turned into this build prompt.
Every citation below was re-read from source during that second pass — a few of the originating
spec's line numbers had drifted by the time of this pass (files move fast in this repo); where
that happened, this prompt gives the corrected numbers and code, not the spec's.
**Mode:** Fully autonomous. No user present. Run all four phases in order; do not skip ahead.
**Run with:** `claude --dangerously-skip-permissions`
**Before starting:**
```bash
git add -A && git commit -m "chore: pre-artist-social-tab-and-profile-chrome checkpoint"
git checkout -b feat/artist-social-tab-and-profile-chrome-2026-09-20
```

---

## 1. What this is

The artist's own **Social** tab is a read-only view dressed as an editable one, with no
explanation and no way forward: an artist who opens it sees a pitch with no button, and four
bordered rows that each show `—` with no action. The cause is a deliberate permission decision
(every connect/verify endpoint is `OwnerOnly`) implemented correctly on the backend and the
button gating, but never given a user-visible explanation.

Wrapped around that, the page renders its **own** sticky `<header>` and `min-h-screen` root
inside a layout that already provides both — a leftover from before the sidebar navigation
shipped (`73c63015`, `feat/sidebar-navigation-2026-09-20`, already merged to `main`). That header
sits at `z-10` under the app header's `z-20`, so on scroll the page's own **← Artists / Edit /
Stop working as an artist** toolbar is pinned *behind* the app header, and the nested
`min-h-screen` root gives the document extra height it doesn't need.

Four workstreams, all four approved to build tonight (every §6 decision in the originating spec
resolved to its recommendation — see §2):

| # | Workstream | Type | Decision behind it |
|---|---|---|---|
| A | Page chrome: remove the nested header/`min-h-screen`, real breadcrumb/title, heading levels | Frontend bug fix | none needed |
| B | Social tab rebuild: one connection-row pattern, read-only notice, state matrix, copy, icons, a11y | Frontend | D-2 (brand icons) |
| C | Tab state in the URL + post-OAuth landing on the right tab | Frontend | none needed |
| D | Who may connect/verify: artist self-service for their own profile | Backend + frontend | D-1 (auth change) |
| E | Adjacent nav/header hygiene + contrast pass | Frontend | D-3, D-4 |

Build in this order — **PR-1 (A+C) → PR-2 (B) → PR-3 (D) → PR-4 (E)** — and commit each phase
separately (§14 gives the exact commit messages). PR-1 goes first because F-03 (below) can hide
the Edit button for every artist and owner using this page today, independent of everything else.

---

## 2. Decisions — already made, implement as specified, do not re-litigate

All four decisions from the originating spec's §6 are **approved as recommended**, effective
immediately for this build:

- **D-1 = Option A.** Artists may connect, verify, edit the handle of, and disconnect **their
  own** social accounts; owners/admins keep today's access to every artist in their studio.
  Implemented by moving five `OwnerOnly` policies to `ArtistAndAbove` **plus** a handler-level
  ownership guard (§Phase 3). D-1's Option B (owners may only edit the typed handle, never run
  OAuth/verification, for an artist other than themselves) is explicitly **not** built tonight —
  it is a documented follow-up (see spec §15.5), not a silent partial implementation.
- **D-2 = Option A.** Brand icons ship as a new `shared/components/icons/brand/` set of inline
  SVG components (Instagram, TikTok, Facebook, X, YouTube), monochrome via `currentColor`, one
  `size` prop, `aria-hidden`. No new npm dependency.
  **Flag, do not silently resolve:** nobody has confirmed each platform's current logo-usage
  terms — the existing `socialPlatforms.ts` header comment already says this is unresolved, and
  it still is. Build the components and ship them, but the exact glyph geometry used must come
  from each platform's official current brand-guideline SVG (fetch/verify against each
  platform's own developer/brand site at build time, not from memory), and the PR description
  must say explicitly "logo geometry taken from platform's current published brand guidelines
  as of {date}; a human should re-confirm usage terms before this reaches production traffic
  at scale." This is a real, unresolved legal/licensing question this prompt cannot close on its
  own — say so, don't paper over it.
- **D-3 = approved as recommended.** Tab `Schedule` (value `"hours"`, `ArtistDetailPage.tsx`)
  renamed to **`Availability`** in its visible label only — the `value="hours"` key is untouched
  (URL stability, Phase 1). Sidebar item **"Reports About Me"** renamed to **`Conduct Reports`**
  (matches the existing route `/conduct-reports` already used in `artistNavSections.tsx:10`).
- **D-4 = approved as recommended.** Any contrast fix that survives §11's measurement script is
  applied at the shared design-token level (once, with a before/after screenshot set), not
  per-component. Phase 4 only *runs* the measurement and reports it — see Phase 4's explicit
  scope note on why token changes are not applied blind tonight.

**Also newly confirmed this pass** (not decisions — facts pinned down by reading live source a
second time, listed here so the phases below don't re-derive them):

- `ArtistResponse` (`Pena_e_Arte.Contracts/Responses/ArtistResponse.cs`) carries `StudioId` only,
  no studio name. Per the spec's own fallback: **the subtitle omits the studio name and reads
  "Artist" only** — do not add a fetch for this alone (Phase 1, A-2).
- `UpdateSocialHandleValidator` requires `RuleFor(x => x.Handle).NotEmpty()` — **the API does not
  support clearing a handle today.** The "Remove handle" secondary action the spec's §8 state
  matrix left conditional is **omitted** (Phase 2, B-1/B-4) — not built as a stretch, not
  silently added.
- Neither `artistTour.ts` nor `ownerTour.ts` has any step that targets anything this prompt
  touches — both files were read in full. Every step in both files targets a `data-tour`
  attribute on a **sidebar nav item** (`artist-schedule-nav`, `artist-conduct-reports-nav`,
  `owner-studio-hours-card`, etc.), never the `ArtistDetailPage` header, its tabs, or the Social
  tab's contents, and `ArtistDetailPage.tsx` itself contains zero `data-tour` attributes. **No
  onboarding-tour file needs a change for this prompt.** State this explicitly in the PR
  description per CLAUDE.md rule #7's "say why, not silently skip" requirement — don't leave the
  question unaddressed.
- `HelpMenu`, `MessagesNavBadge`, `NotificationBell`, and `UserMenu` **already** expose accessible
  names (`aria-label`s on all four, confirmed by reading each file) — the spec's E-3 concern
  about missing accessible names does not hold. What's left of E-3 (distinguishing the feedback
  icon from the Messages icon) is downgraded to optional polish, not a defect — see Phase 4.
- `IInstagramStateSigner` **and** the parallel `ISocialOAuthStateSigner` (generic social OAuth,
  `Pena_e_Arte.Domain/Interfaces/ISocialOAuthStateSigner.cs`) both sign only their payload
  (artistId; or subjectType|subjectId|platform) plus an HMAC — **neither has an expiry or nonce.**
  The spec only flagged the Instagram one; this pass confirms the generic social signer has the
  identical gap. Phase 3's hardening item (D-1f) covers **both** signers, not just Instagram's.
- **New finding, not in the originating spec:** `GetSocialConnectUrlQuery` and
  `DisconnectSocialAccountCommand` both explicitly block `SubjectType.Artist` +
  `Platform.Instagram` with a `BusinessRuleViolationException` telling the caller to use the
  dedicated Instagram flow instead. `RequestSocialVerificationCodeCommand`,
  `VerifySocialBioCodeCommand`, and `UpdateSocialHandleCommand` do **not** have this same guard.
  Today this is harmless — the frontend never sends `platform=Instagram` through the generic
  social path for an artist subject (`SocialLinksCard`'s `platforms` prop excludes it,
  `ArtistDetailPage.tsx:855`). But Phase 3 loosens three of those five endpoints from `OwnerOnly`
  to `ArtistAndAbove`, which makes this gap reachable by a direct API call (not through any UI
  this app ships). Phase 3 closes it for consistency — see D-1b.

---

## 3. Constraints (identical to every prior overnight prompt in this repo)

- No new npm or NuGet packages. Brand icons (D-2) are hand-written inline SVG, not a package.
- No `useEffect` for data fetching. Approved exceptions: resize, keyboard, outside-click,
  scroll-to, clipboard, timer side-effects, browser API calls in event handlers. The existing
  `useDocumentMeta` hook's `useEffect` (imperative DOM/head mutation, not data fetching) and the
  existing OAuth-toast `useEffect` (reading `searchParams`, not fetching) are both pre-existing
  and approved patterns — keep using them, don't refactor them away.
- TypeScript strict mode. No `any`. No default exports on components.
- No business logic in endpoints — MediatR only. Every new/changed command ships a
  FluentValidation validator.
- Tenant isolation via EF Core global query filters everywhere except the one already-approved
  `IgnoreQueryFilters()` usage this prompt touches indirectly (`GetPublicArtistQuery` — read-only
  reference, not modified by this prompt).
- Every endpoint keeps `.RequireAuthorization()` with the correct policy — Phase 3 changes which
  named policy five endpoints use, but none becomes unauthenticated, and no new
  `AllowAnonymous` endpoint is introduced anywhere in this prompt.
- Never log PII. Any new logging this prompt adds (Phase 3's audit entries) logs `platform`,
  `artistId`, `tenant_id`, `user_id`, `request_id`, and outcome only — never a handle, never a name.
- Structured logs only (Serilog). No `Console.WriteLine`/`console.log` in production paths.
- Every backend change ships unit tests; the auth change in Phase 3 additionally ships
  integration tests against a real database (see that phase's test section — unit tests alone
  do not exercise the real DI container or the global query filter, and this repo has already
  been bitten by that gap once).
- Every frontend change ships component tests covering loading/error/empty/read-only states at
  minimum.
- **Do not build blind.** If, while implementing any phase, you discover an open product/business
  question this prompt hasn't already resolved, stop building that specific sub-item, note it in
  the final PR description under "not built — needs a decision," and move on. This prompt has
  pre-decided every ambiguity the originating spec raised (§2 above) — if you find a genuinely
  new one, treat it the same way; don't guess.

---

# PHASE 1 (PR-1) — Page Chrome + URL-Backed Tabs

No product decision blocks this phase. Ship it first.

## 1.1 Root cause, confirmed against source this pass

`frontend/src/features/artists/components/ArtistDetailPage.tsx`, current code (loading/error
branches at lines 358–384, the real page at 385–428):

```tsx
  if (isLoading) {
    return (
      <div className="min-h-screen bg-background">
        <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
          <Skeleton className="h-8 w-24" />
        </header>
        <main className="max-w-lg mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-14 w-14 rounded-full" />
          <Skeleton className="h-6 w-48" />
          <Skeleton className="h-24 w-full" />
        </main>
      </div>
    );
  }

  if (isError || !artist) {
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-destructive-text">Artist not found.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate("/artists")}>
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back to Artists
        </Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button variant="ghost" size="sm" onClick={() => navigate("/artists")} className="gap-1.5">
          <ArrowLeft className="h-4 w-4" />
          Artists
        </Button>

        {(canManage || isOwnProfile) && !isEditing && (
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
            {canManage && (
              <Button variant="outline" size="sm" onClick={() => setDeleteOpen(true)}
                      className="gap-1.5 text-destructive-text hover:text-destructive-text">
                <Trash2 className="h-3.5 w-3.5" />
                {isOwnProfile ? "Stop working as an artist" : "Delete"}
              </Button>
            )}
          </div>
        )}

        {isEditing && (
          <Button variant="ghost" size="sm" onClick={() => setIsEditing(false)} disabled={isSaving}>
            Cancel
          </Button>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-8 space-y-6">
        <div className="flex items-center gap-4">
          <Avatar className="h-14 w-14 text-base">
            <AvatarFallback>{getInitials(artist.firstName, artist.lastName)}</AvatarFallback>
          </Avatar>
          <div>
            <h1 className="text-lg font-semibold leading-tight">
```

`ArtistLayout.tsx` (lines 54–91) already renders its own header at `z-20`, and — a detail the
originating spec didn't call out — **three banner components before that header**, which is why
the nested `min-h-screen bg-background` in the page is doubly wrong (it's not just a duplicate
header, it's a duplicate full-viewport root sitting below content the layout already pushed down):

```tsx
  return (
    <div className="min-h-screen flex flex-col bg-background">
      <SuspensionBanner role="artist" />
      <ReadOnlyBanner />
      <PlanLimitBanner />
      <header className="flex items-center gap-2 px-6 h-14 border-b bg-background sticky top-0 z-20">
        <PenLine className="h-5 w-5" />
        <span className="font-semibold tracking-tight">TattooOS</span>
        <NavDrawer sections={navSections} title="TattooOS" open={navOpen} onOpenChange={setNavOpen} revealTourId={revealTourId} />
        <div className="ml-auto flex items-center gap-3">
          <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => setFeedbackOpen(true)}
                  title="Send feedback" aria-label="Send feedback">
            <MessageSquareMore className="h-4 w-4" />
          </Button>
          <HelpMenu onBeforeTourStep={onBeforeTourStep} />
          <MessagesNavBadge />
          <NotificationBell />
          <UserMenu onLogout={handleLogout} />
        </div>
      </header>
      <div className="flex flex-1 min-h-0">
        <AppSidebar sections={navSections} revealTourId={revealTourId} />
        <div className="flex-1 min-w-0"><Outlet /></div>
      </div>
      <FeedbackDialog open={feedbackOpen} onOpenChange={setFeedbackOpen} />
    </div>
  );
```

`OwnerLayout.tsx` (around line 231) has the identical `min-h-screen flex flex-col` root, the same
`z-20` sticky header, plus a fourth banner (`SoloStudioPublishBanner`) and, for an owner with an
artist profile, an `ArtistModeSwitcher` in the header. The owner reaches `ArtistDetailPage` at the
same route (`/artists/:id`), so this fix must be correct rendered under **both** layouts.

Net effect: the page's own header (`z-10`, sticky) sits *underneath* the layout's header
(`z-20`, sticky) once the page scrolls past the point both would occupy — the page's Edit/←
Artists/Stop-working controls scroll up and disappear behind the app header instead of staying
visible, and the nested `min-h-screen` adds height the layout doesn't need.

## 1.2 Target code

**Delete** both nested headers (lines 358–384's `<header>` inside the loading branch, and
385–428's `<header>` inside the main return) and every `min-h-screen bg-background` wrapper in
this file's three return branches (loading, error, main). Nothing in `ArtistDetailPage.tsx` may
be `position: sticky` or `top-0` after this change — that positioning belongs to the layout alone.

Replace the deleted header with an in-page title row, still inside `<main className="max-w-2xl
mx-auto px-4 py-8 space-y-6">`:

```tsx
  return (
    <main className="max-w-2xl mx-auto px-4 py-8 space-y-6">
      {!isOwnProfile && (
        <nav aria-label="Breadcrumb" className="text-sm text-muted-foreground">
          <Link to="/artists" className="hover:text-foreground transition-colors">Artists</Link>
          <span className="mx-1.5" aria-hidden="true">/</span>
          <span className="text-foreground">{artist.firstName} {artist.lastName}</span>
        </nav>
      )}

      <div className="flex items-center justify-between gap-4">
        <div className="flex items-center gap-4 min-w-0">
          <Avatar className="h-14 w-14 text-base shrink-0">
            <AvatarFallback>{getInitials(artist.firstName, artist.lastName)}</AvatarFallback>
          </Avatar>
          <div className="min-w-0">
            <h1 className="text-lg font-semibold leading-tight truncate">
              {artist.firstName} {artist.lastName}
            </h1>
            <p className="text-sm text-muted-foreground">Artist</p>
          </div>
        </div>

        {isEditing ? (
          <Button variant="ghost" size="sm" onClick={() => setIsEditing(false)} disabled={isSaving}>
            Cancel
          </Button>
        ) : (canManage || isOwnProfile) && (
          <div className="flex items-center gap-2 shrink-0">
            <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
            {canManage && (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="outline" size="icon" className="h-8 w-8" aria-label="More actions">
                    <MoreHorizontal className="h-4 w-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem
                    onSelect={() => setDeleteOpen(true)}
                    className="text-destructive-text focus:text-destructive-text"
                  >
                    <Trash2 className="h-3.5 w-3.5 mr-2" />
                    {isOwnProfile ? "Stop working as an artist" : "Delete"}
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            )}
          </div>
        )}
      </div>
```

Notes on this target:

- `isOwnProfile` is computed later in the current file (line 224, `const isOwnProfile =
  isArtistRole && artist?.userId != null && artist.userId === currentUserId`) — **move this
  computation above `useDocumentMeta`** (§1.3) since both the breadcrumb-hiding logic above and
  the title now depend on it before it's otherwise needed.
- The subtitle is **"Artist"**, not "Artist · {studio name}" — `ArtistResponse` carries no studio
  name (confirmed §2), and this prompt does not add a fetch to get one. Do not invent a studio
  name from context that happens to be available on `OwnerLayout` (e.g. via its own studio query)
  — that would make the page behave differently depending on which layout rendered it, which is
  exactly the kind of inconsistency this phase is fixing, not introducing.
- Use whatever `DropdownMenu`/`DropdownMenuTrigger`/`DropdownMenuContent`/`DropdownMenuItem`
  primitives the codebase already has under `shared/components/ui/` (the same family already
  imported for other menus in this codebase, e.g. `UserMenu.tsx`) — do not add a new dropdown
  primitive.
- Find `Link` and `MoreHorizontal` imports needed (`react-router-dom`, `lucide-react`) — both are
  already dependencies used elsewhere in this file/codebase.
- The existing delete-confirmation `Dialog` (unchanged, still triggered by `setDeleteOpen(true)`)
  stays exactly as-is; only its trigger moved into the `⋯` menu.
- Update the two other return branches (loading skeleton, not-found) to match — no nested
  `<header>`, and the not-found branch's "Back to Artists" button behavior is unchanged, just no
  longer wrapped in an extra `min-h-screen` div.

## 1.3 Heading levels + document title

Two `<h3 className="text-sm font-semibold mb-2">` at lines 850 and 855 (Instagram / Other
platforms headings inside the Social tab) become `<h2>` with the same visual classes — the page
title is now the only `<h1>`, so these tab-section headings are the correct next level. The edit
form's `<h2>Edit Artist</h2>` (unchanged) stays `<h2>`.

`useDocumentMeta` is already wired at line 187 — this is not a new mechanism, it needs a small
edit. Current code:

```tsx
  useDocumentMeta({
    title: artist
      ? `${artist.firstName} ${artist.lastName} — Artists — TattooOS`
      : "Artists — TattooOS",
    canonical: `/artists/${id ?? ""}`,
  });
```

This always says "— Artists —", even when the artist is viewing their own profile through *My
Portfolio*, where there is no "Artists" list they came from. Target:

```tsx
  const isOwnProfile = isArtistRole && artist?.userId != null && artist.userId === currentUserId;

  useDocumentMeta({
    title: artist
      ? isOwnProfile
        ? `${artist.firstName} ${artist.lastName} — TattooOS`
        : `${artist.firstName} ${artist.lastName} — Artists — TattooOS`
      : "Artists — TattooOS",
    canonical: `/artists/${id ?? ""}`,
  });
```

This requires moving the `isOwnProfile` declaration (currently line 224) above this hook call
(currently line 187) — do this as a straight reorder, no logic change to `isOwnProfile` itself.
Everything between the old and new position of this line that depends on `isOwnProfile` (line
224 onward) is unaffected since it's still declared before its first use there.

## 1.4 URL-backed tabs + OAuth landing (Workstream C)

Current tab markup (line ~535):

```tsx
          <Tabs defaultValue="profile">
            <TabsList className="w-full">
              <TabsTrigger value="profile"    className="flex-1">Profile</TabsTrigger>
              <TabsTrigger value="portfolio"  className="flex-1">Portfolio</TabsTrigger>
              <TabsTrigger value="hours"      className="flex-1">Schedule</TabsTrigger>
              <TabsTrigger value="bookings"   className="flex-1">Bookings</TabsTrigger>
              <TabsTrigger value="designs"    className="flex-1">Designs</TabsTrigger>
              <TabsTrigger value="social"     className="flex-1">Social</TabsTrigger>
            </TabsList>
```

Make it controlled, reading/writing `?tab=`:

```tsx
const VALID_TABS = ["profile", "portfolio", "hours", "bookings", "designs", "social"] as const;
type ArtistDetailTab = (typeof VALID_TABS)[number];

function isValidTab(value: string | null): value is ArtistDetailTab {
  return value !== null && (VALID_TABS as readonly string[]).includes(value);
}
```

```tsx
  const rawTab = searchParams.get("tab");
  const activeTab: ArtistDetailTab = isValidTab(rawTab) ? rawTab : "profile";

  function handleTabChange(next: string): void {
    const params = new URLSearchParams(searchParams);
    params.set("tab", next);
    setSearchParams(params, { replace: true });
  }
```

```tsx
          <Tabs value={activeTab} onValueChange={handleTabChange}>
```

(keep every `TabsTrigger`/`TabsContent` value unchanged — only the `Tabs` root becomes
controlled.) This needs `setSearchParams` added to the existing
`const [searchParams] = useSearchParams();` destructure (line 179) → `const [searchParams,
setSearchParams] = useSearchParams();`.

The existing OAuth-toast effect (lines 211–222 today):

```tsx
  useEffect(() => {
    const ig = searchParams.get("instagram");
    if (ig === "connected") toast.success("Instagram connected successfully!");
    if (ig === "error")     toast.error("Instagram connection failed. Please try again.");
    if (ig === "denied")    toast.info("Instagram connection cancelled.");

    const social = searchParams.get("social");
    const platform = searchParams.get("platform");
    if (social === "connected") toast.success(`${platform ?? "Account"} connected successfully!`);
    if (social === "error")     toast.error(`${platform ?? "Account"} connection failed. Please try again.`);
    if (social === "denied")    toast.info(`${platform ?? "Account"} connection cancelled.`);
  }, [searchParams]);
```

fires the toast correctly but never routes to the Social tab and never removes the params, so a
refresh re-toasts forever. Target:

```tsx
  useEffect(() => {
    const ig = searchParams.get("instagram");
    const social = searchParams.get("social");
    const platform = searchParams.get("platform");

    if (ig === null && social === null) return;

    if (ig === "connected") toast.success("Instagram connected successfully!");
    if (ig === "error")     toast.error("Instagram connection failed. Please try again.");
    if (ig === "denied")    toast.info("Instagram connection cancelled.");
    if (social === "connected") toast.success(`${platform ?? "Account"} connected successfully!`);
    if (social === "error")     toast.error(`${platform ?? "Account"} connection failed. Please try again.`);
    if (social === "denied")    toast.info(`${platform ?? "Account"} connection cancelled.`);

    const params = new URLSearchParams(searchParams);
    params.set("tab", "social");
    params.delete("instagram");
    params.delete("social");
    params.delete("platform");
    setSearchParams(params, { replace: true });
    // Only ever runs off the params present on the OAuth-redirect landing — deliberately
    // NOT re-run when the user later navigates tabs normally, hence the eslint-disable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
```

**Backend redirect fix, same phase.** `InstagramEndpoints.cs`'s callback (lines 77–102 today):

```csharp
    private static async Task<IResult> HandleCallback(
        string? code, string? state, string? error,
        ISender mediator, IInstagramStateSigner stateSigner, IAppSettings appSettings,
        CancellationToken ct)
    {
        if (error is not null || code is null || state is null)
            return Results.Redirect($"{appSettings.BaseUrl}/artists?instagram=denied");

        if (!stateSigner.TryValidate(state, out Guid artistId))
            return Results.BadRequest("Invalid state parameter.");
        ...
```

Instagram's own OAuth flow echoes the original `state` back even on denial (only `code` is
absent, not `state`), so the `denied` branch above discards a decodable `state` and sends the
artist to `/artists` — a route the artist role may not usefully land on. Confirmed by reading
`IInstagramStateSigner.TryValidate`'s signature: it only needs `state`, nothing else. Target —
attempt to decode `state` even on the denied/missing-code path, and only fall back to the bare
`/artists` redirect when `state` truly can't be decoded:

```csharp
    private static async Task<IResult> HandleCallback(
        string? code, string? state, string? error,
        ISender mediator, IInstagramStateSigner stateSigner, IAppSettings appSettings,
        CancellationToken ct)
    {
        if (error is not null || code is null)
        {
            if (state is not null && stateSigner.TryValidate(state, out Guid deniedArtistId))
                return Results.Redirect($"{appSettings.BaseUrl}/artists/{deniedArtistId}?instagram=denied");

            return Results.Redirect($"{appSettings.BaseUrl}/artists?instagram=denied");
        }

        if (state is null || !stateSigner.TryValidate(state, out Guid artistId))
            return Results.BadRequest("Invalid state parameter.");
        ...
```

Apply the identical fix to `SocialEndpoints.cs`'s `HandleCallback` (lines 126–155 today) using
`ISocialOAuthStateSigner.TryValidate`, redirecting to the same `basePath` computation the
`connected`/`error` branches already use (`subjectType == Studio ? "studios/me" :
"artists/{subjectId}"`), so a denied studio-level connection still lands on `/studios/me` rather
than falling back to `/artists`.

## 1.5 Tab strip responsiveness

Below `sm` (640px), `TabsList` (`className="w-full"`, six `flex-1` triggers) will crush or
overflow. Change to:

```tsx
            <TabsList className="w-full overflow-x-auto sm:overflow-x-visible flex sm:grid sm:grid-cols-6 gap-1 sm:gap-0 scroll-smooth">
              <TabsTrigger value="profile"    className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Profile</TabsTrigger>
              <TabsTrigger value="portfolio"  className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Portfolio</TabsTrigger>
              <TabsTrigger value="hours"      className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Availability</TabsTrigger>
              <TabsTrigger value="bookings"   className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Bookings</TabsTrigger>
              <TabsTrigger value="designs"    className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Designs</TabsTrigger>
              <TabsTrigger value="social"     className="flex-none sm:flex-1 whitespace-nowrap min-h-10">Social</TabsTrigger>
            </TabsList>
```

(the `Schedule` → `Availability` label change is D-3, folded in here since it's the same line —
see §2.) Add a `ref` on the active trigger and `scrollIntoView({ inline: "center", block:
"nearest", behavior: "smooth" })` on `activeTab` change so the active tab is never scrolled out of
view — check whatever the codebase's existing `Tabs`/`TabsTrigger` wrapper (`shared/components/
ui/tabs.tsx`, read it first) already exposes for refs before adding new plumbing.

## 1.6 Tests (Phase 1)

**Frontend component tests** (`ArtistDetailPage.test.tsx` or wherever this page's existing tests
live — find them first, extend rather than duplicate):
- Heading outline: exactly one `h1`, both Social-tab section headings render as `h2`, no `h3`
  appears before an `h2` anywhere on the page.
- `document.title` is `"{name} — TattooOS"` when `isOwnProfile` is true, `"{name} — Artists —
  TattooOS"` otherwise.
- Breadcrumb renders for an owner viewing an artist, is absent for `isOwnProfile`.
- `?tab=social` on mount renders the Social tab; an invalid `?tab=nonsense` falls back to Profile.
- The OAuth-toast effect: `?instagram=connected` toasts once, switches to the Social tab, and the
  resulting URL has no `instagram`/`social`/`platform` params (assert via the router's location,
  not just that `setSearchParams` was called).
- `⋯` menu: opens on click/Enter/Space, closes on Escape and returns focus to its trigger
  (Radix's own `DropdownMenu` behavior — assert it, don't reimplement it).

**Backend unit test** (`Pena_e_Arte.UnitTests`, new or extended Instagram-endpoint test file):
- `HandleCallback` denial path: given a decodable `state` and no `code`, redirects to
  `/artists/{artistId}?instagram=denied`; given an undecodable/missing `state`, redirects to the
  bare `/artists?instagram=denied`. Mirror both cases for `SocialEndpoints.HandleCallback`.

**E2E (Playwright, new spec — none of the four existing `frontend/e2e/*.spec.ts` files touch this
page today):**
- At scrollTop 0 and scrolled to the bottom, under both `ArtistLayout` (logged in as the artist)
  and `OwnerLayout` (logged in as owner viewing an artist): exactly one horizontal header rule is
  visible, and Edit is visible and clickable.
- `/artists/{id}?tab=social` deep link lands on Social; refresh keeps it there.
- At 375px width: all six tabs are reachable, the active tab is never clipped, and there is no
  page-level horizontal scroll.

## 1.7 Help sync (CLAUDE.md rule #7 — mandatory in this phase)

This phase's user-visible changes are: the page's layout chrome (breadcrumb, `⋯` menu replacing a
visible Delete/Stop-working button, tab position surviving refresh) and one tab label rename
(`Schedule` → `Availability`).

1. **`frontend/src/features/help/helpContent.ts`** — grep the file for any article mentioning
   "Stop working as an artist" or describing the artist-profile toolbar as a row of buttons next
   to "Artists" (search before assuming none exists — this codebase's Help content is large and
   this exact toolbar has almost certainly been documented before, e.g. in whatever article
   documents the owner-artist-mode switch). If found, update the step describing where that
   action lives to say "the `⋯` menu next to Edit" instead of a visible button. If no article
   currently documents that toolbar location precisely enough to be affected, say so in the PR
   description rather than skipping the question.
2. Any Help content or manual text that says "Schedule tab" in the context of an *artist's own
   profile* working-hours tab (not the studio-level "Studio hours" card, which is a different
   feature and keeps its own name) must say **Availability** instead — search
   `helpContent.ts` and `frontend/public/user-manual/index.html` for "Schedule" near "artist
   profile"/"working hours"/"My Portfolio" context and update precisely those, leaving every
   other "Schedule" reference (the sidebar nav item, the calendar page) untouched.
3. Onboarding tours: **no change** — confirmed in §2, cite that confirmation in the PR
   description rather than silently omitting a tours section.

---

# PHASE 2 (PR-2) — Social Tab Rebuild

Ships under **D-1 = C behavior today** (current `OwnerOnly` policies still in force) — Phase 3
loosens the policies afterward, and this phase's UI must behave correctly both before and after
that change without a second rewrite, since `canManage`/`isOwnProfile` gating is already the
mechanism both phases share.

## 2.1 Current code (re-verified this pass)

`InstagramTab.tsx` (full file, 197 lines) and `SocialLinksCard.tsx` (full file, 259 lines) were
both re-read in full this pass; every citation in the originating spec's §3.3 (line numbers,
copy, structure) checks out against current `main` with only minor line-number drift (the spec's
`InstagramTab.tsx:94-111` empty-state block is at lines 94–110 today; `SocialLinksCard.tsx:149-
212` main render is at 149–211). Do not re-quote them here in full — read the files directly
before editing; the structure described in the originating spec's §3.3 is accurate.

Two facts worth restating because they change what Phase 2 must build:

- `socialPlatforms.ts` currently exports `SOCIAL_PLATFORM_ICON: Record<string, LucideIcon>` —
  `AtSign`/`Music2`/`Globe`/`Hash`/`Video`. `Hash` for X is the worst offender (a hashtag glyph
  standing in for a wordmark) and must not ship under any option.
- `SocialLinksCard`'s `canManage` prop already defaults to `true` (for callers like Studio
  Settings that are Owner-gated at the route level) and is passed `canManage={canManage}` (the
  `usePermission(Role.Owner)` result) from `ArtistDetailPage.tsx:858` today. `InstagramTab`
  receives the same `canManage` value as its `canConnect` prop (`ArtistDetailPage.tsx:852`).
  Phase 2 does not change either prop's *source* — it changes what gets built with it, see §2.4.

## 2.2 Brand icons (D-2 = A)

Create `frontend/src/shared/components/icons/brand/`:

```
InstagramIcon.tsx
TikTokIcon.tsx
FacebookIcon.tsx
XIcon.tsx
YouTubeIcon.tsx
index.ts
```

Each component takes the same shape:

```tsx
interface BrandIconProps {
  className?: string;
  size?: number;
}

export function InstagramIcon({ className, size = 20 }: BrandIconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="currentColor"
      aria-hidden="true"
      className={className}
    >
      {/* path data from Instagram's current published brand/developer glyph — verify against
          the platform's own current brand guidelines page before shipping, per §2's D-2 flag */}
    </svg>
  );
}
```

Fill in each platform's actual current glyph path data — do not invent placeholder geometry, and
do not reuse the previous Lucide icon's bounding assumptions. `index.ts` re-exports all five.

`socialPlatforms.ts` target:

```ts
import type { ComponentType } from "react";
import { InstagramIcon, TikTokIcon, FacebookIcon, XIcon, YouTubeIcon } from "@/shared/components/icons/brand";
import { AtSign } from "lucide-react";

export type SocialIcon = ComponentType<{ className?: string; size?: number }>;

export const SOCIAL_PLATFORM_ICON: Record<string, SocialIcon> = {
  Instagram: InstagramIcon,
  TikTok:    TikTokIcon,
  Facebook:  FacebookIcon,
  X:         XIcon,
  YouTube:   YouTubeIcon,
};

export const SOCIAL_PLATFORM_FALLBACK_ICON: SocialIcon = AtSign;

export const SOCIAL_PLATFORM_LABEL: Record<string, string> = {
  Instagram: "Instagram",
  TikTok:    "TikTok",
  Facebook:  "Facebook",
  X:         "X",
  YouTube:   "YouTube",
};
```

`SOCIAL_PLATFORM_FALLBACK_ICON` exists for D-2's own fallback path (an unrecognized platform
string, defensive only) — every caller of `SOCIAL_PLATFORM_ICON[platform]` should fall back to it
rather than crashing on an unexpected key, matching `ArtistPortfolioPage.tsx`'s existing `??
AtSign` pattern (line ~618) — update that call site's import too, since `AtSign` there was
standing in for a missing brand icon, which no longer applies.

## 2.3 `ConnectionRow` — one pattern for all five platforms

New file `frontend/src/features/social/components/ConnectionRow.tsx`. Anatomy (single `Card`,
`p-4`, `flex items-center gap-3 flex-wrap`):

```tsx
interface ConnectionRowProps {
  icon: SocialIcon;
  label: string;
  handle: string | null;
  isVerified: boolean;
  /** null = read-only viewer (no action slot at all, secondary line explains why). */
  action: ConnectionRowAction | null;
  secondaryOverride?: string;
}

type ConnectionRowAction =
  | { kind: "connect"; onClick: () => void; busy?: boolean }
  | { kind: "get-code"; onClick: () => void; disabled: boolean; busy?: boolean }
  | { kind: "verify"; onClick: () => void; busy?: boolean }
  | { kind: "disconnect"; onClick: () => void }
  | { kind: "unavailable" }
  | { kind: "handle-input"; value: string; onChange: (v: string) => void; onBlur: () => void; error?: string };
```

Render the label + a `Badge`-style status text (`Verified` / `Handle added` / `Not linked` /
`Unavailable` — never colour alone, per §8's a11y requirement) + the icon at a fixed `h-5 w-5`
for every platform (fixes the Instagram-was-bigger inconsistency: `InstagramTab.tsx`'s empty
state currently renders its `AtSign` at `h-10 w-10`, three times the SocialLinksCard rows' `h-5
w-5` — the empty state disappears in this rebuild, see §2.4, which is what actually resolves
this). Wire `handle-input` to render exactly the existing `Input` pattern from
`SocialLinksCard.tsx` (the `@` prefix adornment, `autoCapitalize="off"`, `autoCorrect="off"`,
`spellCheck={false}`, `aria-label={`${label} handle`}`), just relocated into this shared
component so both `SocialLinksCard` and the Instagram row use it identically.

Wrap the whole section in `<ul aria-label="Connected accounts">` / `<li>` per row, inside
`<section aria-labelledby="social-heading">` with `<h2 id="social-heading">Connected
accounts</h2>` — this satisfies the heading-outline requirement from Phase 1 (§1.3) and the
`aria-labelledby` region pattern from the originating spec's §11.

## 2.4 `ArtistSocialTab` — the extraction + rebuild

New file `frontend/src/features/artists/components/ArtistSocialTab.tsx`, taking `{ artistId,
slug, canManage, isOwnProfile }`. This replaces the two-heading block currently inline in
`ArtistDetailPage.tsx` at lines 848–863:

```tsx
            {/* Social tab — Instagram keeps its own dedicated photo-sync UI; the other
                four platforms are verification-only, managed via SocialLinksCard. */}
            <TabsContent value="social" className="mt-4 space-y-6">
              <div>
                <h3 className="text-sm font-semibold mb-2">Instagram</h3>
                <InstagramTab artistId={artist.id} canConnect={canManage} canManagePosts={isArtistRole} />
              </div>
              <div>
                <h3 className="text-sm font-semibold mb-2">Other platforms</h3>
                <SocialLinksCard
                  subjectType="Artist"
                  subjectId={artist.id}
                  platforms={["TikTok", "Facebook", "X", "YouTube"]}
                  canManage={canManage}
                />
              </div>
            </TabsContent>
```

becomes:

```tsx
            <TabsContent value="social" className="mt-4">
              <ArtistSocialTab
                artistId={artist.id}
                slug={artist.slug ?? null}
                canManage={canManage}
                isOwnProfile={isOwnProfile}
              />
            </TabsContent>
```

Inside `ArtistSocialTab.tsx`:

- `canManageSocial = canManage || isOwnProfile` — the value this component and its children use
  for whether any action slot renders at all. **This is the D-1-ready gate**: today (Phase 2,
  before Phase 3 ships) an artist's own `isOwnProfile` branch still hits `OwnerOnly` endpoints and
  gets a 403 if it ever calls a mutation — so until Phase 3 loosens the backend, `canManageSocial`
  must **not** be used to show action buttons to a non-owner. Ship `canManageSocial` as a named
  constant now (so Phase 3 is a one-line change: swap `canManage` for `canManageSocial` at each
  call site listed below), but for this phase pass `canManage` (owner-only, as today) into every
  child that renders an action, and pass `canManageSocial` only into the *read-only explanation*
  branch's condition (§2.4's third bullet). Comment this clearly in the code so Phase 3 doesn't
  have to reverse-engineer why two flags exist:
  ```tsx
  // canManageSocial is broader than canManage on purpose: it already reflects the D-1 target
  // state (owner OR the artist's own profile). Action buttons still gate on the narrower
  // `canManage` until the backend policies in Phase 3 ship — see that phase's D-1d — because
  // showing a button here that 403s on click would be worse than the bug this rebuild fixes.
  // Phase 3 flips every `canManage` below to `canManageSocial` in one pass.
  const canManageSocial = canManage || isOwnProfile;
  ```
- Section structure: `<h2>Connected accounts</h2>`, one helper `<p>` (copy per §2.6), then a
  `<ul>` of five `ConnectionRow`s (Instagram first, then TikTok/Facebook/X/YouTube), then — if
  `isOwnProfile && slug` — the "View public profile" link (`ArtistDetailPage.tsx` already computes
  this exact condition at line ~595 for its own use; reuse the same `slug` prop rather than
  re-deriving it).
- **Read-only explanation branch**: when `!canManage && !isOwnProfile` (an artist viewing another
  artist's profile — currently unreachable given the artist role's own routing, per the
  originating spec's B-4, but kept as a defensive branch with its own test per that spec's
  instruction) OR when `!canManageSocial` under D-1 = C's *current* behavior (i.e., today, before
  Phase 3 ships, `isOwnProfile` artists still see this): render every row with `action={null}`
  and one section-level helper line: *"Your studio owner manages connections for your profile.
  Ask them to connect these accounts."* — this is F-01/F-02's actual fix: never show a button
  that always fails, always either show a working action or the reason there isn't one.
- The Instagram row's post-sync grid (`InstagramTab.tsx`'s post grid, lines ~147 onward) moves
  into a collapsible/inline panel rendered directly under the Instagram `ConnectionRow` when
  connected — behavior unchanged, just relocated. Keep `InstagramTab.tsx` as the file that owns
  this panel's logic (rename its default export's *usage* to "Instagram row + posts panel" in a
  comment, but you do not have to rename the file if that creates unnecessary churn — use
  judgment, but note the choice in the PR description).
- Loading: **one** skeleton block for the whole section — five row-height placeholders — replacing
  the two independent skeleton sets `InstagramTab.tsx:86-92` and `SocialLinksCard.tsx:141-147`
  currently render separately (this is what removes the visible "one card style vs one bare
  `py-12` block" inconsistency, F-07).
- Error: if the underlying `useGetInstagramStatusQuery`/`useGetSocialLinksQuery` calls error,
  render an inline `Alert` — *"We couldn't load {your/{First}'s} connected accounts."* + a "Try
  again" button calling `refetch()` on both queries. **Do not** fall through to a "Not linked"
  render on error — `InstagramTab.tsx:94` currently does exactly that
  (`if (!status?.isConnected)` is true both when genuinely not connected and when the query
  errored with no data), which presents a false empty state. Fix this at the point where
  `isConnected` is read: treat `isError` as its own branch, checked before the not-connected
  branch.

## 2.5 `window.confirm` → themed dialog (B-7)

New `frontend/src/features/social/components/ConfirmDisconnectDialog.tsx` wrapping the existing
`Dialog`/`DialogContent`/`DialogHeader`/`DialogTitle`/`DialogFooter` primitives already imported
in `SocialLinksCard.tsx` for the verification-code dialog. Props: `platform: string`, `body:
string`, `open: boolean`, `onOpenChange`, `onConfirm`. Footer: `Cancel` (ghost, **autofocus**) +
`Disconnect` (destructive variant). Replace both existing calls:

- `InstagramTab.tsx:69` — `window.confirm("Disconnect Instagram? Synced posts remain but no new
  posts will be fetched.")` → `ConfirmDisconnectDialog` with the Instagram-specific body from
  §2.6's copy deck.
- `SocialLinksCard.tsx:134` — `window.confirm(\`Disconnect ${label}? The handle stays, but the
  Verified badge will be removed.\`)` → same dialog, generic body.

Test requirement: spy on `window.confirm` in both components' test suites and assert zero calls
after this change.

## 2.6 Copy (own-profile second person, owner-viewing-artist third person with first name)

| Slot | Own profile | Owner viewing artist |
|---|---|---|
| Section heading | Connected accounts | Connected accounts |
| Helper (can manage) | Link your accounts so clients can find you and see a Verified badge on your public profile. | Link {First}'s accounts so clients can find them and see a Verified badge on their public profile. |
| Helper (cannot manage) | Your studio owner manages connections for your profile. Ask them to connect these accounts. | — |
| Instagram, not linked | Connect Instagram to automatically show your latest posts on your public portfolio. | Connect {First}'s Instagram to automatically show their latest posts on their public portfolio. |
| Unavailable row | Not available on this server yet. | same |
| Disconnect dialog title | Disconnect {Platform}? | same |
| Disconnect body (Instagram) | Your synced posts stay on your portfolio, but no new posts will be fetched. | Synced posts stay on {First}'s portfolio, but no new posts will be fetched. |
| Disconnect body (other) | Your handle stays, but the Verified badge will be removed. | The handle stays, but the Verified badge will be removed. |
| Load error | We couldn't load your connected accounts. | We couldn't load {First}'s connected accounts. |
| Public profile link | View public profile | View public profile |

`InstagramTab.test.tsx:138` (or wherever it lands after any file move) currently asserts the old
third-person empty-state copy — update that assertion, don't delete the test's coverage of the
empty state itself.

## 2.7 Tests (Phase 2)

- `ConnectionRow`: every state in the originating spec's §8 state matrix renders correctly; no
  interactive control appears when `action === null`; badge text is always present (never colour
  alone).
- `ArtistSocialTab`: viewer permutations (owner / own-profile artist under D-1=C's current
  gating / defensive artist-viewing-artist); error state does not render "Not linked"; skeleton
  row count is 5.
- `ConfirmDisconnectDialog`: Cancel has initial focus; confirming calls the mutation; `0` calls to
  `window.confirm` across both consuming components.
- Axe (`@axe-core` + whatever test runner integration already exists in this repo, or Playwright's
  `@axe-core/playwright` for the e2e layer — check which is already set up before adding a new
  one): zero violations on the Social tab in each state.

## 2.8 Help sync (mandatory, same change)

1. **`helpContent.ts`**, the `owner-social-verification` article (confirmed this pass — id and
   full current copy read directly from `frontend/src/features/help/helpContent.ts`, roles
   `[Owner, Artist]`, already covers both a studio and an artist subject). Its `steps` array's
   first line currently reads *"For a studio: go to Studio Settings and find the 'Social Media'
   card. For an artist: open the artist's profile and go to the 'Social' tab."* — this is still
   accurate after Phase 2 (the tab still exists, still called Social) and needs **no wording
   change from Phase 2 alone** (Phase 3 is what changes who can click through it — update it
   there, not here, to avoid the two phases stepping on the same lines). Do add one line to its
   `tips` array in this phase, though, since it's new information Phase 2 introduces: *"'Not
   available on this server yet' means that platform isn't connected on this server — this isn't
   something you can fix from Settings."* — replacing/aligning with the near-identical existing
   tip that currently reads "'Not available yet' means…" (update the existing tip's wording to
   match the rebuilt row's new copy from §2.6 rather than adding a duplicate tip).
2. **`frontend/public/user-manual/index.html`** — find its Social/Instagram section(s) (search for
   `id="owner-social-verification"` or nearby anchors) and align the described UI with the new
   `ConnectionRow` pattern: one row per platform with a status label and one action, not a
   pitch-with-no-button description if the manual currently describes the old broken state.
3. Onboarding tours: **no change**, confirmed §2 — restate in this phase's PR description too.

## 2.9 Industry benchmark note (Phase 2)

A connection is a row with a status and one action, not a paragraph of pitch text — this is the
category norm (Fresha/Vagaro/Boulevard/GlossGenius-tier staff-profile screens) the originating
spec cited from category knowledge, not freshly re-verified competitor screenshots this pass —
say so in the PR description rather than presenting it as freshly confirmed. A connected state
showing "last synced" + a themed disconnect confirmation (§2.5) matches the same benchmark set.

---

# PHASE 3 (PR-3) — Artist Self-Service (D-1 = A)

**Highest-risk phase — authorization change.** Everything in this phase is additive to Phase 2's
UI (Phase 2 already ships the `canManageSocial` scaffolding this phase activates).

## 3.1 Policy changes

`Pena_e_Arte.API/Endpoints/InstagramEndpoints.cs`, current lines 17 and 22:

```csharp
        group.MapGet("/connect-url", GetConnectUrl).RequireAuthorization("OwnerOnly");
        ...
        group.MapDelete("/disconnect", Disconnect).RequireAuthorization("OwnerOnly");
```

→

```csharp
        group.MapGet("/connect-url", GetConnectUrl).RequireAuthorization("ArtistAndAbove");
        ...
        group.MapDelete("/disconnect", Disconnect).RequireAuthorization("ArtistAndAbove");
```

`Pena_e_Arte.API/Endpoints/SocialEndpoints.cs`, current lines 21, 23, 25, 27, 29 (the **artist**
group only — lines 20–29):

```csharp
        artistGroup.MapGet("/{platform}/connect-url", ...).RequireAuthorization("OwnerOnly");
        artistGroup.MapPut("/{platform}/handle", ...).RequireAuthorization("OwnerOnly");
        artistGroup.MapPost("/{platform}/request-code", ...).RequireAuthorization("OwnerOnly");
        artistGroup.MapPost("/{platform}/verify-code", ...).RequireAuthorization("OwnerOnly");
        artistGroup.MapDelete("/{platform}/disconnect", ...).RequireAuthorization("OwnerOnly");
```

→ all five become `RequireAuthorization("ArtistAndAbove")`. **Do not touch** the `studioGroup`
block (lines 34–45) — it stays `OwnerOnly` unconditionally; a studio-subject social link has no
concept of an "owning artist."

## 3.2 Handler ownership guard — mandatory, same commit as §3.1

Without this, loosening the policy lets any artist in a studio mint a connect URL, request a
verification code, or disconnect a **colleague's** account — the tenant filter on `db.Artists`
scopes to the studio, not to the individual. `ToggleInstagramPostVisibilityHandler` (already in
the codebase, `Pena_e_Arte.Application/Instagram/Commands/ToggleInstagramPostVisibilityCommand.cs`
lines 13–26) is the existing precedent for exactly this shape:

```csharp
public class ToggleInstagramPostVisibilityHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ToggleInstagramPostVisibilityCommand, Unit>
{
    public async Task<Unit> Handle(ToggleInstagramPostVisibilityCommand request, CancellationToken ct)
    {
        bool artistExists = await db.Artists.AnyAsync(a => a.Id == request.ArtistId, ct);
        if (!artistExists) throw new NotFoundException("Artist", request.ArtistId);

        if (currentUser.Role == "artist")
        {
            bool ownsProfile = await db.Artists
                .AnyAsync(a => a.Id == request.ArtistId && a.UserId == currentUser.UserId, ct);
            if (!ownsProfile) throw new ForbiddenException();
        }
        ...
```

Extract this into a shared helper so the (now) **nine** call sites can't drift — six newly-guarded
handlers plus this existing one, plus the two defense-in-depth additions from §3.3:

```csharp
// Pena_e_Arte.Application/Common/ArtistOwnershipGuard.cs
namespace Pena_e_Arte.Application.Common;

public static class ArtistOwnershipGuard
{
    /// <summary>
    /// Throws ForbiddenException if the caller is an artist acting on someone else's
    /// profile. Owner/admin pass through unchecked (including a dual-role owner-who-is-
    /// also-an-artist — their role claim is "owner", never "artist", so this never runs
    /// for them). Call AFTER confirming the artist exists/is in-tenant.
    /// </summary>
    public static async Task EnsureCanActAsync(
        IAppDbContext db, ICurrentUser currentUser, Guid artistId, CancellationToken ct)
    {
        if (currentUser.Role != "artist") return;

        bool ownsProfile = await db.Artists
            .AnyAsync(a => a.Id == artistId && a.UserId == currentUser.UserId, ct);
        if (!ownsProfile) throw new ForbiddenException();
    }
}
```

Wire it into, in order:

1. **`GetInstagramConnectUrlHandler`** (`Pena_e_Arte.Application/Instagram/Queries/
   GetInstagramConnectUrlQuery.cs`, current lines 16–25) — add `ICurrentUser currentUser` to the
   constructor, call the guard right after the existing `exists` check:
   ```csharp
   public class GetInstagramConnectUrlHandler(
       IAppDbContext db, IInstagramService instagram, IInstagramStateSigner stateSigner,
       ICurrentUser currentUser)
       : IRequestHandler<GetInstagramConnectUrlQuery, string>
   {
       public async Task<string> Handle(GetInstagramConnectUrlQuery request, CancellationToken ct)
       {
           bool exists = await db.Artists.AnyAsync(a => a.Id == request.ArtistId, ct);
           if (!exists) throw new NotFoundException("Artist", request.ArtistId);

           await ArtistOwnershipGuard.EnsureCanActAsync(db, currentUser, request.ArtistId, ct);

           string state = stateSigner.Sign(request.ArtistId);
           return instagram.BuildAuthorizationUrl(state);
       }
   }
   ```
2. **`DisconnectInstagramHandler`** (`Pena_e_Arte.Application/Instagram/Commands/
   DisconnectInstagramCommand.cs`, current lines 12–18) — same pattern: add `ICurrentUser`, call
   the guard right after the existing `exists` check, before touching `InstagramConnections`/
   `SocialAccountLinks`.
3. **`ToggleInstagramPostVisibilityHandler`** — replace its inline `if (currentUser.Role ==
   "artist") { ... }` block with a call to `ArtistOwnershipGuard.EnsureCanActAsync` (no behavior
   change, just de-duplicating onto the shared helper this phase introduces).
4. **`GetSocialConnectUrlHandler`** (`Pena_e_Arte.Application/Social/Queries/
   GetSocialConnectUrlQuery.cs`, current lines 13–34) — add `ICurrentUser currentUser`; call the
   guard **only when `request.SubjectType == SocialLinkSubjectType.Artist`**, using
   `request.SubjectId` as the artist id, right after `SocialSubjectResolver.ResolveStudioIdAsync`
   returns (that call already confirms the artist exists/is in-tenant — the ownership guard
   layers on top of it, it doesn't replace it). For `SubjectType.Studio`, skip the guard entirely
   (an artist can never reach the studio group's endpoints anyway — different route, different
   policy, untouched by this phase).
5. **`UpdateSocialHandleHandler`** (`Pena_e_Arte.Application/Social/Commands/
   UpdateSocialHandleCommand.cs`, current lines 21–34) — same conditional-on-SubjectType pattern
   as #4, guard called right after `SocialSubjectResolver.ResolveStudioIdAsync`.
6. **`RequestSocialVerificationCodeHandler`** (current lines 17–52) — same pattern.
7. **`VerifySocialBioCodeHandler`** (current lines 17–70) — same pattern.
8. **`DisconnectSocialAccountHandler`** (`Pena_e_Arte.Application/Social/Commands/
   DisconnectSocialAccountCommand.cs`, current lines 14–50) — same pattern, guard called after
   the existing `SubjectType == Artist && Platform == Instagram` business-rule check (that check
   throws before ever reaching the resolver for that specific combination, so ordering the guard
   after `ResolveStudioIdAsync` as in the others is correct and consistent).

## 3.3 Defense-in-depth: close the Artist+Instagram gap in the three ungated Social handlers

New finding from this pass (§2) — `RequestSocialVerificationCodeHandler`,
`VerifySocialBioCodeHandler`, and `UpdateSocialHandleHandler` have **no** guard against
`SubjectType.Artist && Platform.Instagram`, unlike `GetSocialConnectUrlHandler` and
`DisconnectSocialAccountHandler`, which both throw:

```csharp
        if (request.SubjectType == SocialLinkSubjectType.Artist && request.Platform == SocialPlatform.Instagram)
            throw new BusinessRuleViolationException(
                "Use the artist's own Instagram connect flow (/artists/{id}/instagram/connect-url) instead.");
```

Today this is unreachable through the UI (the frontend never sends `platform=Instagram` through
the generic social path for an artist subject). After §3.1 loosens these three endpoints to
`ArtistAndAbove`, it becomes reachable by a direct authenticated API call from an artist account —
still not a tenant-isolation break (the resolver still scopes correctly), but it would let an
artist create a second, divergent `SocialAccountLink` row for "Instagram" alongside the real
`InstagramConnection`-backed one, which nothing in this codebase expects to coexist. Add the
identical guard (same exception, same message) to the top of `RequestSocialVerificationCodeHandler`
`UpdateSocialHandleHandler`, and `VerifySocialBioCodeHandler`, before their resolver call — copy
the exact wording already used in the other two handlers so all five agree.

## 3.4 Frontend gating flip

`ArtistDetailPage.tsx` currently passes `canManage` (owner-only) into `InstagramTab`'s
`canConnect` (line 852) and `SocialLinksCard`'s `canManage` (line 860). Per Phase 2's §2.4
scaffolding, `ArtistSocialTab` already computes `canManageSocial = canManage || isOwnProfile`.
This phase's frontend change is exactly: everywhere `ArtistSocialTab` (or its children) currently
reads the narrower `canManage` to decide whether to render an action, switch it to
`canManageSocial`. Update the doc comments that currently assert owner-only behavior:

- `InstagramTab.tsx`'s `canConnect` prop doc (currently *"Owner-only: connect/disconnect the
  Instagram account (matches OwnerOnly policy)"*) → *"Owner, or the artist managing their own
  profile: connect/disconnect the Instagram account (matches the ArtistAndAbove policy plus a
  handler-side ownership guard)."*
- `SocialLinksCard.tsx`'s `canManage` prop doc (currently describes every backend call as
  `OwnerOnly`) → update to describe the same ArtistAndAbove-plus-guard behavior for the artist
  subject group, while noting the studio subject group (Studio Settings' own usage of this same
  component) is unaffected and stays owner-only.

No other frontend file changes — `canManage`'s original owner-only meaning is unchanged and still
used correctly elsewhere (e.g. the Delete/Stop-working `⋯` item from Phase 1 stays gated on the
narrower `canManage`, never `canManageSocial` — an artist must never see their own "Stop working
as an artist" action gated any more broadly than it already is; this phase only touches social
connection actions).

## 3.5 Auditing (D-1e)

None of the seven Instagram/Social commands currently implement `IAuditableCommand`
(`Pena_e_Arte.Domain/Interfaces/IAuditableCommand.cs`) — confirmed this pass by grepping every
usage in `Pena_e_Arte.Application/`; existing adopters include `CancelAppointmentCommand` and
`ActivateSubscriptionManuallyCommand`. The interface:

```csharp
public interface IAuditableCommand
{
    string AuditAction { get; }
    string AuditTargetType { get; }
    Guid AuditTargetId { get; }
    Guid? AuditStudioId => null;
}
```

Implement it on: `GetInstagramConnectUrlQuery` (no — it's a query, not a command; **audit only the
mutations**), `DisconnectInstagramCommand`, `UpdateSocialHandleCommand`,
`RequestSocialVerificationCodeCommand`, `VerifySocialBioCodeCommand`,
`DisconnectSocialAccountCommand`. `ExchangeInstagramCodeCommand`/`ExchangeSocialOAuthCodeCommand`
(the OAuth-callback commands) should be audited too, since a completed OAuth connection is the
security-relevant event auditing exists to catch — check whichever pattern
`AuditLogBehavior`/an existing `IAuditableCommand` adopter uses for its `AuditAction` string
convention (likely `"{noun}.{verb}"`, e.g. `appointment.cancelled`) and match it, e.g.
`social.instagram_connected`, `social.handle_updated`, `social.verification_requested`,
`social.verified`, `social.disconnected`. `AuditTargetType` = `"Artist"` or `"Studio"` depending
on subject; `AuditTargetId` = the artist/studio id. **Metadata must never include the handle or
any name** — the pipeline behavior's own doc comment already says this is enforced via an
allowlist (`AuditMetadataBuilder`); confirm what fields the allowlist accepts for this event
family and only pass `platform` plus whatever this command's own strongly-typed id fields already
are (never freeform text fields that could carry PII).

## 3.6 Tenant scope

No query in any of the six/eight touched handlers bypasses the existing tenant filter — every one
already goes through `db.Artists` (filtered) or `SocialSubjectResolver.ResolveStudioIdAsync`
(which explicitly re-checks `tenant.StudioId` for the Studio subject case, since `Studio` itself
carries no query filter — this is existing, correct code, unchanged by this phase). No
`IgnoreQueryFilters()` is added anywhere in this phase. Admin behavior (bypasses tenant scope
entirely, per existing global convention) is unchanged.

## 3.7 Signed-state hardening (D-1f — both signers, per §2)

`IInstagramStateSigner` (`Pena_e_Arte.Infrastructure/Services/InstagramStateSigner.cs`) and
`ISocialOAuthStateSigner` (`Pena_e_Arte.Infrastructure/Services/Social/SocialOAuthStateSigner.cs`)
both currently sign only their payload (bare `artistId`, or `subjectType|subjectId|platform`) with
HMAC-SHA256 and no expiry or nonce — confirmed by reading both implementations in full this pass.
This matters more once artists, not just owners, routinely initiate these flows (more traffic
through the same unbounded-lifetime tokens). Bind both to an issued-at timestamp and reject stale
state:

For `InstagramStateSigner`, change the payload from `artistId:N` to `artistId:N|{unix
timestamp}`, and in `TryValidate`, after the HMAC check passes, parse the timestamp and reject
(`return false`) if it's older than a fixed window (use 15 minutes — OAuth round-trips are
seconds, not hours, and this only needs to survive a slow user on the provider's consent screen).
Apply the identical shape change to `SocialOAuthStateSigner`. Update both interfaces'
`Sign`/`TryValidate` signatures only if you need to (a timestamp embedded in the existing string
payload doesn't require an interface change — prefer that over adding a new out-parameter, to
minimize the blast radius of this hardening item across the six call sites already relying on the
current signatures). Add a unit test per signer: a validly-signed-but-expired state fails
validation; a state within the window succeeds; tampering with the embedded timestamp (without
re-signing) fails the HMAC check as it does today.

## 3.8 Tests (Phase 3)

**Backend unit** (`Pena_e_Arte.UnitTests`, one test class per touched handler, or extend existing
ones): for each of the eight guarded handlers — artist-own succeeds; artist-other (same studio) →
`ForbiddenException`; owner/admin succeed for any artist in their tenant; artist id not in tenant
→ `NotFoundException`. `ArtistOwnershipGuard` gets its own direct unit test independent of any
handler. The three newly-Instagram-blocked handlers (§3.3) each get a test asserting
`BusinessRuleViolationException` for `Artist + Instagram`, matching the existing tests (if any) for
`GetSocialConnectUrlHandler`/`DisconnectSocialAccountHandler`'s identical check — mirror that
test's shape exactly.

**Backend integration** (real MySQL — required, not optional, per this repo's own established
gap): two studios × two artists each; assert the full 403/404 matrix over real HTTP with real
JWTs for all seven artist-scoped endpoints (five Social + two Instagram); assert
`/studios/{id}/social/*` is still 403 for an artist role regardless of tenant, unchanged from
today. Explicit dual-role test: an owner account that also has an artist profile can act on that
artist profile without hitting the guard (their role claim is `"owner"`, never `"artist"` — the
guard's `if (currentUser.Role != "artist") return;` early-return already covers this, but assert
it, don't assume it).

**Frontend**: extend Phase 2's `ArtistSocialTab` tests with the `canManageSocial = true` (own
profile) case now actually rendering action buttons, and assert the corresponding RTK Query
mutation hooks are called with the artist's own id, not some other id.

## 3.9 Help sync (mandatory, same change)

1. `helpContent.ts`'s `owner-social-verification` article — this is the phase that actually
   changes who this is true for. Update its first `steps` line, currently *"For a studio: go to
   Studio Settings and find the 'Social Media' card. For an artist: open the artist's profile and
   go to the 'Social' tab."* to make explicit that an artist can now act on their own profile
   directly: *"For a studio, or for your own artist profile: … click 'Connect' … For a studio
   owner managing a different artist's profile: the same steps apply — you can connect and
   verify on their behalf."* — write the exact final copy to match this article's existing voice
   (read the full article before editing, don't just append).
2. `frontend/public/user-manual/index.html`'s corresponding section: same "who can do this" update.
3. Onboarding tours: **no change**, confirmed §2.

## 3.10 Industry benchmark note (Phase 3)

The person who owns an external account is the one who authorizes it — this is the category norm
(every booking-SaaS staff-profile integration puts OAuth consent in the staff member's own hands,
not their manager's) and is also just how OAuth consent works technically (§3.2 of the originating
spec's root-cause analysis: whoever completes the Instagram consent screen in that browser session
binds *their* Instagram to whatever `artistId` the signed state carries — an owner completing it
authorizes the owner's own account, not the artist's, unless the owner happens to also be logged
into the artist's Instagram in that browser). This phase fixes the actual mechanism, not just the
UI symptom.

---

# PHASE 4 (PR-4) — Adjacent Hygiene

Independent of Phases 1–3; can ship separately, smallest risk, but touches shared files so keep it
in its own PR per the delivery-plan risk table.

## 4.1 Nav renames (D-3)

`frontend/src/layouts/artistNavSections.tsx`, current lines 53–59 (group `id: "clients"`, `label:
"Clients"`, containing an item `label: "Clients"`):

```tsx
    {
      id: "clients",
      label: "Clients",
      entries: [
        { label: "Clients",  href: "/clients",  icon: <Users className={icon} />,         tourId: tour("artist-clients-nav") },
        { label: "Messages", href: "/messages", icon: <MessageCircle className={icon} />, tourId: tour("artist-messages-nav") },
        { label: "Waitlist", href: "/waitlist", icon: <ListOrdered className={icon} />,   tourId: tour("artist-waitlist-nav") },
      ],
    },
```

Rename the group label to **`People`** (keep the child item labeled `Clients`, keep `id:
"clients"` and every `tourId` unchanged — confirmed §2, no tour references need updating since
they target `data-tour`, not label text or group id):

```tsx
    {
      id: "clients",
      label: "People",
      entries: [
        { label: "Clients",  href: "/clients",  icon: <Users className={icon} />,         tourId: tour("artist-clients-nav") },
        { label: "Messages", href: "/messages", icon: <MessageCircle className={icon} />, tourId: tour("artist-messages-nav") },
        { label: "Waitlist", href: "/waitlist", icon: <ListOrdered className={icon} />,   tourId: tour("artist-waitlist-nav") },
      ],
    },
```

Current line 44 (`"Reports About Me"`, group `"me"`, line 42–48):

```tsx
    { label: "Reports About Me", href: opts.reportsHref, icon: <ShieldAlert className={icon} />, tourId: tour("artist-conduct-reports-nav") },
```

→

```tsx
    { label: "Conduct Reports", href: opts.reportsHref, icon: <ShieldAlert className={icon} />, tourId: tour("artist-conduct-reports-nav") },
```

`tourId` and `href` (`opts.reportsHref`, resolving to `/conduct-reports`) are unchanged — this is
a label-only rename, confirmed against `ownerTour.ts`'s matching `owner-conduct-reports-nav` step
(also unaffected, targets the same `data-tour` value on the owner-side equivalent nav item, not
this file).

This function (`buildArtistSections`) is shared by both `ArtistLayout` and `OwnerLayout`'s
artist-mode — verify the rename renders correctly in both contexts, since both consume this same
function.

## 4.2 Tab rename (D-3, already applied in §1.5)

Already folded into Phase 1's tab-strip diff (`Schedule` → `Availability`, `value="hours"`
unchanged). No separate change needed here — this section exists only so the delivery plan's
phase boundaries stay legible; do not re-apply it.

## 4.3 Header icon distinctness (E-3) — confirmed largely already correct

Re-verified this pass, contradicting part of the originating spec's F-16: `HelpMenu.tsx` (`title=
"Help" aria-label="Open help menu"`), `MessagesNavBadge.tsx` (`aria-label` dynamic, `title=
"Messages"`, icon `MessageCircle`), `NotificationBell.tsx` (`aria-label` dynamic, `title=
"Notifications"`, icon `Bell`), and `UserMenu.tsx` (`aria-label="User menu"`) **all already** have
accessible names — there is no accessible-name gap to fix. The feedback button
(`ArtistLayout.tsx`/`OwnerLayout.tsx`, `title="Send feedback" aria-label="Send feedback"`, icon
`MessageSquareMore`) is already a technically distinct Lucide icon from Messages'
`MessageCircle`, just visually similar in silhouette at 16px. Given both already have correct
accessible names and distinct (if similar-looking) icons, **do not change the feedback icon** in
this phase — treat it as resolved-not-broken and say so explicitly in the PR description rather
than making a subjective glyph swap with no clear improvement criterion. If a future design pass
wants a more differentiated icon set for the header, that's a separate, dedicated icon-audit task,
not something to fold in here on a hunch.

## 4.4 Avatar consistency (E-4)

Confirmed: `UserChip` (header) derives its initial from the JWT's `given_name` claim only — no
family-name source is available there without a JWT claim change or an extra fetch, both
explicitly out of scope (originating spec's own instruction). Per that instruction: **do not**
touch `UserChip`. Instead, align only the *page* avatar's fill/style treatment (not its initials
logic — `getInitials(firstName, lastName)` on `ArtistDetailPage.tsx` stays two-letter) to match
whatever visual treatment `UserChip` uses (fill color, border, etc.) so the two avatars look like
the same design system even though one shows one initial and the other shows two. Read
`UserChip`'s implementation first to find the exact classes/tokens to match.

## 4.5 Contrast measurement (D-4) — measure, report, do not blind-fix tokens

Per D-4 (§2): a token-level fix is approved *in principle*, but only after the measurement in the
originating spec's §11 actually runs and produces numbers — this prompt does not pre-authorize
changing shared design tokens based on a screenshot estimate, because a token change touches every
screen in the app and needs its own visual-regression pass, which is bigger than tonight's scope.

**This phase's actual deliverable**: write and commit the measurement script (Playwright,
`getComputedStyle` walk to the first non-transparent ancestor background, WCAG relative-luminance
ratio) against the selectors the originating spec's §11 lists (sidebar group labels, sidebar
item inactive/active, tab triggers inactive/active, the `Ctrl B`/collapse-shortcut chip, header
user subtitle, row secondary lines, badges, helper text, placeholder text), run it in both light
and dark themes, and commit its output as a table in the PR description and as a new file
`docs/claude/contrast-audit-2026-09-20.md` (this doc-only output is within this project's write
scope). **Do not** change any shared token or component style based on this phase's findings — if
the script finds failures, list them with their exact ratios and defer the fix to a follow-up PR
scoped specifically to the token change plus its visual-regression suite (naming that follow-up
explicitly in the new doc's own "Next steps" section). This keeps Phase 4 measurement-only, which
is what makes it safe to ship without a full-app visual-regression pass tonight.

## 4.6 Tests (Phase 4)

- Nav rename: snapshot/assertion that `buildArtistSections` renders `"People"` as the group label
  and `"Conduct Reports"` as the item label, under both `tourIds: true` and `tourIds: false`
  (`OwnerLayout`'s artist-mode call site) configurations.
- Contrast script itself is the "test" for §4.5 — it is a script that produces a report, not a
  pass/fail CI gate in this phase (no token changes are being validated against a threshold yet,
  since none are shipping). Do commit it somewhere sensible for reuse once the follow-up token PR
  exists (e.g. `frontend/e2e/` alongside the other Playwright specs, or a `scripts/` location if
  this repo already has one for non-test tooling — check before choosing).

## 4.7 Help sync

No user-visible behavior changes beyond the two label renames already covered by Phase 1's Help
update (§1.7) — this phase's nav-group rename (`Clients` group → `People`) is new and needs its
own check: grep `helpContent.ts` and the manual for any reference to the artist sidebar's
"Clients" *group* (as opposed to the "Clients" *nav item*, which is unchanged) and update if found;
if no Help content specifically names the group label (likely, since groups are usually
undescribed navigational chrome), state that explicitly rather than leaving the question open.

---

## 5. Scope boundary — do not touch

- `frontend/src/features/social/socialApi.ts`'s RTK Query endpoint definitions — this prompt
  changes what calls them and how the UI reacts, never their request/response shapes or cache
  tags.
- `Pena_e_Arte.Domain/Entities/SocialAccountLink.cs`, `InstagramConnection.cs`, or any migration —
  no schema change anywhere in this prompt.
- The Instagram nightly-sync Hangfire job, or anything under a `Jobs`/`BackgroundServices` folder
  that syncs posts — out of scope per the originating spec's non-goals.
- `frontend/src/features/public/components/ArtistPortfolioPage.tsx` and
  `Pena_e_Arte.Application/Public/Queries/GetPublicArtistQuery.cs` — confirmed this pass that
  unverified handles render publicly with no distinguishing signal beyond the missing badge
  (§2's newly-confirmed fact). This is real, pre-existing, and unrelated to this prompt's changes
  — do not "fix" it here. It is tracked as the originating spec's §15.6 follow-up; leave a comment
  pointing to that doc if you touch either file for an unrelated reason, but do not add a
  verified-only filter in this prompt.
- `frontend/src/shared/components/UserChip.tsx` (or wherever the header avatar component actually
  lives) — Phase 4's E-4 explicitly does not touch it (§4.4).
- Any shared design token file (`tailwind.config.*`, a `tokens.css`/theme file, or equivalent) —
  Phase 4 measures contrast, it does not change tokens (§4.5).
- `frontend/src/features/studios/components/StudioProfilePage.tsx`'s own `SocialLinksCard` usage
  (Studio Settings' "Social Media" card) beyond whatever `ConnectionRow`/icon-import changes
  ripple through automatically because it's the same shared component — its `canManage` prop
  stays hardcoded/owner-gated exactly as today; this prompt does not change who can manage a
  studio-level social link.

---

## 6. Not built tonight — needs a human decision (do not build blind)

- **D-1 Option B** (owner may only edit a typed handle for other artists, never run
  OAuth/verification on their behalf, so "Verified" always means the artist proved it themselves)
  — a real, more-correct follow-up, but changes the meaning of existing verified data and removes
  a capability owners have today. Not built.
- **D-2's brand-licensing sign-off** — the SVGs ship with best-effort current-guideline geometry
  (§2.2), but nobody with authority over the frontend's legal/brand posture has confirmed usage
  terms. Flag this in the PR description as a pre-production gate, not a resolved question.
- **"Needs reconnection" token-expiry state** — the backend exposes no token-expiry data today;
  do not invent a UI state for data that doesn't exist.
- **Public-page unverified-handle treatment** (§5's do-not-touch item) — confirmed real this pass,
  deliberately deferred, tracked in the originating spec's §15.6.
- **Contrast token fix itself** (only the measurement ships, per §4.5) — the follow-up PR needs
  its own scope, its own visual-regression pass, and is not authorized by this prompt.

---

## 7. Final verification checklist — do not mark this done until all of these pass

1. `dotnet build` clean; `dotnet test` — every existing test plus every new test from Phases 1–3
   (§1.6, §2.7, §3.8) passes.
2. `pnpm lint` and `pnpm test` clean, including every new/updated frontend test.
3. `pnpm build` clean (catches `tsc` errors the unit-test runner can miss) — run this separately
   from `pnpm test` and confirm both are green independently.
4. Manual/E2E: Edit is visible and clickable at scrollTop 0 and at maximum scroll, under both
   `ArtistLayout` and `OwnerLayout`, with short content (no phantom scrollbar from a stray
   `min-h-screen`).
5. Artist logs in → My Portfolio → Social: every row shows a status and either a working action
   or an explanation, never a bare `—` with no action and no reason (F-01/F-02, the core defect
   this prompt exists to fix).
6. After Phase 3: artist can connect/verify/disconnect their **own** accounts end-to-end (mocked
   OAuth acceptable for the connect leg); the same artist gets 403 acting on a colleague's
   `artistId` in the same studio, for all seven operations; 404 for an artist in a different
   tenant; owner/admin and dual-role-owner behavior is unchanged from before this prompt.
7. `?tab=social` deep link and refresh both land correctly; a mocked OAuth return lands on Social,
   toasts exactly once, and the resulting URL carries no `instagram`/`social`/`platform` params.
8. At 375px width: no page-level horizontal scroll; all six tabs reachable; the active tab is
   never clipped.
9. Zero `window.confirm` calls remain in `features/social` or `features/artists`.
10. Axe reports zero violations on the Social tab in every state from Phase 2's §8 state matrix.
11. `Studio.Nipt`-style PII grep, adapted for this feature: confirm no new Serilog call anywhere
    in this diff logs a handle, a name, or any other PII — only `platform`/`artistId`/
    `tenant_id`/`user_id`/`request_id`/outcome, per §3.5's audit entries and CLAUDE.md rule #3.
12. Confirm every item in §5's "do not touch" list is actually untouched — diff those exact
    files/folders against the pre-prompt checkpoint commit and confirm zero changes (schema
    migrations especially — there must be none).
13. Help surfaces: `helpContent.ts` renders correctly in the in-app Help Menu for both the
    `owner-social-verification` article's updated copy and whatever Phase 1/Phase 4 label-rename
    checks turned up; `user-manual/index.html` opens standalone and its updated sections read
    correctly; confirm, per phase, that "no tour change needed" was stated explicitly somewhere in
    the cumulative PR description rather than left unaddressed.
14. `docs/claude/contrast-audit-2026-09-20.md` exists, is populated with real measured ratios (not
    placeholders), and its "Next steps" section names the follow-up token PR.
15. For each phase, confirm the phase's own PII/tenant/auth-specific checks above are green before
    moving to the next phase — do not batch all verification to the very end only, since Phase 3
    failing should block Phase 4 from being considered "done" even if Phase 4's own tests pass.

---

## 8. Final deliverable spec

**Code**: four PRs, in the order given, each against `feat/artist-social-tab-and-profile-chrome-
2026-09-20` (or its own sub-branch per PR if this repo's convention is one PR per branch — check
recent merged PR history, e.g. `#161`'s branch-per-PR pattern, and match it), each with the tests
and Help-sync updates from its own phase included in the same commit, never a follow-up commit.

**Commit messages** (one per phase, Conventional-Commits style matching this repo's existing log,
e.g. `feat(clients): let a client edit their own name and phone`):

```
fix(artists): remove nested header, add breadcrumb + URL-backed tabs on artist profile
feat(social): rebuild connection-row UI with brand icons and read-only explanations
feat(social): let an artist connect and verify their own social accounts
chore(nav): rename artist sidebar labels, add contrast measurement script
```

**Docs**:
- `docs/claude/architecture.md` — append these four rows to the existing Decisions Log table
  (found at line 1707 in this repo as of this pass; append after its last row, do not insert
  mid-table):

  ```
  | Artist social self-service | Artist role can connect/verify/disconnect their own social accounts (ArtistAndAbove + handler ownership guard); owner keeps full access to every artist in studio | OAuth consent must be completed by the account holder — an owner clicking Connect authorizes the owner's own Instagram, not the artist's |
  | Brand social icons | Inline SVG components under `shared/components/icons/brand/`, no new npm dependency | lucide-react ships no brand glyphs; licensing/usage-terms sign-off still pending, flagged not resolved |
  | Social OAuth state signing | Both `IInstagramStateSigner` and `ISocialOAuthStateSigner` bind an issued-at timestamp (15-minute window) in addition to the existing HMAC | Previously unbounded-lifetime signed state; hardened once artist-initiated traffic through these signers increased |
  ```

  Then, once all four phases are actually built and verified (not before), append one narrative
  subsection following this file's own established convention (see `### Webhooks — 2026-09-11`
  or `### Support Impersonation with Audit Trail — 2026-09-10` for the exact shape: "What was
  built," a Help-sync line, a "Verification" line with real test counts) titled `### Artist Social
  Tab, Profile Chrome & Adjacent UI Hygiene — 2026-09-20`.
- `docs/claude/contrast-audit-2026-09-20.md` — new file, per §4.5.
- `docs/claude/feature-spec-artist-social-tab-and-profile-chrome-2026-09-20.md` — leave as-is; it
  is the historical record of the audit this prompt was built from, not something to edit
  retroactively.

**This file itself** stays at
`docs/claude/overnight-prompt-artist-social-tab-and-profile-chrome-2026-09-20.md` as the permanent
record of what was specified, per this repo's existing convention of keeping every overnight
prompt alongside its originating spec.
