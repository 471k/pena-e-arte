# Accessibility audit — 2026-09-05

**Owner:** Phi · **Related:** `frontend/e2e/accessibility.spec.ts`, `docs/qa/cross-device-checklist.md`

No automated WCAG coverage existed before this pass — `frontend/package.json` had no
`axe-core`/`pa11y`/`lighthouse` dependency, and nothing in `.github/workflows/ci.yml` gated on
accessibility. This added `@axe-core/playwright` (chosen over `axe-playwright`: the official
Deque package, published days before this pass vs. the alternative's ~1-year-stale last
release) and `frontend/e2e/accessibility.spec.ts`, asserting zero WCAG 2.1 AA violations on 7
states across the 5 highest-traffic/highest-risk surfaces named in the source prompt: sign-in,
studio sign-up, the public guest booking flow, the client home (My Studios), the owner
dashboard, the deposit-payment page's not-found state, and the in-flow `PaymentMethodSelector`
(both its Card-unavailable and Cash states). This runs as part of the existing `pnpm test:e2e`
step in CI's `Frontend` job — no separate job needed, the job's ~3.5 min runtime had headroom.

## What this found — much bigger than expected

Running the new suite surfaced a real, undiscovered, app-wide bug, not just isolated
contrast tweaks:

### 1. The dark theme was hardcoded for every visitor, regardless of OS preference

`frontend/src/index.css` declared light-mode color tokens via `@theme`, then wrapped a
dark-mode override in `@media (prefers-color-scheme: dark) { @theme { ... } }`. **Tailwind v4's
`@theme` directive cannot be conditionally scoped** — nesting it inside `@media` silently
collapses both blocks into one unconditional `:root` declaration, with whichever block is later
in the source winning. Confirmed directly against the built CSS (`dist/assets/*.css`) before the
fix: exactly one `--color-background` declaration existed, `#09090b` (dark), with no media query
and no light variant at all. **Every visitor got the dark theme, unconditionally, since this CSS
was written.** Fixed by moving the override to a plain `:root { ... }` rule inside the `@media`
block (normal CSS custom-property cascade, doesn't need `@theme` a second time) — verified in
the rebuilt CSS that both a light-mode unconditional value and a real, gated dark-mode override
now exist.

This is also why the original axe run (Playwright's default `colorScheme` is `light`) still saw
dark colors: the app was never actually rendering light mode for anyone, in any browser.

**Consequence for testing going forward:** `frontend/playwright.config.ts` now defines two full
projects, `chromium-light` and `chromium-dark`, instead of one `chromium` project — every e2e
spec (not just the new accessibility one) now runs under both color schemes, in CI included.
Before this, the entire suite had zero coverage of whichever scheme wasn't the runner's default.

### 2. A systemic pattern: bare Tailwind colors tuned for one theme, used unconditionally

Once light mode was actually rendered for the first time, several more real violations surfaced
that were invisible before:

- **Opacity-blended text** (`text-foreground/NN`, `text-muted-foreground/NN`) cannot pass 4.5:1
  in both themes from a single percentage — light mode needs far more opacity than dark mode for
  the same nominal blend (computed: `text-foreground` needs ≥56% in light vs. ≥46% in dark;
  `text-muted-foreground` needs ≥97% in light vs. ≥73% in dark). Fixed by bumping
  `text-foreground/NN` below 65% up to `/65` (clears both with margin) and dropping the opacity
  suffix entirely on `text-muted-foreground` (the plain token already passes both themes) —
  applied across every non-icon instance found (icons only need the lower 3:1 non-text-contrast
  bar, already met, and were left alone). ~40 occurrences across ~20 files.
- **Raw colors chosen for one theme, applied without a `dark:` split** — the same root cause as
  finding 1, just at the component level instead of the token level. Found and fixed three
  instances directly: `OAuthButtons`'s divider text, a `text-violet-400` pattern used bare across
  ~10 public-facing files (sign-up links, filter pills, badges — now `text-violet-700
  dark:text-violet-400` with matching hover shades, matching the pattern this codebase already
  used correctly elsewhere, e.g. `DesignDetailPage.tsx`, `PlanLimitBanner.tsx`), a
  `text-emerald-500` "current studio" badge (now `text-emerald-800 dark:text-emerald-500`), and
  `BookPage`'s email-verification banner, whose background/border/text (`bg-amber-950/20
  border-amber-800/50 text-amber-300`) were tuned for dark mode only — in light mode it measured
  1.03:1 and 1.14:1 (both far under 4.5:1), effectively invisible. Rewritten to match the
  established `border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-400` pattern
  already used correctly in `ReadOnlyBanner`, `LoginPage`, and `DashboardPage`.
- **A base-token near-miss**: light mode's `--color-muted-foreground` (46.1% lightness) computed
  to 4.40:1 against `bg-muted` and 3.98:1 against a further-tinted card background — both under
  4.5:1, with no opacity modifier involved at all. Darkened to 42% lightness (5.10:1 / 4.62:1),
  confirmed dark mode is unaffected (already passes at its own value).

**Not fully swept**: a broader inventory turned up hundreds of other raw-Tailwind-color text
usages (`text-red-*`, `text-green-*`, `text-amber-*`, etc.) across the codebase. Many already
follow the correct `text-X-700 dark:text-X-400`-style split (evidenced by near-equal usage counts
of paired light/dark shades for several colors); an unknown number may not. This pass fixed only
the instances the 7 axe-scanned states actually exercise — a full inventory-and-verify pass
across every raw color usage in the app is real, separate, larger follow-up work, not done here.

### 3. `text-destructive` used for body/alert text in 167 places (unrelated to the theme bug)

Separately, `index.css` has long documented that `--color-destructive` "is tuned for
button/border fills and fails WCAG 1.4.3 (4.5:1) as body text" — with a dedicated
`--color-destructive-text` token existing specifically for text use, already adopted in 9 files.
The remaining 167 occurrences across 65 files (validation-error paragraphs, alert banners, status
text) still used `text-destructive` directly. Swept the whole app: every text-bearing instance
now uses `text-destructive-text`; pure icon-coloring instances (SVG components, icon-only hover
buttons) were left on `text-destructive`, since icons only need the lower 3:1 non-text-contrast
threshold that token was already tuned for.

## Verification

All of the above was fixed in this same session, not deferred — `frontend/e2e/accessibility.spec.ts`
passes with zero violations across all 7 scanned states, under both `chromium-light` and
`chromium-dark` projects (26/26 e2e tests green, confirmed via a real `pnpm build` + `pnpm
test:e2e` run, not assumed from the CSS diff alone).
