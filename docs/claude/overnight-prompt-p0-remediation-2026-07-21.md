# Overnight Master Prompt — Industry Audit P0 Remediation (Round 2)

**Date:** 2026-07-21
**Mode:** Fully autonomous. No user present. Run until every phase exits clean.
**Run with:** `claude --dangerously-skip-permissions`
**Before starting:** `git add -A && git commit -m "chore: pre-P0-remediation checkpoint"` then
`git checkout -b fix/p0-remediation-2026-07-21`

---

## Context — read this before anything else

`docs/claude/industry-feature-parity-report-2026-07-20.md` is the completed findings report from
last night's audit (methodology: `overnight-prompt-industry-feature-parity-audit-2026-07-20.md`). It
already shipped several whitelist items (studio closures, artist working-hours UI, sitemap/robots,
cookie consent, hiding the undelivered `AllowApiAccess` toggle, Help-sync fixes, small UI polish — see
its "What was built tonight" section). This prompt is **round 2**: it builds the highest-severity items
from that report's **P0 backlog** that were deliberately left unbuilt because they were larger than a
single-night whitelist item, but are now scoped, sequenced, and specified precisely enough to build
tonight.

**Read the full report before touching any file** — every file path, line number, and entity name below
is taken directly from it. It is one day old; re-verify every citation against current source before
acting on it, since other work may have touched these files since. If a citation is stale, trust the
live source and note the discrepancy in the deliverable, don't silently proceed on stale information.

Tonight's scope, in build order (each phase's output is a dependency for the next):

1. **Cancellation policy configuration** (report §D20) — prerequisite for #2.
2. **Client self-cancel** (report §B3).
3. **Client self-reschedule** (report §B2).
4. **Owner revenue & trend reporting** (report §D8).
5. **Structured admin/audit log** (report §E11 + §D24, merged).
6. **Verification pass** on the already-hidden `Plan.AllowApiAccess` toggle (report §E12) — confirm no
   regression, not a rebuild.
7. **Backlog carry-forward** — refresh the P1–P3 backlog to reflect what shipped tonight and what it
   unblocks, per the project's own rule: nothing money/auth/tenant-related gets built without an explicit
   decision behind it.

Everything built tonight is subject to **CLAUDE.md rules #6 and #7**, added specifically to prevent the
kind of drift this audit-and-fix cycle exists to catch:

- **Rule #6** — benchmark against the Industry-Standard Benchmark Set (`architecture.md`) for both
  backend structure and frontend UI/UX, and verify correctness across every role/tenant it touches, not
  just the one it was built for.
- **Rule #7** — every feature added or changed updates `helpContent.ts`, the standalone manual, and any
  affected onboarding-tour step in the same change. Not optional, not a follow-up commit.

---

## Required reading

```
CLAUDE.md                                    — all 7 non-negotiable rules, especially #6/#7 (new)
docs/claude/architecture.md                  — Feature Module Map, Decisions Log, IgnoreQueryFilters
                                                table, "In-App Help Menu" cluster, PlanLimitBehavior /
                                                IQuotaCheckedCommand (the pipeline-behavior precedent
                                                you will mirror for the audit log), FeedbackReport's
                                                IsAccessibleBy ownership-check precedent (the pattern
                                                you will mirror for client-owned-appointment checks)
docs/claude/industry-feature-parity-report-2026-07-20.md   — tonight's source of truth for scope
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/conventions.md
```

---

## Constraints (identical to every prior overnight prompt, plus the two new rules)

- No new npm or NuGet packages. The revenue trend chart reuses whatever charting library is already in
  the codebase (recharts, per `MrrChart.tsx`'s existing usage) — do not add a second charting library.
- No `useEffect` for data fetching. Approved exceptions as documented in every prior prompt.
- TypeScript strict mode, no `any`. Explicit C# types, no unclear `var`.
- No business logic in endpoints — MediatR only. Every command has a FluentValidation validator.
- Tenant isolation via EF Core global query filters everywhere **except** the one deliberately-designed
  exception in Phase 5 (audit log entries are readable cross-tenant by the issuer by design — see that
  phase for exactly how this must be modeled and guarded, since it is not the same shape as the existing
  `IgnoreQueryFilters()` precedents).
- Every endpoint has `.RequireAuthorization()` with the correct policy.
- Never log PII. This applies doubly hard to Phase 5 — an audit log is a new place PII could leak if
  built carelessly (see the explicit scrubbing requirement in that phase).
- Structured logs only. No secrets in source.
- Every backend change ships with unit + integration tests. Every frontend change ships with component
  tests covering loading/error/empty states at minimum.
- **Do not build blind.** If, while implementing any phase below, you discover an open product/business
  question that isn't already resolved by a decision stated in this prompt, stop building that specific
  sub-item, write it into the backlog carry-forward (Phase 7) with the open question explicit, and move
  on. This prompt has deliberately pre-decided every ambiguity the source report flagged for these six
  items — if you find a NEW one it didn't anticipate, treat it the same way, don't guess.

---

# PHASE 1 — Cancellation Policy Configuration (prerequisite for Phase 2)

Report reference: §D20. "`DepositRule.cs` has no cancellation-window/tiered-refund fields; cancel always
refunds 100%." This must exist before client self-cancel can be safely exposed — without it, self-cancel
would mean any client can cancel seconds before their appointment and always get a full refund, which is
strictly worse than today's staff-only cancellation.

## Design decisions (pre-resolved — do not re-litigate)

- Two new fields on `DepositRule`: `CancellationWindowHours` (`int?`, nullable — null means "use the
  platform default of 24 hours") and `RefundPercentOnLateCancel` (`int`, `0`–`100`, default `0` — `0`
  means a late cancellation forfeits the full deposit, matching today's existing forfeiture behavior for
  staff-initiated cancels; a studio can raise this toward `100` to be more lenient).
- A platform-wide constant (`AppointmentSelfServiceDefaults.CancellationWindowHours = 24`, or wherever
  similar cross-cutting defaults already live in the codebase — check for an existing constants location
  before creating a new one) is the fallback when an appointment has no `DepositRule` attached at all, or
  when the attached rule's `CancellationWindowHours` is null. This exists purely to gate the **timing**
  of self-service actions — it applies even when there's no deposit to forfeit, because studios
  reasonably don't want last-minute client-initiated changes regardless of whether money is involved.
- **This window/refund-percent gate applies only to client-initiated (self-service) cancellation.**
  Staff-initiated cancellation (artist/owner) keeps its existing behavior unchanged — a studio choosing
  to cancel on a client's behalf must never be penalized with a forced partial refund; that would be a
  real regression. Verify this distinction against `CancelAppointmentCommand.cs`'s actual current
  behavior before changing anything — if the current refund logic already varies by caller role in some
  way this prompt doesn't anticipate, preserve that and only add the new client-specific branch.

## Backend

1. `Pena_e_Arte.Domain/Entities/DepositRule.cs` — add the two fields above with private setters and a
   method to update them (matching this entity's existing mutation-method convention).
2. Migration: `AddCancellationPolicyToDepositRule` — `CancellationWindowHours` (`INT NULL`),
   `RefundPercentOnLateCancel` (`INT NOT NULL DEFAULT 0`), plus a `CHECK` constraint (or FluentValidation
   equivalent if the codebase doesn't use DB-level checks elsewhere — match existing convention) that
   `RefundPercentOnLateCancel` is between 0 and 100.
3. `CreateDepositRuleCommand`/`UpdateDepositRuleCommand` and their validators — add both fields as
   optional inputs. Validator: `CancellationWindowHours` when provided must be `> 0`;
   `RefundPercentOnLateCancel` must be `InclusiveBetween(0, 100)`.
4. `DepositRuleResponse` (Contracts) — add both fields.
5. Read `CancelAppointmentCommand.cs`/`CancelAppointmentHandler.cs` in full before changing it. Do not
   modify its behavior in this phase — Phase 1 only adds the configuration surface. The handler changes
   happen in Phase 2, once the fields exist to read from.

## Frontend

6. `CreateDepositRulePage.tsx` / `DepositRuleDetailPage.tsx` — add two new form fields:
   - "Cancellation notice window" — number input, hours, placeholder/help text: "How much notice a
     client must give to cancel without forfeiting their deposit. Leave blank to use the platform
     default (24 hours)."
   - "Refund if cancelled late" — number input, percent, default `0`, help text: "What percentage of the
     deposit to refund if a client cancels within the notice window. 0 means the deposit is forfeited,
     matching today's behavior."
7. `depositRulesApi.ts` / `deposit-rule.types.ts` — add both fields to request/response types.

## Tests

- Validator tests: rejects `RefundPercentOnLateCancel` outside 0–100; accepts `CancellationWindowHours`
  null and positive-int; rejects zero/negative when provided.
- Handler tests: create/update persist both fields correctly; omitting them defaults to null/0.
- Frontend: form renders both fields with correct defaults and help text; validation errors shown inline.

## Help sync (rule #7)

- `helpContent.ts` — update the existing deposit-rules entry (owner-facing) to describe the two new
  fields.
- Standalone manual — update the deposit-rules section to match.
- No onboarding-tour change needed (not a new nav item or primary action, just new fields on an existing
  form) — confirm this judgment is correct by checking whether any tour step already targets the deposit
  rule form; if one does, verify it doesn't need updating for the new fields.

---

# PHASE 2 — Client Self-Cancel

Report reference: §B3. "Cancel route is also `ArtistAndAbove` only; `AppointmentDetailPage.tsx:230`
hides the entire cancel block for clients."

**Verify the routing assumption first.** The original client-surface documentation (2026-07-01 client QA
pass) states clients have no route to `/appointments/:id` at all — they manage bookings exclusively
through `MyBookingsSection` on `/book`. The parity report's citation of `AppointmentDetailPage.tsx:230`
may mean that gate exists in shared code not actually reachable by a client today, or routing may have
changed since 07-01. **Check `router.tsx` directly before deciding where the UI goes.** If clients still
cannot reach `AppointmentDetailPage`, build the self-cancel affordance inside `BookingRow`/
`MyBookingsSection` instead (the same place deposit-payment actions already live), not by opening up a
route clients have never had access to. Either way, the backend change is identical — only the frontend
entry point differs.

## Design decisions (pre-resolved)

- Reuse the existing `CancelAppointmentCommand`/`CancelAppointmentHandler` — do not create a parallel
  command. Widen the endpoint's policy from `ArtistAndAbove` to `ClientAndAbove`, matching the established
  precedent of widening `POST /api/v1/feedback` to `ClientAndAbove` for the Support Escalation feature
  (same reasoning: one command, role-conditional behavior inside the handler, not a duplicated command
  class).
- Ownership check: when `ICurrentUser.Role == Client`, resolve the calling client the same way
  `ReviewDesignHandler`/`GetMyAppointmentsQuery` already do, and verify `appointment.ClientId` matches.
  On mismatch, return **404**, not 403 — this codebase's established convention (see the
  `ReviewDesignCommand` fix in the 2026-07-02 client QA pass) is to 404 scope violations rather than 403,
  to avoid confirming a valid-but-not-yours resource ID exists.
- Refund calculation for a client-initiated cancel:
  ```
  hoursUntilAppointment = appointment.Date - utcNow
  window = depositRule?.CancellationWindowHours ?? PLATFORM_DEFAULT_WINDOW_HOURS   // Phase 1
  if hoursUntilAppointment >= window:
      refund 100% of the deposit (same refund-eligible-states logic already fixed in the owner QA pass —
      Captured OR Paid — reuse that exact branch, don't re-derive it)
  else:
      refundPercent = depositRule?.RefundPercentOnLateCancel ?? 0
      refund refundPercent% of the deposit; forfeit the remainder
  ```
  If there is no deposit at all (`DepositAmount <= 0` or no `Payment` record), the window still gates
  *whether the client can self-cancel at all* but there is nothing to refund or forfeit either way.
- Staff-initiated cancellation is unaffected — this whole branch is gated on `Role == Client`.
- A client can only self-cancel a `Pending` or `Confirmed` appointment (matches the existing state-machine
  guard already in `CancelAppointmentHandler` — do not weaken it).

## Backend

1. `AppointmentEndpoints.cs` — widen the cancel route's policy to `ClientAndAbove`.
2. `CancelAppointmentHandler.cs` — add the role-conditional ownership check and the refund-percent branch
   above. Extract the refund-percent calculation into a small, independently-testable private method or
   domain helper (not inlined in the handler) — this exact calculation is reused verbatim in Phase 3 is
   not needed there, but keeping it isolated makes the unit tests below straightforward and keeps the
   handler readable.
3. `CancelAppointmentValidator.cs` — confirm it doesn't need changes (the request shape shouldn't grow;
   the policy/business-rule logic lives in the handler, not the validator, per this codebase's convention
   of validators only handling shape/format validation).

## Frontend

4. Wherever the cancel affordance ends up (per the routing check above) — add a "Cancel appointment"
   action for the client role. Before confirming, show the computed consequence: fetch or compute
   whether this cancellation would be inside or outside the notice window, and show either "You'll
   receive a full refund" or "Cancelling now forfeits {100 - refundPercent}% of your deposit" — computed
   client-side from the same appointment/deposit-rule data already available, confirmed authoritatively
   server-side regardless (never trust the client-side computation for the actual refund amount).
5. Confirmation dialog before the destructive action, matching the existing inline-confirm pattern used
   everywhere else in the app (do not introduce a new confirmation UI pattern).
6. Toast on success/error, matching existing conventions.

## Tests

- Handler tests: client cancelling own appointment succeeds with correct refund math for both inside-
  and outside-window cases; client cancelling another client's appointment returns 404; staff cancelling
  is unaffected by the new branch entirely (regression test); cancelling a `Completed`/`Cancelled`/
  `NoShow` appointment as a client is still rejected.
- Frontend tests: consequence message shows correctly for both cases; confirmation dialog gates the
  mutation call; success/error toasts fire; the entry point (wherever it landed) is reachable by the
  client role and not by roles that shouldn't see it there.

## Help sync (rule #7)

- `helpContent.ts` — new or updated client-facing entry describing self-cancel and the notice-window
  consequence in plain language.
- Standalone manual — same, client section.
- Onboarding tour — if the client tour has a "manage your bookings" step, verify whether it should
  mention the new cancel affordance; update if the step's target selector covers the relevant area.

## Industry-standard benchmark note (rule #6)

The market research already done for this (see the parity report's Market Research Summary) cites
Booksy/Fresha's configurable deposit/no-show fee protection as the baseline pattern — the
window-plus-refund-percent design above matches that shape rather than a flat all-or-nothing rule.

---

# PHASE 3 — Client Self-Reschedule

Report reference: §B2. "`AppointmentEndpoints.cs:25` reschedule route is `ArtistAndAbove` only; no
client command/UI exists."

## Design decisions (pre-resolved — this phase's two open questions from the report are answered here)

- **Cutoff, not tiered consequence.** Unlike cancel, reschedule has no natural partial-consequence
  concept — either the client can self-serve or they can't. Reuse the *same* window used for cancel
  (`depositRule?.CancellationWindowHours ?? PLATFORM_DEFAULT_WINDOW_HOURS`) rather than introducing a
  second, separate "reschedule window" field. This is a deliberate simplicity choice for v1 — log it as a
  decision (Phase 8) so a future session doesn't wonder why there's only one window field covering two
  different actions; split them later only if real studio usage shows they actually want different
  windows for each.
  - Outside the window: self-reschedule allowed, subject to the same slot-availability/conflict check
    already used by the existing (staff) reschedule path — reuse it, don't re-derive it.
  - Inside the window: blocked with a clear, specific message: "This appointment is less than {window}
    hours away — please contact the studio directly to reschedule." Not a generic error.
- **No separate re-confirmation state.** Per `architecture.md`'s own documented behavior,
  `RescheduleAppointmentCommand` "updates the `Date` field, triggers confirmation notification. Does NOT
  reset `Status` back to `Pending`" for staff-initiated reschedules. Client self-reschedule uses the
  *exact same* state-machine behavior — the appointment's status is unchanged, the same
  `AppointmentUpdated` SignalR event fires. Do not invent a new "awaiting artist re-approval" status for
  this — that would be a second, parallel appointment-lifecycle concept for a feature that's supposed to
  be a fully-specified, ready-to-build v1. If real usage later shows studios want artist approval on
  client-initiated reschedules specifically, that's a legitimate fast-follow, not part of tonight's scope.
- Reuse `RescheduleAppointmentCommand`/`RescheduleAppointmentHandler` — same "widen the policy, add a
  role-conditional check inside the handler" pattern as Phase 2, not a parallel command.
- Ownership check: identical pattern to Phase 2 (404 on mismatch, not 403).
- A client can only self-reschedule a `Pending` or `Confirmed` appointment — matches the existing guard
  (`Cancelled`/`Completed`/`NoShow` are rejected with `BusinessRuleViolationException`, unchanged).

## Backend

1. `AppointmentEndpoints.cs:25` — widen the reschedule route's policy to `ClientAndAbove`.
2. `RescheduleAppointmentHandler.cs` — add: (a) the ownership check when `Role == Client`; (b) the
   cutoff-window check when `Role == Client` (staff bypass this entirely — an artist/owner can reschedule
   at any notice, unchanged); (c) everything else (slot-conflict check, `BusinessRuleViolationException`
   on terminal status, `AppointmentUpdated` SignalR notification) is already correct and shared — do not
   duplicate it.
3. `RescheduleAppointmentValidator.cs` — already validates `NewDate` in the future and
   `NewDurationMinutes` in range per the existing artist-facing reschedule UI prompt; confirm this is
   still correct and sufcient for the client path (it should be — the same request shape works for both
   roles).

## Frontend

4. Same routing caveat as Phase 2 — verify where a client can actually reach this action today before
   building the UI. Reuse whatever `DURATION_OPTIONS`/date-picker pattern the existing artist/owner
   reschedule dialog uses (built in `overnight-prompt-reschedule-appointment-ui-2026-07-18.md`) rather
   than inventing a second reschedule UI from scratch — that dialog's component may be directly reusable
   with a role-conditional cutoff check added, or may need a thin client-facing wrapper; read it before
   deciding.
5. When inside the cutoff window, disable the reschedule action with the specific message above rather
   than letting the client attempt it and get a generic 422 from the backend.

## Tests

- Handler tests: client rescheduling own appointment outside the window succeeds; inside the window
  returns the specific business-rule rejection; rescheduling another client's appointment returns 404;
  staff reschedule behavior is unaffected (regression test, including the "does not reset to Pending"
  behavior); slot-conflict rejection still returns 409 for the client path exactly as it does for staff.
- Frontend tests: cutoff message shown and action disabled when inside the window; successful reschedule
  updates the UI via the existing RTK Query invalidation tag (`["Appointment", { id }]`) without a manual
  refresh; reusing (not duplicating) the existing dialog component is verified by the test file structure
  itself, not just by inspection.

## Help sync (rule #7)

- `helpContent.ts`, standalone manual, and — if the entry point is a new client-facing surface rather
  than an existing one — a tour-step check, same as Phase 2.

## Industry-standard benchmark note (rule #6)

Boulevard's "Precision Scheduling" self-reschedule flow (cited in the parity report's market research) is
the closest analog — cutoff-gated, no staff-approval step, immediate confirmation. This design matches
that shape.

---

# PHASE 4 — Owner Revenue & Trend Reporting

Report reference: §D8. "`DashboardPage.tsx` shows only today/week counts + pending deposits — zero
revenue figures, zero trend/per-artist/busiest-hour analytics."

## Scope decision (pre-resolved)

Build, in priority order, stopping at whichever point the night's remaining time runs out — do not sacrifice
correctness/tests on the earlier items to rush later ones:

1. **Must-have:** month-over-month revenue trend (last 12 months, matching the lookback window already
   used by `GetMrrHistoryQuery` for consistency) and a per-artist revenue breakdown for a selectable
   period (this month / last 30 days / all-time).
2. **Nice-to-have if time remains:** busiest day-of-week / hour-of-day analytics. If not reached tonight,
   say so explicitly in the deliverable and add it to the backlog carry-forward rather than half-building
   it.

## Backend

1. New `Pena_e_Arte.Application/Reports/Queries/GetRevenueSummaryQuery.cs` — `OwnerOnly` (this is
   financial data; artists get their own scoped view separately per the backlog's §C7, not part of
   tonight's scope — see Phase 7). Inputs: optional `from`/`to` date range, defaults to the trailing 12
   months for the trend series. Aggregates `Payment` rows with `Status == Paid` (confirm the exact status
   enum values by reading `Payment.cs`/`PaymentStatus` — do not assume without checking) grouped by:
   - Calendar month (for the trend series)
   - `Appointment.ArtistId` (for the per-artist breakdown, current selectable period only)
   Always scoped by the standard tenant global query filter — no `IgnoreQueryFilters()` needed here, this
   is a single-tenant owner report, not a cross-tenant issuer one.
2. New `Pena_e_Arte.API/Endpoints/ReportEndpoints.cs` (or add to an existing appropriate endpoint group if
   one better fits this codebase's actual grouping convention — check `Program.cs`'s `MapGroup` structure
   before creating a new file) — `GET /api/v1/reports/revenue-summary` (`OwnerOnly`).
3. `RevenueSummaryResponse` (Contracts) — `{ monthlyTrend: { month: string; revenue: decimal }[],
   perArtist: { artistId: Guid; artistName: string; revenue: decimal }[] }` (extend with a busiest-hour
   shape only if Phase 4 reaches that stretch goal).

## Frontend

4. New `frontend/src/features/reports/` module: `reportsApi.ts` (RTK Query), `ReportsPage.tsx` at a new
   route `/reports` (`OwnerOnly`), added to `OwnerLayout`'s nav (mind the existing mobile-nav-overflow
   fix — this is now an 9th item on an already-crowded owner nav; verify `overflow-x-auto` still handles
   it rather than assuming).
5. Trend chart: reuse `recharts` exactly as `MrrChart.tsx` already does — same library, same general
   treatment, don't introduce a second charting approach for internal consistency's sake even though this
   is an owner-facing page rather than an issuer one.
6. Per-artist breakdown: a simple bar chart or sorted list (whichever is more consistent with the rest of
   this codebase's reporting pages — check `IndustryReportsPage.tsx`'s plain-table convention as the more
   likely fit for a first version, matching the established preference for simple tables over
   over-engineered visualizations noted elsewhere in this codebase's Decisions Log).
7. Loading skeleton, error state with retry, empty state ("No revenue recorded yet" for a brand-new
   studio) — per the standing convention enforced across every prior QA pass.
8. Document title (`useDocumentMeta`) — "Reports — Pena e Artë".

## Tests

- Handler tests: correct aggregation by month and by artist; correctly excludes non-`Paid` payments;
  correctly scoped to the calling tenant only (cross-tenant leak test); empty-data edge case (brand-new
  studio, zero payments) returns empty arrays, not an error.
- Frontend tests: chart renders with mock data; loading/error/empty states; per-artist breakdown sorts
  correctly (highest revenue first, matching how every other ranked list in this app is ordered).

## Help sync (rule #7)

- `helpContent.ts` — new owner-facing entry for the Reports page.
- Standalone manual — new section.
- Onboarding tour — add a step to `ownerTour.ts` pointing at the new nav item, matching how the C2/D2
  artist-schedule-editing feature from last night's pass added its own tour step in the same change
  rather than as an afterthought.

## Industry-standard benchmark note (rule #6)

Every competitor in the researched set (Vagaro, Boulevard, Mindbody, Zenoti, Mangomint) treats
revenue/trend reporting as core owner-facing table stakes, not a premium add-on — this is squarely a P0
gap closure, not a differentiator play.

---

# PHASE 5 — Structured Admin/Audit Log

Report reference: §E11 + §D24 (explicitly merged in the report as "the same underlying gap, different
roles affected"). "Suspend/unsuspend has zero logging of any kind... `TenantEntity` has no
actor-attribution field at all; no `AuditLog` entity."

This is the largest and most architecturally sensitive phase tonight — it introduces a genuinely new
cross-cutting concept (a MediatR pipeline behavior with a marker interface, mirroring the existing
`PlanLimitBehavior`/`IQuotaCheckedCommand` pattern) and a new entity with a deliberately non-standard
tenant-scoping shape. Read `PlanLimitBehavior` and `IQuotaCheckedCommand` in full before starting — this
phase's mechanism should feel like a sibling to that one, not a new, differently-shaped pattern.

## Design decisions (pre-resolved)

- **New entity:** `AuditLogEntry` — `Id (Guid)`, `ActorUserId (Guid)`, `ActorRole (string)`,
  `Action (string)` (a stable, human-readable identifier like `"Studio.Suspended"`,
  `"Plan.Updated"`, `"Appointment.Cancelled"` — not the raw C# command type name, which is an
  implementation detail that could change; define these as constants somewhere sensible, not inline
  magic strings scattered across handlers), `TargetType (string)`, `TargetId (Guid)`,
  `StudioId (Guid?)` — **nullable, and this is the deliberate deviation from the normal `TenantEntity`
  shape.** Populate it whenever the action targets a specific studio (suspend, cash-activate, referral
  code actions, and every owner-side action in the list below) — leave it null only for genuinely
  platform-wide actions with no single studio target (e.g., `Plan.Updated`, which affects a tier, not one
  studio). `Metadata (string, JSON)`, `CreatedAt (DateTime)`.
- **Query filter:** do NOT inherit the standard `TenantEntity` global query filter — it assumes every row
  belongs to exactly one tenant and would either wrongly exclude the platform-wide (`StudioId == null`)
  rows or wrongly scope issuer reads. Instead: no EF Core global query filter on this entity at all (same
  approach already used for `FeedbackReport`/`UserOnboardingState` — non-tenant-scoped, configured inline
  in `AppDbContext.OnModelCreating`, per that established precedent). Authorization for *who can read
  which rows* is enforced in the query handlers, not the query filter:
  - Issuer: reads everything (`GetAuditLogQuery`, `IssuerOnly`, effectively `IgnoreQueryFilters()`-shaped
    even though there's no filter to ignore — since there is no filter, no new table entry is needed in
    the "IgnoreQueryFilters() Approved Usages" list, but do add a short note to that table's surrounding
    text pointing at this entity's shape as a related-but-different pattern, so a future reader
    understands why it's not listed there).
  - Owner: reads only rows where `StudioId == callingOwner'sStudioId` (`GetMyStudioAuditLogQuery`,
    `OwnerOnly`, explicit `.Where(a => a.StudioId == tenant.StudioId)` — never trust the absence of a
    global filter to do this scoping for you, since there deliberately isn't one).
- **PII scrubbing.** `Metadata` must NEVER contain names, emails, phone numbers, or free-text notes —
  only IDs, enum values, and structural before/after values (e.g., a plan-price change logs the old/new
  price and interval, not any customer-identifying data). Write a small, explicitly-tested serialization
  helper that whitelists fields per action type rather than serializing an entire command object
  wholesale — a wholesale serialize-the-command approach is exactly how PII would leak into this table
  by accident.
- **Mechanism:** new `IAuditableCommand` marker interface (mirrors `IQuotaCheckedCommand` exactly) with an
  `AuditAction` (string) and an `AuditTargetId` (`Guid`) property, plus optional `AuditStudioId` (`Guid?`)
  when the command doesn't otherwise expose a resolvable studio id. New `AuditLogBehavior` (MediatR
  pipeline behavior), registered in the same pipeline position as `PlanLimitBehavior` (after
  `ValidationBehavior`) or immediately after it — check the actual DI registration order before deciding
  exact placement, and only log **after successful execution** (mirror `PlanLimitBehavior`'s "only after
  `SaveChangesAsync` succeeds" discipline — a command that fails validation or throws mid-handler must not
  produce a misleading "this happened" audit row).
- **Commands to wire up** (exact list from the report, carried forward verbatim):
  - Issuer side: `SuspendStudioCommand`, `UnsuspendStudioCommand`, `ExtendTrialCommand`,
    `CancelSubscriptionCommand`, `ActivateSubscriptionManuallyCommand`, `UpdatePlanCommand`,
    `DeactivateReferralCodeCommand`, `ReactivateReferralCodeCommand`, `DeleteReferralCodeCommand`.
  - Owner side: `CancelAppointmentCommand` (now also client-callable per Phase 2 — the audit entry should
    record which role actually performed it), whatever the actual "delete client record" command is
    (confirm its real name — the report describes it generically, do not guess a class name that doesn't
    exist), `UpdateSessionSplitsCommand`.
  - Do not wire up every command in the app tonight — this specific list is deliberately scoped to the
    actions the report identified as trust/compliance-sensitive. Wiring the marker interface onto more
    commands is a trivial, safe fast-follow once the mechanism exists; don't let scope creep here risk the
    core mechanism's quality.

## Backend

1. `Pena_e_Arte.Domain/Entities/AuditLogEntry.cs` — new entity per the shape above.
2. Migration: `AddAuditLogEntry`.
3. `Pena_e_Arte.Application/Common/Behaviors/AuditLogBehavior.cs` +
   `Pena_e_Arte.Application/Common/Interfaces/IAuditableCommand.cs` (or wherever `IQuotaCheckedCommand`
   actually lives — mirror its exact location convention).
4. Wire `IAuditableCommand` onto each command in the list above; define the `AuditAction` string
   constants in one place (e.g., `AuditActions.cs`) rather than inline per command.
5. `GetAuditLogQuery`/`GetAuditLogHandler` (`IssuerOnly`) and
   `GetMyStudioAuditLogQuery`/`GetMyStudioAuditLogHandler` (`OwnerOnly`) — both support filtering by
   `action`/date range/`targetType`, paginated.
6. New endpoints (check `PlatformEndpoints.cs`'s existing grouping convention for the issuer one; check
   the studio/owner endpoint grouping for the owner one): `GET /api/v1/platform/audit-log` (`IssuerOnly`),
   `GET /api/v1/studios/me/audit-log` (`OwnerOnly`).

## Frontend

7. New `AuditLogPage.tsx` (issuer, `/platform/audit-log`) — follow `IndustryReportsPage.tsx`'s plain
   `<table>` style precedent (not a chart), filterable by action/date/target, matching the same
   established preference noted in Phase 4.
8. New, lighter owner-facing view — could be its own page or a tab/section on an existing owner settings
   page (`StudioProfilePage.tsx` is a reasonable fit given how many other "studio meta" concerns already
   live there) — read-only list of recent actions on their own studio.
9. Both need loading/error/empty states, document titles, and nav entries (issuer nav already exists;
   confirm mobile-nav-overflow handling still holds with one more item).

## Tests

- `AuditLogBehavior` tests: fires only on success, not on validation failure or an exception thrown
  mid-handler; correctly resolves `ActorUserId`/`ActorRole` from `ICurrentUser`; correctly scrubs
  metadata (a dedicated test asserting no PII-shaped field ever appears in the serialized metadata for
  each wired command is worth the effort here, not just a happy-path test).
- Handler tests for both read queries: issuer sees all studios' entries; owner sees only their own
  studio's entries and gets 403 attempting another studio's via query manipulation; pagination and filter
  params work correctly; a `StudioId == null` platform-wide entry is visible to issuer but correctly
  excluded from every owner's view.
- Frontend tests: both pages' loading/error/empty states; filter controls narrow results correctly.

## Help sync (rule #7)

- `helpContent.ts` — new entries for both the issuer audit-log page and the owner-facing view.
- Standalone manual — same, in both the issuer and owner sections.
- Onboarding tour — add a step to `issuerTour.ts` for the new audit-log nav item; evaluate whether
  `ownerTour.ts` needs one depending on where the owner-facing view landed (a settings-page tab may not
  warrant its own tour step if the settings page itself is already a tour stop — use judgment, but state
  the reasoning in the deliverable rather than silently skipping it).

## Industry-standard benchmark note (rule #6)

Per the parity report's own market research: "a structured queryable admin-action log is treated as
near-compliance-mandatory once suspend/cancel/plan-edit actions touch paying customers." This phase closes
that specific gap, not a nice-to-have.

## Note for the backlog (do not build tonight, just note it)

The report's §E10 (support impersonation) explicitly said it "should follow #6 (audit log), not precede
it." That dependency is resolved as of tonight — flag in the Phase 7 backlog carry-forward that
impersonation is now unblocked and ready to be scoped as its own future prompt, but do not attempt to
build it tonight; it wasn't in tonight's requested scope and still has its own genuinely open product
question (which endpoints belong in the impersonation allow-list) that this prompt hasn't resolved.

---

# PHASE 6 — Verification: `Plan.AllowApiAccess` (no rebuild, confirm no regression)

Report reference: §E12, already remediated last night ("removed from `PlanEditPage.tsx` and its
`helpContent.ts`/manual mentions"). Tonight's job is a quick, explicit confirmation, not new work:

1. Grep the entire frontend and backend for `AllowApiAccess` and `PrioritySupport` (the report separately
   flagged `PrioritySupport` in §E15 as having "the same unwired risk... lower severity"). Confirm:
   - `AllowApiAccess` does not appear in any user-facing component, `helpContent.ts`, or the standalone
     manual — it may still exist on the `Plan` entity/DTOs (fine — the field itself isn't the problem, an
     unbacked user-facing promise is).
   - `PrioritySupport` — assess it the same way. If it is ALSO a live, sold, or help-documented toggle
     with zero backing implementation, apply the exact same fix (hide it from user-facing surfaces,
     update Help content to match) — this is small and safe enough to be whitelist-eligible tonight, per
     the same reasoning as `AllowApiAccess` last night. If it's genuinely unreferenced anywhere
     user-facing already, no action needed — just confirm and note it.
2. Record the outcome explicitly in the deliverable — "confirmed clean" is a valid, useful finding, don't
   pad this section if there's nothing to fix.

---

# PHASE 7 — Backlog Carry-Forward

Update `docs/claude/industry-feature-parity-report-2026-07-20.md` (or add a clearly-dated addendum
section to it — do not create a fragmented second full report; this is a living document) to reflect:

- Move items #1–6 above from "P0 backlog" to "shipped 2026-07-21," with a one-line pointer to where each
  landed (file paths, not a re-explanation).
- Note that §E10 (support impersonation) is now unblocked (its stated dependency on the audit log existing
  is satisfied) but remains unbuilt and still has its own open product question — do not build it tonight.
- Re-confirm every remaining P1–P3 item's "open questions" field is still accurate; if anything built
  tonight changes an assumption a backlog item depended on (e.g., §E8's usage-limit completion work could
  plausibly reuse the new `AuditLogBehavior` pipeline-position precedent when it eventually wires
  notification/storage/location enforcement — note this kind of connection where you find one, don't
  invent connections that aren't real).
- Everything explicitly on the "do not build blind" list from the original parity-audit prompt (gift
  cards, packages/memberships, POS/inventory, payroll/commission automation, multi-location, native
  mobile, SSO, i18n, tax handling, marketing-campaign sending) remains untouched tonight — confirm this by
  grepping your own diff against that list before finishing, exactly as that prompt's own final self-check
  required.

---

## Final self-check before declaring done

```
dotnet build   → 0 errors, 0 warnings
pnpm build     → 0 TypeScript errors
dotnet test    → All green
pnpm test      → All green
```

Plus, walk through each explicitly:
- Every new endpoint has the correct `.RequireAuthorization()` policy — no exceptions, no new
  undocumented `AllowAnonymous`.
- `AuditLogEntry`'s unusual nullable-tenant shape is exactly as specified — not accidentally inheriting
  the standard global query filter, not accidentally readable cross-studio by an owner.
- Every phase's `Metadata`/logged fields contain zero PII — spot check by reading actual serialized
  output in a test, not just the code that produces it.
- `helpContent.ts`, the standalone manual, and onboarding-tour steps are updated for every phase that
  shipped a user-facing change — this is rule #7, not optional polish. Re-run the onboarding tour for
  owner and issuer roles (the two roles touched tonight) and confirm no step silently fails to resolve a
  target that changed.
- Every phase's design-decision section above was actually followed as specified — if you deviated from
  any of them because current source didn't match what the report described, that deviation and its
  reason is called out explicitly in the deliverable, not silently absorbed.

---

## Final Deliverable

Append a new section to `docs/claude/architecture.md`'s Decisions Log / QA-pass log area (matching the
established format of prior entries — e.g., "## Cancellation Policy + Client Self-Service — 2026-07-21",
"## Owner Revenue Reporting — 2026-07-21", "## Structured Admin/Audit Log — 2026-07-21") covering:

```markdown
### What was built (per phase)
- [phase → files touched → what shipped]

### Design decisions confirmed or revised against live source
- [any place where current source didn't match the report's citation, and what you did instead]

### Help / documentation sync
- [helpContent.ts / manual / tour updates per phase]

### Backlog carry-forward
- [what moved from P0 backlog to shipped; what's newly unblocked; confirmation the "do not build blind"
  list was respected]

### Deferred within scope (if any)
- [e.g., busiest-hour analytics if Phase 4 didn't reach it — say so plainly]
```

Commit: `git add -A && git commit -m "feat: client self-service reschedule/cancel, owner revenue reporting, structured audit log — P0 remediation round 2"`
