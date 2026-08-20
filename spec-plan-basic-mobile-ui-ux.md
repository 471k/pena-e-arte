# Spec Plan — Basic Mobile UI/UX Standard

**From:** Engineering consultation (Pena e Artë — Engineering Consultation project)
**Branch suggestion:** `feat/mobile-ui-ux-baseline`
**Read first:** `docs/claude/frontend.md`, `docs/claude/conventions.md`, `docs/claude/architecture.md` (Feature Module Map + Decisions Log) — this spec was scoped by reading the current four role layouts, `DataTable.tsx`, the `shared/components/ui/` primitives, and the 2026-07-20 industry-parity audit (F12), not written from scratch. It slots into existing patterns rather than inventing new ones.

**Not re-litigating:** `industry-feature-parity-report-2026-07-20.md` item F12 already flagged "Mobile responsiveness" as PARTIAL/P2 and shipped a narrow CSS-only fix (issuer action-button `flex-wrap`, `HelpInsightsPage.tsx`'s `overflow-x-auto` table wrapper). That fix is still correct and untouched by this spec. What follows is the broader surface F12 didn't cover: navigation architecture, touch-target consistency, and the shared `DataTable` primitive — none of which were in scope for that audit item.

---

## Business context (why)

CLAUDE.md rule #6 requires every frontend surface to match current vertical-SaaS UX standards (Fresha, Vagaro, Boulevard, Mindbody, GlossGenius tier) for every role. All of those products treat phone-width usage as a first-class case — clients book from their phone, artists check their schedule between sessions, owners glance at the dashboard from the studio floor. Right now this app has no *written* mobile standard anywhere in `docs/claude/`, and the mobile behavior that exists was added reactively, page by page, as bugs rather than as a baseline. That shows up as real inconsistency (documented below with file:line evidence), and it means every new page shipped without this spec re-derives — or skips — mobile handling from scratch.

This spec defines the baseline (breakpoints, touch-target minimum, the one navigation pattern all four role layouts should share, and a responsive fallback for the shared `DataTable`), then hands off concrete, file-level changes so an overnight prompt can execute it mechanically.

---

## What already exists and doesn't need to change

Read this before writing code — most of the primitives this needs are already in the design system:

- **`shared/components/ui/sheet.tsx`** — a Radix-based slide-over panel, already proven on mobile in production: `StudioNotificationSheet` (My Studios overflow menu, shipped 2026-07-05) uses it successfully. This is the component to reuse for the nav drawer below — do not add a new drawer/off-canvas library.
- **Breakpoint vocabulary already in use** — the codebase already treats `sm` (640px) and `lg` (1024px) as the operative breakpoints in several places: `ClientLayout.tsx`'s `hidden sm:inline` / `sm:hidden` label swap, its `py-2.5 sm:py-1.5` touch-target bump, and `ArtistPortfolioPage.tsx`'s `lg:hidden` sticky mobile Book CTA (architecture.md Decisions Log, "Sticky Book CTA on mobile", 2026-07-04). This spec formalizes those two breakpoints as the standard rather than introducing new ones.
- **44px touch-target precedent already established once** — architecture.md's Decisions Log (2026-07-04, "My Designs Page — UX Audit Fixes", item 7) already states the rule in prose: *"`py-1.5` → `py-2.5 sm:py-1.5` ensures mobile touch targets are ≥40px at the breakpoint where short labels are active."* This spec just needs to (a) apply that same fix to the three layouts that never got it, and (b) write the rule down somewhere durable (`conventions.md`) instead of leaving it as a one-off comment in a changelog only `ClientLayout.tsx` obeys.
- **`overflow-x-auto` table pattern already exists once** — `HelpInsightsPage.tsx` already wraps its table for horizontal scroll (per F12). That pattern is fine for wide-but-few-column tables; it is not a substitute for the responsive card fallback this spec adds to `DataTable.tsx` itself, which is used by three list pages with row-click affordances that don't work well as a horizontally-scrolled table on a 375px screen.

---

## Gaps identified (verified against live source, 2026-08-20)

### 1. All four role layouts share one nav pattern, and it doesn't scale past phone width

`ClientLayout.tsx`, `ArtistLayout.tsx`, `OwnerLayout.tsx`, and `IssuerLayout.tsx` (`frontend/src/layouts/`) each render:

```tsx
<nav className="ml-6 flex items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
  {NAV_ITEMS.map(...)}
</nav>
```

This is a horizontally-scrolling strip of icon+label pills inside the sticky header — not a hamburger menu, not a bottom tab bar, not a collapsible drawer. It "works" only because a user can side-scroll to find an item, with no visual affordance telling them the strip scrolls. Item count makes this materially worse for some roles than others:

| Layout | Nav items | Label behavior at phone width |
|---|---|---|
| `ClientLayout` | 6 | Icon + short label (`shortLabel ?? label`) — already responsive |
| `ArtistLayout` | 7 (+1 conditional "My Portfolio") | Icon + **full label always** |
| `OwnerLayout` | 9 | Icon + **full label always** |
| `IssuerLayout` | 10 (one with a numeric badge) | Icon + **full label always** |

Only `ClientLayout` implements the short-label pattern. The other three always render the full label text next to the icon, so their nav strips are proportionally *wider* per item — Issuer's 10-item bar with full labels is the worst case in the app, and it's the role most likely to be triaging on a phone away from a desk. The architecture.md Decisions Log entries for "Mobile nav overflow on `ArtistLayout`" (P1.2) and "same mobile-nav-overflow gap already fixed in `ArtistLayout`" (OwnerLayout) treated *scrolling without clipping* as the fix — they did not address discoverability, which is the actual UX problem: a new artist/owner/issuer has no way to see their full nav without scrolling sideways through a strip with no scroll indicator.

**This is the primary decision point in this spec** — see "Proposed standard" below for the recommended fix (drawer nav using the existing `Sheet` primitive) and why a bottom tab bar was not chosen.

### 2. Touch-target fix was applied once and never propagated

`ClientLayout.tsx` line 53 uses `px-3 py-2.5 sm:py-1.5` on its `NavLink` — a deliberate mobile touch-target bump per the Decisions Log entry cited above. `ArtistLayout.tsx` line 62, `OwnerLayout.tsx` line 66, and `IssuerLayout.tsx` line 50 all still use `px-3 py-1.5` unconditionally — no phone-width bump. At `py-1.5` with `text-sm` (14px) content, the rendered pill height is roughly 30–32px, below both the iOS HIG (44pt) and Material (48dp) minimum, and below the very rule this codebase already wrote down for itself once.

### 3. `DataTable.tsx` has zero responsive behavior

`frontend/src/shared/components/DataTable.tsx` renders a bare `<Table>` (`shared/components/ui/table.tsx`) with no wrapping `overflow-x-auto`, no `min-width`, and no narrow-viewport fallback. It's used by:
- `frontend/src/features/artists/components/ArtistListPage.tsx`
- `frontend/src/features/clients/components/ClientListPage.tsx`
- `frontend/src/features/payments/components/PaymentListPage.tsx`

Any of these with more than 3–4 columns will either overflow the viewport horizontally with no scroll affordance, or squeeze columns unreadably narrow — there is no card-per-row fallback the way `HelpInsightsPage.tsx` (a different, one-off table) already does with `overflow-x-auto`. This is the shared primitive three list pages depend on, so fixing it here fixes all three at once instead of patching each page separately.

### 4. No written mobile standard exists

`conventions.md`, `frontend.md`, and `architecture.md`'s own "Component Rules" section are all silent on breakpoints, touch-target minimums, or which component to reach for on mobile (drawer vs. sheet vs. accordion vs. stacked card). Every mobile fix found in the Decisions Log — nav overflow (×2), sticky Book CTA, touch-target bump, issuer flex-wrap (F12) — was a reactive patch discovered during an unrelated feature's QA pass, not something checked against a written rule. This is exactly the gap CLAUDE.md rule #6 calls out: *"Introduce a ... frontend UI/UX pattern that falls behind the current standard ... without flagging the gap explicitly"* is the thing not to do, and right now there's no written standard to check new pages against.

---

## Proposed standard

### Breakpoints (formalizing what's already in use — no new values)

```
< 640px   ("mobile")   — drawer nav, stacked/card list views, full-width controls
640–1023px ("sm–lg")   — transitional; most pages behave like desktop but dense
                         controls (nav, action-button rows) still collapse
≥ 1024px  ("lg+")      — current desktop experience, unchanged
```

Use Tailwind's default `sm`/`lg` tokens directly (already the case throughout the codebase) — do not add custom breakpoints to `index.css`'s `@theme`.

### Touch targets

Minimum **44×44px** hit area for any tappable element below the `sm` breakpoint (buttons, nav items, icon-only actions, table row actions). Above `sm`, the existing denser desktop sizing (`py-1.5` etc.) is fine. This is the same threshold `ClientLayout.tsx` already implements — just written down and applied consistently.

### Navigation — recommendation: hamburger-triggered `Sheet` drawer, not a bottom tab bar

Recommended: below `lg`, replace the horizontal-scroll `<nav>` in all four layouts with a hamburger icon button in the header that opens a left-side `Sheet` (reusing `shared/components/ui/sheet.tsx`) containing the full `NAV_ITEMS` list as a vertical stack, each item ≥44px tall. Above `lg`, keep the current horizontal nav as-is (desktop has room for it).

Why a drawer over a bottom tab bar (the other common vertical-SaaS mobile pattern, used by e.g. some competitors' client-facing apps):
- Every layout already has a role-specific item count that doesn't fit a 4–5-item bottom bar (Owner: 9, Issuer: 10) without an overflow "More" menu — which just recreates the drawer one level down.
- The header already carries `SuspensionBanner` / `ReadOnlyBanner` / `PlanLimitBanner` plus `HelpMenu`, `NotificationBell`, and `UserMenu` — a bottom bar would be a second navigation surface to keep in sync with the top one; a drawer keeps navigation in one place.
- `Sheet` is already in the dependency tree and already proven on mobile (`StudioNotificationSheet`) — zero new packages, zero new interaction pattern to learn.
- Each `NavLink`'s `data-tour="..."` attribute (used by `OnboardingTour.tsx`) moves with it into the drawer with no logic change — tour steps that target a nav item just need the tour to open the drawer first (see Help-sync section below).

This is a judgment call, not a fact pulled from source — flagging it explicitly per this project's role. If there's a product-side preference for a bottom tab bar instead (common on Fresha's and Booksy's client-facing mobile web), say so before this becomes an overnight prompt; the drawer approach below assumes no such preference.

### Tables — card fallback in the shared primitive, not per-page

Extend `DataTable<T>` with an optional `mobileCard?: (row: T) => React.ReactNode` render prop. When provided and the viewport is below `sm`, render a stacked list of cards (one per row, using the same `keyExtractor`/`onRowClick`) instead of the `<Table>`. When not provided, fall back to wrapping the existing table in `overflow-x-auto` (matching the `HelpInsightsPage.tsx` pattern) so no table regresses even before its page is migrated to a custom card. This lets `ArtistListPage`, `ClientListPage`, and `PaymentListPage` be migrated to a real card view independently, without a big-bang rewrite.

---

## What needs to change (file-level)

1. **`frontend/src/shared/components/ui/sheet.tsx`** — no change needed; confirm `side="left"` support (already used for right-side sheets; check the `Sheet`/`SheetContent` prop before assuming).
2. **New shared component `frontend/src/shared/components/NavDrawer.tsx`** — takes `navItems`, `title`, renders the hamburger trigger + `Sheet` with a vertical `NavLink` stack, each item `min-h-[44px]`, closes on navigation (`onOpenChange`/route-change effect). Extract this once, use it from all four layouts, rather than duplicating drawer markup four times.
3. **`frontend/src/layouts/ClientLayout.tsx`, `ArtistLayout.tsx`, `OwnerLayout.tsx`, `IssuerLayout.tsx`** — replace the inline `<nav className="overflow-x-auto ...">` with: `<nav className="hidden lg:flex ...">` (existing markup, desktop only) + `<NavDrawer navItems={...} />` rendered `lg:hidden`. `IssuerLayout`'s numeric feedback badge (line 59-63) needs to render inside the drawer item too — pass it through as part of the nav item shape rather than hardcoding it in the drawer component.
4. **`ArtistLayout.tsx`, `OwnerLayout.tsx`, `IssuerLayout.tsx`** — bump remaining desktop-only nav `py-1.5` is fine to leave (desktop nav stays as today); no touch-target change needed there once the drawer is the mobile path, since the drawer component owns its own `min-h-[44px]` sizing. (This supersedes the earlier idea of just patching `py-1.5` → `py-2.5 sm:py-1.5` on the existing pills — once mobile users get the drawer instead of the horizontal strip, the strip itself no longer needs a mobile touch-target fix.)
5. **`frontend/src/shared/components/DataTable.tsx`** — add the `mobileCard` prop and `sm:` conditional render described above; wrap the existing `<Table>` in `overflow-x-auto` unconditionally as the no-op fallback.
6. **`frontend/src/features/artists/components/ArtistListPage.tsx`, `clients/components/ClientListPage.tsx`, `payments/components/PaymentListPage.tsx`** — pass a `mobileCard` renderer once `DataTable` supports it. Card content: same fields as the table's `ColumnDef`s, stacked with labels, primary identifier (name) as the card heading, existing `onRowClick` behavior preserved.
7. **`docs/claude/conventions.md`** — add a new "Mobile / Responsive Conventions" section documenting the breakpoints, 44px touch-target minimum, and "use `NavDrawer` + `Sheet` below `lg`, never build a new off-canvas pattern" rule, so this doesn't regress to reactive patching again.
8. **`docs/claude/architecture.md`** — add a Decisions Log entry for this pass (matching the existing entry format) once implemented, and update the "Industry-Standard Benchmark" cross-reference if useful.

---

## Testing plan

- Unit tests for `NavDrawer` (opens/closes, renders all items, closes on navigate, badge passthrough for Issuer feedback count) — one test file, reused across the four layouts' existing test suites (update each `*Layout.test.tsx` to assert the drawer trigger exists at narrow viewport instead of asserting on the old horizontal-scroll nav).
- Unit tests for `DataTable`'s new `mobileCard` branch (renders cards when prop present + viewport narrow via a mocked media query hook or a `useMediaQuery`-style approach — check whether the codebase already has a responsive-detection hook before adding a new one; `grep -r "matchMedia\|useMediaQuery" frontend/src` first).
- Existing `ArtistListPage.test.tsx` / `ClientListPage.test.tsx` / `PaymentListPage.test.tsx` — extend with mobile-card assertions, don't replace desktop table assertions.
- Manual/visual QA at 375px (iPhone SE-class, the narrowest realistic target) for all four layouts post-drawer, plus the three migrated list pages.
- Playwright e2e: at least one spec confirming the drawer nav is keyboard-accessible and closes on Escape (matches the existing `alert-dialog`/`sheet` accessibility bar set elsewhere in the app).

---

## Help-sync obligations (CLAUDE.md rule #7)

This changes where nav items live in the DOM (drawer vs. inline) at narrow viewports but not their route or label — `helpContent.ts` entries that reference nav items by label/route don't need content changes. The one thing that does need updating: any `OnboardingTour.tsx` step (`frontend/src/features/help/tours/*.ts`) that targets a `data-tour="..."` nav attribute assumes that element is visible without opening anything first. Once those items live inside the closed-by-default `NavDrawer` below `lg`, the tour step needs to either (a) run the drawer-open action before highlighting the target, or (b) force desktop nav rendering during the tour regardless of viewport. Check `OnboardingTour.tsx`'s targeting logic before choosing — this is a real behavior change, not a copy update, so it belongs in the overnight prompt's explicit scope, not a "no user-visible surface" checkbox.

---

## Industry-standard benchmark check (CLAUDE.md rule #6)

Fresha, Vagaro, Boulevard, and GlossGenius's client- and staff-facing web apps all use a collapsible/drawer nav below tablet width, never a raw horizontally-scrolling icon strip. None of them ship a data table without either a responsive card view or, at minimum, a visibly-scrollable wrapper with a shadow/fade affordance. This spec brings the app to that baseline; it does not attempt native-app-tier polish (e.g., swipe gestures, pull-to-refresh) — those would be separate, larger specs if wanted.

---

## Open questions for the user before this becomes an overnight prompt

1. Confirm the drawer-over-bottom-tab-bar call above, or say if a bottom tab bar is preferred for the client-facing role specifically (it's the one role where a bottom bar is most common in the benchmark set, even if the other three roles stay drawer-based).
2. Confirm scope: should this pass also migrate `ArtistListPage`/`ClientListPage`/`PaymentListPage` to the new `mobileCard` prop in the same overnight run, or land `DataTable`'s capability first and migrate pages in a follow-up?
3. Any existing `useMediaQuery`-style hook to reuse for the `DataTable` viewport check, or should the overnight prompt add one to `shared/hooks/`?
