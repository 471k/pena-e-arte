# Cross-device manual QA checklist

**Owner:** Phi · **Related:** `docs/claude/architecture.md`'s "Mobile UI/UX Baseline" entry,
`frontend/e2e/accessibility.spec.ts` (WCAG coverage — a different axis, see note below)

Mobile bugs so far have been found ad hoc (see the "Mobile UI/UX Baseline" work — 2 real bugs
were only caught via a manual browser pass, not unit tests). This is a repeatable checklist for
a human to run through, not automation: `frontend/e2e/accessibility.spec.ts` already catches
DOM-structural/contrast/semantics violations (WCAG 2.1 AA) via axe-core, but axe-core cannot see
visual layout problems — overlapping elements, clipped text, a sticky header covering content,
a touch target that's *technically* present but visually squeezed — the class of bug Tailwind's
responsive utility classes get wrong when a new component ships without ever being resized down.
That's what this checklist is for.

## When to run this

- Before shipping any change that touches layout/CSS on one of the flows below.
- After any Tailwind, shadcn/ui, or Radix version bump.
- Spot-check monthly even with no known layout change, since a change to a *shared* component
  (Button, Card, NavDrawer, etc.) can silently regress a page nobody directly touched.

## Device / breakpoint matrix

Use real device emulation (Chrome DevTools device toolbar, or an actual phone/tablet) — a
resized desktop browser window does not reproduce mobile touch-target sizing or iOS Safari's
address-bar chrome eating viewport height, both real sources of past bugs in this app.

| Breakpoint | Width | Reference device | Tailwind range |
|---|---|---|---|
| Smallest mobile | 375px | iPhone SE (3rd gen) | below `sm` (640px) |
| Mid-size Android | 412px | Pixel 7 / most current mid-range Android | below `sm` (640px) |
| Tablet portrait | 768px | iPad (10th gen), portrait | `md` (768px) boundary — exactly the size layouts switch at, so check both just-under and just-at this width |
| Desktop baseline | 1280px+ | Existing baseline — what most manual testing already uses | `xl` (1280px)+ |

## Flows to check (same list as `accessibility.spec.ts`'s coverage)

For each flow, at each breakpoint above, check:

- **No horizontal scroll** on the page body (a single overflowing element is the most common
  cause — check any fixed-width element, wide table, or unwrapped long string).
- **No overlapping or clipped text** — headings, badges, and buttons stay fully visible and
  don't overlap a neighboring element (sticky headers are the most common repeat offender —
  confirm scrolled content never renders *underneath* one).
- **Tap targets ≥44×44px** — buttons, icon-only buttons, checkboxes/toggles, and Select/dropdown
  triggers. This app uses a lot of icon-only `lucide-react` buttons (nav bell, help, feedback,
  kebab menus) — these are the ones most likely to have a comfortable pointer-sized hitbox on
  desktop that's too small once rendered at real mobile density.
- **No dead-end interactions** — a modal/Sheet/dropdown that opens must have a visible, tappable
  way to close it at this width (matches the class of bug `my-studios-kebab-menu.spec.ts`
  regression-tests for pointer-events, but that spec only covers the one flow it was written
  for — this checklist is the net for everywhere else).

### 1. Public booking flow (`/book`, guest and authenticated)

- [ ] Studio/artist picker, date/time input, and duration Select all remain usable and fully
      visible at 375px — this is the most layout-complex form in the app (also has image
      upload tiles, deposit-rule preview, and the tattoo-intake fields stacked below).
- [ ] The body-map/placement picker (`DesiredPlacementField`) specifically — this renders an
      interactive graphic and is the single most likely control to break at narrow widths.
- [ ] Category image-upload tiles wrap correctly instead of overflowing the card at 375px.

### 2. Sign-in / sign-up (`/login`, `/register`, `/client-register`)

- [ ] `/register`'s Leaflet map (`LocationPicker`) is usable with touch — pinch-zoom and tap-to-
      place-pin both work, and the map doesn't trap vertical page scroll on a touch device.
- [ ] Multi-step forms (`/register`'s two steps) show a clear current-step indicator at 375px.

### 3. Client home (`/my-studios`, `/book` as a logged-in client)

- [ ] Studio cards in `MyStudiosPage` reflow to a single column below `md` without clipping the
      cover image or the kebab menu.
- [ ] The kebab menu's dropdown/Sheet panels (Leave studio, Manage notifications) render fully
      on-screen at 375px — don't let a dropdown clip off the right edge of a narrow viewport.

### 4. Owner dashboard (`/dashboard`)

- [ ] The 3-column KPI stat-card grid (Today / This Week / Deposits Due) reflows sensibly below
      `sm` — confirm it doesn't squeeze into 3 unreadable columns instead of stacking.
- [ ] `OwnerLayout`'s top nav collapses into `NavDrawer` below `lg` (per the Mobile UI/UX
      Baseline work) — confirm every nav item is reachable from the drawer, including the
      conditionally-shown "My Portfolio"/"My Earnings" items for a solo owner-as-artist.
- [ ] Subscription/read-only/plan-limit banners stack without overlapping the header at 375px.

### 5. Deposit payment (`/pay/:paymentId`, and the in-flow `PaymentMethodSelector`)

- [ ] The Card/Cash tab bar in `PaymentMethodSelector` stays tappable and legible at 375px —
      this is a real-money flow, the highest-stakes place in the app for a squeezed tap target.
- [ ] The embedded Stripe `PaymentElement` iframe resizes to the viewport instead of forcing
      horizontal scroll (test with a real Stripe test-mode key locally — the CI/e2e environment
      has none configured, so this specific check can't run there and must be manual).

## Future upgrade option (not built here — Phi's call)

Playwright supports screenshot-comparison ("visual regression") testing, which could turn some
of the above into automated pixel-diff assertions per breakpoint. Deliberately not built in this
pass — it's a real scope decision (baseline-image maintenance overhead, flakiness from font
rendering/anti-aliasing differences across CI runners) that Phi should make deliberately rather
than have slipped in as a side effect of writing this checklist. If manual QA fatigue becomes a
real problem, revisit this.
