# Overnight Prompt — Help Menu, Full Package
**Date:** 2026-07-20
**Supersedes:** `docs/claude/overnight-prompt-help-menu-2026-07-20.md` (Part A below —
same content, referenced not repeated). This document adds the three pieces that make it
match full industry-standard help-widget coverage (Intercom Messenger / Zendesk / Help
Scout Beacon pattern): search analytics, a real support-escalation thread, and a first-run
product tour.

**Scope warning:** Part A is frontend-only. Parts B–D are not. Together this is a multi-day
effort (2 new entities, 1 new SignalR hub, ~10 new endpoints, 2 migrations). Ship Part A
first, then B, C, D in that order — each is independently useful and independently PR-able.
Do not attempt all four parts in a single sitting/branch.

---

## Part A — Searchable Guides + FAQ (recap, see prior doc for full detail)

`frontend/src/features/help/` — `HelpMenu`, `helpContent.ts`, `helpSearch.ts`, role-scoped
articles, FAQ tab, `Shift+?` shortcut, layout integration next to `NotificationBell`. Zero
backend. Build this first; Parts B–D all attach to the `HelpMenu` Sheet it creates.

---

## Part B — Search Analytics ("what are people stuck on")

### Why this matches industry standard

Every mature help widget (Intercom Articles, Zendesk Guide, Help Scout) logs what users
search for and specifically flags zero-result queries — that's the single highest-signal
list of missing documentation or confusing UX a support/product team gets. This is an
aggregate, anonymized-enough (no free-text PII expected in search queries, but treat as
sensitive anyway — never log body text of support tickets here, only the help search box).

### Backend

**Entity** — `Pena_e_Arte.Domain/Entities/HelpSearchLog.cs`:
```csharp
namespace Pena_e_Arte.Domain.Entities;

public class HelpSearchLog : TenantEntity   // StudioId, CreatedAt, UpdatedAt, DeletedAt from base
{
    public Guid   UserId      { get; private set; }
    public string Role        { get; private set; } = string.Empty; // "client"|"artist"|"owner"|"issuer"
    public string Query       { get; private set; } = string.Empty; // trimmed, lowercased, max 200 chars
    public int    ResultCount { get; private set; }

    private HelpSearchLog() { }

    public static HelpSearchLog Create(Guid studioId, Guid userId, string role, string query, int resultCount) =>
        new()
        {
            StudioId    = studioId,
            UserId      = userId,
            Role        = role,
            Query       = query.Trim().ToLowerInvariant(),
            ResultCount = resultCount,
        };
}
```
Normal `TenantEntity` — gets the standard global query filter. No `IgnoreQueryFilters()` for
the write path (`LogHelpSearchCommand` runs inside the current tenant, `ICurrentTenant`
supplies `StudioId` same as every other tenant-scoped write). The **read** path for issuer
insights is cross-tenant and does need `IgnoreQueryFilters()` — see below, that's the new
approved-usage table entry.

**Migration:** `AddHelpSearchLogs` — one table, index on `(Query, CreatedAt)` for the
aggregate query, index on `(StudioId, CreatedAt)` for the standard tenant filter path.

**Command** — `Application/Help/Commands/LogHelpSearchCommand.cs`:
```csharp
public record LogHelpSearchCommand(string Query, int ResultCount) : IRequest;
```
Handler reads `StudioId` from `ICurrentTenant`, `UserId`/`Role` from `ICurrentUser`, creates
and saves the entity. No response needed (`IRequest`, not `IRequest<T>`). This is the same
"cheap, fire-and-forget, no domain complexity" shape as `RecordArtistView`, except this one
does write to the DB (a search log has analytical value the anonymous view counter doesn't;
Redis-only would lose the query text).

**Validator** — `LogHelpSearchValidator`: `Query` not empty, max 200 chars; `ResultCount` >= 0.

**Endpoint** — add to a new `Pena_e_Arte.API/Endpoints/HelpEndpoints.cs`:
```csharp
app.MapPost("/api/v1/help/search-log", LogHelpSearch)
    .RequireAuthorization("ClientAndAbove");
```
No rate limiting — per `conventions.md`/the Redis rate-limiting prompt's own rule, "Do NOT
add rate limiting to authenticated-only endpoints." Volume is controlled client-side instead
(see frontend debounce below). Register `MapHelpEndpoints()` in `Program.cs` alongside the
other `Map*Endpoints()` calls.

**Query (issuer insights)** — `Application/Platform/Queries/GetHelpSearchInsightsQuery.cs`:
```csharp
public record GetHelpSearchInsightsQuery(int Days = 30) : IRequest<HelpSearchInsightsResponse>;
```
Handler uses `_db.HelpSearchLogs.IgnoreQueryFilters()` — **add this as the next numbered row**
in `docs/claude/architecture.md`'s "IgnoreQueryFilters() Approved Usages" table (grep the
file first for the current highest number — it was 38 as of the last full-app audit, but
verify, don't hardcode 39 blindly since other work may have landed since). Purpose: "Cross-
tenant aggregate of help search queries for the issuer product-insights view." Who calls it:
`IssuerOnly`.

Groups by lowercased `Query`, filtered to `CreatedAt >= UtcNow.AddDays(-Days)`, returns:
```csharp
public record HelpSearchInsightsResponse(
    int                              TotalSearches,
    int                              Days,
    List<HelpQueryFrequency>         TopQueries,        // ordered by count desc, top 20
    List<HelpQueryFrequency>         ZeroResultQueries); // ResultCount == 0, ordered by count desc

public record HelpQueryFrequency(string Query, int Count, string[] RolesAsked);
```

**Endpoint:** `GET /api/v1/platform/help-search-insights?days=30` — `IssuerOnly`, in
`FeedbackEndpoints.cs`'s sibling `PlatformEndpoints.cs` if one exists, else add to a new
group in `HelpEndpoints.cs` under `/api/v1/platform/...` — check where the existing
`platform/feedback`, `platform/subscriptions` etc. groups live and match that file's location
convention exactly rather than guessing.

### Frontend

**Do not add this to `helpApi` in a way that blocks search.** The log call must never delay
or fail the search UI.

`features/help/helpApi.ts` (new — Part A deliberately had none, this is the first thing that
needs one):
```typescript
export const helpApi = createApi({
  reducerPath: "helpApi",
  baseQuery,
  endpoints: (builder) => ({
    logHelpSearch: builder.mutation<void, { query: string; resultCount: number }>({
      query: (body) => ({ url: "help/search-log", method: "POST", body }),
    }),
  }),
});
export const { useLogHelpSearchMutation } = helpApi;
```
Add `[helpApi.reducerPath]: helpApi.reducer` and `.concat(helpApi.middleware)` to
`app/store.ts` (this file must be edited — flag it explicitly to the engineer, it's outside
`features/help/` but unavoidable, same as every other new API slice in this codebase).

In `HelpSearchInput.tsx`: debounce the query 800ms (existing `useDebouncedValue`-style hook
if one exists in `shared/hooks/` — check before writing a new one), then on the debounced
value firing AND only once per distinct query per open-session (dedupe with a `Set<string>`
in a `useRef`, reset when the Sheet closes), call:
```typescript
logHelpSearch({ query: debouncedQuery, resultCount: results.length })
  .unwrap()
  .catch(() => {}); // never surface this failure to the user
```

**Issuer-side insights view** — add to `platformApi.ts` (per `frontend.md`'s explicit rule:
"Do NOT add issuer platform queries to billingApi or studiosApi — keep them in platformApi"
— same rule applies here, this is issuer platform data):
```typescript
getHelpSearchInsights: builder.query<HelpSearchInsightsResponse, { days?: number } | void>({
  query: (args) => `platform/help-search-insights${args?.days ? `?days=${args.days}` : ""}`,
  providesTags: ["PlatformStats"], // reuse existing tag, no new tagType needed for a read-only report
}),
```
New page `features/platform/components/HelpInsightsPage.tsx` at route `/platform/help-
insights` (add to `router.tsx`'s `platform` children, `IssuerOnly`), linked from
`IssuerDashboardPage`'s quick nav and added as a nav item in `IssuerLayout.tsx`. Renders two
simple tables: Top Queries, Zero-Result Queries, each with a role breakdown chip row. No
chart library needed — this is two `<table>`s, consistent with `IndustryReportsPage`'s
plain-list style rather than `MrrChart`'s chart treatment.

### Tests
- Backend: `LogHelpSearchHandlerTests` (creates and saves), `GetHelpSearchInsightsHandlerTests`
  (grouping/ordering/zero-result filtering, cross-tenant aggregation correctness).
- Frontend: `helpApi` mutation fires exactly once per distinct debounced query;
  `HelpInsightsPage` renders both tables; a client/artist/owner role never sees the
  `/platform/help-search-insights` route (`RoleGuard` already handles this — add a router
  test asserting the redirect, matching the pattern in `app/__tests__/router.test.tsx`).

---

## Part C — Support Escalation ("Contact Support" thread inside the Help menu)

### What this is, and what it deliberately is not

Industry-standard "contact support from the help widget" (Intercom Messenger, Help Scout
Beacon) is **async, real-time-pushed messaging** — not synchronous live chat requiring an
agent to be online at that exact second. Given this platform has a small issuer/platform-
admin team, not a staffed live-chat desk, this is the correct shape: a threaded ticket that
delivers new replies instantly via SignalR if the other party happens to be online, and
otherwise waits (plus, later, an email notification — out of scope for this pass, flagged
as a follow-up at the end).

This builds on the existing `FeedbackReport` system (`docs/claude/architecture.md`'s
"Feedback / Bug Report Feature — 2026-07-02" section) rather than inventing a parallel
ticket system — `FeedbackReport` + `FeedbackStatus` + `FeedbackInboxPage` already are an
issuer-facing ticket inbox, just currently one-shot (a single `IssuerNote` overwritten per
status change, no back-and-forth). This adds the missing piece: a message thread.

### Backend

**1. New enum value** — `Pena_e_Arte.Domain/Enums/FeedbackType.cs`: add `SupportRequest`
alongside the existing `BugReport | FeatureRequest | General`. Update `FEEDBACK_TYPE` const
in `frontend/src/features/feedback/feedback.types.ts` to match (`SupportRequest: "SupportRequest"`).
Do **not** add `SupportRequest` as a selectable option in the existing `FeedbackDialog`'s
type `Select` — that dialog stays exactly as-is for Bug Report / Feature Request / General.
`SupportRequest` is only ever created from the new Help-menu flow described below.

**2. New entity** — `Pena_e_Arte.Domain/Entities/FeedbackMessage.cs`:
```csharp
public class FeedbackMessage
{
    public Guid     Id                { get; private set; } = Guid.NewGuid();
    public Guid     FeedbackReportId  { get; private set; }
    public Guid     AuthorUserId      { get; private set; }
    public string   AuthorRole        { get; private set; } = string.Empty;
    public string   Body              { get; private set; } = string.Empty;
    public DateTime CreatedAt         { get; private set; } = DateTime.UtcNow;

    private FeedbackMessage() { }

    public static FeedbackMessage Create(Guid feedbackReportId, Guid authorUserId, string authorRole, string body) =>
        new()
        {
            FeedbackReportId = feedbackReportId,
            AuthorUserId     = authorUserId,
            AuthorRole       = authorRole,
            Body             = body.Trim(),
        };
}
```
**Not a `TenantEntity`** — same reasoning as `FeedbackReport` itself (documented in
architecture.md: "`FeedbackReport` is NOT a `TenantEntity` — no EF Core global query filter,
... `GetFeedbackReportsHandler` queries across all studios without `IgnoreQueryFilters()`").
A child of a non-tenant entity must not silently gain a filter its parent doesn't have —
keep the two consistent. Configure via `IEntityTypeConfiguration<FeedbackMessage>`, FK to
`FeedbackReport.Id`, cascade delete.

Add `IReadOnlyList<FeedbackMessage> Messages` navigation to `FeedbackReport` (private setter
collection, exposed read-only, same pattern as other aggregate-root child collections in
this codebase — check `Appointment`/`SessionSplit` or `Design`/`DesignRevision` for the exact
existing idiom and match it).

**Migration:** `AddFeedbackMessages`.

**3. Authorization change — read this carefully before implementing:**

`POST /api/v1/feedback` is currently `ArtistAndAbove` (architecture.md, line ~2052: "Feedback
(2.6) — `POST /api/v1/feedback` is `ArtistAndAbove`"). Clients cannot submit feedback today.
But clients must be able to reach Contact Support from their Help menu. Do **not** blanket-
loosen the endpoint to `ClientAndAbove` and call it done — that would let clients submit Bug
Report / Feature Request too, which was presumably deliberately restricted to studio staff.

Instead:
- Change the endpoint policy to `"ClientAndAbove"`.
- Add a rule enforced in `SubmitFeedbackValidator` (inject `ICurrentUser`, FluentValidation
  validators can take constructor-injected dependencies — same DI container as everything
  else): if `_currentUser.Role == "client"`, the request `Type` must equal `SupportRequest`
  or validation fails with a clear message ("Clients can only submit support requests").
  Artist/Owner/Issuer keep unrestricted access to all three original types plus
  `SupportRequest`.
- Update the architecture.md line referenced above to describe the new, narrower rule rather
  than leaving stale text that says "ArtistAndAbove" unqualified.

**4. New endpoints** — add to `FeedbackEndpoints.cs` (do not create a separate file, this is
the same resource):
```csharp
group.MapGet("mine", GetMyFeedbackReports)
    .RequireAuthorization("ClientAndAbove"); // note: this MUST be its own group at
    // /api/v1/feedback (ClientAndAbove), separate from the /api/v1/platform/feedback
    // group which stays IssuerOnly — do not nest "mine" under the IssuerOnly group.

app.MapGet("/api/v1/feedback/{id:guid}/messages", GetFeedbackMessages)
    .RequireAuthorization("ClientAndAbove");
app.MapPost("/api/v1/feedback/{id:guid}/messages", PostFeedbackMessage)
    .RequireAuthorization("ClientAndAbove");
```

`GetMyFeedbackReportsQuery` — returns reports where `SubmitterUserId == currentUser.UserId`
**and** `StudioId == currentTenant.StudioId` (manual filter in the handler, matching how
`SubmitFeedbackHandler` already manually reads `StudioId` from `ICurrentTenant` — there is no
EF filter to rely on here since the entity isn't tenant-scoped by EF).

`GetFeedbackMessagesQuery(FeedbackReportId)` / `PostFeedbackMessageCommand(FeedbackReportId,
Body)` — both must do a **resource-ownership check** in the handler, not just a role policy,
since "can this user see this ticket" is not expressible as a static RBAC policy:
```
if currentUser.Role == "issuer": allow (issuer sees all tickets, matching existing
    GetFeedbackReportsHandler cross-studio behavior)
else:
    load the FeedbackReport by id
    if report is null: throw NotFoundException
    if report.SubmitterUserId != currentUser.UserId or report.StudioId != currentTenant.StudioId:
        throw ForbiddenException  // maps to 403 via ExceptionMiddleware
```
Check `Domain/Exceptions/` for an existing `ForbiddenException`/`NotFoundException` base
before adding a new one — reuse what's there.

`PostFeedbackMessageHandler` additionally pushes the new message over SignalR (see below) and,
if the author is the studio-side user (not issuer) and the report status is `Resolved` or
`Dismissed`, reopens it by setting `Status = FeedbackStatus.Open` — replying to a closed
ticket should reopen it, same UX every helpdesk uses.

**5. SignalR — new hub** `Pena_e_Arte.Infrastructure/Hubs/SupportHub.cs`, matching the exact
shape of `ScheduleHub.cs`:
```csharp
[Authorize]
public class SupportHub : Hub
{
    public async Task JoinTicket(string feedbackReportId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ticket:{feedbackReportId}");

    public async Task LeaveTicket(string feedbackReportId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket:{feedbackReportId}");
}
```
Map it in `Program.cs` next to the other hub mappings: `app.MapHub<SupportHub>("/hubs/support");`

Push from `PostFeedbackMessageHandler` via `IHubContext<SupportHub>`:
```csharp
await _hub.Clients.Group($"ticket:{feedbackReportId}")
    .SendAsync("SupportMessageReceived", messageResponse, ct);
```
Add a row to architecture.md's "SignalR Event Naming Convention" table:
`SupportMessageReceived   new reply posted on a support ticket`.

**Authorization note for the hub itself:** `[Authorize]` alone doesn't stop a user from
calling `JoinTicket` with someone else's ticket ID and eavesdropping. Add the same ownership
check inside `JoinTicket` before adding to the group (resolve `IServiceScopeFactory` in the
hub, or accept the current design tradeoff and document it — check how `ScheduleHub`'s
`JoinStudio` currently handles this, since if it has the same gap, matching its existing
precedent is a legitimate scoped decision rather than a new hole; if `ScheduleHub` does
validate studio membership before joining, `SupportHub` must do the equivalent ticket-
ownership validation, not skip it).

### Frontend

New components in `features/help/components/`:

- **`SupportRequestForm.tsx`** — subject + message form, reuses `useSubmitFeedbackMutation`
  from `features/feedback` (not duplicated) with `type: "SupportRequest"`. Shown in the
  Help menu's new "Contact Support" tab (a third tab alongside Guides/FAQ from Part A) when
  `useGetMyFeedbackReportsQuery({ type: "SupportRequest" })` returns no open ticket.
- **`SupportTicketThread.tsx`** — shown instead of the form when an open `SupportRequest`
  ticket exists for the current user. Lists `FeedbackMessage[]` (oldest first), a reply
  box, and the ticket's current `Status` badge. Subscribes to the new hub via a dedicated
  hook (below). `canReply` is always true for the ticket owner and for issuer.
- **`useSupportHub.ts`** (new hook, `shared/hooks/` or co-located in `features/help/`) —
  mirrors `useSignalR.ts`'s connection-building pattern exactly (same dev/prod `hubBase`
  branch, same `withAutomaticReconnect()`, same **block-bodied** handler requirement — see
  the comment already in `useSignalR.ts` explaining why arrow functions with implicit
  returns break SignalR's client; copy that same discipline here) but joins a **ticket**
  group instead of a **studio** group, and only connects while `SupportTicketThread` is
  mounted (unlike `useSignalR`, which is always-on for the whole layout):
```typescript
export function useSupportHub(feedbackReportId: string | null) {
  const token = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (!feedbackReportId || !token) return;
    const hubBase = import.meta.env.DEV ? "http://localhost:5078" : "";
    const conn = new HubConnectionBuilder()
      .withUrl(`${hubBase}/hubs/support`, { accessTokenFactory: () => token! })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    conn.on("SupportMessageReceived", () => {
      dispatch(feedbackApi.util.invalidateTags(["FeedbackMessage"]));
    });

    const start = conn.start()
      .then(() => conn.invoke("JoinTicket", feedbackReportId))
      .catch(() => {});

    return () => { void start.finally(() => conn.stop()); };
  }, [feedbackReportId, token, dispatch]);
}
```

Extend `feedbackApi.ts` (existing file, not a new one — this is the same feature) with:
`getMyFeedbackReports`, `getFeedbackMessages`, `postFeedbackMessage` (add `"FeedbackMessage"`
to `tagTypes`, `postFeedbackMessage` invalidates it, `getFeedbackMessages` provides it).

**Issuer side:** `FeedbackInboxPage.tsx` gains a detail/thread view when a report row is
clicked (currently — check whether it already has a detail expansion or is list-only; if
list-only, add one) — reuse `SupportTicketThread` there too, passed `canReply={true}`
unconditionally since issuer can always reply, and keep the existing status-change dropdown
alongside it.

### Tests

Backend: ownership-check handler tests (issuer allowed cross-tenant, owning client/artist/
owner allowed, a different tenant's user gets 403/404), reopen-on-reply behavior, the new
`SupportRequest`-only-for-clients validator rule (client submitting `BugReport` → validation
error; client submitting `SupportRequest` → success; artist submitting any type → success).

Frontend: `SupportRequestForm` submit flow, `SupportTicketThread` renders messages + reply,
`useSupportHub` joins/leaves on mount/unmount (mock `HubConnectionBuilder` the same way
existing `useSignalR` tests do — check `shared/hooks/__tests__/` or wherever those live).

### Follow-up explicitly out of scope for this pass
Email notification when a reply arrives and the recipient is offline (would need a MailKit
template + Hangfire job checking hub-connection presence, or simpler: always email on issuer
reply, never on studio-side reply since issuer is presumably watching the inbox actively).
Note this as a TODO in the architecture.md entry rather than building it now.

---

## Part D — First-Run Product Tour (contextual coachmarks)

### What this is

A short, skippable, per-role guided walkthrough on first login, highlighting the 4–6 most
important actions for that role, replayable anytime from the Help menu. This is the
Intro.js/Shepherd.js/driver.js pattern — but built by hand here rather than adding a new
npm dependency, matching this codebase's consistent preference (masonry via CSS columns
"no package," lightbox via shadcn Dialog "no extra package," QR via `QRCoder` only after
being explicitly logged as an approved exception in the Decisions Log). If the engineer
implementing this believes a library is genuinely warranted, that's a legitimate call to
make — but it must be logged as a new Decisions Log entry with justification, the same way
`QRCoder` was, not added silently.

### Frontend engine (no backend needed for the mechanics themselves)

`frontend/src/shared/components/OnboardingTour.tsx` — generic, reusable:
```typescript
interface TourStep {
  targetSelector: string;   // CSS selector, e.g. '[data-tour="owner-add-artist"]'
  title: string;
  body: string;
  placement?: "top" | "bottom" | "left" | "right"; // default "bottom"
}
interface OnboardingTourProps {
  steps: TourStep[];
  onComplete: () => void;
  onSkip: () => void;
}
```
Mechanics: find the target element via `document.querySelector`, read its
`getBoundingClientRect()`, render a fixed-position backdrop with a CSS `box-shadow` spotlight
cutout around that rect (a `div` sized to the rect with a huge `box-shadow: 0 0 0 9999px
rgba(0,0,0,.6)` is the standard trick — no canvas, no SVG mask needed), and a popover
positioned adjacent per `placement`. Recompute position on `window resize`/`scroll` via a
`ResizeObserver` + scroll listener while a step is active. Next/Back/Skip buttons; Escape key
= Skip. If `targetSelector` doesn't resolve to an element (route not matched, conditional
render), skip that step automatically rather than showing a spotlight on nothing.

Add `data-tour="..."` attributes to the specific existing elements each tour points at — this
is a small, additive change to already-existing JSX in each layout/page, not a rewrite. List
the exact attributes to add per role in the per-role tour files below; the engineer adds each
attribute to the real element while reading that component (do not guess DOM structure from
this spec alone — open each target file first).

### Per-role tour content

`frontend/src/features/help/tours/`:

- **`clientTour.ts`** (4 steps) — `client-book-nav` (Book nav item) → `client-my-studios-nav`
  (My Studios, only if the tour detects the user belongs to >1 studio — otherwise skip this
  step) → `client-designs-nav` → `client-help-button` (the Help icon itself, closing the loop).
- **`artistTour.ts`** (5 steps) — `artist-schedule-nav` → `artist-clients-nav` →
  `artist-create-design-button` (on the Designs page, only relevant there — this tour may
  need to navigate the user between routes as steps advance, which `OnboardingTour` must
  support: a step can optionally include a `route` field that triggers `navigate()` before
  rendering that step) → `artist-notifications-bell` → `artist-help-button`.
- **`ownerTour.ts`** (6 steps) — `owner-dashboard-nav` → `owner-add-artist-nav` →
  `owner-deposit-rules-nav` → `owner-studio-profile-nav` → `owner-billing-nav` →
  `owner-help-button`.
- **`issuerTour.ts`** (5 steps) — `issuer-dashboard-nav` → `issuer-studios-nav` →
  `issuer-plans-nav` → `issuer-subscriptions-nav` → `issuer-help-button`.

Add a `route` field to `TourStep` for cross-page tours (owner/artist tours span more than one
screen): `{ targetSelector, title, body, route?: string }` — if `route` is set and differs
from the current location, `OnboardingTour` navigates there before positioning the spotlight,
with a short delay (`requestAnimationFrame` twice, or wait for the target element to appear
via a brief polling loop capped at ~1s) to let the new page render before measuring the rect.

### Persistence (this is the only part of Part D that touches the backend)

New entity — `Pena_e_Arte.Domain/Entities/UserOnboardingState.cs`:
```csharp
public class UserOnboardingState
{
    public Guid     Id             { get; private set; } = Guid.NewGuid();
    public Guid     UserId         { get; private set; }
    public string   Role           { get; private set; } = string.Empty;
    public bool     HasCompletedTour { get; private set; }
    public DateTime? CompletedAt   { get; private set; }

    private UserOnboardingState() { }

    public static UserOnboardingState Create(Guid userId, string role) =>
        new() { UserId = userId, Role = role };

    public void MarkComplete() { HasCompletedTour = true; CompletedAt = DateTime.UtcNow; }
}
```
**Not tenant-scoped** — modeled the same way as `SavedPortfolioImage` (Feature Module Map
row #21: "Per-user, cross-tenant"), since tour-completion state belongs to the person, not
the studio, and a client who belongs to multiple studios shouldn't see the tour again just
because they're viewing a different tenant. Unique constraint on `(UserId, Role)`.

Migration: `AddUserOnboardingState`.

Query/Command:
```csharp
public record GetOnboardingTourStatusQuery(string Role) : IRequest<OnboardingTourStatusResponse>;
public record MarkOnboardingTourCompleteCommand(string Role) : IRequest;
```
Both read `UserId` from `ICurrentUser`; no tenant concept involved at all, so no
`IgnoreQueryFilters()` needed (there's no filter to bypass — same non-tenant-entity shape as
`FeedbackReport`/`SavedPortfolioImage`). Upsert semantics in the command handler (create if
no row exists for `(UserId, Role)`, else mark complete on the existing row).

Endpoints, new file or added to `HelpEndpoints.cs`:
```
GET  /api/v1/onboarding/tour-status?role=owner   ClientAndAbove
POST /api/v1/onboarding/tour-complete            ClientAndAbove   { role: string }
```
`role` in the query/body must match the caller's actual current role (validate this in the
handler — a client should not be able to mark the owner tour complete for themselves).

Frontend: `features/help/onboardingApi.ts` (new slice, add to `store.ts`), a
`useOnboardingTour(role)` hook that:
1. Fetches tour status on mount (one query, cached — `RTK Query`, not `useEffect` for the
   fetch itself, only for the *launch* side-effect once data resolves).
2. If `!hasCompletedTour`, renders `<OnboardingTour>` with that role's step list.
3. On complete or skip, calls the complete-mutation (skip counts as complete — don't nag
   again after an explicit skip, that's the universal convention for these tours).

Wire `useOnboardingTour` into each of the four layouts, and add a "Take the tour again"
button at the top of the Help menu's Guides tab (from Part A) that calls the same
`<OnboardingTour>` render path with `force=true`, bypassing the completed-check.

### Tests
Backend: upsert command tests (create-if-missing, idempotent-if-already-complete, role
mismatch rejected). Frontend: `OnboardingTour` positions correctly against a mocked
`getBoundingClientRect`, skips a step whose selector doesn't resolve, Escape key skips,
multi-route tour navigates between steps, `useOnboardingTour` doesn't re-launch after
completion, "Take the tour again" bypasses the completed-check.

---

## Suggested Delivery Order

```
1. Part A  (frontend-only, ships alone, immediately useful)
2. Part B  (adds one small entity + one issuer report — low risk, no RBAC surface change)
3. Part D  (adds one small entity, purely additive UX, no change to existing endpoints)
4. Part C  (the biggest one — new hub, new authorization rule change on an existing
            endpoint, resource-ownership checks — do this last and review the
            SubmitFeedback policy change especially carefully, since it changes who can
            call an existing production endpoint)
```

---

## Master Exit Condition

After each part, append its own dated subsection to `docs/claude/architecture.md` (Feature
Module Map row + prose block), following the exact style already used for "Feedback / Bug
Report Feature — 2026-07-02" and "Redis-Backed Distributed Rate Limiting — 2026-07-02" —
problem/solution/key decisions/files changed. Do not batch all four parts into one entry;
each part is a separate dated change and should be traceable independently, since they will
likely land as separate PRs on separate days.
