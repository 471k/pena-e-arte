# Slice 8 (E06, admin D01–D08): end-of-slice report

- **Date:** 2026-11-20 · **Author:** Claude Code · **Spec:** `spec-slice8-admin-e06-2026-11-19.md` §8
- **Main at writing:** `35fe8f5b`, with phase 4a open as #410 (merge on green)
- **Format:** the parity reports' split. **Present** = the acceptance is met. **Partial** = met in part, with the
  rest named. **Not built** = deliberately left out, with its reason (whitelist) or its trigger (backlog).

## Where each item stands

| Item | Status | Shipped in | What's true now | What isn't |
|---|---|---|---|---|
| D01 audit log | **Present** | #403, #407 | Studio/actor/action/target/date filters; whole-UTC-day ranges; studio name resolved server-side; row details; paging and filters in the URL; staff actors named by current account name, clients never named | No outcome field: refused actions are not audited by decision, and reach Loki only as the request line (method + path + status + user id; see `claude-shared/handoffs/code-note-loki-failed-admin-action-check-2026-11-20.md`) |
| D02 unknown is not a fact | **Present** | #405 | Subscription state is known / loading / failed; no action is offered from an unknown state; load errors say whether retrying can help | — |
| D03 consequence review | **Present** | #406, #408 | One review for extend / cash-activate / cancel at all four entry points; cancel bound to a server quote (409 if it changed); extend refused for PastDue with the two moves that restore access | ~~Extending a Cancelled studio also changes nothing for access; raised with Phi, not decided~~ **Delivered 20 Nov** (refused, Phi's decision): `ExtendTrialCommand.cs:30-33,57-58`, `subscriptionKnowledge.ts`. See "Phase 7 verification" below, which also finds extend wrong for `NoSubscription` |
| D04 failed edits kept, receipts | **Partial** | #405 | Cash activation keeps its input on failure, re-checks before offering a retry, and leaves a receipt linking the audit log | **The other 19 admin mutations report by toast, or by navigating, and keep no input on failure. Backlog by Phi's decision.** They are: Suspend studio, Reinstate studio, Extend trial, Cancel subscription, Exclude from / include in dunning, Generate a referral code for a studio, Deactivate referral code, Reactivate referral code, Delete referral code, Create plan, Update plan, Delete plan, Billed-amount backfill, Revenue-ledger backfill, Queue an industry report, Start a support session, End a support session, Feedback report status + internal note, Conduct report status (from `code-note-d04-named-and-d08-watch-2026-11-23.md`, rows 2-20). The receipt links a filtered audit view, not the exact entry (backlog row below) |
| D05 support view visibly read-only | **Partial** | #410 | Banner says Read-only and shows the reason; quiet countdown (announced only at 10/5/1 min and expiry); eight create/edit pages replaced by the read-only reason; sessions land on the schedule, not the error-only dashboard | **Write controls on the pages that do load** are still shown and fail with the server's notice. Inventory and mechanism below |
| D06 triage vs browsing | **Present** | #410 | Support triage (support requests + bug reports, Needs action, oldest first) and Product feedback (feature requests + general, newest first), as URL state | No assignee, priority or due date: a product decision (whitelist) |
| D07 failed refunds | **Present** | #406 | Failed-refunds work list with reason, time, invoice and next action as text; next action split by whether Stripe ever saw the request | No retry and no "resolved" mark, by Phi's decision (backlog row below) |
| D08 reachable records | **Partial** | #404 | Studio list status/plan/page/focus in the URL; quick views; oversight status/sort/page in the URL; detail reads scoped | **Server paging of the studio list** is deferred. Its trigger was **measured on 25 Nov: `GET /api/v1/studios` passes 1 s at about 20 studios**, and the cause is a per-row query, not the missing paging (backlog row below) |

## D05: what phase 4b would have to cover (specified, not built)

Pages that do load in a support session (their reads are on `ImpersonationAllowList`), and the writes they offer:

| Page | Writes offered |
|---|---|
| Appointment detail | assign artist, confirm, complete, cancel, decline, start conversation; reschedule and no-show dialogs |
| Artist list / detail | create own profile, delete, update, resend invite, portfolio, catalog flag |
| Client list / detail | archive, restore, profile, body map, preferred artist |
| Deposit rule detail | update, delete |
| Studio settings | the Save of every card in the eight settings groups |

**Why not built tonight.** Hiding some of these would make the shell *look* read-only while it isn't, which is worse than today's honest server refusal. A global switch (a disabled `fieldset` around the page) would also disable reading: tabs, filters and the schedule's date navigation.

**Recommended mechanism.** An explicit `writes` prop on the shared `Button`. In a support session, a writes-button renders disabled, with the read-only reason as its accessible description. Then mark the controls page by page from the table above. Each page is a small, testable change, and the default stays "shown" until marked. This needs no product decision, so it can go as one PR when scheduled.

## Not built (whitelist: deliberate)

- **An outcome or failure field on audit entries.** `MrrInputLoader` reads the audit table by action and target, so every added field becomes something a consumer may come to depend on. Failures are visible in Loki instead.
- **An assignee, priority or SLA on feedback reports.** These are product decisions.
- **Refund retry**, automatic or manual. Phi decided "no retry ever": a request that failed may still have refunded.
- **Customer consent before a support session.** A product decision; the benchmark set treats it as optional.

## Backlog (with triggers)

| Row | Cost | Trigger |
|---|---|---|
| Return the audit entry's id on admin receipts, so the receipt links the exact entry | Changes `AuditLogBehavior` for every audited command | When something else also needs the id |
| "Mark resolved" on a failed refund: how it was fixed (refunded in Stripe / paid another way / not needed), plus who and when | One field set and an endpoint; it never moves money | When subscription billing actually takes money (Stripe live is deferred; billing moves to POK) |
| **Server paging of the admin studio list** (search in a POST body, never the URL) | A paged query under approved usage #5 and a list rewrite | **Measured 25 Nov on staging (below): the endpoint passes 1 s at about 20 studios, not ~300.** The rest of this cell is the trigger as it stood before the measurement, kept for the record. **Both halves (restored 24 Nov): ~300 studios, or the list endpoint's p95 over 1 s.** The p95 half is watched by the Grafana alert `admin-list-p95-slow` (p95 over 1 s in an hour, at least 3 requests, either route). The count half is read from the admin dashboard's "Total studios". On 23 Nov the count half was dropped, because Code's 23 Nov note said `TotalStudios` leaves out suspended studios. That premise was false: `GetPlatformStatsQuery` sets `totalStudios = studios.Count` over every studio, suspended ones included, so the count is the list's own size. Measured 2026-11-20: neither read appears in production Loki in the last 30 days (the list hasn't been opened in production), so there is no timing yet. To measure: `{namespace="tattooos", container="api"} \|= "RequestLoggingMiddleware" \|~ "/api/v1/(studios\|platform/subscriptions)\""` and read `Elapsed`. Use a raw filter, not `\| json`: the CRI prefix makes `\| json` silently match nothing. Studio count: a read-only `SELECT COUNT(*) FROM studios` in Phi's DB session.<br><br>**Measured 2026-11-25, staging only** (seeded studios with subscriptions and no images; bucket listed before and after, identical; everything removed afterwards). Server `Elapsed` for `GET /api/v1/studios`, three runs per size, from staging Loki: **5 studios 252-345 ms · 20 → 963-1,041 ms · 50 → 2,434-2,563 ms · 100 → 4,919-5,111 ms · 300 → 15,388-15,911 ms · 500 → 24,765-25,783 ms.** Linear, about 51 ms per studio, nearly all of it before the first byte. **The cause is `GetStudiosQuery`'s per-row `StudioCurrencyLock.IsLockedAsync`: four `EXISTS` round trips per studio (about 2,001 queries at 500) for a `CurrencyLocked` field that none of the three pages calling this endpoint reads.** The handler's own comment ("not hot-path/paginated at any real scale today") is wrong by this measurement. The admin studio list page waits on this read, so it shows skeletons for the whole time. **`GET /api/v1/platform/subscriptions` is fine:** 10-186 ms server-side at every size, and the subscription oversight page is ready in 0.8-1.5 s at 500 (0.8-1.1 s at a 4x CPU throttle). The admin dashboard, which also reads every subscription, is ready in 0.8-1.2 s at 500 (2.2-3.9 s at 4x). **The browser does not hurt first:** both lists page 10 rows in the browser, typing in the search box stays at 33-64 ms per keystroke, and Next page paints in under 65 ms, at 500. Raw results and scripts: `code-session-scratch-2026-11-24/d08-2026-11-25/` (outside the repo).<br><br>**The alert `admin-list-p95-slow` is dormant, not healthy.** It reads production only and needs at least 3 requests in an hour; the list has never been opened in production, so it has no series and cannot fire. Its silence says nothing yet. Its logic does trip on real slow traffic: the same expression with `environment="staging"`, read during the measurement, returned 10 (above the 1 s threshold). It reports 10 because 10 s is the histogram's top bucket, so a 25 s request reads as "10". Not changed |
| D05 phase 4b (above) | ~40 controls across 6 page groups | When scheduled; no decision needed |

## One pattern worth recording

**Help drift clusters at renames.** Three instances in this slice, every one where a feature was renamed and the prose wasn't re-read:

- the Dashboard "Activate" button that never existed (D04);
- D05's claim that the reason was shown;
- the `IssuerOnly` / `IssuerLayout` text in `architecture.md`.

The cheap guard is to grep both Help surfaces for a feature's old name in the same change that renames it.

## Benchmarks

Each phase cited its own sources in its PR: #403 (audit-record contents), #405, #406 (Stripe's cancel and refund docs), and #410 (impersonation: Pigment, Okta, Files.com, WorkOS, AppMaster). The one place we diverge from the set is impersonation consent, which is whitelisted above.

## Phase 7 verification, 23 Nov: D03 closed, and what `NoSubscription` actually does

**D03 is delivered.** Extending a Cancelled studio's trial is refused, as Phi decided on 20 Nov:
`ExtendTrialCommand.cs:30-33` (`CancelledMessage`) and `:57-58` (the refusal), with `subscriptionKnowledge.ts` leaving
`Cancelled` out of `TRIAL_EXTENDABLE`. The "raised with Phi, not decided" note in the table above is out of date.

**`NoSubscription` — verified from source and with a real run. It is right for cash activation and wrong for trial
extension.**

- `TenantMiddleware.EnforceAsync` reads only `SubscriptionAccessService.GetSnapshotAsync`, which reads the
  `Subscription` row. **It never reads `Studio.TrialExpiresAt`.** With no row the snapshot is null and every tenant
  request gets a 402, `/studios/me` included (`if (snapshot is null) throw new SubscriptionRequiredException`), pinned
  by `TenantMiddlewareTests.InvokeAsync_NoSubscriptionRecord_Throws`. (`SubscriptionAccessService` exists:
  `Pena_e_Arte.Infrastructure/Services/SubscriptionAccessService.cs`, since #35. Its comment calls a missing row
  "data corruption".)
- **Cash activation** on a studio with no row *creates* an Active `Subscription`
  (`ActivateSubscriptionManuallyCommand.cs`, the `studio.Subscription is null` branch), so access returns.
  `NoSubscription` in `CASH_ACTIVATABLE` is **correct**.
- **Trial extension** on a studio with no row only moves `Studio.TrialExpiresAt` (`ExtendTrialCommand.cs:63`; the
  `Subscription` branch at `:65` is skipped). The middleware never reads that field, so **the extension reports success
  and grants nothing.** That is the same defect as PastDue and Cancelled, which Phi chose to refuse.
- **The real run** (23 Nov, local MySQL, a throwaway integration probe, not committed): a studio with an expired
  trial and no `Subscription` row, then the real `ExtendTrialHandler` (+14 days), then the real
  `SubscriptionAccessService`. Result: `Studio.TrialExpiresAt` moved from 20 Nov to 7 Dec, **no row was created, and
  the snapshot was null**. That is the input that 402s.

### Spec (not built): refuse trial extension for `NoSubscription`

1. `ExtendTrialHandler`: when `studio.Subscription is null`, throw `BusinessRuleViolationException(NoSubscriptionMessage)`,
   in the same shape as `PastDueMessage`/`CancelledMessage`. Suggested text: *"This studio has no subscription record,
   so a trial extension wouldn't restore access. Record a cash payment with Activate on the studio's page, which
   creates one."* It names the move that works (cash activation creates the row), so it isn't a dead end.
2. `subscriptionKnowledge.ts`: remove `NoSubscription` from `TRIAL_EXTENDABLE`. Keep it in `CASH_ACTIVATABLE`.
3. `AdminStudioDetailPage.tsx:149` shows the trial date for `NoSubscription`. That is `Studio.TrialExpiresAt`, which
   nothing enforces, so either drop `NoSubscription` from that condition or label the date as not in effect.
4. Tests: the handler refuses with the message; the admin list and detail don't offer Extend for `NoSubscription`;
   cash activation still does. Mutation: put `NoSubscription` back into `TRIAL_EXTENDABLE`.
5. Help: `admin-studio-detail` / the manual say Extend isn't offered for a studio with no subscription record, and why.

**Why not built:** Phase 7 verifies, it doesn't build. It's a one-line refusal of the same kind Phi already decided
twice, so it should be a short decision, not a design question. **Rollback cost of leaving it:** an admin can
extend a `NoSubscription` studio, see a success toast, and the studio stays locked out.
