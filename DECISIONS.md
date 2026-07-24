# DECISIONS — Auth Flow Design-Token, Accessibility & UX Remediation

Overnight pass fixing the systemic design-token bugs behind the ResetPasswordPage
audit, plus the same anti-patterns across the rest of the unauthenticated auth flow.

## Contrast — before / after

Computed via `frontend/scripts/check-contrast.ts` (WCAG relative-luminance formula).

| Pair                              | Theme | Before   | After    | Threshold | 
|------------------------------------|-------|----------|----------|-----------|
| `--color-border` / background      | Light | 1.95:1   | 3.25:1   | 3:1       |
| `--color-input` / background       | Light | 1.95:1   | 3.25:1   | 3:1       |
| `--color-border` / background      | Dark  | 1.90:1   | 3.25:1   | 3:1       |
| `--color-input` / background       | Dark  | 1.90:1   | 3.25:1   | 3:1       |
| destructive text / background      | Light | ~1.98:1* | 6.41:1   | 4.5:1     |
| destructive text / background      | Dark  | ~1.98:1* | 6.03:1   | 4.5:1     |

\* Before: error copy used `--color-destructive` (tuned for button fills) directly as
text color. That token's contrast against its own theme's background was never
designed to meet 1.4.3; ~1.98:1 is its approximate ratio in dark mode per the
original audit. A dedicated `--color-destructive-text` token now exists for
text-on-background use in both themes; `--color-destructive` is untouched and still
used for button/border/ring fills.

Run `node scripts/check-contrast.ts` from `frontend/` to reverify.

## Judgment calls

- **Token propagation scope.** `text-destructive` → `text-destructive-text` was
  applied to the shared `Alert` (destructive variant) and `PasswordStrengthMeter`
  components (both cascade into every auth page automatically) plus the inline
  field-error paragraphs on all 5 auth pages (Login, ClientRegister, ForgotPassword,
  ResetPassword, ChangePassword). It was **not** applied to the other ~75 files in
  the app that also use `text-destructive` for various purposes (badges, buttons,
  non-auth pages) — that's a much larger design-system pass outside this task's
  explicit scope (non-goal #1: no full design-system overhaul). The border/input
  token fix in `index.css`, by contrast, *is* global and fixes every consumer
  automatically since it's a CSS variable, not a per-usage class swap.

- **RESET_TOKEN_INVALID discrimination is real but coarse.** Added a `code` field
  (`RESET_TOKEN_INVALID`) to the reset-password error response, backed by a new
  `PasswordResetTokenInvalidException` and a `TokenInvalid` flag threaded through
  `IIdentityService.ResetPasswordAsync`. This lets the frontend show a combined
  "invalid or expired" message with a "Request a new reset link" CTA, distinct from
  password-policy failures (weak password) which show the raw server message with no
  CTA. **However**, ASP.NET Core Identity's `DataProtectorTokenProvider` reports the
  *same* `InvalidToken` error code for both an expired token and a malformed/garbage
  one — it doesn't distinguish them internally. So the three-way split described in
  the original spec (expired / invalid / network) is actually a two-way split in
  practice (token-problem / other) with combined copy ("invalid or has expired").
  Getting true expired-vs-malformed differentiation would require swapping the reset
  token implementation (e.g. a custom token store with an explicit expiry column)
  — flagged here as a backend follow-up, not done tonight since it's a bigger change
  than "trivially available."

- **Reset-token TTL confirmed, not guessed.** Found in
  `Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs`:
  `DataProtectionTokenProviderOptions.TokenLifespan = TimeSpan.FromHours(1)`. Used
  that real number in the UI hint, help content, and user manual instead of a
  placeholder.

- **Password policy alignment.** The backend's actual Identity policy (from the same
  file) is: length ≥ 8, requires a digit, requires uppercase, requires lowercase,
  does **not** require a symbol (`RequireNonAlphanumeric = false`). The
  `ResetPasswordPage` and `ClientRegisterPage` zod schemas previously only checked
  `min(8)`, meaning a user could pass client-side validation and still get a generic
  server rejection. Tightened both schemas (and `ChangePasswordPage`'s, which was
  missing the lowercase check) to match the real policy, and surfaced it as static
  `FieldHint` copy instead of only failing at submit time.

- **Live-validation mode: `onTouched`, not `onChange`.** The spec called out that
  full `onChange` validation "can be noisy/expensive" and to scope live feedback to
  password/confirm fields. React Hook Form doesn't support a per-field `mode`, so
  instead of turning on `onChange` for the whole form (which would also re-validate
  email/token on every keystroke), the form mode is `onTouched` (validates after
  first blur, then live) and the password-match indicator is implemented as an
  independent `watch()`-driven component that updates live regardless of validation
  mode — it doesn't wait for RHF's error state at all.

- **Match-indicator wording deliberately differs from the validation-error wording.**
  Originally both said "Passwords do not match", which is literally true but
  produces two near-duplicate strings on screen once the field is touched and
  submitted. The live indicator now says "Doesn't match yet" (present, provisional)
  vs. the submit-time error "Passwords do not match" (past, definitive) — avoids
  redundant screen-reader announcements and reads better as you type.

- **Read-only-by-default email/token fields, not always-editable.** Per the spec's
  own suggestion: since both fields normally arrive pre-filled from the emailed
  link's query params, they render read-only with a pencil-icon "Edit" affordance
  (`readOnly`, not `disabled`, so the value still submits via RHF and the field stays
  keyboard-focusable/selectable for copy-paste verification). When either param is
  absent from the URL (e.g. someone navigates to `/reset-password` directly), the
  corresponding field defaults to editable instead — nothing to lock.

- **Token truncation fix: character-count confirmation, not masking.** Chose the
  "character count" half of the two options offered in the spec, not the masked
  preview — simpler, and a live `{n} characters entered` hint under the field once
  it's unlocked for editing gives the same "did my paste land" confirmation without
  extra visual complexity.

- **AuthShellFooter is unconditional, so the old inline "Sign in" link inside the
  success state was removed** on both ResetPasswordPage and ForgotPasswordPage to
  avoid showing two "back to sign in" links stacked on top of each other after a
  successful reset/request. The footer link's accessible name ("Back to sign in")
  still matches `/sign in/i`, so no test regressions.

- **No "email not found" error state on ForgotPasswordPage.** The spec suggested
  differentiating "email not found" from other errors, matching the ResetPasswordPage
  pattern. Checked the backend (`AuthEndpoints.cs` `ForgotPassword`) and it
  deliberately returns an identical 200 response regardless of whether the account
  exists, specifically to prevent user enumeration. Differentiating "email not
  found" client-side would defeat that protection, so this page only gained the
  generic-fallback wording improvement, not a new error branch. (LoginPage/
  ClientRegisterPage's existing 429 rate-limit handling was left as-is; it doesn't
  reveal anything about account existence.)

- **LoginPage left structurally as-is.** It already has "Forgot password?" and
  "Sign up" / "Register your studio" links reachable at all times — no dead end to
  fix, and its escape hatches don't follow the bottom-of-card divider pattern
  `AuthShellFooter` encodes, so forcing it in would just be churn. Per the spec's own
  "verify, don't duplicate" guidance.

- **ChangePasswordPage gets the shared primitives it was missing** (it's an
  authenticated settings page, not part of the unauthenticated `AuthShellFooter`
  rollout, so that component doesn't apply here): its confirm-password field used a
  raw `<input type="password">` instead of `PasswordInput` — no show/hide toggle,
  inconsistent with every other password field in the app. Switched it to
  `PasswordInput` and added the same `FieldHint` + `PasswordMatchIndicator` treatment
  as the other two forms.

- **Password-input toggle hit target: 44×44 via padding, not `h-11` app-wide.** Per
  the explicit non-goal, did not migrate the shared `Input` from `h-10` to `h-11`.
  The toggle button's hit area was widened with `min-h-[44px] min-w-[44px]` while the
  visible input stays 40px tall. The two new pencil "edit" buttons on
  ResetPasswordPage got the same treatment for consistency, since they're new
  interactive elements introduced by this pass.

## Deferred / flagged for follow-up (not done tonight)

- **True expired-vs-malformed reset-token differentiation** — blocked on Identity's
  token provider not exposing that distinction; would need a custom token store.
- **App-wide `text-destructive` → `text-destructive-text` sweep** — ~75 other files
  use the old token for various purposes; needs its own audit pass to sort
  text-on-background usages (needs the fix) from background/icon-on-fill usages
  (fine as-is) file by file.
- **Document `<title>` / meta description** on ForgotPasswordPage, ClientRegisterPage,
  ResetPasswordPage, ChangePasswordPage — only LoginPage currently calls
  `useDocumentMeta`. Worth a follow-up for WCAG 2.4.2 (Page Titled), but touches
  pages beyond this task's explicit file list, so left alone tonight.
- **RegisterStudioPage** (owner-role signup) has the same `text-destructive` pattern
  as the client-facing pages but wasn't named in the task's page list — not touched.

## Manual / visual QA

Not run in a real browser this session — no browser tooling available in this
environment for a login-session-dependent flow like this. Automated coverage is
comprehensive (see below) but per this repo's own standing guidance, a real-browser
pass is recommended before shipping, specifically for: dark-mode contrast look at
the new token values, and tabbing through the full reset-password form to confirm no
keyboard traps.

## Test results

- Backend: `dotnet test tests/Pena_e_Arte.UnitTests` → 1335 passed, 0 failed
  (includes 3 new tests in `ResetPasswordHandlerTests.cs`).
- Backend: `tests/Pena_e_Arte.IntegrationTests` — builds clean; no existing
  integration tests touched `ResetPasswordAsync`, none needed updating.
- Frontend: `pnpm test` (`vitest run`) → 113 files / 1689 tests passed, 0 failed
  (includes rewritten `ResetPasswordPage.test.tsx` and new
  `password-input.test.tsx`).
- Frontend: `pnpm lint` on every file touched this session — clean. (3 pre-existing
  lint errors remain in `StudioNotificationSheet.tsx`, `VerifyEmailPage.tsx`, and
  `UserMenu.test.tsx` — none touched by this change, confirmed via `git diff --stat`
  against those paths.)
- `scripts/check-contrast.ts` — all 6 token pairs PASS in both themes.

## Files touched

**Backend**
- `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs` — `ResetPasswordAsync` now
  also returns `TokenInvalid`.
- `Pena_e_Arte.Domain/Exceptions/PasswordResetTokenInvalidException.cs` (new)
- `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`
- `Pena_e_Arte.Application/Auth/Commands/ResetPasswordCommand.cs`
- `Pena_e_Arte.API/Middleware/ExceptionMiddleware.cs`
- `tests/Pena_e_Arte.UnitTests/Auth/ResetPasswordHandlerTests.cs` (new)

**Frontend — tokens & shared primitives**
- `frontend/src/index.css`
- `frontend/scripts/check-contrast.ts` (new)
- `frontend/src/shared/components/ui/alert.tsx`
- `frontend/src/shared/components/ui/PasswordStrengthMeter.tsx`
- `frontend/src/shared/components/ui/password-input.tsx`
- `frontend/src/shared/components/AuthShellFooter.tsx` (new)
- `frontend/src/shared/components/ui/field-hint.tsx` (new)
- `frontend/src/shared/components/ui/password-match-indicator.tsx` (new)
- `frontend/src/shared/components/ui/__tests__/password-input.test.tsx` (new)

**Frontend — pages**
- `frontend/src/features/auth/components/ResetPasswordPage.tsx`
- `frontend/src/features/auth/components/LoginPage.tsx`
- `frontend/src/features/auth/components/ClientRegisterPage.tsx`
- `frontend/src/features/auth/components/ForgotPasswordPage.tsx`
- `frontend/src/features/auth/components/ChangePasswordPage.tsx`
- `frontend/src/features/auth/__tests__/ResetPasswordPage.test.tsx` (rewritten)

**Help / docs**
- `frontend/src/features/help/helpContent.ts`
- `frontend/public/user-manual/index.html`
- (no onboarding-tour files reference the auth flow — none needed updating)
