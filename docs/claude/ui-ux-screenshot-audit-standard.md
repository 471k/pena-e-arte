# UI/UX Screenshot Audit Standard

> Load this file when asked to audit a specific screen/screenshot for visual design, layout,
> accessibility, or UX friction — as opposed to a whole-app feature-parity sweep (that's
> `industry-feature-parity-report-2026-07-20.md`'s "Section F — UI/UX heuristics" methodology,
> which this file complements rather than replaces). Written 2026-09-20 in response to a request
> to turn a generic pixel-audit prompt template into something this project can actually stand
> behind: grounded in this repo's real tokens/components, not generic SaaS-audit boilerplate.

---

## 0. What this is for, and what it fixes about the generic version

A generic "audit this screenshot" prompt (spacing/contrast/hierarchy/etc. checklist, ending in
Critical/Quick-Win/Longer-Term buckets) produces plausible-sounding but frequently wrong output,
for four specific reasons — each has a concrete fix below:

1. **Contrast ratios cannot be reliably eyeballed from a raster screenshot.** JPEG compression,
   anti-aliasing at glyph edges, display gamma, and screenshot scaling all shift the sampled pixel
   color away from the true rendered value. `accessibility-audit-2026-09-05.md` found real AA
   failures (4.40:1 vs the 4.5:1 bar, a 0.10 miss) that no amount of careful looking would have
   caught — it took reading the actual token values out of `index.css` and computing relative
   luminance. **Fix: compute, don't estimate.** Every contrast claim in this audit's output must
   either (a) cite the actual CSS custom-property values from `frontend/src/index.css` — or the
   Tailwind color-scale stop for a raw utility class — and show the computed ratio, or (b) if the
   source isn't available, be explicitly labeled `[estimated from image, unverified]` rather than
   stated as fact.

2. **A static screenshot is one theme, one breakpoint, one moment.** This app has no manual
   light/dark toggle — it follows `prefers-color-scheme` — and `accessibility-audit-2026-09-05.md`
   found the *entire app* was silently rendering one theme for every visitor for a period, a bug
   invisible in any single screenshot of either theme alone. **Fix: state which theme and which
   breakpoint the screenshot shows, explicitly note the other three combinations (other theme ×
   same breakpoint, same theme × other breakpoints per `sm`/`lg`) were NOT checked unless they
   were, and recommend a Playwright capture across `chromium-light`/`chromium-dark` (the project's
   own two-project convention, `frontend/playwright.config.ts`) via the `verifier-gui` skill
   whenever the audit target is a live page rather than a pasted image.**

3. **Generic advice ("add a loading skeleton," "increase touch targets") ignores that this app
   already has the primitive and a convention for using it.** Recommending a bespoke spinner when
   `shared/components/ui/skeleton.tsx` exists, or a toast library when `sonner` is already wired
   up, isn't a quick win — it's a regression against `conventions.md`'s "use shadcn/ui primitives
   before writing a custom component" rule. **Fix: every "missing element" or "inconsistency"
   finding must first check whether the fix is "use the existing primitive/pattern this screen
   isn't using" before proposing anything new.** §4 below lists the actual inventory to check
   against.

4. **A generic audit treats the screen as belonging to nobody.** This is a 4-role, multi-tenant
   product (`client`/`artist`/`owner`/`admin`) with its own non-negotiable rules (CLAUDE.md #6,
   #7). A finding on one role's screen often implies the same gap exists on the analogous screen
   for the other three roles, and every user-visible change carries a Help-sync obligation whether
   or not the audit was asked to check for it. **Fix: §4.9 and §4.10 are not optional sections —
   every audit run under this standard includes them.**

---

## 1. Pre-flight — establish before scoring anything

Before any of the eight/ten dimensions below, record:

| Field | How to determine | Why it matters |
|---|---|---|
| **Screen identity** | Match against `docs/claude/architecture.md`'s Feature Module Map and the route table in `docs/claude/frontend.md` (`app/router.tsx`). Name the exact route and component file (e.g. `AdminDashboardPage.tsx`). | Findings must cite the real file, not a description. |
| **Role** | `client` / `artist` / `owner` / `admin` — inferred from layout chrome (sidebar, header) or route prefix (`/platform` = admin only). | Drives §4.9's "check the other three roles" step and which nav/tour files are relevant. |
| **Theme** | Light or dark, judged from `--color-background` (white vs. `hsl(240 10% 3.9%)` / near-black). | Contrast math and dark-mode-consistency checks are theme-specific; state which one you're looking at. |
| **Breakpoint** | Below `sm` (640px, phone) / `sm`–`lg` (tablet) / `lg`+ (desktop) — inferred from layout shape (drawer vs. sidebar, stacked cards vs. table). | `conventions.md`'s breakpoint rules apply per-range; a finding at one breakpoint doesn't imply the others are fine or broken. |
| **Source availability** | Is the live repo reachable (can you open the component file and `index.css`), or is this a bare image with no code access? | Determines whether contrast/spacing findings are computed (§0.1) or must be labeled estimated. |
| **Recency** | Check the component file's/relevant `overnight-prompt-*.md`'s last-modified date if available. A screenshot of a page rebuilt last week may not reflect current code. | Don't file a finding that's already fixed; note if the screenshot might be stale. |

---

## 2. Non-negotiable grounding rules (extends CLAUDE.md rule #6 / project rules)

- **Never invent a token, component name, or file path.** If you haven't read `index.css`,
  `frontend.md`, `conventions.md`, and the actual component source for this screen, don't cite
  them — say "not verified against source" instead.
- **Every visual-design or accessibility claim gets a computed or explicitly-flagged-as-estimated
  basis** — see §0.1.
- **Every "missing element" finding checks the existing inventory first** — see §4 and the
  component list in §4.3.
- **Every finding is benchmarked against the Industry-Standard Benchmark Set**
  (`architecture.md`'s "Industry-Standard Benchmark Set" — Vagaro/Fresha/Boulevard/Mindbody/
  Zenoti/GlossGenius/Booksy/Mangomint/Schedulicity/Square Appointments for
  client/artist/owner surfaces; general B2B SaaS admin-panel standards for admin; the Trust &
  Safety set for report/moderation flows) **or explicitly marked as a deliberate divergence** —
  never silently presented as benchmark-driven and never silently omitted. Refresh the benchmark
  set via web search if it's been more than a few months since last checked, per that section's
  own instruction — don't rely on stale training knowledge for what these competitors currently do.
- **Every finding that implies a user-visible change states its Help-sync obligation** explicitly
  — even when the verdict is "no Help change needed," say why (§4.10).
- **Money, auth, tenant-isolation, and compliance-adjacent UI** (payment forms, consent forms,
  anything touching `Flow A`/`Flow B` per `architecture.md`'s Payment Architecture section) gets
  flagged for a fully-specified backlog entry, not a quick-fix suggestion, per the parent
  project's "do not build blind" rule.
- **This doc never ships the fix.** Per this project's scope, the output of a screenshot audit is
  a report (or, when severe enough, an overnight-prompt-shaped spec) — not an edited component
  file. Name the exact target file and exact change; don't touch it.

---

## 3. The Audit Dimensions

Each of the user's original eight dimensions, refined with this repo's actual inventory and
computation method. Two more are added (§4.9 role-parity, §4.10 Help-sync) because they're
non-negotiable project rules, not optional nice-to-haves.

### 4.1 — Visual Design & Polish

- **Spacing.** This app has no custom spacing scale in `index.css`'s `@theme` — it's Tailwind's
  default 4px-increment scale (`p-1`=4px … `p-4`=16px … `p-6`=24px, etc.) applied via utility
  classes. A finding here names the actual class combination in the component if visible/known
  (e.g. "this card uses `p-4` while its siblings in the same list use `p-6`") rather than a vague
  "padding feels inconsistent." If the component source isn't available, describe the *visible*
  inconsistency precisely (in px or as a ratio to a nearby consistent element) and label it
  estimated.
- **Border radius.** Single global token: `--radius: 0.5rem` (8px), consumed by shadcn/ui's
  `rounded-*` scale (`rounded-md` = `--radius`, `rounded-lg` = `--radius` + 2px, etc.). Any
  element with a visually different corner radius than its siblings is a real finding — this
  repo has exactly one radius token, so drift is a bug, not a style choice.
- **Typography.** Single font family: `"Fraunces", sans-serif` (`--font-sans` / `body`'s
  `font-family`), applied globally — there is no secondary display or mono font declared in
  `index.css`. A screenshot showing a second typeface is either a third-party embed (map tiles,
  Stripe Elements iframe — expected) or a real bug. Font-*size*/*weight* scale is Tailwind
  defaults (`text-sm`/`text-base`/`text-lg`… × `font-normal`/`font-medium`/`font-semibold`/
  `font-bold`) — check for a heading using a *smaller* size than body text near it (a real,
  fairly common mistake) or two visually-same-purpose headings (e.g. two card titles) at
  different weights.
- **Color usage.** Compute contrast, don't estimate (§0.1). The exact light/dark token pairs to
  check against are in §4.5's table. Flag any accent/brand color used for more than one distinct
  *meaning* on the same screen (e.g. the same shade used for both a positive/success signal and a
  neutral "new" badge) — this repo doesn't have a documented semantic-color map beyond
  `destructive`/`destructive-text`, so meaning-overload is easy to introduce and easy to miss.
- **Icons.** `lucide-react` is the only icon library in `package.json` — any icon that doesn't
  look like a Lucide-style icon (consistent 24×24 viewBox, ~2px stroke, no fill) is either an
  emoji-as-icon (flag it — inconsistent with the rest of the app and inaccessible to screen
  readers without an explicit label) or a mixed-library import (flag it as a `conventions.md`
  violation). Check icon size consistency: Lucide icons default to 24px; a mix of 16/20/24px
  *without* a clear size hierarchy (e.g. inline-with-text icons smaller than standalone button
  icons) is a real inconsistency, not a stylistic choice, since there's no documented icon-sizing
  scale in this repo.
- **Shadows/borders.** `--color-border` is a single token (`hsl(240 5.9% 58%)` light /
  `hsl(240 5% 40%)` dark) applied globally via the `* { border-color: var(--color-border) }`
  rule in `index.css` — every bordered element in this app should be pulling from the same
  color. A border that looks like a different gray is either a raw Tailwind gray class (a
  `conventions.md`-adjacent drift, same class of bug as the raw-color contrast bugs
  `accessibility-audit-2026-09-05.md` found) or an intentional `border-destructive`/similar
  semantic override (fine, but should be visually distinct on purpose, not by accident).
- **"Looks off but can't name it"** is a legitimate finding category — but push once before
  filing it: check alignment against a shared baseline grid (do left edges of stacked elements
  actually line up, going by the screenshot's pixel coordinates if you can measure them), check
  for asymmetric padding inside a single container (top ≠ bottom, or left ≠ right, on an element
  that should be symmetric), and check optical alignment on icon+text pairs (icon vertically
  centered against the text's cap-height, not its full line-height box).

### 4.2 — Layout & Hierarchy

- **What draws the eye first, and is it correct** for this screen's actual primary task? Name the
  primary task from the Feature Module Map entry for this screen, then say what visually
  dominates (size, color, position, whitespace-isolation) and whether the two match.
- **Action differentiation.** This app doesn't have a documented primary/secondary/tertiary
  button-variant convention beyond shadcn/ui's stock `button.tsx` variants (`default`,
  `destructive`, `outline`, `secondary`, `ghost`, `link`) — check whether the *visually* most
  prominent action on screen matches the variant it should logically have (e.g. a destructive
  action styled as `default`/primary is both a hierarchy bug and a safety concern — compare
  against `conventions.md`'s "confirmation on destructive actions" expectation and this app's
  existing inline-confirm pattern, Section F5 of the parity report, which was found consistent —
  a screen that breaks that pattern is a regression, not just an isolated finding).
- **Balance / density.** Compare against the parity report's F8 finding ("newer pages
  appropriately sparse and well-sectioned") — a screen that reads as noticeably denser or
  sparser than that established baseline is worth naming as a regression or an improvement,
  not just "feels cluttered."
- **Responsive concerns.** Per `conventions.md`: `sm` (640px) and `lg` (1024px) are the only
  breakpoints; below `sm` should be stacked/full-width; `sm`–`lg` is "desktop behavior with
  denser controls"; any list of records with >3 columns should be using `DataTable`'s
  `mobileCard` prop, not a bare `<Table>` (bare tables get an automatic `overflow-x-auto`
  fallback, which is not a target state per that doc — flag it as a gap even though it "works").
  A action-button row that doesn't visibly wrap or collapse below `sm` is a known regression
  class (parity report F12 flagged exactly this on issuer screens) — check for it explicitly on
  any screen with 2+ inline action buttons.
- **Gestalt grouping.** Related fields/actions should share a visual container (`card.tsx`) or
  consistent spacing-proximity; unrelated items sharing a container, or related items split
  across containers, is a real finding — name the specific fields/actions.

### 4.3 — Missing UI Elements (Industry-Standard Gaps)

Before proposing *any* missing element, check this repo's actual inventory
(`frontend/src/shared/components/ui/`) — if the primitive exists, the finding is "not used here,"
not "doesn't exist":

```
accordion · alert · alert-dialog · avatar · badge · button · card · dialog · dropdown-menu ·
field-hint · input · label · location-picker · password-input · password-match-indicator ·
phone-input · select · separator · sheet · skeleton · sonner (toast) · table · tabs · textarea ·
toggle-switch · PasswordStrengthMeter · StarRating
```

- **Empty state.** Does this screen have one, and does it match the app's established pattern
  (check an adjacent list page for the convention) rather than a bare "No data" string?
- **Loading state.** `skeleton.tsx` exists — a spinner or blank-flash instead of a skeleton on a
  data-driven screen is a real regression against the app's own established pattern, not a
  generic best-practice suggestion.
- **Error state.** Compare against parity report F9 (`HelpInsightsPage.tsx`'s generic "Failed to
  load..." was flagged and fixed) — a generic error string with no retry action is a known,
  previously-fixed-elsewhere anti-pattern; naming a recurrence is a high-value finding.
- **Success feedback / toasts.** `sonner` is wired up app-wide — any action that changes state
  (save, confirm, delete) without a toast or equivalent inline confirmation is a real gap, not a
  nice-to-have, since the pattern and the library are both already present.
- **Confirmation on destructive actions.** Cross-reference `alert-dialog.tsx`'s existence and the
  parity report's F5 finding (inline-confirm pattern used consistently for destructive actions)
  — a destructive action on this screen with no confirmation step at all is a real regression,
  not a stylistic gap.
- **Escape hatches / undo.** Parity report F3 already flagged "no universal Escape-to-cancel on
  inline-confirm patterns" as PARTIAL/P2 app-wide — if this screen has an inline-confirm without
  Escape handling, it's confirming a known, already-logged gap, not a new finding; cite F3 rather
  than re-deriving it, and only escalate if this screen's case is more severe than F3's baseline.
- **Affordance mismatches.** Anything that looks clickable but isn't (hover cursor won't be
  visible in a static screenshot, so check for the *visual signal* — button-like padding/border
  on non-interactive text, or link-blue text that doesn't navigate) and anything interactive that
  doesn't look it (icon-only buttons with no visible hit-state, plain text acting as a toggle).
- **Benchmark comparison.** Name 2–3 specific competitors from the Industry-Standard Benchmark
  Set whose analogous screen has an element this one lacks, based on current web research (not
  memory) — cite what was found, per the non-negotiable benchmarking rule.

### 4.4 — Usability & UX Friction

- **Click count for the primary task**, counted from this screen (or the flow it's part of) — can
  any step be collapsed (e.g. a confirm-then-confirm-again double gate that isn't a destructive
  action, or a value that could be defaulted/pre-filled from context already available, like the
  current studio/tenant).
- **Hidden-but-should-be-visible / visible-but-should-be-hidden.** Progressive disclosure that
  hides a frequently-needed control behind a menu, or a rarely-needed/advanced control shown
  inline and competing for attention with the primary action.
- **Dead ends.** Any state (especially an error or empty state) with no visible next action —
  cross-reference against F9's now-fixed pattern (specific copy + retry) as the bar to meet.
- **Copy.** Vague labels/CTAs ("Submit" where "Save Changes"/"Confirm Booking"/etc. would be
  specific), jargon without a `field-hint.tsx` explainer, placeholder text doing double duty as
  a label (accessibility issue — see §4.5). This is a genuine gray area per the parent project's
  own scope rules: structural copy issues (is there a message at all, does it follow this app's
  conventions) are in scope here; pure brand-voice/wording polish is Marketing's `ux-copy`-style
  work — say so if a finding is really about tone rather than function.
- **Target size.** See §4.5's touch-target rule — this is also a usability finding, not just an
  accessibility one, since a hard-to-hit control is friction regardless of assistive-tech use.
- **Mental model fit.** Does the flow match how the equivalent step works on the 2–3 nearest
  benchmark competitors, or does it invent a different order/grouping for no apparent reason?

### 4.5 — Accessibility

**Compute, don't eyeball** (§0.1). The exact token pairs (light / dark), from `index.css`:

| Token | Light | Dark | Known constraint |
|---|---|---|---|
| `--color-foreground` on `--color-background` | `hsl(240 10% 3.9%)` on `hsl(0 0% 100%)` | `hsl(0 0% 98%)` on `hsl(240 10% 3.9%)` | Base body text — should be far above 4.5:1 in both; verify, don't assume. |
| `--color-muted-foreground` on `--color-muted`/card | `hsl(240 3.8% 42%)` on `hsl(240 4.8% 95.9%)` | `hsl(240 5% 64.9%)` on `hsl(240 3.7% 15.9%)` | Light value was deliberately darkened from shadcn's stock 46.1% to clear 4.5:1 — any *new* muted-foreground usage on a *different*, further-tinted background (e.g. a colored card) needs its own contrast check; the 42% figure was only verified against `bg-muted` and one specific emerald-tinted card. |
| `--color-destructive-text` (NOT `--color-destructive`) for body/alert text | `hsl(0 74% 42%)` | `hsl(0 90% 65%)` | `--color-destructive` is fill/border-tuned only and fails 4.5:1 as text — any error copy or alert body text using `text-destructive` instead of `text-destructive-text` is a direct, previously-documented regression (167 instances of exactly this were fixed 2026-09-05; check whether this screen is a pre-existing or re-introduced instance). |
| Opacity-modified text (`text-foreground/NN`, `text-muted-foreground/NN`) | needs ≥65% for `text-foreground` blends | needs ≥46% (but ≥65% clears both) | An opacity value tuned by eye in one theme will very likely fail the other — any `/NN` suffix on body text is a flag-and-verify-both-themes finding by default. |

Contrast formula (WCAG relative luminance), for any token/value not in the table above:

```
For each of R, G, B (0–255, converted to 0–1):
  c = value / 255
  c_lin = c/12.92                       if c <= 0.03928
        = ((c+0.055)/1.055) ^ 2.4       otherwise
L = 0.2126*R_lin + 0.7152*G_lin + 0.0722*B_lin
Contrast ratio = (L_lighter + 0.05) / (L_darker + 0.05)
AA body text: ≥ 4.5:1.  AA large text (≥24px, or ≥19px bold): ≥ 3:1.  Non-text (icons, borders
carrying meaning): ≥ 3:1.
```

Other checks:
- **Keyboard navigation / focus states.** `--color-ring` is the focus-ring token — every
  interactive element on screen should show a visible ring on focus (radix primitives handle
  this by default; a *custom*-built interactive element, not from `ui/`, is the likely place
  this breaks). Flag anything that looks like a custom click-handler on a `<div>` rather than a
  real button/link.
- **Icon-only controls need an accessible name.** Lucide icons carry no inherent label — check
  for `aria-label`/visually-hidden text on every icon-only button; parity report F11 found this
  "generally" present but not fully verified — treat any specific instance you can point to as a
  real, citable finding rather than repeating F11's hedge.
- **Color as the only signal.** Status/badge patterns that rely on color alone (a colored dot
  with no text/icon) fail WCAG 1.4.1 — check every status indicator on screen for a non-color
  cue (text label, icon, pattern).
- **Touch targets: 44×44px minimum below `sm`**, per `conventions.md`'s explicit rule (not just
  a generic WCAG citation — this is this repo's own stated convention for phone breakpoints).
  Icon-only buttons, table row actions, and nav items are the usual offenders.
- **Content on hover only** fails WCAG 2.1 §1.4.13 — this app already has a documented fix for
  exactly this pattern (portfolio tile attribution strip, made always-visible specifically for
  this reason, per the Decisions Log). Any *new* hover-only-reveal of meaningful content on this
  screen is a regression against that established precedent, not a fresh question.
- **Both themes.** Per §0.2 — state explicitly whether the other theme was checked; if not,
  recommend it rather than silently assuming parity.

### 4.6 — Consistency & Patterns

- Same action type (destructive confirm, save, cancel) styled/behaving the same way *everywhere
  else in this app* — not just internally consistent within the one screenshot. Cross-reference
  against the parity report's F4 finding (card-row vs. raw-`<table>` drift found once already,
  in the `platform` feature folder specifically) — check whether this screen repeats that
  specific drift or introduces a new instance of the same class of problem.
- Modal (`dialog.tsx`/`sheet.tsx`) vs. inline-expansion used for the same *kind* of interaction
  elsewhere — flag if this screen picked the less-common option without an apparent reason
  (e.g. a `Dialog` for what's inline everywhere else, or vice versa).
- Terminology — parity report F2 already found and fixed one drift ("Session Length" vs.
  "Appointment"/"duration"); check whether this screen's copy matches the terms used on the
  screens immediately before/after it in the same flow.

### 4.7 — Content & Copywriting

- Labels that need a tooltip/`field-hint.tsx` to be understood but don't have one.
- Placeholder-as-label (an `input.tsx` with no visible `<label>`, relying on the placeholder
  text alone) — flag as both a UX and accessibility issue (placeholder text disappears on focus
  and isn't reliably announced by all screen readers).
- CTA text specificity — "Submit"/"Continue"/"OK" where the actual consequence
  ("Save Changes"/"Confirm Booking"/"Delete Studio") should be named, especially before a
  destructive or financially-consequential action (payment, cancellation, consent).
- Validation-message quality — present, specific, and attached to the right field (FluentValidation
  backs every write endpoint per `backend.md`, so a generic/misattributed frontend validation
  message on a field that has real backend validation is worth flagging as a frontend gap, not a
  backend one).

### 4.8 — Performance & Technical Red Flags (visible from UI)

- Heavy above-the-fold imagery with no visible lazy-loading treatment, especially on
  image-dense surfaces (portfolio/design galleries — this app's masonry feed already handles
  this deliberately per the Decisions Log; a *different* image-heavy screen without an equivalent
  treatment is the actual finding).
- A data-dense screen with no skeleton/pagination/virtualization visible — hints at an
  unbounded query; name the likely RTK Query endpoint if identifiable from `frontend.md`'s
  feature-slice map and flag it for a backend N+1/pagination check (outside this audit's own
  scope, but worth surfacing per the parent project's "proactive recommendations" rule).
- Visibly over-complex UI (many simultaneous live widgets/counters) that suggests over-fetching —
  same as above, name the likely slice rather than speculating generically.

### 4.9 — Role Parity (non-optional — CLAUDE.md rule #6)

For every finding above, state whether the same UI pattern exists on the analogous screen for
the other three roles (`client`/`artist`/`owner`/`admin`) and, if it does, whether the finding
applies there too. A spacing bug on the owner dashboard that also exists on the identical client
dashboard layout is one fix with four times the impact — say so. If the analogous screen wasn't
checked, say that explicitly rather than silently scoping the finding to one role.

### 4.10 — Help-Sync Obligation (non-optional — CLAUDE.md rule #7)

For every finding whose fix changes what a user sees or can do (not a pure visual-polish fix like
a spacing/radius correction), state:
1. Whether `frontend/src/features/help/helpContent.ts` needs a new article or an addition to an
   existing one (name the article `id` if you can identify it).
2. Whether the standalone manual (`frontend/public/user-manual/index.html` — confirm this is
   still the live copy before citing it, per the parent project's own verification rule) needs
   the matching update.
3. Whether the relevant `frontend/src/features/help/tours/{client,artist,owner,admin}Tour.ts`
   needs a new/updated step, or an explicit "no — existing step's selector already covers this"
   verdict.
A pure visual-polish fix (spacing, radius, color-token correction) with zero change to what the
user can *do* gets an explicit "no Help change needed — zero user-visible behavior change,"
not silence.

---

## 4. Severity & Priority Taxonomy

Map every finding to both an audit-report bucket (for readability) and this repo's own P0–P3
scale (for backlog integration — matches `industry-feature-parity-report-2026-07-20.md`'s
convention):

| Report bucket | Definition | Typical P-level |
|---|---|---|
| **Critical** | Actively breaks usability, fails WCAG AA, or looks broken (not just unpolished) — a user would notice something is *wrong*, not just plain. | P0–P1 |
| **Quick Win** | Small, low-risk change (a token swap, a class change, adding an existing primitive) with outsized visible impact. | P2, occasionally P1 if it's a known-fixed-elsewhere regression |
| **Longer-Term** | Needs a design-system-level decision, a new pattern, or touches multiple screens/roles to do properly — not a same-night fix. | P2–P3, or routed to "do not build blind" if it touches money/auth/compliance UI |

---

## 5. Output Report Template

```markdown
# UI/UX Screenshot Audit — <screen name> — <date>

**Screen:** <route> / <ComponentFile.tsx> · **Role:** <client|artist|owner|admin> ·
**Theme shown:** <light|dark> (other theme: checked / not checked) ·
**Breakpoint shown:** <phone <sm | tablet sm–lg | desktop lg+> (others: checked / not checked) ·
**Source access:** <live repo read | image only — contrast findings estimated>

## Critical Issues
### C1 — <exact element/component> — <one-line problem>
**Evidence:** <computed contrast ratio + token values, or "[estimated from image]"> ·
**Benchmark:** <competitor(s) + what they do, or "matches category norm" / "deliberate divergence, see note">
**Fix:** <exact file, exact change>
**Role parity:** <same gap on other roles? checked?>
**Help-sync:** <obligation stated per §4.10>
**Priority:** P<0-3>

## Quick Wins
(same shape)

## Longer-Term
(same shape, plus: what design-system/product decision it depends on)

## Confirmed Clean
<dimensions/checks that were verified and found no issue — don't leave these blank; say
what was checked and that it passed>
```

---

## 6. Final Verification Checklist (run before delivering the report)

- [ ] Every contrast claim is computed against real token values, or explicitly labeled estimated.
- [ ] Theme and breakpoint of the screenshot are stated; other combinations' check-status is stated, not silently omitted.
- [ ] Every "missing element" finding checked against the real `ui/` component inventory (§4.3) first.
- [ ] Every finding is benchmarked (or explicitly marked a deliberate divergence) per §2.
- [ ] Role-parity (§4.9) addressed for every finding.
- [ ] Help-sync obligation (§4.10) stated for every behavior-changing finding, including explicit "no change needed" verdicts.
- [ ] No finding invents a file path, token name, or component that wasn't verified against source.
- [ ] Money/auth/tenant-isolation/compliance-adjacent findings are routed to a backlog spec, not a same-night fix suggestion.
- [ ] Report uses the §5 template — no bare bullet list of vague complaints.
- [ ] This audit did not edit any source file — output is the report only, per this project's scope boundary.
