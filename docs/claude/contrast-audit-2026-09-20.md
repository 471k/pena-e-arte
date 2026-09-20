# Contrast Audit — Rendered UI, 2026-09-20

> Origin: F-18 of `feature-spec-artist-social-tab-and-profile-chrome-2026-09-20.md` — a screenshot
> audit *eyeballed* several low-contrast candidates and explicitly left them unmeasured. This document
> is the measurement. **Nothing was changed as a result** (see "What this does not change").

## Method

`frontend/scripts/measure-rendered-contrast.ts` (`node scripts/measure-rendered-contrast.ts` with the Vite
dev server running; API calls are mocked like the Playwright e2e specs, no backend needed). For each
target it reads the element's *computed* `color` and its effective background — the first opaque
ancestor `background-color`, with any alpha layers composited over what's behind them — normalises both
to sRGB through a 1×1 canvas (Tailwind v4 emits `oklch()` / `color-mix()`, which a plain `rgb()` parser
can't read) and applies the WCAG 2.x relative-luminance formula. Pages: the artist profile (owner
layout, artist-mode sidebar) and its Social tab, at 1280×900, in a light and a dark browser context.

Thresholds: 4.5:1 normal text; 3:1 large text (≥ 24 px, or ≥ 18.66 px bold) and non-text boundaries
(1.4.11). The script is **report-only** — it always exits 0 and is not wired into CI.

Companion, different tool: `frontend/scripts/check-contrast.ts` (`pnpm check-contrast`) checks a few
*hand-copied design-token pairs* against each other; it cannot see a token used on a background it
was never meant for. This script can, which is why both exist.

## Results

| Theme | Element | Sample text | Size/weight | Ratio | Required | Result |
|---|---|---|---|---|---|---|
| light | Sidebar group label | People | 12px/500 | 5.61:1 | 4.5:1 | Pass |
| light | Sidebar item (inactive) | Owner Dashboard | 14px/400 | 5.61:1 | 4.5:1 | Pass |
| light | Sidebar item (active) | My Portfolio | 14px/500 | 14.34:1 | 4.5:1 | Pass |
| light | Sidebar active fill vs page (boundary, 1.4.11) | My Portfolio | 14px/500 | 1.24:1 | 3:1 | Fail |
| light | Collapse-shortcut chip (Ctrl B) | Ctrl B | 10px/500 | 5.61:1 | 4.5:1 | Pass |
| light | Header user subtitle (role) | Owner | 12px/400 | 5.61:1 | 4.5:1 | Pass |
| light | Page subtitle ("Artist") | Artist | 14px/400 | 5.61:1 | 4.5:1 | Pass |
| light | Tab trigger (inactive) | Portfolio | 14px/500 | 5.1:1 | 4.5:1 | Pass |
| light | Tab trigger (active) | Profile | 14px/500 | 19.9:1 | 4.5:1 | Pass |
| light | Section helper text | Link Rafaela's accounts so clients can f | 14px/400 | 5.61:1 | 4.5:1 | Pass |
| light | Row helper / secondary line | Connect Rafaela's Instagram to automatic | 12px/400 | 5.61:1 | 4.5:1 | Pass |
| light | Row handle (@handle) | @rafa.ink | 14px/400 | 19.9:1 | 4.5:1 | Pass |
| light | Status badge (Not linked) | Not linked | 11px/500 | 19.9:1 | 4.5:1 | Pass |
| light | Verified badge | Verified | 10px/500 | 6.38:1 | 4.5:1 | Pass |
| light | Handle input placeholder |  | 14px/400 | 5.61:1 | 4.5:1 | Pass |
| light | Handle input text |  | 14px/400 | 19.9:1 | 4.5:1 | Pass |
| light | Disabled button (Get verification code) | Get verification code | 14px/500 | 3.74:1 | 4.5:1 | Fail |
| light | Ghost destructive button (Disconnect) | Disconnect | 14px/500 | 6.42:1 | 4.5:1 | Pass |
| light | Destructive button in dialog (Disconnect) | Disconnect | 14px/500 | 3.61:1 | 4.5:1 | Fail |
| dark | Sidebar group label | People | 12px/500 | 7.76:1 | 4.5:1 | Pass |
| dark | Sidebar item (inactive) | Owner Dashboard | 14px/400 | 7.76:1 | 4.5:1 | Pass |
| dark | Sidebar item (active) | My Portfolio | 14px/500 | 15.39:1 | 4.5:1 | Pass |
| dark | Sidebar active fill vs page (boundary, 1.4.11) | My Portfolio | 14px/500 | 1.24:1 | 3:1 | Fail |
| dark | Collapse-shortcut chip (Ctrl B) | Ctrl B | 10px/500 | 7.76:1 | 4.5:1 | Pass |
| dark | Header user subtitle (role) | Owner | 12px/400 | 7.76:1 | 4.5:1 | Pass |
| dark | Page subtitle ("Artist") | Artist | 14px/400 | 7.76:1 | 4.5:1 | Pass |
| dark | Tab trigger (inactive) | Portfolio | 14px/500 | 5.81:1 | 4.5:1 | Pass |
| dark | Tab trigger (active) | Profile | 14px/500 | 19.06:1 | 4.5:1 | Pass |
| dark | Section helper text | Link Rafaela's accounts so clients can f | 14px/400 | 7.76:1 | 4.5:1 | Pass |
| dark | Row helper / secondary line | Connect Rafaela's Instagram to automatic | 12px/400 | 7.76:1 | 4.5:1 | Pass |
| dark | Row handle (@handle) | @rafa.ink | 14px/400 | 19.06:1 | 4.5:1 | Pass |
| dark | Status badge (Not linked) | Not linked | 11px/500 | 19.06:1 | 4.5:1 | Pass |
| dark | Verified badge | Verified | 10px/500 | 6.5:1 | 4.5:1 | Pass |
| dark | Handle input placeholder |  | 14px/400 | 7.76:1 | 4.5:1 | Pass |
| dark | Handle input text |  | 14px/400 | 19.06:1 | 4.5:1 | Pass |
| dark | Disabled button (Get verification code) | Get verification code | 14px/500 | 5.15:1 | 4.5:1 | Pass |
| dark | Ghost destructive button (Disconnect) | Disconnect | 14px/500 | 6.02:1 | 4.5:1 | Pass |
| dark | Destructive button in dialog (Disconnect) | Disconnect | 14px/500 | 9.6:1 | 4.5:1 | Pass |

38 measurements, 4 below threshold, 0 not found.

## Reading the results

**The screenshot's eyeballed candidates do not hold up.** Sidebar group labels, the "Artist" /
role subtitles, the `Ctrl B` chip, helper and secondary text, inactive tabs and the placeholder all
measure **5.1:1–7.8:1** in both themes — comfortably above 4.5:1. `text-muted-foreground` is not the
problem the audit suspected.

**Real findings — three rows below threshold:**

1. **Destructive button, light theme — 3.61:1** (white `text-destructive-foreground` on the
   `bg-destructive` fill, `#ef4444`). Real, and it matches what `@axe-core/playwright` reports (3.6:1,
   `color-contrast`, serious) on the Social tab's disconnect dialog. It affects **every**
   `variant="destructive"` button in the app in the light theme, not just this feature. Dark theme is
   fine (9.6:1). The *ghost* destructive button ("Disconnect" row action) uses the separate
   `text-destructive-text` token and passes (6.4:1 light, 6.0:1 dark) — so the app already has a
   darker red that clears the bar.
2. **Disabled button, light theme — 3.74:1.** Exempt from SC 1.4.3 (inactive user-interface
   components) — listed for legibility only. No action required.
3. **Active sidebar item fill vs page background — 1.24:1, both themes.** SC 1.4.11 only bites if the
   fill is the *sole* indicator of the active state. It is not: the active item also switches its text
   from 5.6:1 muted to 14.3:1 foreground and from weight 400 to 500. Judged a pass by exception, but a
   non-colour indicator (e.g. a left accent bar) would make this robust for low-vision users who can't
   perceive the text-colour change either.

Also confirmed while building the Social tab (no measurement needed): axe reports **zero
violations** on every Social-tab state (owner, artist, read-only, error, disconnect dialog, 375 px),
in both themes, apart from finding 1 in the dialog — the dialog test disables `color-contrast` for that
one state with a comment pointing here, rather than patching a shared token per component.

## What this does not change

Per decision D-4, contrast fixes belong at the **shared design-token level, once**, with a
before/after screenshot set — a token change touches every screen. That needs its own visual
regression pass (four widths, both themes), so this PR is measurement only: no token, `tailwind`
config or component style was changed on the strength of these numbers.

Not measured here (out of the audited surface): hover / focus / pressed states, the onboarding-tour
overlay, toasts, the public pages, charts, and any page other than the artist profile.

## Next steps

1. **Follow-up PR: `fix(design-tokens): light-theme destructive button contrast`** — raise
   `bg-destructive` (light) to a fill that clears 4.5:1 against `text-destructive-foreground`. The
   existing `destructive-text` red (`hsl(0 74% 42%)`, 6.4:1 on white) is the obvious candidate to reuse
   as the fill. Ship with the before/after screenshots (1440 / 1024 / 768 / 375 px, light + dark) of
   every screen that renders a destructive button, then remove the `color-contrast` opt-out from the
   disconnect-dialog case in `frontend/e2e/artist-social-tab.spec.ts`.
2. Decide whether to add a non-colour active-item indicator to the sidebar (finding 3).
3. Turn this script's `Fail` rows into assertions (or fold the token pairs into `check-contrast.ts`)
   once step 1 has landed, so a regression fails CI instead of waiting for another audit.
