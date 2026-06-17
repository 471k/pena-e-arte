# Issues & Known Gaps

This file tracks known issues, gaps in test coverage, and deferred improvements.
Issues are grouped by priority tier (P1–P7) and updated as they are resolved.

---

## P1 — Blocking / Security

_No open issues._

---

## P2 — Test Coverage & Quality

| # | Description | Status |
|---|---|---|
| 36 | No Playwright / Cypress e2e setup. | ✅ Resolved 2026-06-17 — `@playwright/test` added, `playwright.config.ts` created, critical-path test (`register → login → create appointment`) added to `frontend/e2e/`. |

---

## P3 — Feature Gaps

_No open issues. See `docs/claude/self-promotion-prompts.md` for planned features._

---

## P4 — Housekeeping

| # | Description | Status |
|---|---|---|
| 12 | `issues.md` tracking doc missing. | ✅ Resolved 2026-06-17 — this file created. |
| 13 | Placeholder test stubs `UnitTest1.cs` left over from project creation. | ✅ Resolved 2026-06-17 — deleted from both `UnitTests` and `IntegrationTests` projects. |
| 14 | Discrepancy between SP-02 spec (`IsPublished`) and implementation (`IsActive`) for public portfolio filtering. | ✅ Resolved 2026-06-17 — documented in `docs/claude/architecture.md` and as XML doc on `Studio.IsActive`. |

---

## P5 — Performance / Observability

_No open issues._

---

## P6 — DevOps / Infrastructure

_No open issues._

---

## P7 — Superseded / Obsolete

| # | Description | Status |
|---|---|---|
| 33 | ~3% frontend test coverage (only 28 tests at the time). | ✅ Obsolete — 908 tests across 67 files as of 2026-06-17. |
| 34 | No auth flow tests. | ✅ Obsolete — `LoginPage.test.tsx`, `authSlice.test.ts`, `RegisterStudioPage.test.tsx`, `ForgotPasswordPage.test.tsx`, `ResetPasswordPage.test.tsx` all exist. |
| 35 | No RTK Query endpoint tests. | ✅ Partially resolved — all major API slices exercised through component tests. Dedicated contract tests deemed unnecessary given the component-level coverage. Judgment call recorded here. |

---

## Adding New Issues

Use the next sequential number. Assign priority P1–P7:

- **P1** Blocking, security, data-loss
- **P2** Test coverage / quality gates
- **P3** Incomplete feature implementation
- **P4** Low-effort housekeeping
- **P5** Performance or observability gap
- **P6** DevOps / infra / deployment
- **P7** Superseded, obsolete, or recorded-for-posterity only
