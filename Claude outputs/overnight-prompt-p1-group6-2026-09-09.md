# Overnight Master Prompt — P1 Backlog, Group 6 ("Support Impersonation")

**Date:** 2026-09-09 (spec date; see build-status note below)
**Mode:** Autonomous overnight build, but treat this one phase with more caution than any prior
group — it mints a new class of privileged token. Do not stop to ask clarifying questions; every
open question has been pre-resolved via explicit product/security sign-off (see the decision
table below), which is the missing piece that held this item back from every earlier group.
**Run with:** a fresh branch off `main`, e.g. `feat/p1-group6-impersonation-2026-09-09`.
**Before starting:** read `CLAUDE.md` in full, `Pena_e_Arte.API/Extensions/AuthorizationExtensions.cs`
in full (five lines, but the single most important file for this phase — see the correction
below), and `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`'s `GenerateJwt` method.

## Context

This is the final master prompt executing the 2026-09-09 P1 backlog audit
(`docs/claude/audit-p1-backlog-2026-09-09.md` against
`docs/claude/p1-backlog-master-build-spec-2026-09-09.md`). Groups 1 through 5 covered every other
item; this one covers the single item that was deliberately held back across all of them —
**Support Impersonation with Audit Trail (#15)** — because its remaining open question (which
admin endpoints are safe to use while impersonating a studio) needed a real product/security
conversation, not a quick pick. That conversation happened; the decisions are below.

**Build-status note:** at the time this prompt was written, Groups 1, 2, and 4 were already
merged to `main` (confirmed via `git log`: commits `9fbde8c`, `45fead9`, `11158aa`). Group 3's
branch existed but had no commits yet. Group 5 had not been started. Whoever picks up this prompt
should verify current `main` state before branching — if Group 3 or 5 landed in the meantime,
this phase doesn't depend on either (Support Impersonation touches no entity or endpoint either
of those groups adds), so it's safe to build against whatever `main` looks like when this runs.

**Decisions (confirmed):**

| Question | Decision |
|---|---|
| Should an impersonating admin be able to read client medical/PII data (allergies, medical notes, body maps, intake/consent form content)? | **No — denied even as read-only** |
| Should an impersonating admin be able to read financial data (payments, billing, revenue reports, invoices)? | **No — denied entirely** |

Combined with the report's own recommended posture (deny by default, allow-list explicitly
rather than deny-list the dangerous ones — a missed allow-list entry is an inconvenience, a
missed deny-list entry is a security bug), this phase's allow-list is deliberately narrow. It
covers the operational, non-sensitive data a support admin actually needs to diagnose "why can't
this studio see appointment X" / "why is this client's booking stuck" style tickets: appointments,
artists, basic client identity (not profile/medical), studio settings, deposit rules, manual
reminders, notifications. Everything else — every write, every financial read, every PII read,
every export — is denied. The allow-list is a maintained, explicit list (not a blanket "all GETs"
rule), so it can be extended later in a small, well-understood change if support finds a genuine
gap; do not pre-emptively widen it beyond what's specified here.

## Required reading before touching code

- `Pena_e_Arte.API/Extensions/AuthorizationExtensions.cs` — **critical correction, read this
  first.** Every RBAC policy in this codebase already includes `"admin"` in its role list:
  `ClientAndAbove`, `ArtistAndAbove`, and `OwnerOnly` all accept role `admin` alongside their
  named roles. This means an admin's own JWT, if it simply carried a `tenant_id` claim for a
  target studio, would **already** satisfy every single authorization check on every
  studio-scoped endpoint in the app — `TenantMiddleware` even explicitly exempts
  `IsInRole("admin")` callers from its own subscription-enforcement check. There is no RBAC gap to
  close here; the actual, entire security surface this feature protects is: (1) whether a
  `tenant_id` claim for an arbitrary studio ever gets minted onto an admin's token at all outside
  the normal login flow, and (2) once it has been (deliberately, via this feature), which
  endpoints that now-unrestricted combination is allowed to reach. The `imp:true` claim and its
  gate middleware (Backend, below) is not a secondary layer on top of RBAC — it is the entire
  mechanism. Get this file's five lines fully understood before writing anything else in this
  phase.
- `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`'s private `GenerateJwt(user, roles,
  userClaims, activeTenantId)` method — the only JWT-minting code path in this codebase.
  `StartImpersonationCommand` needs a variant of this exact call, not a hand-rolled second
  JWT-issuance path.
- `Pena_e_Arte.Infrastructure/Services/CurrentUserService.cs` — `ICurrentUser.UserId`/`.Role`
  read `ClaimTypes.NameIdentifier`/`ClaimTypes.Role` directly off the current JWT with no
  impersonation-awareness today. This phase adds that awareness (see Backend).
- `Pena_e_Arte.Application/Common/Behaviors/AuditLogBehavior.cs` and
  `Pena_e_Arte.Domain/Entities/AuditLogEntry.cs` — `AuditLogEntry.ActorRole` is a plain string
  (not an enum, not FK'd to the RBAC role list), which is exactly the hook this phase needs to
  distinguish an impersonated action from the admin's own direct action, with zero schema change.
- **Correction:** the original spec's frontend section says to add the "Impersonate" action to
  `IssuerStudioDetailPage.tsx`. That file no longer exists — it was renamed to
  `frontend/src/features/platform/components/AdminStudioDetailPage.tsx` when the platform-admin
  role itself was renamed from "issuer" to "admin" (commit `0845e57`, already on `main`). Use the
  current file.

## Constraints (apply to this phase, same as every prior group)

- No new npm or NuGet packages.
- No `useEffect` for data fetching — RTK Query hooks only.
- TypeScript strict, no `any`.
- All business logic through MediatR commands/queries; no logic in endpoint lambdas beyond
  mapping.
- Every new entity needs an EF Core migration.
- Tests: unit tests for every new validator/handler; integration tests for every new endpoint —
  this phase in particular needs integration tests that assert the *negative* case (an
  impersonation token rejected on a non-allow-listed endpoint) at least as thoroughly as the
  positive case, since a false negative here is the actual security bug the report warned about.
- Help sync: admin manual section only (not a studio-facing feature studio users need
  documented, per the original spec).

---

## PHASE 1 — Support Impersonation with Audit Trail (#15)

### Design decisions (pre-resolved — do not re-litigate)

**Impersonation session data model:**
```csharp
public class ImpersonationSession : TenantEntity   // StudioId = TargetStudioId; see note below
{
    public Guid ActorUserId { get; set; }          // the real admin, never overwritten
    public string ReasonCode { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }          // hard cap, e.g. now + 45 minutes
    public DateTime? EndedAt { get; set; }
}
```
Made a `TenantEntity` with `StudioId = TargetStudioId` (not platform-wide with a separate
`TargetStudioId` field as the original spec's sketch left ambiguous) — this is a record that
belongs to the target studio's own audit trail as much as the platform's, matches every other
audit-adjacent entity's shape in this codebase, and lets `GetMyStudioAuditLog` (owner-facing) as
well as the admin's own view both query it through the normal tenant-scoped path without a second
cross-tenant query shape. Confirm this doesn't conflict with `IgnoreQueryFilters()` needs for the
admin's own listing query (`GetImpersonationSessionsQuery`, below) — that query needs the same
cross-tenant `IgnoreQueryFilters()` treatment `GetStudiosQuery`/`ExtendTrialCommand` already use
for admin-facing cross-tenant reads (cite that same approved-usage precedent in a comment).

**JWT minting (`StartImpersonationCommand`):**
- `AdminOnly`. Validates the target studio exists (`IgnoreQueryFilters()`, same precedent as
  above), creates the `ImpersonationSession` row (`ExpiresAt = DateTime.UtcNow.AddMinutes(45)`),
  and calls `IdentityService`'s JWT-minting path (expose a new method on whatever interface
  fronts `IdentityService` for `IssueImpersonationTokenAsync(adminUserId, targetStudioId,
  sessionId, expiresAt)` if `GenerateJwt` isn't already reachable from the Application layer —
  check the current interface boundary before assuming direct access) with:
  - `roles = ["admin"]` — **do not change this to `"owner"` or invent a new role string.**
    Changing the role claim would either break every `RequireRole` policy check (if using an
    unrecognized role) or silently grant genuine `OwnerOnly`-gated admin superpowers with no
    distinguishing marker (if reusing `"owner"`) — `"admin"` is already sufficient for every
    policy in this codebase per the correction above, and keeping it unchanged is what makes the
    later gate-check (which inspects the `imp` claim, not the role claim) the actual enforcement
    point.
  - `activeTenantId = targetStudioId`.
  - `UserId`/`ClaimTypes.NameIdentifier` = the **real admin's own user id**, unchanged — this is
    what makes `AuditLogEntry.ActorUserId` correctly attribute every action taken during the
    session to the real person, not a synthetic identity.
  - A new custom claim, `imp` = the `ImpersonationSession.Id` (not just `"true"` — carrying the
    session id lets the gate-check middleware and the audit behavior both reference the exact
    session without a second lookup keyed on something else).
  - Token expiry capped to `ImpersonationSession.ExpiresAt` (45 minutes), shorter than the normal
    access-token lifetime — check `GenerateJwt`'s current expiry parameter and confirm it accepts
    an override rather than always using the standard session length.
- `EndImpersonationSessionCommand` — sets `EndedAt`, `AdminOnly`. The frontend's "End session"
  control (see Frontend) calls this and then discards the impersonation token client-side,
  reverting to the admin's normal session token (which must therefore still be held/refreshable
  client-side throughout — confirm the frontend's token-storage approach can hold both
  simultaneously, or re-authenticates the admin's normal token on end, before assuming this is a
  trivial swap).

**Gate middleware — the actual security mechanism, get this exactly right:**
- New middleware (or a MediatR pipeline behavior, if that fits this codebase's existing
  request-inspection points better than raw ASP.NET middleware — check whether
  `TenantMiddleware` is the more natural place to extend, since it already runs early and already
  inspects claims) that: if the current request's JWT carries an `imp` claim, check the request's
  route against the explicit allow-list below; if the route isn't on it, reject with 403
  (a new `ImpersonationScopeException` or similar, distinct from the existing `ForbiddenException`
  so it's unambiguous in logs which mechanism blocked the request) **before** the request reaches
  any handler. If no `imp` claim is present, this check is a no-op — normal admin requests
  (without an active impersonation session) are completely unaffected.
- **Allow-list (GET only, nothing else — every POST/PUT/PATCH/DELETE is denied while
  impersonating, full stop, no exceptions in this phase):**
  - `GET /api/v1/appointments`, `GET /api/v1/appointments/{id}`, `GET
    /api/v1/appointments/check-slot`
  - `GET /api/v1/artists`, `GET /api/v1/artists/{id}`, `GET /api/v1/artists/{id}/schedule`
  - `GET /api/v1/clients`, `GET /api/v1/clients/{id}` — **basic identity fields only
    (name/email/phone/artist assignment)**; if `GetClientById`'s response already includes
    anything profile/medical-adjacent inline (check the actual response shape before assuming
    it's clean), that's a reason to trim the response for this caller, not to exclude the whole
    route — but if trimming isn't straightforward, exclude the route entirely rather than leak
    the field. Do **not** allow `GET /clients/{id}/profile`, `/tattoos`, `/portable-profile` —
    those are the PII surfaces the sign-off decision explicitly denied.
  - `GET /api/v1/studios/me`, `GET /api/v1/studios/{id}/closures`
  - `GET /api/v1/deposit-rules`, `GET /api/v1/deposit-rules/{id}` — pricing configuration, not
    financial transaction data; distinct from the denied Payments/Billing/Reports surface.
  - `GET /api/v1/manual-reminders`
  - `GET /api/v1/notifications`
  - Every other endpoint in the app — every `Payment`/`Billing`/`Report` route, every
    `ClientProfile`/`TattooRecord`/`IntakeForm`/`ConsentForm` route, every `Design` route
    (client-uploaded reference images carry similar sensitivity to tattoo records — excluded from
    this starting list, not evaluated case-by-case), every export (Group 4's CSV export, Group
    1), `GET /studios/me/audit-log` (deliberately excluded from this starting list too — mildly
    useful for support, but wasn't part of the explicit sign-off above; add it later in a small
    follow-up if support finds they need it, don't add it silently now), and everything under
    `/api/v1/platform` (admin's own platform-level routes — an impersonation token's `tenant_id`
    claim makes these irrelevant to the impersonated context anyway, but deny explicitly rather
    than relying on that being incidentally true) — **denied**.
- Maintain this list as an explicit, readable array/table in code (not scattered
  `.RequireImpersonationSafe()`-style per-endpoint attributes across 25 files) so a future
  extension is a one-file, one-line-per-route change, easy to review for exactly what it grants.

**Audit trail:**
- `AuditLogBehavior` (or a small wrapper around it) checks whether the current request carries an
  `imp` claim; if so, record `ActorRole` as `"admin-impersonating"` instead of the raw `"admin"`
  role claim, so every audited action taken during a session is distinguishable from the admin's
  own direct actions in the existing audit log with **zero schema change** —
  `AuditLogEntry.ActorRole` is already a plain string. `ActorUserId` continues to correctly
  identify the real admin (see JWT minting above). Since this phase's own allow-list is GET-only,
  there may be *no* `IAuditableCommand`-implementing writes ever reachable during a session at
  all — confirm this is actually true given the allow-list above (it should be, since every
  listed route is a read), and if so, note in the Decisions Log entry that the "distinguish
  impersonated actions in the audit log" requirement is currently satisfied vacuously (nothing
  auditable can happen during a read-only session) but the mechanism is in place for whenever the
  allow-list is ever extended to include a write.

### Frontend

- Admin: "Impersonate" action on `AdminStudioDetailPage.tsx` (corrected filename — see above),
  reason-code prompt (free text or a small fixed set — check whether other admin actions in this
  codebase use a structured reason-code enum or free text, and match whichever precedent exists
  rather than inventing a third pattern).
- Persistent, unmissable "Viewing as {studio}" banner at the layout root, visible on every page
  while impersonating, with an "End session" control — hard requirement per the original spec,
  not optional. Given the allow-list is GET-only, most of the normal owner UI's write actions
  (create/edit/delete buttons throughout the app) will now 403 if clicked during an impersonation
  session — either disable/hide those controls client-side when an `imp` claim is detected in the
  active token (preferred: the admin sees a read-only view, not a working-then-failing one), or
  at minimum ensure every write attempt surfaces the 403 as a clear "not available while
  impersonating" message rather than a generic error. Check which approach is feasible given how
  deeply write-affordances are threaded through the existing owner UI before committing to
  full client-side hiding — a clear error message on attempted writes is an acceptable fallback
  if disabling every control individually is too large a change for this phase.

### Tests

- Unit: JWT minting produces the exact claim set specified (role stays `admin`, tenant_id set to
  target, `imp` claim carries the session id, expiry capped to 45 minutes); gate middleware
  allows every listed route and denies every route not listed, including routes that don't exist
  yet at test-writing time conceptually (i.e. test the deny-by-default behavior itself, not just
  the allow-list's positive cases).
- Integration: full session lifecycle (start → allowed GET succeeds → denied GET returns 403 with
  the distinct `ImpersonationScopeException` → denied POST/PUT/DELETE returns 403 → end session →
  the same token, now past `EndedAt`, is rejected entirely, not just scope-limited — confirm
  `EndImpersonationSessionCommand` actually invalidates the token rather than just marking the DB
  row, since a JWT is self-contained and can't be revoked by a DB update alone unless something
  checks `EndedAt`/`ExpiresAt` per-request; the gate middleware itself is the natural place to add
  that check — look up the `ImpersonationSession` by the `imp` claim's session id on every
  impersonated request and reject if `EndedAt is not null || ExpiresAt < DateTime.UtcNow`, not
  just trust the JWT's own `exp` claim, so "End session" takes effect immediately rather than
  waiting for the token to naturally expire).
- Integration: audit log entries created during a session show `ActorRole ==
  "admin-impersonating"` and the correct real `ActorUserId`.

### Help sync

- Admin manual section only — what impersonation is for, the reason-code prompt, what's visible
  vs. blocked, session duration, how to end early.

---

## Out of Scope — flagged explicitly, not silently dropped

- **Allow-list expansion**: `studios/me/audit-log`, `Design`/portfolio routes, and anything else
  not explicitly listed above are deliberately excluded from this first pass, not overlooked.
  Extending the list later is a small, reviewable change given how it's structured (see Backend).
- **Write access under impersonation**: nothing in the allow-list permits any mutation. If a
  future need arises (e.g. an admin needs to manually fix a stuck appointment status while
  impersonating), that's a new, separate sign-off decision — this phase's posture is read-only,
  full stop.
- **Financial and PII read access**: explicitly denied per the sign-off decisions above; revisit
  only via an equally explicit future decision, not by quietly widening the allow-list.

## Final self-check

- [ ] `dotnet build` clean, `dotnet test` green (unit + integration).
- [ ] `pnpm tsc`/`pnpm lint`/`pnpm test` green.
- [ ] `pnpm build` clean.
- [ ] The gate middleware denies by default — verified by a test asserting a route NOT on the
      allow-list is rejected, not just that listed routes are accepted.
- [ ] Every route on the allow-list is a `GET`; no write route is ever allow-listed in this phase.
- [ ] `ImpersonationSession.ActorUserId` is the real admin's id in every test case, never
      overwritten by the impersonated context.
- [ ] `EndImpersonationSessionCommand` actually blocks further use of that session's token
      immediately (checked per-request against the DB row), not just marks it ended for display
      purposes.
- [ ] `AdminStudioDetailPage.tsx` (not the old `IssuerStudioDetailPage.tsx` name) carries the new
      "Impersonate" action.
- [ ] The "Viewing as {studio}" banner is genuinely unmissable — present at the layout root, not
      a dismissible toast that could go unnoticed.

## Final Deliverable

Append one Decisions Log entry to `docs/claude/architecture.md`, matching the existing
prose-paragraph entry format. Note: what was built, the two sign-off decisions this phase was
built against (quoting the table above), the correction to `IssuerStudioDetailPage.tsx`'s
filename, the insight that RBAC policies already include `admin` (making the `imp` gate the
actual security mechanism, not a secondary layer), and verified test status — including whether
the audit-log distinguishing behavior is currently exercised by any real write (it likely isn't,
given the read-only allow-list, and the entry should say so plainly rather than implying it's
been tested against a live write it never actually reaches).

This closes the entire 2026-09-09 P1 backlog audit — all 19 original items addressed (18 built or
explicitly and permanently descoped with reasoning on record, one — item #5, in-app messaging —
already shipped before the audit started and removed from the list).

Commit message:

```
feat: P1 backlog Group 6 — support impersonation with scoped audit trail (#15)

Builds the final P1 backlog item, held back from every earlier group pending product/security
sign-off on its admin-endpoint allow-list. Sign-off: deny impersonated read access to client
medical/PII data and all financial data; allow-list is a narrow, explicit, GET-only set of
operational routes (appointments, artists, basic client identity, studio settings, deposit
rules, manual reminders, notifications) rather than a blanket rule, extensible later in a small
reviewable change. Corrects the original spec's reference to the now-renamed
IssuerStudioDetailPage.tsx (renamed to AdminStudioDetailPage.tsx when the platform-admin role
was renamed issuer -> admin). Establishes that this codebase's existing RBAC policies already
grant "admin" every role-based permission studio-scoped endpoints check, making the new imp-claim
gate middleware the actual, sole enforcement mechanism for impersonation scope, not a defense in
depth layer. This closes the 2026-09-09 P1 backlog audit in full.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01S2ze276hAyTgmpbWntd3Kf
```
