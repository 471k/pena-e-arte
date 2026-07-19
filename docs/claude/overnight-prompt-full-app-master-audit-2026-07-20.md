# Overnight Master Prompt — Full-App Audit (Guest → Client → Artist → Owner → Issuer)

**Date:** 2026-07-20
**Mode:** Fully autonomous. No user present. Run until every phase exits clean.
**Run with:** `claude --dangerously-skip-permissions`
**Before starting:** `git add -A && git commit -m "chore: pre-audit checkpoint"` then
`git checkout -b fix/full-app-audit-2026-07-20`

---

## Why this prompt exists

Five dedicated role QA passes already happened (`overnight-prompt-client-qa-polish-2026-07-01.md`,
`-guest-qa-polish-2026-07-01.md`, `-artist-qa-polish-2026-07-01.md`, `-owner-qa-polish-2026-07-01.md`,
`-issuer-qa-polish-2026-07-01.md`, with fixes logged 2026-07-01/07-02 in `architecture.md`). Since then,
roughly twenty more feature/fix prompts have shipped — OAuth sign-in, multi-studio client view, Instagram
sync, saved/bookmarked images, Redis rate limiting, the feedback/bug-report feature, plan usage limits,
two-sided referral rewards, a free plan tier, a full **Plan/PlanPrice schema split** (2026-07-19, the most
recent entry in the Decisions Log), and a reschedule-appointment UI. None of these went through a
full-surface QA pass, and — more importantly — **no pass has ever checked whether they broke each other**.
Each shipped in isolation against a green test suite for its own feature; a green suite for feature N does
not prove feature N didn't quietly break feature N-3's assumptions (this has already happened twice: see
`bug-report-plans-page-data-mismatch.md` and `bug-report-premium-plan-duplicate-legacy-row.md` in the
Decisions Log, both caused by an earlier plan-pricing decision that later features silently invalidated).

This prompt has three phases, run in strict order:

- **Phase 1 — Regression sweep.** Re-verify every bug fixed in the five 2026-07-01/02 QA passes is still
  fixed. Later commits touched the same files for unrelated reasons; nothing has re-checked these.
- **Phase 2 — New-surface audit.** Full bug-hunt + correctness pass over everything shipped since
  2026-07-02 that never had a QA-depth review: OAuth, multi-studio, Instagram sync, saved images, plan
  usage limits, referral rewards, the free tier, the Plan/PlanPrice split, reschedule UI.
- **Phase 3 — Cross-feature integration matrix.** Deliberately exercise the seams between features that
  were built independently, by different prompts, weeks apart, by an agent with no memory of the others.

Do not skip ahead. Do not stop early because a phase "looks" done — each phase has an explicit exit
condition below.

---

## Constraints (apply everywhere, identical to every prior overnight prompt)

- No new npm or NuGet packages.
- No `useEffect` for data fetching. Approved: resize/keyboard/outside-click/scroll-to listeners, clipboard
  calls, timer side-effects (debounce, cooldown countdowns, auto-dismiss), browser API calls in event
  handlers, form-state sync from async data, geolocation callbacks, analytics-on-mount view tracking,
  URL/search-param reads on mount.
- TypeScript strict mode. No `any`. No default exports on components. Explicit C# types — no unclear `var`.
- No business logic in endpoints — endpoints call MediatR only.
- Every DB query on tenant data through EF Core global query filters. Only `issuer`-scoped handlers may
  call `IgnoreQueryFilters()`, and only if the call site is already listed in `architecture.md`'s
  "IgnoreQueryFilters() Approved Usages" table (currently 26 entries) — if you find a usage NOT in that
  table, that is itself a P0 bug (undocumented cross-tenant read).
- Every endpoint has `.RequireAuthorization()` with the correct policy, OR is listed in the
  "AllowAnonymous Exceptions" table in `architecture.md` with a documented security mechanism. No
  unprotected, undocumented endpoint may exist.
- Never log PII. Serilog logs must include `tenant_id`, `user_id`, `request_id` (via `RequestIdMiddleware`
  + `RequestLoggingEnrichment` — already implemented, verify it's not been bypassed by new code).
- No secrets in source. Environment variables or Vault only.
- Structured logs only — no `Console.WriteLine`, no `console.log` in production paths.
- Every command has a FluentValidation validator, registered in DI.
- Don't add a new ORM. Don't bypass query filters without an explicit `issuer` check. Don't put
  session/slot/rate-limit state in memory — Redis only.

### Do-not-re-litigate list (already decided — pulled from `architecture.md` Decisions Log)

If you find code that contradicts one of these, the code is the bug, not the decision:

- `Studio.IsActive` is the only activation flag. Do not add `IsPublished`.
- `StripeConnectService` is `[Obsolete]`. Never call it. `IStripePaymentService` must never pass
  `RequestOptions { StripeAccount = ... }` — the platform uses the aggregator model only, no Connect.
- **Plan pricing lives on `PlanPrice`, not `Plan`, as of the 2026-07-19 migration.** `Plan.BillingInterval`
  / `PriceMonthly` / `PriceYearly` / `StripePriceIdMonthly` / `StripePriceIdYearly` / `PairedPlanId` were
  **removed**. A tier is a `Plan` row; each cadence it offers is a `PlanPrice` row
  (`PlanId`, `Interval`, `Price`, `StripePriceId`, `IsActive`, unique on `(PlanId, Interval)`).
  `Subscription.BillingInterval` (required) and `Subscription.PendingBillingInterval` (nullable) now carry
  the cadence — it belongs to the subscription, not the plan. **If you see any code, test, or doc still
  referencing `Plan.PriceMonthly`, `Plan.PairedPlanId`, or the old "Plan Monthly/Yearly pairing" /
  "billing interval stays locked per-row" decisions, that code is stale and is itself a bug** — those two
  earlier decisions are explicitly superseded. `DataSeeder.ReconcileCoreTiersAsync` is the current seeding
  entrypoint (replaces both `ReconcileCorePlansAsync` and `RetireOrphanedNamedPlansAsync`, which no longer
  exist).
- `GetPlatformStatsQuery`/`GetMrrHistoryQuery` must compute MRR from the `PlanPrice` matching each
  subscription's actual `BillingInterval` — not a flat `Plan` price. This was a real revenue-overstatement
  bug fixed in the same 07-19 migration; regression-check it explicitly in Phase 1.
- `SavedPortfolioImage` is intentionally NOT a `TenantEntity` — cross-tenant by design. Do not add a
  tenant FK to it.
- Plan usage limits (`Plan.MaxArtists`, `MaxAppointmentsPerMonth`, `MaxNotificationsPerMonth`,
  `MaxStorageGb`, `MaxLocations`, `AllowApiAccess`, `PrioritySupport`) are enforced via `IQuotaCheckedCommand`
  + `PlanLimitBehavior` + `IPlanLimitService` (Redis-cached, 30s TTL, write-through invalidation on
  `CreateArtistCommand`/`CreateAppointmentCommand` only). Notification-send commands, upload commands
  (storage bytes), and location-create are explicitly **not yet wired** — this is documented as a known gap,
  not a bug to silently "fix" by guessing enforcement logic. If you wire one of these in Phase 2, you must
  add the corresponding test coverage and update the Decisions Log entry, not silently extend scope.
- The `ReferralRewardService` two-sided reward is intentionally idempotent via
  `ReferralRedemption.ReferrerRewardApplied` and intentionally skips (logs, does not throw) on self-referral
  or when the referrer has no active Stripe subscription (Free/Trialing/cash-billed studios cannot receive
  a Stripe coupon). Do not "fix" the skip case by inventing a non-Stripe reward mechanism — that's an open
  product decision (see `// TODO(product)` in `ReferralRewardService`), not a bug.
- OAuth registration (`RegisterOAuthUserHandler`/`RegisterOAuthUserValidator`) intentionally mirrors
  password-registration's `OwnerEmail`-match check and client/owner-only role restriction. If you find an
  OAuth code path that allows artist/issuer registration or skips the owner-email check, that is the
  regression this decision explicitly guards against — treat it as P0.
- Apple Sign In requires HTTPS even in local development — this is a known, accepted dev-environment
  limitation, not a bug to work around.

---

## Required reading (in this order, before touching any file)

```
CLAUDE.md
docs/claude/architecture.md         — read in full; this is the longest and most important file.
                                       Pay special attention to: Feature Module Map (24 features),
                                       IgnoreQueryFilters Approved Usages, AllowAnonymous Exceptions,
                                       Decisions Log (bottom of file), and every "QA Pass" section.
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/conventions.md
docs/claude/issues.md               — stale (last updated 2026-06-17, predates this prompt by a month);
                                       treat every item as resolved UNLESS Phase 1 proves otherwise.
```

Then, at the start of each phase below, read the specific prior-prompt files that phase references.

---

## The Loop Algorithm (identical to every prior overnight prompt)

```
LOOP:
  1. Build:
       cd "Pena e Arte" && dotnet build
       cd frontend && pnpm build          (TypeScript errors surface here)
  2. Test:
       dotnet test --no-build
       pnpm test
  3. Collect every failure.
  4. For each failure:
       a. Read the source file in full.
       b. Diagnose the exact root cause.
       c. Fix only what is broken.
       d. Re-run just that test file to confirm the fix.
       e. If still failing: diagnose from scratch. Fix differently. Re-run.
       f. Repeat until that test is green.
  5. After all fixes: re-run the full suite.
  6. If new failures appeared: go to step 4.
  7. All green → proceed to the audit checklist for the current phase.
```

Run this loop at the start of Phase 1 and again before exiting each phase.

---

# PHASE 1 — Regression Sweep

Read the five source QA-pass prompts fully before starting this phase:

```
docs/claude/overnight-prompt-client-qa-polish-2026-07-01.md
docs/claude/overnight-prompt-guest-qa-polish-2026-07-01.md
docs/claude/overnight-prompt-artist-qa-polish-2026-07-01.md
docs/claude/overnight-prompt-owner-qa-polish-2026-07-01.md
docs/claude/overnight-prompt-issuer-qa-polish-2026-07-01.md
```

And the corresponding "QA Pass" sections in `architecture.md` (`Artist QA Pass — 2026-07-01`,
`Owner QA Pass — 2026-07-02`, `Client QA Pass — 2026-07-02`, `Guest/Visitor QA Pass — 2026-07-02`). Each
"Bugs found and fixed" list is your regression checklist. For every single item in every list, re-verify
it is still true today. Do not assume — read the current source.

**Documentation gap to close while you're here:** unlike the other four roles, there is no `## Issuer QA
Pass` section in `architecture.md` — the issuer pass ran (`overnight-prompt-issuer-qa-polish-2026-07-01.md`
exists and is fully written) but its results were apparently never logged. Reconstruct what you can: read
that prompt file, then diff its checklist against current source the same way you're doing for the other
four, and add a best-effort `## Issuer QA Pass — 2026-07-01 (reconstructed 2026-07-20)` section to the
Decisions Log documenting what you find — flagged as reconstructed since the original session's actual
diff is lost.

## Priority regression checks (highest risk of silent breakage, verify these first)

1. **`ReviewDesignCommand.cs`** — must still inject `ICurrentUser` and 404 when a `client` role's resolved
   client doesn't own the design. This was a real security hole (any client could approve/reject any
   design by guessing a GUID) fixed 2026-07-02. Confirm no later refactor of the design module dropped it.
2. **`GetNotificationsQuery.cs`** — must have both an artist-scoping branch and a client-scoping branch,
   route policy `ClientAndAbove`. Two separate scoping bugs were found and fixed here across the artist and
   client passes; confirm both branches still exist and the route policy wasn't reverted to `ArtistAndAbove`.
3. **Artist-ownership checks** across `UpdateArtistCommand`, `UpsertArtistScheduleCommand`,
   `AddArtistTimeOffCommand`, `DeleteArtistTimeOffCommand`, `CreateDesignCommand`,
   `UploadDesignRevisionCommand`, `DeleteDesignRevisionCommand`, `CreateDesignShareTokenCommand`,
   `RevokeDesignShareTokenCommand`, `ConfirmCashDepositCommand` — every one of these must scope to the
   calling artist's own `ArtistId` (not just tenant), per the 2026-07-01 artist pass. Confirm the
   Instagram-sync feature (which touches `Artist`) and the plan-usage-limit feature (which touches
   `CreateAppointmentCommand`, adjacent to several of these) didn't regress any of them.
4. **`CancelAppointmentCommand.cs`** refund branch — must refund on both `PaymentStatus.Captured` and
   `PaymentStatus.Paid`, and must only flip `DepositStatus` inside the branch where a refund action
   actually occurred. Confirm the reschedule-UI work (2026-07-18, touches the same appointment lifecycle)
   didn't touch this branch.
5. **`GetPlatformStatsQuery`/`GetMrrHistoryQuery`** MRR calculation — must read from `PlanPrice` matching
   `Subscription.BillingInterval`, not a flat `Plan` price field (which no longer exists post-07-19
   migration; if it still exists, that's a bigger problem — see Phase 2 item 8). This is the freshest
   decision in the whole Decisions Log — verify it did not get half-migrated.
6. **`DepositCheckoutPage.tsx` / `EmbedCodeCard.tsx` / any Stripe `return_url` or share-link builder** —
   must use `VITE_PUBLIC_URL`, never `window.location.origin`. This exact bug class was fixed independently
   in at least three places (client pass, design share, embed page); grep the whole frontend for
   `window.location.origin` and check every hit against this rule.
7. **Mobile nav overflow** (`overflow-x-auto scrollbar-none shrink min-w-0`) — verify on all four
   authenticated layouts (`ClientLayout`, `ArtistLayout`, `OwnerLayout`, `IssuerLayout`) plus check whether
   the newer `MyStudiosPage` nav (multi-studio feature, 2026-07-04) or the Instagram tab
   (`InstagramTab.tsx`, artist profile) introduced a new unwrapped nav row that needs the same fix.
8. **Per-route `<ErrorBoundary>` wrapping** in `router.tsx` — confirmed present for owner/artist/issuer
   routes as of the 07-02 passes. Verify every route added since (`/my-studios`, OAuth callback routes,
   `/platform/studios/:id`, any reschedule-related route) is also wrapped. New routes added by later
   prompts are the most likely to have been added without this wrapper since the prompts that added them
   didn't carry this specific instruction forward.
9. **Cross-slice RTK Query invalidation pattern** (`paymentsApi`'s `confirmCashDeposit` dispatching
   `appointmentsApi.util.invalidateTags(["Appointment"])` via `onQueryStarted`) — this was the *first* use
   of this pattern in the codebase (07-02 owner pass). Check whether any RTK Query slice added since
   (`savedImagesApi`, `platformApi` additions for studio detail, the reschedule mutation once added in
   Phase 2, Instagram sync mutations) needs the same cross-slice pattern and is missing it — e.g., does
   reschedule need to invalidate `["Appointment", { id }]` the same way confirm/cancel/complete do?
10. **`SetupChecklist.tsx`** — the "working hours" step was removed (no cheap way to check
    per-artist schedule client-side) rather than fixed. Confirm nothing re-added a broken version of this
    step, and confirm the deposit-rule step still links to `/deposit-rules/new` (not `/settings/deposits`).

## Phase 1 exit condition

```
dotnet build   → 0 errors, 0 warnings
pnpm build     → 0 TypeScript errors
dotnet test    → All green
pnpm test      → All green
```
Plus: every item above has been individually re-verified against current source (not assumed from the
architecture.md log) and, if broken, fixed.

---

# PHASE 2 — New-Surface Audit

These features shipped after the original five QA passes and have never had a dedicated correctness +
RBAC + tenant-isolation review at that depth. Read the listed source prompt for each before auditing it.

## 2.1 — OAuth Sign-In (`docs/claude/overnight-prompt-oauth-2026-06-25.md`)

Endpoints: `POST /api/v1/auth/oauth/login`, `POST /api/v1/auth/oauth/register` (both `AllowAnonymous`,
rate-limited). Commands: `OAuthLoginCommand`, `RegisterOAuthUserCommand`. Frontend: `OAuthButtons` shared
component, `useGoogleSignIn`/`useAppleSignIn` hooks, wired into `LoginPage` and `RegisterStudioPage` step 2.

Verify:
- `IOAuthTokenValidator` actually validates the ID token signature against the provider's live JWKS
  (cached 1h in Redis) — it must not trust an unverified token payload.
- `RegisterOAuthUserHandler` enforces `info.Email == studio.OwnerEmail` for `role="owner"` — test this
  explicitly by attempting an OAuth registration with a mismatched email and confirming rejection.
- `RegisterOAuthUserValidator` rejects `role="artist"` and `role="issuer"` — only `client`/`owner` allowed.
- `CreateOAuthUserAsync` creates the Identity user with `EmailConfirmed = true` and no password — confirm
  a user created this way can still later use "forgot password" if they want to add password login
  (or confirm this is explicitly out of scope and documented as such — don't leave it silently broken).
- Google/Apple JS SDKs are loaded from CDN `<script>` tags in `index.html`, not npm packages — confirm no
  npm package for either was added since (violates the "no new npm packages" constraint).
- Rate limiting is applied to both OAuth endpoints matching the `auth` policy group (10 req/min).
- Tenant isolation: an OAuth-created client account must land in the correct tenant/studio the same way
  password registration does — verify `studioId` flows through the OAuth registration path identically.

## 2.2 — Multi-Studio Client View / "My Studios" (`architecture.md` §"Multi-Studio Plan — Phase 2", plus
the three follow-up "My Studios" prompts dated 2026-07-04/05)

Endpoints: `GET /api/v1/auth/my-studios` (`ClientOnly`, no rate limit), `DELETE
/api/v1/auth/my-studios/{studioId}` (`ClientOnly`), `GET`/`PUT
/api/v1/auth/my-studios/{studioId}/notification-preferences` (`ClientOnly`). Frontend: `MyStudiosPage` at
`/my-studios`.

Verify:
- `GetMyStudiosQuery` (or equivalent) returns only studios where the calling user has a `Client` record —
  never another user's studios. This is a cross-tenant read by design (a client can have accounts across
  multiple tenants) — confirm it's scoped by `UserId`, not left unscoped.
- "Leave studio" (`DELETE .../my-studios/{studioId}`) — confirm it only removes THIS user's client
  association with THIS studio, and does not cascade-delete the `Client` record's history (appointments,
  designs, tattoo records) that the studio itself still needs for its own records.
- Per-studio notification preferences are correctly scoped per `(userId, studioId)` pair — a client with
  three studio relationships must be able to set different preferences for each.
- Interaction with **Client Portable Profiles** (feature #12, `IPortableProfileService`): if a client
  leaves a studio via "My Studios," does their opted-in portable profile data (tattoo history, body map)
  correctly remain readable by OTHER studios they're still registered with, or does leaving one studio
  incorrectly wipe cross-tenant-visible data? This exact interaction has never been tested — write a test
  for it if one doesn't exist.
- Regression test file exists: `frontend/e2e/my-studios-kebab-menu.spec.ts` — confirm it still passes and
  covers the overflow-menu / dialog-from-dropdown-menu-item pattern documented in architecture.md (a real
  gotcha was found and fixed here — dialogs opened from a `DropdownMenuItem` need specific focus handling).

## 2.3 — Instagram Sync (`architecture.md` §"ArtistPortfolioPage" Instagram note, commit `f7e2962`)

Endpoints: `GET /api/v1/artists/{id}/instagram/connect-url` (owner-only), anonymous OAuth callback
(`GET /api/v1/instagram/callback`, state-signed via `IInstagramStateSigner` HMAC), `PUT
.../posts/{postId}/visibility`, `GET /api/v1/public/artists/{slug}/instagram-posts` (`AllowAnonymous`).
Backend job: `InstagramSyncJob` (nightly, tenant-wide). Frontend: `InstagramTab.tsx`.

Verify:
- The anonymous callback endpoint validates the signed `state` param BEFORE trusting the embedded
  `artistId` — confirm there's no code path where an unsigned or tampered `state` is accepted. This is
  listed as `IgnoreQueryFilters()` approved usage #22 specifically because the artistId is "pre-authenticated
  via `IInstagramStateSigner` HMAC before this handler runs" — verify that ordering is actually enforced
  in the handler, not just assumed.
- `PUT .../posts/{postId}/visibility` is owner/artist-scoped correctly — an artist can only toggle
  visibility on their OWN synced posts, not a colleague's.
- `GetPublicArtistInstagramPostsQuery` only returns `IsVisible = true` posts, and only for active studios
  (suspended studio's artist should not leak Instagram posts publicly — cross-check against the
  `Studio.IsActive` public-endpoint convention used everywhere else).
- `InstagramSyncJob` refreshes OAuth tokens correctly and does not crash the whole nightly run if ONE
  artist's Instagram connection has expired/been revoked — confirm per-artist error isolation (one bad
  token shouldn't block the rest of the tenant, or the platform, from syncing).
- No PII leak: Instagram post captions/usernames are not logged; confirm `InstagramSyncJob`'s logging
  follows the same `tenant_id`/`user_id`/`request_id` convention (jobs don't have a natural `request_id` —
  confirm what the actual convention is here and that it's consistent, not silently unlogged).

## 2.4 — Saved / Bookmarked Portfolio Images (feature #21 in the Feature Module Map)

Endpoints live at `/api/v1/saved-images/` via the dedicated `savedImagesApi` slice.

Verify:
- `SavePortfolioImageHandler` / `GetSavedPortfolioImagesHandler` correctly use `IgnoreQueryFilters()`
  (approved usages #16–#18) for the cross-tenant image/studio lookups, scoped by the saving user's own
  `UserId` — never returns another user's saved images.
- Un-save / delete works and is idempotent (saving an already-saved image doesn't create duplicates;
  un-saving an already-unsaved image doesn't error).
- The bookmark button on `PortfolioFeed` is visible on hover/focus when authenticated — confirm it's
  hidden (not broken) for unauthenticated guests, and that clicking it while unauthenticated doesn't throw
  an unhandled promise rejection (route to `/login` or show a toast instead).
- `SavedPortfolioImage` correctly has no `TenantEntity` inheritance (per the do-not-re-litigate list above)
  — confirm no later migration accidentally added a tenant FK to it.

## 2.5 — Redis-Backed Rate Limiting (`architecture.md` §"Redis-Backed Distributed Rate Limiting")

Verify:
- Policy limits match the documented table (`auth`: 10/min, plus whatever other policy groups exist —
  re-read the full section in architecture.md for the complete table).
- Confirm the newer endpoints from 2.1–2.4 above (OAuth login/register, Instagram connect/callback,
  saved-images write endpoints) are actually wired to a rate-limit policy, not just documented as "should
  be" — check the actual `.RequireRateLimiting(...)` call on each endpoint registration.
- Confirm rate limiting is genuinely Redis-backed (distributed) and not falling back to in-memory state —
  this was the whole point of the feature (CLAUDE.md forbids in-memory rate-limit state).

## 2.6 — Feedback / Bug-Report Feature (`architecture.md` §"Feedback / Bug Report Feature — 2026-07-02")

Verify:
- The submission endpoint has correct authorization (confirm which roles can submit — likely
  `ClientAndAbove` or `AllowAnonymous` depending on what was actually built; read the section and check
  against the current endpoint registration).
- No PII violation — if screenshots or free-text descriptions are stored, confirm they're not logged
  verbatim anywhere, and confirm they're stored per the R2/Cloudflare convention used elsewhere, not
  inline in a database text column with no size limit.

## 2.7 — Plan Usage Limits (`architecture.md` Decisions Log entry "Plan usage limits")

Verify:
- `PlanLimitBehavior` is registered in the MediatR pipeline AFTER `ValidationBehavior` (order matters —
  confirm the actual DI registration order in `Program.cs` or the Application layer's DI extension).
- Only `CreateArtistCommand` and `CreateAppointmentCommand` currently implement `IQuotaCheckedCommand` —
  confirm this hasn't silently drifted (e.g., someone added the marker interface to another command without
  updating the Decisions Log, or removed it from one of the two that should have it).
- `PlanLimitExceededException` → 403 with error code `PLAN_LIMIT_EXCEEDED` — confirm the frontend actually
  surfaces this distinctly (not a generic "Something went wrong") on `CreateArtistPage` and
  `BookAppointmentForm`/wherever appointment creation happens for owner/artist roles.
- The write-through cache invalidation (`InvalidateUsageCacheAsync` called immediately after
  `SaveChangesAsync` in both handlers) is present in both places — this was added the night after the
  original feature shipped specifically to narrow a staleness race; confirm it wasn't lost in a later merge.
- Explicitly confirm the known gap is still just a gap, not a half-implementation: the 7
  notification-send commands, upload commands (storage bytes — the `Studio.StorageUsageBytes` counter
  field exists but increment call sites don't), and location-create are NOT wired. If Phase 2 work
  elsewhere touched any upload command, verify it didn't add a partial, untested increment call.
- **Interaction with the free plan tier (2.9 below):** confirm `Plan.Max*` fields on the Free plan are set
  to sensible (likely low, non-null) values, not accidentally `null` (unlimited) — a free tier with
  unlimited artists/appointments defeats the purpose of every other plan.
- `GetPlanUsageReportHandler` (issuer-only, `IgnoreQueryFilters()` #25) surfaces real usage against caps in
  `IndustryReportsPage.tsx`. Confirm it renders correctly and the report doesn't crash when a studio has
  zero usage in a category (division by zero, null caps, etc.).

## 2.8 — Two-Sided Referral Rewards (`architecture.md` Decisions Log entry "Two-sided referral reward")

Verify:
- `ReferralRedemption.ReferrerRewardApplied` correctly gates against double-issuing the reward — call the
  reward path twice for the same redemption (in a test) and confirm the second call is a no-op, not a
  duplicate Stripe coupon.
- Self-referral guard (`OwnerEmail` match between referrer and new studio) actually prevents an owner from
  referring themselves for a free month — write a test if one doesn't exist.
- The no-active-Stripe-subscription skip path (referrer on Free/Trialing/cash-billed) logs but does not
  throw, and does not silently retry forever or leave `ReferrerRewardApplied` in an inconsistent state.
- Called from both `CreateSubscriptionHandler` AND `ActivateCheckoutSubscriptionHandler` — confirm both
  call sites are still wired (two call sites for one behavior is a common place for one to silently drift
  out of sync during a refactor — check both, not just one).
- **Interaction with the Plan/PlanPrice split (2026-07-19, the newest change in the whole codebase):**
  the reward service was built against the old `Plan.PriceMonthly`-shaped model. Confirm
  `ReferralRewardService` still compiles and behaves correctly against `Subscription.BillingInterval` +
  `PlanPrice` — this is exactly the kind of silent breakage this whole prompt exists to catch. Trace every
  reference to plan pricing inside `ReferralRewardService` by hand.

## 2.9 — Free Plan Tier (`docs/claude/overnight-prompt-free-plan-tier-2026-07-18.md`)

Verify:
- `CreatePlanCommand`/`UpdatePlanCommand` validation allows `Price == 0` (the original bug was a hard
  `GreaterThan(0)` check blocking this) — but confirm it still rejects negative prices.
- A Free-tier subscription has a working, sensible `CurrentPeriodEnd` sentinel (far-future date) and never
  triggers the past-due/grace-period machinery meant for paid subscriptions that stop paying.
- No Stripe subscription object is created for a Free-tier signup — confirm the activation path branches
  correctly and doesn't call `CreateSubscriptionAsync` against Stripe for a €0 plan.
- Referral coupon guard: confirm a referral code cannot be "redeemed" against a Free-tier signup in a way
  that produces a nonsensical Stripe coupon application (there's nothing to discount).
- **Interaction with Plan/PlanPrice split:** the free-tier work (07-18) predates the schema split (07-19).
  Confirm the Free plan's pricing now correctly lives on a `PlanPrice` row (likely `Price = 0`,
  `Interval = Monthly`, no `StripePriceId`) rather than a stale `Plan.PriceMonthly = 0` field that no
  longer exists post-migration. This is the single highest-risk seam in the entire codebase right now —
  two large, independent features touching the same entity one day apart.
- `SubscribePage`/`PlanManagementPage` correctly render and allow selecting/activating the Free tier
  end-to-end through the current (post-split) pricing model.

## 2.10 — Plan/PlanPrice Schema Split (2026-07-19 — the newest and highest-risk change)

This is not a "feature" with its own prompt file — it's a database and domain model migration
(`AddPlanPriceAndSubscriptionBillingInterval` + `DropLegacyPlanBillingFields`) that touched nearly every
billing/plan code path in the app. Audit it directly against the codebase rather than a source prompt:

- Grep the entire backend and frontend for `PriceMonthly`, `PriceYearly`, `StripePriceIdMonthly`,
  `StripePriceIdYearly`, and `PairedPlanId`. Every hit outside of migration files, historical test
  snapshots, or `architecture.md` itself is either dead code that should have been removed, or a live bug
  referencing fields that no longer exist (which would be a compile error in C# but could easily be a
  silent runtime issue in TypeScript if a type still declares the field optionally).
- Confirm `PlanPrice` has the unique index on `(PlanId, Interval)` actually applied in the EF Core
  configuration and migration, not just described in the Decisions Log.
- Confirm `Subscription.PendingBillingInterval` is set and cleared in lockstep with `PendingPlanId` by
  `ChangePlanHandler`, `CancelPlanChangeHandler`, and `HandleSubscriptionUpdatedHandler` — all three, not
  just one or two. A plan change that updates `PendingPlanId` without also updating
  `PendingBillingInterval` (or vice versa) would leave a subscription in an inconsistent pending state.
- Confirm `DataSeeder.ReconcileCoreTiersAsync` runs correctly on a fresh database AND on a database that
  still has pre-split data (the migration's raw-SQL backfill step) — if there's any way to test against a
  pre-migration snapshot, do so; otherwise reason through the backfill SQL by hand and confirm it correctly
  maps every existing `Subscription` to a `BillingInterval` and every existing `Plan` row to its `PlanPrice`
  children before the legacy columns were dropped.
- `PlanManagementPage.tsx` (issuer) and `SubscribePage.tsx`/`BillingPage.tsx` (owner) must all read/write
  through the new `PlanPrice` shape. Confirm none of the three still POST/PUT a flat `priceMonthly` field
  that the backend no longer accepts.
- Re-run the two specific bug reports that motivated this migration
  (`bug-report-plans-page-data-mismatch.md`, `bug-report-premium-plan-duplicate-legacy-row.md` — read
  these if present in the repo) and confirm both original symptoms are gone.

## 2.11 — Reschedule Appointment UI (`docs/claude/overnight-prompt-reschedule-appointment-ui-2026-07-18.md`)

Backend (`RescheduleAppointmentCommand`, `PATCH /api/v1/appointments/{id}/reschedule`, `ArtistAndAbove`)
already existed and was tested before this prompt — it added ONLY the frontend. Verify:

- A reschedule button/dialog now exists somewhere reachable by artist/owner roles (`AppointmentCard` and/or
  `AppointmentDetailPage`) — confirm it was actually built, not left as a Phase-2-deferred item again.
- The dialog correctly disables/hides itself for `Cancelled`/`Completed`/`NoShow` appointments (matching
  the backend's `BusinessRuleViolationException` guard) rather than relying solely on the backend to
  reject and showing a raw error.
- `NewDurationMinutes` uses the same `DURATION_OPTIONS`/valid-durations list as `BookAppointmentForm`
  (30–480, discrete set) — not a free-text number input that could submit an invalid value the backend
  then 422s on.
- On success, confirm the correct RTK Query invalidation fires (`["Appointment", { id }]` at minimum) so
  the schedule view updates without a manual refresh — cross-check against regression item 9 in Phase 1.
- A 409 (slot conflict) shows a specific "that time is already booked" message, not a generic failure toast.
- Confirm the client-facing "request a new time" flow was correctly NOT built (per the prompt's explicit
  scope boundary) — if it WAS built by a later, unrelated change, verify it has its own correct
  authorization and business rules rather than accidentally reusing the artist-only endpoint in a way that
  lets clients reschedule their own appointments without artist review.

## 2.12 — Issuer Studio Detail, List Audit, Subscription Oversight (the three 2026-07-17 issuer prompts)

Read `overnight-prompt-issuer-studio-detail-2026-07-17.md`,
`overnight-prompt-issuer-studio-subs-audit-2026-07-17.md`,
`overnight-prompt-issuer-studio-list-audit-2026-07-17.md`, and `overnight-prompt-plan-management-audit-
2026-07-18.md` in full, then verify:

- `IssuerStudioDetailPage` (`/platform/studios/:id`) is now fully built (the original 2026-07-01 issuer
  pass left it as a likely placeholder) — confirm it shows studio identity, subscription status,
  subscription actions (extend/activate/cancel), and referral codes for that studio, with a working
  `getStudioById` query.
- `GetStudioByIdHandler`'s `IgnoreQueryFilters()` usage (#8 in the approved table) is still correctly
  scoped to `IssuerOnly` and doesn't leak into any owner-accessible code path.
- Whatever the plan-management audit changed is consistent with the Plan/PlanPrice split above — this
  audit (07-18) predates the schema split (07-19) by one day, same risk pattern as the free-plan-tier seam.
  Trace `PlanManagementPage.tsx` specifically for stale references to the pre-split shape.

## Phase 2 exit condition

Same four commands as Phase 1, plus: every numbered subsection above (2.1–2.12) has been individually
audited against current source, with any found bug fixed and a regression test added.

---

# PHASE 3 — Cross-Feature Integration Matrix

Every feature above was built in isolation. This phase exists specifically to exercise the seams between
them — the places two independently-correct features can combine into an incorrect result. For each row,
reason through the scenario against current source, write a test that exercises it if one doesn't already
exist, and fix what's broken.

| # | Scenario | What to check |
|---|---|---|
| 1 | A client uses **My Studios** to leave a studio, then that studio's owner looks up the client via **Client Portable Profile** cross-tenant read. | Does the portable-profile read still work correctly for studios the client remains registered with? Does leaving one studio never affect data visible to a different studio? |
| 2 | A studio on the **Free plan tier** tries to exceed **Plan usage limits** (e.g., a 6th artist on a plan capped at 5). | Confirm the Free plan's `Max*` fields are actually enforced, not left `null` (unlimited) by oversight — this is flagged as a specific risk in 2.7 above. |
| 3 | An owner referred by another studio's **referral code** signs up onto the **Free plan tier**. | Confirm `ReferralRewardService`'s "referrer has no active Stripe subscription" skip and the "new studio has no discount to apply" case for a €0 plan don't produce a crash or a nonsensical Stripe API call. |
| 4 | An artist syncs **Instagram** posts, then the studio is suspended by an issuer (`SuspendStudioCommand`). | Do the artist's public Instagram posts stop appearing on `ArtistPortfolioPage` (which should already 404/hide for inactive studios), and does `InstagramSyncJob`'s nightly run skip suspended-studio tenants rather than continuing to burn API quota against a dead studio? |
| 5 | A studio manager (owner) changes their plan via **PlanManagementPage** while a **plan-usage-limit** check is mid-flight for a concurrent `CreateAppointmentCommand`. | Confirm the known, documented race (two concurrent requests both reading a stale Redis cache before either writes) is still the *only* known gap — i.e., a plan downgrade doesn't leave the Redis usage cache pointing at limits from the old plan for the full 30s TTL in a way that under-enforces the new, stricter plan. |
| 6 | A client registers via **OAuth** for a studio that has an active **referral code** (`?ref=CODE` in the URL). | Does the OAuth registration path correctly pass the referral code through to `CreateSubscriptionCommand`/redemption the same way password-based `ClientRegisterPage` does? Read both registration paths side by side — this is exactly the kind of thing an OAuth-specific prompt would not have thought to check. |
| 7 | A **saved/bookmarked** portfolio image belongs to an artist whose studio later gets suspended. | Does `GetSavedPortfolioImagesHandler` gracefully skip or flag images from now-inactive studios, or does it error / leak a suspended studio's content into a client's saved list? |
| 8 | The **reschedule** UI (artist-only) is used on an appointment that has a **plan-usage-limit**-checked deposit rule or that counts toward `MaxAppointmentsPerMonth`. | Rescheduling changes `Date`, not count — confirm it correctly does NOT re-check or re-decrement the monthly appointment quota (it's the same appointment, not a new one). |
| 9 | The **issuer studio detail page** shows subscription status for a studio on the **Free plan tier**, referred via a **referral code**, whose owner signed up via **OAuth**. | This is the single point in the app where four independently-built features converge on one read. Load-test this manually: seed a studio matching every one of these conditions and confirm `IssuerStudioDetailPage` renders every section correctly with no null-reference crash, no "undefined" string rendered, no missing referral-code section. |
| 10 | **Rate limiting** (Redis-backed) is exercised across the **OAuth** and **Instagram callback** endpoints during a simulated burst. | Confirm both newer anonymous endpoint families are actually covered by the rate-limit policy table, not just the original five roles' endpoints that existed when the rate-limiting feature shipped. |

For any row where you find a real bug, fix it and add both a targeted regression test AND an entry in the
Decisions Log documenting the interaction (future agents building feature N+1 need this cross-reference —
that's the entire reason this phase exists).

## Phase 3 exit condition

```
dotnet build   → 0 errors, 0 warnings
pnpm build     → 0 TypeScript errors
dotnet test    → All green
pnpm test      → All green
```
Plus: all 10 matrix rows above have been reasoned through against current source, with a test written for
each where none existed, and any found bug fixed.

---

## Final self-review (all roles, all phases)

Before writing the deliverable, walk through this checklist once as a single pass covering every role:

- Does every list page (across all 5 roles) have a loading skeleton, an error state with retry, and an
  empty state with role-appropriate copy?
- Does every mutation fire a success toast and a specific (not generic) error toast?
- Does every destructive action have an inline confirmation step?
- Is every button disabled + spinner-shown while its mutation is in flight?
- Does every authenticated route have both a role guard AND an `<ErrorBoundary>`?
- Does every new endpoint added since 2026-07-02 appear in either the `.RequireAuthorization()` policy
  system or the `AllowAnonymous Exceptions` table in `architecture.md` — no exceptions?
- Does every new `IgnoreQueryFilters()` call added since 2026-07-02 appear in the approved-usages table?
- Grep the whole repo one more time for `window.location.origin` — zero hits outside of the documented
  fallback pattern (`VITE_PUBLIC_URL ?? window.location.origin`).
- Grep the whole repo for `Plan.PriceMonthly`, `Plan.PriceYearly`, `Plan.PairedPlanId` — zero hits.

---

## Final Deliverable

When all three phases exit cleanly, append a new section to `docs/claude/architecture.md`:

```markdown
## Full-App Master Audit — 2026-07-20

### Phase 1 — Regressions found (should be none; list any that were)
- [file → what regressed → fix]

### Phase 2 — New-surface bugs found and fixed
- [feature → file → bug → fix]

### Phase 3 — Cross-feature bugs found and fixed
- [matrix row # → scenario → bug → fix]

### New Decisions Log entries added
- [any new architectural decisions made resolving an ambiguity found during this audit]

### Confirmed clean (no action needed)
- [anything explicitly checked and found already correct — worth recording so a future
  audit doesn't re-spend time re-checking it from scratch]

### Deferred items (with reason)
- [anything found but not fixed this pass, and why]
```

Keep it concise — this is a reference log for future agents, not a narrative.

Commit: `git add -A && git commit -m "fix: full-app master audit — regression sweep + new-surface + cross-feature integration"`
