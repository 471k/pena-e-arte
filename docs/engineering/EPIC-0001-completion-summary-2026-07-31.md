# EPIC-0001 — Completion summary (overnight run, 31 July 2026)

**Branch:** `epic-0001/pre-implementation-hardening` (pushed, NOT merged — human
review required; touches legal disclosure, and later phases touch payments/secrets/
consent/GDPR).
**Base:** `f3bf5d3` (this isolated worktree was one docs-only commit behind
`main`'s `7e4196c`; the missing commit is a K3s docs log, immaterial to every code
phase here).
**Mode:** fully autonomous, no user present.

This is the direct-to-branch equivalent of a PR description (no PR flow was
configured for this run).

---

## Commit history on the branch

| Commit | Phase | Description |
|---|---|---|
| `7a8d3c2` | setup | Import EPIC-0001 reference docs into worktree branch for context (these were untracked in the main checkout and absent from this worktree) |
| `ab92a98` | checkpoint | `chore: checkpoint before EPIC-0001 hardening pass` (empty, per §0) |
| `6f1491f` | **Phase 1 — PENA-100/101** | `feat(public): platform legal-entity disclosure + dead /privacy /terms link fix` |
| `1b26e7e` | **Phase 2 — PENA-102** | `feat(public): policy routes, public home shell, refund policy from live deposit rules` |
| _(this doc)_ | stop-boundary | Open questions + Execution status + Decisions Log + this summary |

## Phases landed

- **Phase 1 (PENA-100 + PENA-101)** — legal-entity disclosure + dead-link fix. ✅ Verified.
- **Phase 2 (PENA-102)** — public policy pages + home shell + refund copy from live code. ✅ Verified.

## Phases NOT started (stopped at the Phase 2 boundary)

Phases 3, 4, 5, 6, 7 — see the Execution status note in
`docs/engineering/EPIC-0001-pre-implementation-hardening.md` for the full remaining
scope and the reason for stopping (single bounded autonomous session; Phases 3–6
each need a full `dotnet build`, `dotnet test` incl. the MySQL-backed integration
suite, live EF migrations, and — for 5/6 — a Vault container and a ~22-site
payments refactor, none verifiable to DoD within budget; the master prompt
mandates stopping at a phase boundary rather than committing unverified payments/
secrets/consent code onto a review branch).

---

## Help-sync verdict per phase (DoD item 4)

- **Phase 1:** NO Help-content change needed. A footer and page `<title>`/meta are
  not "how do I…" topics; no `helpContent.ts` article covers them and no
  onboarding-tour step references them (confirmed via grep). Stated explicitly per
  the project rule.
- **Phase 2:** NO `helpContent.ts` change needed — Help content is scoped to
  authenticated in-app usage; these are pre-login surfaces. The in-scope
  user-visible addition (signup Terms/Privacy consent links on both register
  pages) WAS made. No onboarding-tour step covers pre-login pages.
- **Phases 3–7:** not reached — their Help-sync obligations (Phase 3 has three
  concrete `helpContent.ts` edits incl. fixing the `faq-portable-profile`
  inaccuracy; Phase 6 requires provider-neutralising Stripe wording) remain
  outstanding.

## Industry-standard citation per phase

- **Phase 1:** EU e-Commerce Directive Art. 5 + Albanian consumer-protection/
  e-commerce trader-identification disclosure; the brand-in-header / legal-entity-
  in-footer split used by Stripe/Notion/Linear/Vercel and checked by PSP/MoR KYC
  reviewers.
- **Phase 2:** GDPR Art. 13/14 + Albania Law 124/2024 (Privacy content bar);
  EU/Albanian consumer-protection distance-selling rules (refund disclosure bar);
  WCAG 2.1 AA landmark structure (header/main/footer) on every page.

---

## Final self-check (run at the stop boundary)

Scoped to what this run actually touched (frontend + one `appsettings.json` JSON
edit; no backend C# compiled).

- [x] `dotnet build` — **N/A / not run.** No C# was modified. The only backend
  file touched is `Pena_e_Arte.API/appsettings.json`; validated as parseable JSON
  (`JSON.parse` OK). No `.cs`, `.csproj`, or project structure changed.
- [x] `dotnet format --verify-no-changes` — N/A (no C# changed).
- [x] `dotnet test` incl. architecture test — N/A (architecture test is Phase 6,
  not built; no backend logic changed).
- [x] `pnpm lint` — **clean: 0 errors** (15 pre-existing warnings, all in
  untouched `StudioProfilePage.tsx`/`RegisterStudioPage.tsx` react-compiler
  `watch()` cases; none introduced by this run).
- [x] `pnpm build` (`tsc -b` + vite) — **clean** (`✓ built`; only pre-existing
  node_modules pure-annotation / chunk-size warnings).
- [x] `pnpm test` (affected suites) — **green:** router (incl. new policy-route
  assertions), SiteFooter, CookieConsentBanner, PublicContentPages,
  ClientRegisterPage, RegisterStudioPage.
- [x] No `Console.WriteLine` / `console.log` introduced (grep of full diff — only
  matches are inside copied reference docs, not code).
- [x] No secret / connection string / API key added anywhere — `appsettings.json`
  additions are empty-string placeholders matching the existing `BaseUrl` pattern.
- [x] Every touched file inside the §4 scope boundary (`frontend/src`,
  `frontend/index.html`, `frontend/public`—none needed, `Pena_e_Arte.API/appsettings.json`,
  `docs/`).
- [x] `IStripeBillingService.cs` / `StripeBillingService.cs` /
  `StripeDiscountService.cs` byte-for-byte unchanged (`git diff f3bf5d3 --` = empty).
- [x] Every new/changed endpoint has `.RequireAuthorization()` — N/A, no endpoints
  added or changed this run.
- [x] Every phase's Help-sync verdict stated explicitly (in each commit body and
  above).
- [x] `## Open questions for the founder` present in the epic doc with all 8 items
  + phase tags.
- [x] `## Execution status` note present in the epic doc with the last-good commit
  SHA (`1b26e7e`).

## Deviations from the spec's assumptions (judgment calls)

1. **The spec files and companion docs were not in this worktree.** They were
   untracked in the main checkout and absent from this isolated worktree
   (`docs/engineering/`, `docs/payments/`, the epic prompt). Read them via absolute
   path; copied the ones needed as edit targets / references into the branch
   (commit `7a8d3c2`) so the branch is self-contained for the reviewer.
2. **§0's `git checkout main && git pull` could not run literally** — this is an
   isolated worktree that cannot target the shared checkout. Branched off the
   worktree HEAD (`f3bf5d3`) instead; the one missing commit is docs-only and
   immaterial. Flagged in the Execution status note.
3. **Phase 1's "placeholder policy pages" vs. §1e's "no placeholder-then-real
   split":** resolved by making Phase 1 create real (minimal) page components so
   routes and links resolve and tests pass, then Phase 2 enriched their content —
   each commit builds and is independently reviewable, honoring both the
   one-commit-per-phase rule and the "no dead stub" intent.
4. **`/` Home surface:** rather than add a second `/` route conflicting with the
   authenticated `AppRoot` tree, changed `IndexRedirect` so an unauthenticated `/`
   renders `HomePage` while authenticated users still go to their role home — the
   least-disruptive way to achieve the spec's intent.
5. **Cookie-banner link uses a plain `<a href="/privacy">`** (not react-router
   `<Link>`) because its existing unit test renders the banner outside any Router
   context; `<Link>` would have thrown. Matches `LoginPage.tsx`'s existing pattern.
