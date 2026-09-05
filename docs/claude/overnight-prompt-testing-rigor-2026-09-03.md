# Overnight Prompt — Testing Rigor (Tier 5)

> Feed this file directly to Claude Code (main **Pena e Artë - Engineering** project, full repo
> write access) as the task prompt. **Fully autonomous** for §1 (accessibility), §2 (cross-device
> QA checklist), and §3 (E2E staleness review) — no external dependency. §4 (load/performance
> testing) needs a real target to run against and should run **after**
> `docs/claude/overnight-prompt-staging-goes-live-2026-09-03.md` has landed — run it against
> staging, never against production. If staging isn't live yet, do §1–§3 now and leave §4 for a
> follow-up run once it is; say so plainly rather than skipping ahead to production.

**Date logged:** 2026-09-03
**Requested by:** Phi
**Origin:** Engineering-consultation gap audit. None of these four exist today — confirmed via
repo search: no `axe-core`/`pa11y`/`lighthouse` dependency anywhere in `frontend/package.json`,
no `k6`/Artillery config anywhere in the repo, and the e2e suite (`frontend/e2e/`) has exactly
two spec files (`critical-path.spec.ts`, `my-studios-kebab-menu.spec.ts`) run via `pnpm
test:e2e` (Playwright) — this is the entire current automated coverage beyond unit tests.

**Checkpoint before starting:**
```bash
git status
git checkout main && git pull
git checkout -b test/accessibility-load-qa-e2e-review
git commit --allow-empty -m "checkpoint: before testing-rigor work"
```

---

## 1. Accessibility audit (autonomous)

No WCAG pass exists on record, and nothing in `ci.yml` gates on it today. Add automated
coverage rather than a one-time manual pass, so this doesn't silently regress:

1. Add `@axe-core/playwright` (or `axe-playwright` — check which has better current maintenance
   status before picking) as a dev dependency.
2. Extend the existing Playwright suite (`frontend/e2e/`) with accessibility assertions on the
   highest-traffic, highest-risk pages for a booking SaaS — at minimum: the public booking flow
   (`/book`), sign-in/sign-up, the client dashboard, the owner dashboard, and the deposit-payment
   page (`/pay/:paymentId` and the in-flow `PaymentMethodSelector`). Use `injectAxe`/`checkA11y`
   (or the chosen library's equivalent) against each, asserting zero WCAG 2.1 AA violations —
   don't just log violations without asserting on them, that produces a report nobody reads.
3. Wire this into `ci.yml`'s existing `Frontend — lint, typecheck, build, unit test, e2e` job
   (or a new dedicated job if the existing one is already at a reasonable size/duration — check
   its current runtime first) so a real regression fails CI, not just a manual audit.
4. Run it against the current app and fix what it finds **in this same session** — don't just
   add the tooling and leave known violations for later; that defeats the point of gating CI on
   it (a red CI from day one gets disabled, not fixed). Common, cheap-to-fix violations to expect
   in a Tailwind/shadcn-based app: missing form-label associations, insufficient color contrast
   on muted-text-on-muted-background combinations, missing accessible names on icon-only buttons
   (this codebase uses a lot of bare `lucide-react` icons in buttons — grep for `<Button` uses
   with only an icon child and no `aria-label` as a starting point).
5. Document what was found and fixed in a short section appended to
   `docs/claude/architecture.md` (or a new `docs/claude/accessibility-audit-2026-09-03.md` if a
   full write-up is warranted — match this project's existing convention of a dated audit doc
   for a substantial one-time pass, e.g. `industry-feature-parity-report-*.md`).

---

## 2. Cross-device manual QA checklist (autonomous)

Mobile bugs so far have been found ad hoc (per the mobile UI/UX baseline work referenced in the
source audit), not via a repeatable check. Build a lightweight, repeatable checklist — **not**
automation (that's what §1's Playwright coverage already does for correctness; this is for
visual/layout regressions automation doesn't reliably catch):

1. Write `docs/qa/cross-device-checklist.md`: a device/breakpoint matrix (at minimum: iPhone SE
   width as the smallest common mobile breakpoint, a mid-size Android width, iPad portrait, and
   the existing desktop baseline) crossed against the highest-traffic flows (same list as §1
   step 2 — booking, sign-in, both dashboards, deposit payment). For each cell, a short list of
   what to visually check (no horizontal scroll, no overlapping/clipped text, tap targets ≥44px,
   the body-map/booking-form specifically since it's the most layout-complex screen in the app).
2. This is a manual-QA artifact, not code — don't try to automate it into Playwright viewport
   assertions in this same step (that would just be a narrower version of §1's coverage under a
   different name). If genuinely valuable, note visual-regression tooling (e.g. Playwright's own
   screenshot-comparison mode) as a possible future upgrade in the doc, but don't build it here
   — that's a real scope decision Phi should make deliberately, not something to slip in.

---

## 3. E2E suite staleness review (autonomous)

The suite has "silently broken before" per the source audit (tests desynced from unrelated form
changes, only caught because CI happened to still run them) — confirmed structurally plausible
given the suite is only two files covering an app with dozens of features (per
`docs/claude/architecture.md`'s Feature Module Map). This is a review pass, not a rewrite:

1. Run `pnpm test:e2e` locally against the current `main` and confirm both existing specs
   actually pass today, with real assertions (open each spec file and read what it actually
   checks — a spec that runs without asserting anything meaningful is worse than no spec, since
   it creates false confidence).
2. For `critical-path.spec.ts` specifically: confirm the flow it exercises still matches the
   current UI — walk through the same steps by hand (or via `claude-in-chrome`/browser
   automation against a local dev server) and diff against what the spec asserts. Fix any
   selector/copy drift found.
3. Identify the two or three highest-value **missing** critical paths given the current feature
   set (e.g. the deposit payment flow now that §1 above touches it directly, and the intake-form
   consent flow if `docs/claude/overnight-prompt-compliance-payment-correctness-2026-09-03.md`
   has already landed) and add focused specs for them — not exhaustive coverage, matching this
   project's stated "handful of focused assertions" testing philosophy elsewhere.
4. Note in the final summary that this should be a **recurring** review, not a one-time fix —
   suggest a cadence (e.g. whenever a booking/payment/auth-flow PR merges, or monthly) rather
   than leaving it purely as "done now."

---

## 4. Load/performance testing (run against staging only, once it's live)

None exists anywhere in the repo today. **Do not run this against production under any
circumstance.**
```bash
kubectl get pods -n pena-e-arte-staging     # must be Running before this section proceeds
```
1. Pick a tool: k6 is the better fit here (scriptable in JS, matching this codebase's existing
   language, and has a straightforward GitHub Actions integration if this ever gets promoted
   into CI later) over Artillery — state this choice rather than silently defaulting to one.
2. Define one baseline scenario covering the two flows most likely to see a real traffic spike
   for this product category (a booking SaaS): the public booking-request flow (`POST` to
   create an appointment as a guest, the heaviest write path a stranger can hit) and the public
   portfolio/discover browse flow (`DiscoverPage`, `EmbedPage` per `docs/claude/architecture.md`
   — the heaviest anonymous read path). Model a modest but real spike, not a stress-to-failure
   test — e.g. ramping to 50 virtual users over 2 minutes, holding for 3 minutes, ramping down —
   this is a first baseline, not a capacity-limits study.
3. Run it against `https://staging.tattooos.co`, capture p50/p95/p99 latency and error rate, and
   cross-reference against the shared Grafana/Prometheus instance (confirm staging's request
   metrics are actually visible there mid-run, not just after — this is also a live check that
   the shared-observability wiring from the staging prompt is really working under load).
4. Write the results and the k6 script itself into the repo (`load-tests/` at the root, or
   wherever fits this project's existing structure — check if such a directory convention
   already exists before inventing one) and a short `docs/infra/load-test-baseline-2026-09-03.md`
   recording what was measured, so a future run has something to compare against.
5. If anything in this baseline run reveals a real problem (error rate spike, a specific
   endpoint's latency far outside the rest), report it clearly but **do not attempt to fix
   backend performance issues in this same prompt** — that's separate, scoped work; this
   prompt's job is to establish the baseline and surface findings, not to chase them.

---

## 5. Explicitly out of scope

- Automating §2's cross-device checklist into visual-regression tests — noted as a future option
  in the doc, not built here.
- Running §4 against production, ever, under any framing.
- Fixing any backend performance issue §4 surfaces — report only.
- A full WCAG 2.1 AAA pass, or manual screen-reader testing — AA via automated `axe-core`
  coverage is this prompt's scope; deeper manual accessibility testing is a separate, larger
  initiative if AA coverage reveals it's warranted.

---

## 6. Final self-check

- [ ] §1's axe-core (or equivalent) coverage is wired into CI, actually fails on a real
      injected violation (verify this, don't just assume the wiring works), and every violation
      it found against the current app was fixed in this session, not deferred.
- [ ] §2's checklist doc exists and covers the flows named above at the breakpoints named above
      — not a generic template with placeholders.
- [ ] §3: both existing e2e specs were confirmed passing with meaningful assertions, any drift
      found was fixed, and at least one new spec was added for a currently-uncovered
      high-value flow.
- [ ] §4 ran against staging only, never production, and — if staging wasn't live yet — this
      session said so plainly and left the section for a follow-up rather than skipping it
      silently or running it against production to "get it done anyway."
- [ ] `pnpm test`, `pnpm test:e2e`, `pnpm build`, and `pnpm lint` all pass at the end of this
      session.
- [ ] The final summary states, per section, what was completed versus deferred (§4 in
      particular) — not a blanket "testing rigor improved" claim.
