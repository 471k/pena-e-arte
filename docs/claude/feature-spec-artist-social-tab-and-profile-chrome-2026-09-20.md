# Feature Spec — Artist Profile "Social" Tab, Profile Page Chrome & Adjacent UI Hygiene

> Date: 2026-09-20
> Status: Draft for review — **not** an implementation prompt yet. Section 6 ("Decisions
> needed") must be resolved first; every decision carries a recommendation so it can be
> approved as-is. Workstreams A and C (§7) need **no** decision and can start immediately.
> Touches: `Pena_e_Arte.API` (Instagram + Social endpoints), `Pena_e_Arte.Application`
> (Instagram + Social handlers), `frontend/src/features/{artists,social}`,
> `frontend/src/layouts/{ArtistLayout,OwnerLayout}.tsx`, `frontend/src/shared`
> (icons, tabs, nav), Help Menu (`helpContent.ts`), standalone user manual
> (`frontend/public/user-manual/index.html`), onboarding tours (`artistTour.ts` — to be
> checked, see §14).
> Origin: 2026-09-20 — a UI/UX audit of a single staging screenshot
> (`staging.tattooos.co`, artist account, `My Portfolio` → `Social` tab). The audit surfaced
> ~30 observations; this spec records which ones survived verification against source, which
> did not, and how to fix the ones that did.

---

## 1. What this is

The artist's own **Social** tab is a read-only view that is dressed as an editable one and
gives the user no explanation and no way forward. An artist who opens it sees a pitch
("Connect this artist's Instagram…") with no button, and four bordered rows that each show
`—` with no action. The cause is a deliberate permission decision (every connect/verify
endpoint is `OwnerOnly`) that was implemented correctly on the backend and on the button
gating, but never given a user-visible explanation.

Wrapped around that, the page has a second class of defect: it renders its **own** sticky
`<header>` and `min-h-screen` root inside a layout that already provides a header — a leftover
from before the sidebar navigation shipped. That is the most likely cause of a doubled rule
under the app header and, more importantly, of the page's own toolbar (**← Artists, Edit,
Stop working as an artist**) being hidden behind the app header when the page is scrolled.

This spec covers, in priority order:

| # | Workstream | Type | Needs a decision? |
|---|---|---|---|
| A | Page chrome: remove the nested header/`min-h-screen`, real breadcrumb/title, heading levels | Frontend bug fix | No |
| B | Social tab rebuild: one connection-row pattern, read-only notice, state matrix, copy, icons, a11y | Frontend | Partly (D-2) |
| C | Tab state in the URL + post-OAuth landing on the right tab | Frontend | No |
| D | Who may connect/verify: artist self-service for their own profile | Backend + frontend | **Yes (D-1)** |
| E | Adjacent nav/header hygiene and a measured contrast pass | Frontend | Partly (D-3, D-4) |

Nothing here adds a new endpoint, a new table, or a new dependency except (optionally) brand
icons (D-2). Workstream D changes **authorization** on five existing endpoint groups and is the
only part with security weight.

---

## 2. Corrections to the original audit

The audit was written from the screenshot alone, then checked against source. These claims
did not survive and must not be carried forward:

| Audit claim | Verdict | Evidence |
|---|---|---|
| "Rows look editable but nothing works — bug" | **Reframed.** Read-only is intentional for non-owners; the defect is the missing explanation, not the gating. | `SocialLinksCard.tsx:38-45` (prop doc), `:177` |
| "No skeleton for the *Other platforms* card" | **Wrong.** It has one. | `SocialLinksCard.tsx:141-147` |
| "Header icons are probably unlabeled" | **Partly wrong.** Feedback button has `aria-label`; Help, Messages and Bell buttons are inside other components and were **not inspected**. | `ArtistLayout.tsx:66-75` |
| "Header shows no unread badges" | **Unverified.** `MessagesNavBadge` exists; a zero-count state renders no badge by design and the screenshot may simply have zero unread. | `ArtistLayout.tsx:77-78` |
| "Touch targets under 44 px are an AA failure" | **Overstated.** WCAG 2.2 SC 2.5.8 (AA) requires 24×24 CSS px; 44 px is the AAA / platform-guideline figure. Treat 44 px as a *recommendation* for the mobile drawer only. | WCAG 2.2 SC 2.5.8 |
| "Doubled header rule is a stray border" | **Superseded.** Very likely the page's own nested sticky header (Finding F-03). | `ArtistDetailPage.tsx:387` |

Contrast statements in the audit (grey group labels, `—`, `Ctrl B` chip, inactive tabs) were
**eyeballed from a JPEG-like screenshot and are unmeasured.** §11 specifies how to measure them.

---

## 3. Current state — verified against live source, 2026-09-20

Read directly from the repo, not inferred from docs or from the screenshot.

### 3.1 Authorization (the root cause)

`AuthorizationExtensions.cs:12-13` — `ArtistAndAbove` = `artist | owner | admin`;
`OwnerOnly` = `owner | admin`.

| Endpoint | Policy | Source |
|---|---|---|
| `GET /artists/{id}/instagram/connect-url` | **OwnerOnly** | `InstagramEndpoints.cs:17` |
| `GET /artists/{id}/instagram/status` | ArtistAndAbove | `:18` |
| `GET /artists/{id}/instagram/posts` | ArtistAndAbove | `:19` |
| `PUT /artists/{id}/instagram/posts/{postId}/visibility` | ArtistAndAbove (+ handler ownership guard) | `:20-21`, `ToggleInstagramPostVisibilityCommand.cs:21-26` |
| `DELETE /artists/{id}/instagram/disconnect` | **OwnerOnly** | `:22` |
| `GET /artists/{id}/social` | ArtistAndAbove | `SocialEndpoints.cs:18-19` |
| `GET …/social/{platform}/connect-url` | **OwnerOnly** | `:20-21` |
| `PUT …/social/{platform}/handle` | **OwnerOnly** | `:22-23` |
| `POST …/social/{platform}/request-code` | **OwnerOnly** | `:24-25` |
| `POST …/social/{platform}/verify-code` | **OwnerOnly** | `:26-27` |
| `DELETE …/social/{platform}/disconnect` | **OwnerOnly** | `:28-29` |

The frontend mirrors this exactly: `ArtistDetailPage.tsx:181` sets `canManage = usePermission(Role.Owner)`
and passes it as `canConnect` to `InstagramTab` (`:852`) and `canManage` to `SocialLinksCard` (`:860`).
So for an `artist`-role user both are `false`.

### 3.2 The design flaw hiding inside the permission

`GetInstagramConnectUrlHandler` (`GetInstagramConnectUrlQuery.cs:16-25`) checks only that the artist
exists in the caller's tenant, then signs `state = Sign(artistId)` and returns the Instagram
authorization URL. **Whoever completes the OAuth consent in that browser binds *their* Instagram
account to that `artistId`.** So under today's `OwnerOnly` policy, an owner clicking Connect on an
artist's profile authorizes *the owner's own* Instagram (or must be logged into the artist's
Instagram in that browser) — neither is the intended result. The person who can naturally
authorize an artist's Instagram is the artist. The same logic applies harder to bio-code
verification (`request-code` / `verify-code`): the code must be placed in the *artist's* bio.

The signed `state` binds only `artistId` (`stateSigner.Sign(request.ArtistId)`). Whether it carries an
expiry, nonce, or initiating user id was **not verified** (see §16).

Reusable pattern already in the codebase for "artist may act only on their own profile":
`ToggleInstagramPostVisibilityHandler` — if `currentUser.Role == "artist"`, require
`db.Artists.Any(a => a.Id == ArtistId && a.UserId == currentUser.UserId)` else `ForbiddenException`.
Owner and admin pass through unchecked.

### 3.3 Frontend — Social tab

- `ArtistDetailPage.tsx:849-863`: `<TabsContent value="social" className="mt-4 space-y-6">` with two
  `<h3 className="text-sm font-semibold mb-2">` sections — **"Instagram"** → `<InstagramTab>` and
  **"Other platforms"** → `<SocialLinksCard platforms={["TikTok","Facebook","X","YouTube"]}>`.
  The two headings are `h3` directly under the page `h1` (`:436`) — no `h2` in view mode.
- `InstagramTab.tsx:94-111` (not connected): a bare (uncontained) flex column, `py-12`, `gap-4`,
  a `h-10 w-10` `AtSign`, a `max-w-xs` paragraph, and the Connect button **only if `canConnect`**.
  Copy is third-person: *"Connect this artist's Instagram account to automatically sync their posts
  to their public portfolio."* (`:98-100`). The unit test `InstagramTab.test.tsx:138` asserts this
  string.
- `InstagramTab.tsx:113-145` (connected): a `Card` row with `AtSign h-5 w-5 text-pink-500`,
  `@username`, verified badge, "Last synced <date>", a `Badge` post count, and a Disconnect button
  (`canConnect` only). Then a 3-column post grid with a per-post visibility toggle.
- `SocialLinksCard.tsx:149-212`: one `Card` per platform, `p-4`, icon `h-5 w-5`, fixed `w-20` label,
  then either a read-only `<span>{@handle | "—"}</span>` (when `row.isVerified || !canManage`, `:162-163`)
  or an `<Input>` saved on blur (`:165-172`); action area is `null` for non-managers (`:177`).
  When OAuth is unconfigured *and* manual check unsupported, the row shows "Not available yet" whose
  explanation is a `title=` attribute only (`:202-207`).
- Both components use `window.confirm` for disconnect (`InstagramTab.tsx:69`, `SocialLinksCard.tsx:134`),
  while a themed `Dialog` is already imported and used in the same card for the verification code.
- Loading skeletons exist in both (`InstagramTab.tsx:86-92`, `SocialLinksCard.tsx:141-147`).
- Icons: `socialPlatforms.ts:10-16` maps Instagram→`AtSign`, TikTok→`Music2`, Facebook→`Globe`,
  **X→`Hash`**, YouTube→`Video`. The file's own header comment says these are placeholders because
  `lucide-react` ships no brand icons, and that brand-guideline compliance was **flagged, not resolved**.

### 3.4 Frontend — page chrome

- `ArtistDetailPage.tsx:386-387`: root `<div className="min-h-screen bg-background">` containing
  `<header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">`
  holding **← Artists**, **Edit**, and (owner) **Delete / Stop working as an artist** (`:388-427`).
  A second copy of the same header exists for the loading/not-found branch (`:361`).
- `ArtistLayout.tsx:54-91`: root `min-h-screen flex flex-col`; its own
  `<header … h-14 border-b sticky top-0 z-20>` (`:59`); page rendered via `<Outlet />` inside
  `flex-1 min-w-0` (`:85-87`). `OwnerLayout.tsx:238` uses the same header pattern.
- The page's header is `z-10`, the layout's is `z-20`, both `sticky top-0`. Nested `min-h-screen` inside
  a layout that already has a 57 px header makes the document ≥ 57 px taller than the viewport.
- The page renders `<Avatar>`+`getInitials(first,last)` = "AK" (`:432-434`); the app header's `UserChip`
  renders a single initial from the JWT `given_name` claim (per the 2026-09-19 image spec §2).
- `<Tabs defaultValue="profile">` (`:535`) is **uncontrolled**: not URL-backed, resets on refresh.
  `TabsList className="w-full"` with six `flex-1` triggers (`:536-543`).
- `ArtistDetailPage.tsx:211-222`: a `useEffect` on `searchParams` toasts for `?instagram=` and
  `?social=&platform=`; it **never removes** those params, so a refresh re-toasts.
- OAuth callbacks redirect to `/artists/{id}?instagram=connected|error` (`InstagramEndpoints.cs:98,101`)
  and `?instagram=denied` to `/artists` (`:87`) — no tab is specified, so the user lands on **Profile**,
  not **Social**.

### 3.5 Frontend — nav / header

- `artistNavSections.tsx:53-60`: group `id:"clients" label:"Clients"` containing an item `label:"Clients"`.
  `:44`: item label "Reports About Me". `:78`: group label "Me".
- Profile tab `value="hours"` is labelled **"Schedule"** (`ArtistDetailPage.tsx:539`), colliding with the
  sidebar's `Schedule` (calendar, `artistNavSections.tsx:32`) which is a different screen.
- Header (`ArtistLayout.tsx:66-78`): feedback button uses `MessageSquareMore`; `MessagesNavBadge` is a
  separate messages entry; the sidebar also lists **Messages** (`artistNavSections.tsx:57`) and
  **Notifications** (`:46`, artists only) alongside the header bell.

### 3.6 Help

`helpContent.ts:1240-1244` tells users *"For an artist: open the artist's profile and go to the 'Social'
tab. For Instagram and TikTok, click 'Connect'…"* — with no mention that only the studio owner sees
those buttons. The standalone manual has 17 Social/Instagram mentions (not individually reviewed).

### 3.7 Not verifiable from source / not yet checked

- Rendered pixel measurements, computed colours and contrast ratios — screenshot only.
- Whether the doubled rule is exactly the nested header (strongly supported by the arithmetic in §4 F-03,
  not confirmed in a browser).
- Whether `ArtistDetailPage` behaves identically under `OwnerLayout` (same header pattern, same nested
  header — expected, not run).
- How `HelpMenu`, `MessagesNavBadge`, `NotificationBell`, `UserMenu` label their buttons.
- Whether unverified (typed, never verified) social handles are rendered on the **public** artist page
  (`ArtistPortfolioPage.tsx` uses the social icons). This matters for D-1 — see §6.
- Whether `IInstagramStateSigner` state has an expiry / nonce / user binding.
- Whether Disconnect / Connect commands are audited (`IAuditableCommand`).
- Current brand-usage rules for the four platform logos (not re-checked; needs a human read).
- Competitor UIs (Fresha, Vagaro, Boulevard, GlossGenius) were **not** re-verified this session; §13
  rests on CLAUDE.md rule 6's comparison set and category knowledge.

---

## 4. Findings register

Severity: **S1** = blocks a user goal or looks broken; **S2** = degrades comprehension/accessibility;
**S3** = polish. "Conf." = confidence the finding is real and correctly diagnosed
(**V** verified in source, **I** inferred from screenshot + source, **U** unverified).

| ID | Sev | Conf. | Finding | Evidence | Workstream |
|---|---|---|---|---|---|
| F-01 | S1 | V | Artist sees a Connect pitch with **no button and no reason** — a dead end | `InstagramTab.tsx:94-111` | B, D |
| F-02 | S1 | V | Four platform rows look like inputs/buttons but are static `—` for artists; no action, no explanation | `SocialLinksCard.tsx:162-163,177` | B, D |
| F-03 | S1 | I | Page's own sticky `<header>` is nested under the app header; when the document scrolls, **Edit / ← Artists / Stop working** are pinned *behind* the app header (z-10 < z-20). Explains the doubled hairline and the avatar sitting ~53 px higher than expected | `ArtistDetailPage.tsx:386-387`; `ArtistLayout.tsx:59,83-88` | A |
| F-04 | S1 | V | Product logic: an owner cannot meaningfully authorize an *artist's* Instagram (OAuth binds whoever consents) — connect/verify belongs to the account holder | §3.2 | D |
| F-05 | S2 | V | Copy is third-person on the artist's own page ("this artist's… their…") | `InstagramTab.tsx:98-100` | B |
| F-06 | S2 | V | X shown as `#`; Facebook as a globe; TikTok as a music note; all placeholder icons, sizes differ from Instagram's `h-10 w-10` | `socialPlatforms.ts:10-16`; `InstagramTab.tsx:97` | B |
| F-07 | S2 | V | Two container styles for peer sections (bare vs `Card`); headings sit closer to the tab bar than to their content | `InstagramTab.tsx:96` (`py-12`); `ArtistDetailPage.tsx:849-855` | B |
| F-08 | S2 | V | Heading levels skip: `h1` → `h3` (no `h2`) in view mode | `ArtistDetailPage.tsx:436,851,855` | A, B |
| F-09 | S2 | V | Tab state not in URL; OAuth return lands on Profile with a success toast for a change visible on another tab; `?instagram=` param never cleared → re-toasts on refresh | `ArtistDetailPage.tsx:211-222,535`; `InstagramEndpoints.cs:87,98,101` | C |
| F-10 | S2 | V | "Not available yet" explanation is `title=` only (not keyboard/touch/AT accessible) | `SocialLinksCard.tsx:202-207` | B |
| F-11 | S2 | V | `—` as an empty value is read as "em dash"/nothing by screen readers | `SocialLinksCard.tsx:163` | B |
| F-12 | S2 | V | `window.confirm` for destructive disconnect; inconsistent with the app's `Dialog` pattern | `InstagramTab.tsx:69`; `SocialLinksCard.tsx:134` | B |
| F-13 | S2 | V | Six tabs at `flex-1` in a `max-w-2xl` column will crush/overflow at phone width | `ArtistDetailPage.tsx:430,536-543` | C |
| F-14 | S2 | V | Profile tab "Schedule" (working hours) collides with sidebar "Schedule" (calendar) | `ArtistDetailPage.tsx:539`; `artistNavSections.tsx:32` | E |
| F-15 | S2 | V | Sidebar group "Clients" contains an item "Clients"; "Reports About Me" and group "Me" are vague | `artistNavSections.tsx:44,53-60,78` | E |
| F-16 | S2 | V | Header feedback icon (`MessageSquareMore`) and Messages entry are visually near-identical; Messages and Notifications also duplicated in the sidebar | `ArtistLayout.tsx:66-78`; `artistNavSections.tsx:46,57` | E |
| F-17 | S2 | I | Header avatar shows "A" (white fill), page avatar shows "AK" (dark fill) — two treatments, two initial rules | `ArtistDetailPage.tsx:432`; `UserChip` | E |
| F-18 | S2 | U | Contrast candidates: sidebar group labels, `—`, "Artist" subtitle, `Ctrl B` chip, inactive tabs, active-item fill (≈1.1:1 boundary) | screenshot only | E |
| F-19 | S3 | V | `Help` text says artists can click Connect; they cannot | `helpContent.ts:1240-1244` | B (docs) |
| F-20 | S3 | I | Whole UI is bold serif at ~14 px, weight is the only hierarchy signal; no accent colour | screenshot | E (design-system, out of scope here) |
| F-21 | S3 | I | No studio/tenant indicator anywhere in the shell; no global search | screenshot | Out of scope (§15) |

---

## 5. Goals and non-goals

**Goals**
1. No role ever sees a promise without either an action or an explanation of who can act (F-01, F-02).
2. An artist can connect/verify **their own** accounts without involving the owner (F-04) — subject to D-1.
3. One consistent connection-row pattern for all five platforms, in one container language (F-06, F-07).
4. The artist profile page has exactly one header, correct heading levels, a correct document title,
   and its toolbar is never hidden (F-03, F-08).
5. Tab position survives refresh and OAuth round-trips (F-09).
6. Every change is keyboard- and screen-reader-operable and passes WCAG 2.2 AA (§11).
7. Help, manual, and tours reflect reality in the same change (CLAUDE.md rule 7).

**Non-goals**
- New OAuth providers, new platforms, or changing how verification works internally.
- Instagram post-sync logic, the nightly sync job, or the public artist page layout.
- A studio switcher, global Ctrl K search, or a typography/theme overhaul (F-20, F-21) — tracked in §15.
- Redesigning `StudioSettings` → Social Media card (studio subject) beyond reusing the new row component.
- Changing which **role** may see the Social tab (unchanged: artist sees own, owner sees all).

---

## 6. Decisions needed

Each has a recommendation; approving §6 as written unblocks everything.

**D-1. Who may connect/verify an artist's social accounts?** *(gates Workstream D)*
- **A (recommended):** Artists may connect, verify, edit the handle of, and disconnect **their own** accounts;
  owners/admins retain today's access to all artists in their studio. Implemented by moving the five
  `OwnerOnly` groups in §3.1 to `ArtistAndAbove` **plus** a handler-level ownership guard identical to
  `ToggleInstagramPostVisibilityHandler` (§3.2). Fixes F-01/F-02/F-04 at the source.
- **B:** As A, but owners may only edit the *typed* handle for other artists, not run OAuth/bio-code
  verification on their behalf — so a **Verified** badge always means the artist proved control.
  More correct, but removes a capability owners have today and changes the meaning of existing data.
  Recommend as a *follow-up*, not now.
- **C:** Keep `OwnerOnly`; ship only the read-only notice + "ask your studio owner" affordance
  (Workstream B). Cheapest; leaves F-04 (owners can't really authorize an artist's account) unresolved.
  This is the **fallback and the mandatory Phase 1 deliverable** regardless of A/B — Workstream B
  ships first and must behave correctly under all three.
- *Blocking check for A/B:* whether **unverified** handles render publicly (§3.7). If they do, artist-editable
  handles let an artist type another person's handle onto their public page. Owners can already do this
  today, so it is not a new class of risk, but confirm and, if needed, render unverified handles without
  the Verified mark or not at all on the public page (separate ticket).
- *Dual-role:* an owner who is also an artist has role `owner`; the guard must not regress that path
  (owner short-circuits the ownership check, like the Toggle handler).

**D-2. Brand icons.** *(gates F-06)*
- **A (recommended):** Add a small `shared/components/icons/brand/` set of inline SVG components
  (Instagram, TikTok, Facebook, X, YouTube) using each platform's official glyph, monochrome via
  `currentColor`, one `size` prop, `aria-hidden`. No new runtime dependency. Requires someone to
  confirm each platform's current logo-usage terms (the existing `socialPlatforms.ts` comment already
  flags this as unresolved).
- **B:** Add an icon-pack dependency (e.g. `simple-icons` / a React wrapper). Faster; adds a dependency and
  still needs the brand-terms check. Licence of the package must be confirmed before adding.
- **C:** Keep Lucide placeholders but fix the worst offender only (drop `Hash` for X, use a text "X" glyph).
  Lowest effort; leaves generic globe/note/camera.

**D-3. Rename "Reports About Me" and the tab "Schedule".** Copy/product owner call.
Recommendation: tab `Schedule` → **`Availability`**; nav "Reports About Me" → **`Conduct Reports`**
(matches the existing route `/conduct-reports`, `ArtistLayout.tsx:41`) — *confirm the product meaning
before renaming; I inferred it from the route name only.*

**D-4. Contrast fixes — how to change tokens.** Decide after the §11 measurement: adjust the shared
`muted-foreground` / sidebar tokens globally (affects every screen; needs a visual regression pass) or
override per component. Recommendation: fix at the token level, once, with a before/after screenshot set.

---

## 7. Requirements

### Workstream A — Page chrome (no decision needed)

**A-1. Remove the nested header and root `min-h-screen`.**
Delete the `<header>` at `ArtistDetailPage.tsx:387-428` and the duplicate at `:361`, and the
`min-h-screen bg-background` root wrapper. The page becomes a fragment/`<main>` that lives inside the
layout's `<Outlet />`. Nothing in the page may be `position: sticky; top: 0`.

**A-2. Replace it with an in-page title row** inside `<main className="max-w-2xl mx-auto px-4 py-8 …">`:

```
[Breadcrumb / back link]
[Avatar 56] [h1 Name]                          [Edit] [overflow ⋯ → Stop working / Delete]
            [subtitle: "Artist · <Studio name>"]
[Tabs …]
```

- **Breadcrumb / back:** for a viewer arriving from the Artists list (owner), show `Artists / <Name>`
  as a `<nav aria-label="Breadcrumb">` with the first crumb linking to `/artists`. For an artist viewing
  their own profile via *My Portfolio*, show **no** back link (there is no list to go back to); the page
  title is the sidebar item's label context.
- **Actions:** `Edit` stays a visible outline button (only when `canManage || isOwnProfile`, unchanged
  logic at `:398`). `Delete` / `Stop working as an artist` moves into a `DropdownMenu` (`⋯`,
  `aria-label="More actions"`), because a destructive action should not sit permanently beside the
  primary one (same rationale as the confirmation dialog already present at `:869-905`, which stays).
- **While editing** (`isEditing`): the `Cancel` ghost button that today lives in the header
  (`:418-427`) moves next to the form's Save button; the title row hides Edit/⋯.
- **Subtitle** requires the studio name. **Verify** that the artist payload or `useGetMyStudioQuery`-style
  data exposes it without a new endpoint; if not, omit the studio name and show only "Artist" — do not add
  a fetch for this alone.

**A-3. Heading levels.** Page title `h1`; tab panels use `h2` for section headings; sub-blocks `h3`.
Concretely `ArtistDetailPage.tsx:851,855` (`h3`) become `h2` (visual size unchanged via class), and the
edit form's `h2 "Edit Artist"` (`:444`) stays `h2`.

**A-4. Document title.** Ensure `document.title` is `"<Name> — TattooOS"` for the artist's own page
and `"<Name> — Artists — TattooOS"` for an owner, replacing the current `"Ali Kreku — Artists"` seen on the
artist's own account. **Find the existing title mechanism first** (route-based or hook) and change it there
rather than adding a second one. *(Mechanism not located during this audit.)*

**A-5. Owner layout.** `OwnerLayout.tsx:238` has the same header pattern, and the owner reaches this
page at `/artists/:id`. Apply A-1…A-4 once, in the page component, so it is correct under both layouts.
Manually verify under both (§12).

**Acceptance (A):**
1. At scrollTop 0 and at maximum scroll, exactly one horizontal header rule is visible under the app
   header, on both `ArtistLayout` and `OwnerLayout`.
2. `Edit` is visible and clickable at every scroll position for `owner` and own-profile `artist`.
3. The document is not taller than `viewport height − app header` solely because of a nested `min-h-screen`
   (page with short content has no vertical scrollbar).
4. Heading outline (axe / `getByRole('heading')` levels) is `h1 → h2 → h3` with no skips on every tab.
5. Keyboard: Tab order is breadcrumb → Edit → ⋯ → tabs → tab panel; `⋯` menu opens with Enter/Space and
   closes with Esc returning focus to the trigger.

### Workstream B — Social tab rebuild

**B-1. One row pattern for all five platforms** — new `ConnectionRow` in
`features/social/components/ConnectionRow.tsx`, used by `SocialLinksCard` **and** by the top of the
Instagram section so Instagram stops being a special-cased blob.

Row anatomy (one `Card`, `p-4`, `flex items-center gap-3`, wraps on narrow widths):

```
[Icon 20]  [Label (font-medium)               ]  [Status badge]  [Primary action]
           [Secondary line: @handle | helper   ]
```

- Icon: platform brand icon (D-2), `h-5 w-5`, `aria-hidden`, **identical size for Instagram**.
- Label: platform name. Secondary line: `@handle` (with `VerifiedSocialBadge` when verified), or helper text.
- **Status badge** (text + icon, never colour alone): `Verified`, `Handle added` (unverified),
  `Not linked`, `Unavailable`. (A `Needs reconnection` state is out of scope — the backend does not
  currently expose token expiry; do not invent it.)
- **Primary action** by state — see §8's matrix. Only one primary button per row.
- The row is a `<li>` inside a `<ul aria-label="Connected accounts">`; the whole section is
  `<section aria-labelledby="social-heading">` with an `h2 id="social-heading"`.

**B-2. Section structure** (replaces the two `h3` blocks at `ArtistDetailPage.tsx:849-863`):

```
h2  Connected accounts
p   (helper) See §9 copy — one sentence: where these appear.
ul  Instagram row   → when connected, expands to the sync panel below it
    TikTok / Facebook / X / YouTube rows
(if isOwnProfile || canManage) link: "View public profile" (opens /artists/<slug> — see B-6)
```

The Instagram *post grid* (visibility toggles, `InstagramTab.tsx:147+`) moves into a collapsible/inline
"Synced posts" panel directly under the Instagram row, shown only when connected. Its behaviour is
unchanged.

**B-3. Spacing (Tailwind, 4 px grid).** Tab bar → first heading: `mt-6`. Heading → content: `mb-3`.
Section → section: `space-y-8`. Row → row: `space-y-3` (unchanged). Empty state no longer exists as a
free-floating `py-12` block (`InstagramTab.tsx:96`); it becomes the Instagram *row* in state `Not linked`.
This fixes F-07 (heading now closer to its content than to the tabs).

**B-4. Read-only state (viewer cannot manage).** When `!canManage` for the subject, rows render the
same anatomy with the action slot replaced by a single, non-interactive line under the label:
`Managed by your studio owner` (own profile) or nothing (owner/admin never lands here). If D-1 = A this
state only occurs for an artist viewing **another** artist's profile — which the artist role cannot do — so
it becomes a defensive branch; keep it and its test regardless (Workstream B ships under D-1 = C).
The section helper carries the reason once, not per row:
`Your studio owner manages connections for your profile. Ask them to connect these accounts.`

**B-5. "Unavailable" row (`isOAuthConfigured === false && isManualCheckSupported === false`).**
Replace the `title=` tooltip with visible secondary text: `Not available on this server yet.`
(`SocialLinksCard.tsx:202-207`). Fixes F-10.

**B-6. Public-profile affordance.** Add `View public profile` (text link with external-link icon,
`target="_blank" rel="noopener noreferrer"`) when the artist has a slug — reuse the same source the page
already uses at `ArtistDetailPage.tsx:595` (`isOwnProfile && artist.slug`). Do not add a fetch.

**B-7. Destructive confirmation.** Replace both `window.confirm` calls with the app's `Dialog`
(`DialogTitle: "Disconnect <Platform>?"`, description per §9, footer `Cancel` (ghost) +
`Disconnect` (destructive, autofocus **Cancel**)). Extract one `ConfirmDisconnectDialog` used by both
components. Fixes F-12.

**B-8. Handle input.** Keep save-on-blur, but add: a visible `@` prefix adornment, `autoCapitalize="off"`,
`autoCorrect="off"`, `spellCheck={false}`, inline validation message (`aria-describedby`) on failure
instead of only a toast, and `aria-live="polite"` "Saved" confirmation. Keep `aria-label="<Platform> handle"`.
Empty-state placeholder text must not be the only label (it isn't today; keep it that way).

**B-9. Icon component.** `socialPlatforms.ts` exports `SOCIAL_PLATFORM_ICON` typed as `LucideIcon`.
Change the type to a shared `SocialIcon = React.ComponentType<{ className?: string }>` so brand SVGs and
Lucide icons are interchangeable, keep `AtSign` etc. as the D-2 = C fallback. `Hash` for X must not ship
under any option.

**B-10. Extract.** Move the Social tab out of the 900-line `ArtistDetailPage.tsx` into
`features/artists/components/ArtistSocialTab.tsx` taking `{ artistId, slug, canManage, isOwnProfile }`.
This is a mechanical extraction done in the same PR because the block is being rewritten anyway.

**B-11. Loading state.** Single skeleton for the whole section (five row-shaped placeholders, matching the
row height so there is no layout shift when data arrives), replacing the two independent skeleton sets.

**B-12. Error state.** If `useGetSocialLinksQuery` / `useGetInstagramStatusQuery` errors, render an inline
`Alert` inside the section: `We couldn't load your connected accounts.` + `Try again` (calls `refetch`).
Do not fall through to "Not linked" on error — that would present a false empty state (the current
`!status?.isConnected` branch does exactly that on error, `InstagramTab.tsx:94`).

**Acceptance (B):**
1. Artist (D-1 = C): every row shows a status and the section shows the "managed by your studio owner"
   line; no row shows a bare `—`; there are no interactive-looking controls that do nothing.
2. Owner: every row shows exactly one primary action appropriate to its state (§8).
3. All five icons are the same rendered size and none is `Hash`/`Globe`/`Music2`/`Video`
   when D-2 = A or B.
4. No `window.confirm` remains in `features/social` or `features/artists`.
5. A failed status request shows the error alert, not the "Not linked" state.
6. Axe reports zero violations on the Social tab in each state of §8.

### Workstream C — Tab state and OAuth landing

**C-1. URL-backed tabs.** Make `Tabs` controlled: value derived from `?tab=` validated against
`["profile","portfolio","hours","bookings","designs","social"]` (unknown/absent → `profile`); changing
tab calls `setSearchParams(next, { replace: true })` so Back doesn't step through tab history.
Preserve unrelated params.

**C-2. OAuth landing.** In the existing `useEffect` (`ArtistDetailPage.tsx:211-222`): when `instagram` or
`social` is present, (1) fire the toast, (2) set `tab=social`, (3) **delete** `instagram`, `social` and
`platform` from the params via `setSearchParams(..., { replace: true })` so a refresh does not re-toast.
No backend redirect change is required. (`?instagram=denied` currently redirects to `/artists`, not the
artist page — `InstagramEndpoints.cs:87` — so an artist who cancels lands on the artists list, which the
artist role may not reach meaningfully; **if D-1 = A, change that redirect to
`/artists/{artistId}?instagram=denied` when the state is valid, else fall back to `/artists`.** That
requires the state to be decodable before the `error` branch — check `IInstagramStateSigner`'s API.)

**C-3. Tab strip responsiveness.** Below `sm` (<640 px): `TabsList` becomes horizontally scrollable
(`overflow-x-auto`, `flex-none` triggers with `whitespace-nowrap`, scroll-snap, right-edge fade,
active trigger `scrollIntoView({ inline: "center", block: "nearest" })` on change). At `≥sm` keep the
current `flex-1` equal-width layout. Trigger min height 40 px (36 today, screenshot-estimated).

**Acceptance (C):**
1. Refresh on `/artists/:id?tab=social` stays on Social; invalid `tab` falls back to Profile.
2. Completing a (mocked) OAuth return `?instagram=connected` lands on Social, toasts once, and the URL
   no longer contains `instagram`; refresh does not re-toast.
3. At 375 px width the six tabs are reachable without page-level horizontal scroll, and the active tab is
   always fully visible.

### Workstream D — Artist self-service (only after D-1 = A)

**D-1a. Policy changes** (`InstagramEndpoints.cs:17,22`; `SocialEndpoints.cs:20-29`) — the five artist-scoped
`OwnerOnly` policies → `ArtistAndAbove`. **Do not** touch the `studios/{id}/social` group
(`SocialEndpoints.cs:31-45`), which stays `OwnerOnly`.

**D-1b. Handler ownership guard (mandatory, same change).** In each corresponding handler
(`GetInstagramConnectUrlHandler`, `DisconnectInstagramHandler`, and the Social connect-url / update-handle /
request-code / verify-code / disconnect handlers) add, after the tenant-scoped existence check:

```csharp
if (currentUser.Role == "artist")
{
    bool ownsProfile = await db.Artists
        .AnyAsync(a => a.Id == request.ArtistId && a.UserId == currentUser.UserId, ct);
    if (!ownsProfile) throw new ForbiddenException();
}
```

Extract this into one shared helper (e.g. `IArtistOwnershipGuard.EnsureCanActAsync(artistId, ct)`) so the
seven call sites cannot drift, and so `ToggleInstagramPostVisibilityHandler` can adopt it too. **Without
this guard, loosening the policy would let any artist mint a connect URL for a colleague in the same
studio** (the tenant filter scopes to the studio, not to the person).

**D-1c. Tenant scope.** Every query still goes through `db.Artists` (global tenant filter). No
`IgnoreQueryFilters()`. The `admin` role behaviour is unchanged.

**D-1d. Frontend gating.** Replace `canManage = usePermission(Role.Owner)` as the source of
`canConnect`/`canManage` on this tab with `canManageSocial = canManage || isOwnProfile`
(`ArtistDetailPage.tsx:181,224` already compute both). Update the doc comments at
`InstagramTab.tsx:26` and `SocialLinksCard.tsx:38-45`, which currently assert "Owner-only".

**D-1e. Auditing.** If Connect/Disconnect/verify commands are not already `IAuditableCommand`, add it.
Metadata is whitelisted via `AuditMetadataBuilder` and **must not include the handle or any name**
(CLAUDE.md rule 3); log `platform`, `artistId`, `tenant_id`, `user_id`, `request_id`, and outcome only.

**D-1f. Hardening (optional, separate ticket).** If `IInstagramStateSigner` lacks expiry/nonce, bind the
signed state to `{artistId, initiatingUserId, issuedAt}` and reject on mismatch/expiry in the callbacks
(`InstagramEndpoints.cs:77-101`, `SocialEndpoints.cs` `HandleCallback`). Matters more once artists — not
just owners — can initiate.

**Acceptance (D):** artist A can connect/verify/disconnect A's accounts (200/204); artist A gets 403 for
artist B's id in the same studio, for **each** of the seven operations; artist A gets 404 for an artist in
another studio (tenant filter); owner and admin behaviour unchanged; dual-role owner unchanged.

### Workstream E — Adjacent hygiene (independent, small, optional PRs)

**E-1. Nav.** `artistNavSections.tsx:53-60`: rename the group so it doesn't echo its child (e.g. group
label `People`, item stays `Clients`) **or** drop the group label and keep three flat items — pick the
former to preserve the grouping rhythm. Rename `Reports About Me` per D-3. Keep `tourId`s untouched.

**E-2. Tab rename.** `ArtistDetailPage.tsx:539` `Schedule` → `Availability` (D-3). The `value="hours"`
key stays (URL stability, C-1).

**E-3. Header icons.** Give the feedback button a distinct glyph from Messages (pick one that exists in the
installed `lucide-react`; verify before choosing), and confirm every icon-only header control exposes an
accessible name and a visible tooltip. Confirm unread badges appear at count > 0 (do not change if they do).

**E-4. Avatar consistency.** Make the header `UserChip` and the profile page use the same initials
function. `UserChip` only has `given_name` from the JWT; **do not** add a JWT claim or a fetch for this —
if a family-name source isn't already present, keep the header at one initial and change the *page* avatar to
match the fill style only. (The name-follows-edit problem for the header is a separate, already-tracked
item and out of scope.)

**E-5. Contrast + tokens.** Per §11.

---

## 8. State matrix (Social tab)

Dimensions: **viewer** (`owner/admin` · `artist-own` · `artist-other` (defensive)) ×
**row state** (`not-linked` · `handle-added` · `verified` · `unavailable`) × **transient**
(`loading` · `error` · `saving` · `connecting`).

| Viewer | Row state | Secondary line | Badge | Primary action | Also |
|---|---|---|---|---|---|
| owner/admin, or artist-own when D-1 = A | not-linked, OAuth configured | "Connect to show a Verified badge." | Not linked | **Connect** (↗) | Handle input available for manual path |
| same | not-linked, manual supported, OAuth not | handle `<Input>` | Not linked | **Get verification code** (disabled until handle non-empty) | |
| same | handle-added | `@handle` | Handle added | **Verify** (OAuth or code, whichever is available) | secondary: Remove handle (only if the API supports clearing — *verify*; else omit) |
| same | verified | `@handle` + Verified badge | Verified | **Disconnect** (ghost/destructive → dialog B-7) | |
| same | unavailable | "Not available on this server yet." | Unavailable | none | |
| artist-own when D-1 = C (or artist-other) | any | `@handle` or "Not linked" | per state | **none** — section helper explains | |
| any | loading | — | — | — | five row skeletons, same height |
| any | error | — | — | **Try again** | inline Alert (B-12) |
| any | connecting | "Opening Instagram…" | — | button `disabled`, `aria-busy` | popup opened synchronously per existing pattern (`InstagramTab.tsx:49-66`) — keep |
| any | saving handle | "Saving…" → "Saved" (`aria-live="polite"`) | — | — | |

Instagram-only extension when `verified/connected`: `Last synced <date>`, post-count badge, `Synced posts`
panel (grid + per-post visibility switch) — unchanged behaviour; `canManagePosts` still governs the switches.

---

## 9. Copy deck

Own-profile (artist) uses second person; owner viewing an artist uses the artist's first name. All strings
sentence case; no exclamation marks except where an existing toast has one and tests assert it.

| Slot | Own profile | Owner viewing artist |
|---|---|---|
| Section h2 | Connected accounts | Connected accounts |
| Section helper (can manage) | Link your accounts so clients can find you and see a Verified badge on your public profile. | Link {First}'s accounts so clients can find them and see a Verified badge on their public profile. |
| Section helper (cannot manage) | Your studio owner manages connections for your profile. Ask them to connect these accounts. | — (n/a) |
| Instagram, not linked | Connect Instagram to automatically show your latest posts on your public portfolio. | Connect {First}'s Instagram to automatically show their latest posts on their public portfolio. |
| Instagram, connected | Last synced {date} · {n} posts | same |
| Handle row, not linked (manage) | (input) placeholder `yourhandle` | placeholder `handle` |
| Handle row, not linked (read-only) | Not linked | Not linked |
| Unavailable | Not available on this server yet. | same |
| Disconnect dialog title | Disconnect {Platform}? | same |
| Disconnect dialog body (IG) | Your synced posts stay on your portfolio, but no new posts will be fetched. | Synced posts stay on {First}'s portfolio, but no new posts will be fetched. |
| Disconnect dialog body (other) | Your handle stays, but the Verified badge will be removed. | The handle stays, but the Verified badge will be removed. |
| Connect error toast | Couldn't start the {Platform} connection. Try again. | same |
| Pop-up blocked toast | Pop-up blocked. Allow pop-ups for this site and try again. | same |
| Section load error | We couldn't load your connected accounts. | We couldn't load {First}'s connected accounts. |
| Public profile link | View public profile | View public profile |

Existing test at `InstagramTab.test.tsx:138` asserts the old string; it changes with B-1/B-2.
`window.confirm` copy at `InstagramTab.tsx:69` and `SocialLinksCard.tsx:134` is the source of the dialog
bodies above.

---

## 10. Responsive and layout spec

- Content column stays `max-w-2xl` (≈640 px). No change to the shell.
- **≥ 640 px:** row = single line (icon · label+secondary · badge · action). Tabs equal-width.
- **< 640 px:** row wraps — icon+label+badge on line 1, secondary line on line 2, action full-width
  (`w-full`, min-height 40 px) on line 3. Tabs scroll horizontally (C-3). Title row stacks: avatar+name on
  top, actions below; `Edit` full-width.
- **Never** rely on hover for any information; status and reasons are always visible text.
- Sidebar behaviour is untouched here; the mobile nav drawer's existing tour z-index issue is a separate
  known defect and not addressed by this spec.

---

## 11. Accessibility and contrast

**Target:** WCAG 2.2 AA on the artist profile page, every tab, every state in §8.

- **Contrast (SC 1.4.3, 1.4.11).** Measure, do not guess. Script (Playwright): for each selector below,
  read `getComputedStyle` `color` and the effective background (walk ancestors to the first non-transparent
  `background-color`; composite alpha), compute the WCAG relative-luminance ratio, and fail the run under
  4.5:1 (text < 18.66 px bold / < 24 px regular), 3:1 (large text, UI component boundaries, focus
  indicators). Selectors to measure: sidebar group labels; sidebar item (inactive/active, plus the active
  fill vs. page background as a *boundary* per 1.4.11 only if the fill is the sole indicator); tab triggers
  (inactive/active); `Ctrl B` chip; header user subtitle ("Artist"); row secondary lines; badges; helper
  text; placeholder text; disabled buttons (exempt from 1.4.3 but should still be legible). Run in both
  the dark and light themes. Commit the script and its threshold table with the PR.
- **Names/roles.** Every icon-only control has an accessible name; decorative icons `aria-hidden`
  (existing convention — keep). The `⋯` menu has `aria-label="More actions"`. Tabs use the Radix
  `tablist/tab/tabpanel` roles already provided; confirm `aria-controls`/labelling survives the controlled
  `value` change (C-1).
- **Status is never colour-only** (SC 1.4.1): every badge has text; the Instagram pink accent
  (`text-pink-500`, `InstagramTab.tsx:118`) is decorative only and must not carry meaning.
- **Live regions.** Save confirmation and connect/disconnect outcomes announced via `aria-live="polite"`
  (toasts from `sonner` already announce; the inline "Saved" needs its own region).
- **Focus (SC 2.4.7, 2.4.11).** Visible focus ring on rows' controls, tabs, ⋯ trigger; dialog focus trap
  and return-focus per Radix; the disconnect dialog autofocuses **Cancel**. Ensure the sticky app header
  does not obscure a focused element on scroll (`scroll-margin-top` ≥ header height on focusable
  regions if needed — this is the accessibility face of F-03).
- **Target size (SC 2.5.8, AA = 24×24).** Ensure ≥ 24×24 everywhere; use 40 px minimum for new
  row actions and tabs; 44 px for the mobile drawer items is a recommendation, not an AA requirement.
- **Reflow (SC 1.4.10).** No horizontal page scroll at 320 px; only the tab strip scrolls internally.
- **Headings (SC 1.3.1 / 2.4.6).** Outline per A-3.

---

## 12. Test plan

**Frontend unit (Vitest + Testing Library) — *note: these pass without proving the real thing works;
see e2e and manual below.***
- `ConnectionRow`: renders each state in §8; no interactive control for read-only; badge text always
  present; `Connect` calls the lazy connect-URL query and opens the popup synchronously.
- `ArtistSocialTab`: viewer permutations; error state doesn't render "Not linked"; skeleton row count.
- `ConfirmDisconnectDialog`: Cancel is initial focus; confirm fires the mutation; no `window.confirm`
  is called (spy on `window.confirm` and assert 0 calls).
- URL tabs (C-1/C-2): with `MemoryRouter` — invalid tab fallback, param cleanup on OAuth return,
  toast fires exactly once.
- Update `InstagramTab.test.tsx:138` for the new copy; delete tests that assert the old bare empty state.
- Heading-level test on the page (`h1` then `h2`, no `h3` before an `h2`).

**Backend unit (Application layer — required by CLAUDE.md).** For each of the seven handlers in D-1b:
artist-own succeeds; artist-other → `ForbiddenException`; owner/admin succeed for any artist in tenant;
artist not in tenant → `NotFoundException`; the shared guard has its own tests.

**Backend integration (real MySQL — required).** Green unit tests don't exercise the real DI container
or the global query filter; this repo has been bitten by that (a missing `HasQueryFilter` was only caught
by a real-DB test). Add: two studios × two artists; assert the 403/404 matrix over HTTP with real JWTs for
each of the five artist-scoped Social endpoints and both Instagram endpoints; assert
`/studios/{id}/social/*` is still `OwnerOnly` for an artist.

**E2E (Playwright, real browser).** (1) Artist logs in → My Portfolio → Social: sees rows + correct
state, no dead pitch. (2) Owner sees Connect. (3) `?tab=social` deep link and refresh. (4) Mocked OAuth
return lands on Social, toasts once, URL cleaned. (5) At 375 px: tabs scroll, rows wrap, no page-level
horizontal scroll. (6) Edit button visible/clickable at scrollTop 0 and at max scroll, under **both**
`ArtistLayout` and `OwnerLayout`. Run against the built app (`pnpm build` catches `tsc` errors Vitest
misses) and confirm e2e separately from unit — "vitest green" is not "e2e green".

**Manual browser pass (required — this touches layout, auth-gated UI and OAuth).** One human pass on
staging as artist, owner, and dual-role owner; cover light and dark; keyboard-only run through the page;
one screen-reader spot check (NVDA or VoiceOver) of the Social tab.

**Accessibility automation.** `@axe-core/playwright` on the Social tab in each §8 state; zero violations.
Plus the §11 contrast script.

**Visual regression.** Before/after screenshots at 1440, 1024, 768, 375 px for the Social tab (4 states)
and the page header (own profile, owner view).

---

## 13. Industry benchmark (CLAUDE.md rule 6)

**Caveat:** competitor UIs were not re-verified in this session; the claims below are the category
pattern to check against, not verified screenshots. Re-check Fresha, Vagaro, Boulevard and GlossGenius'
current staff-profile / connected-accounts screens before sign-off.

Category expectations this spec targets:
1. A connection is a **row with a status and one action**, not a paragraph of pitch. (B-1, B-2)
2. Permission limits are **explained where they bite**, with a route to someone who can act. (B-4)
3. The person who owns an external account is the one who authorizes it. (D-1)
4. A connected state shows **last sync and a way to disconnect**, confirmed in a themed dialog. (B-7)
5. Staff profile pages sit inside the app shell with **one** header, a breadcrumb, and an overflow menu for
   destructive actions. (A-2)
6. Tab position is **linkable** and survives refresh/OAuth. (C-1, C-2)
7. Platform-admin equivalents (support tooling, audit log) are unaffected; audit entries for
   connect/disconnect satisfy the platform-admin expectation for traceability. (D-1e)

**Gaps deliberately left open (flagged, per CLAUDE.md "never silently ship a substandard pattern"):**
no studio/tenant switcher or studio name in the shell, no global Ctrl K search, no token-expiry /
"needs reconnection" state, no per-platform brand-approved visual system, no typography/design-token
overhaul. See §15.

---

## 14. Help, manual and tour sync (CLAUDE.md rule 7 — same change)

- `helpContent.ts:1230` and `:1240-1244`: rewrite. For an artist: *"Open **My Portfolio** → **Social**.
  Connect Instagram or TikTok to prove the account is yours, or type your handle for Facebook, X or
  YouTube and verify it with a code in your bio."* (D-1 = A) — **or**, if D-1 = C: *"Your studio owner
  connects and verifies your accounts. You can see their status on My Portfolio → Social."* Also
  document: the new tab deep-link, the disconnect dialog, the read-only line.
- `frontend/public/user-manual/index.html`: update each of the 17 Social/Instagram mentions that describe
  who connects what, the tab name (`Schedule` → `Availability` if D-3 approved), and add the breadcrumb /
  ⋯ menu location of **Stop working as an artist**.
- `frontend/src/features/help/tours/*.ts`: **not inspected in this audit.** Search `artistTour.ts` and
  `ownerTour.ts` for steps anchored on the artist profile page, the tab labels, "My Portfolio", "Reports
  About Me", or the removed page header; fix any that break. Do not change existing `tourId`s in
  `artistNavSections.tsx` unless the step is updated in the same commit.
- CI has a "Help stays in sync" gate; do not use `[skip-help-sync]`.

---

## 15. Out of scope / follow-ups (each needs its own spec or ticket)

1. Studio name / switcher in the sidebar header (multi-studio users can't tell which studio they're in).
2. Global quick-search (Ctrl K).
3. Typography and design-token pass (serif-everywhere, absent accent colour, weight hierarchy) — needs
   a design decision, not an engineering one.
4. "Needs reconnection" state (requires the backend to expose token expiry).
5. D-1 option B (owner may not verify on an artist's behalf) — after A has shipped and been observed.
6. Public-page treatment of **unverified** handles (see §3.7 / D-1 blocking check).
7. Signed-state hardening (D-1f) if not folded into Workstream D.
8. Header name following a client/artist edit, and the mobile-drawer tour z-index defect — both already
   known and unrelated to this spec.

---

## 16. Open verification items (do these first — each is ≤ 15 minutes)

1. Reproduce F-03 in a browser: at scrollTop 0 and scrolled to bottom, as `artist` and as `owner`; record
   whether Edit/← Artists are hidden and whether the layout scrolls with short content.
2. Locate the `document.title` mechanism (A-4).
3. Inspect `HelpMenu`, `MessagesNavBadge`, `NotificationBell`, `UserMenu` for accessible names (E-3).
4. Inspect `IInstagramStateSigner` for expiry/nonce/user binding (D-1f, C-2's `denied` redirect).
5. Check whether Connect/Disconnect/verify commands implement `IAuditableCommand` (D-1e).
6. Check whether unverified handles render on `ArtistPortfolioPage` (D-1 blocking check).
7. Confirm the studio name is available on the artist page payload (A-2).
8. Read the current brand-usage terms for the four logos (D-2).
9. Grep `artistTour.ts` / `ownerTour.ts` for steps affected by A/E (§14).
10. Confirm whether the API supports clearing a handle (§8 "Remove handle").

---

## 17. Delivery plan

| PR | Scope | Depends on | Size | Risk |
|---|---|---|---|---|
| **PR-1** | Workstream A (chrome) + C (URL tabs, OAuth landing, tab strip) | verification items 1, 2, 7 | S–M | Low; tour anchors, e2e selectors that assume the old header |
| **PR-2** | Workstream B (Social tab rebuild), D-2 icons, help/manual updates for read-only behaviour. Works under D-1 = C | PR-1 (heading levels), D-2 | M | Low–Med; icon licensing |
| **PR-3** | Workstream D (permissions + guard + tests + audit + help update) | D-1 = A, verification items 4–6 | M | **Med–High**: authorization change; needs the integration matrix and a manual pass |
| **PR-4** | Workstream E (nav rename, header glyph, avatar consistency, contrast/tokens) | D-3, D-4, §11 measurement | M | Low functionally, but tokens touch every screen — visual regression required |

PR-1 should go first because it fixes a bug (F-03) that can hide the Edit button for every artist and owner
using the profile page, independent of everything else.

Each PR: updates Help (`helpContent.ts`, manual, tours) in the same change; adds tests per §12; has a
manual browser pass recorded in the PR description; and after merge, is checked on **staging and production**
(this repo has had staging-only fixes that also needed a production check).

## 18. Risks

| Risk | Mitigation |
|---|---|
| Loosening `OwnerOnly` without the ownership guard lets any artist act on a colleague's profile | D-1b is mandatory in the same PR; the 403 matrix is an integration test, not a unit test |
| Dual-role owner (owner who is also an artist) regresses | Owner short-circuits the guard; test the dual-role account explicitly |
| Removing the nested header breaks e2e tests / tour steps that select it | Grep `getByRole('banner')`, `header` selectors, tour anchors before merging; update in the same PR |
| Brand icons introduce licence or trademark exposure | D-2 blocking check; default to inline SVG from official assets after the terms are read |
| Token-level contrast changes shift every screen | §12 visual regression at four widths; ship PR-4 separately |
| Mis-attributing F-03 (screenshot inference) | Verification item 1 runs before PR-1 is written; if the nested header is not the cause, the rest of A still stands (it is a real defect by source alone) |
| Controlled `Tabs` + `replace` navigation interferes with the tour library or drawer | Manual pass with the onboarding tour active |

## 19. Files expected to change

**Backend:** `Pena_e_Arte.API/Endpoints/InstagramEndpoints.cs`, `SocialEndpoints.cs`;
`Pena_e_Arte.Application/Instagram/{Queries/GetInstagramConnectUrlQuery.cs, Commands/DisconnectInstagramCommand.cs,
Commands/ToggleInstagramPostVisibilityCommand.cs}`; `Pena_e_Arte.Application/Social/Commands/*` (connect-url,
update-handle, request-code, verify-code, disconnect); new shared guard; new/updated tests in the Application
and integration test projects.

**Frontend:** `features/artists/components/ArtistDetailPage.tsx` (large edit + extraction),
new `ArtistSocialTab.tsx`, `InstagramTab.tsx` (posts panel only), `features/social/components/SocialLinksCard.tsx`,
new `ConnectionRow.tsx`, `ConfirmDisconnectDialog.tsx`; `shared/utils/socialPlatforms.ts`;
new `shared/components/icons/brand/*` (D-2 A); `layouts/artistNavSections.tsx`;
`layouts/ArtistLayout.tsx` (header glyph only); tests under `features/artists/__tests__` and
`features/social/__tests__`; Playwright specs.

**Docs/Help:** `features/help/helpContent.ts`, `public/user-manual/index.html`, `features/help/tours/*.ts`
(as found), `docs/claude/architecture.md` Decisions Log entry for D-1.
