# Overnight Prompt — Live Traffic Page: Reliability Fix + UX Polish (Issuer-Only)

> Feed this file directly to Claude Code as the task prompt, in the main
> **"Pena e Artë - Engineering"** project (the one with repo write access —
> this file was produced in the separate, read-only "Engineering Consultation"
> project and cannot touch source itself). It is self-contained: exact files,
> exact current code, exact target code, exact tests, exact docs to sync. Read
> the whole file before writing anything. Mode: fully autonomous, no user
> present.

**Date logged:** 2026-08-05
**Requested by:** Phi
**Origin:** An external UI/UX audit of `/platform/traffic` (the Live Traffic
page, shipped 2026-08-04 per `docs/claude/overnight-prompt-live-traffic-analytics-2026-08-03.md`
and the matching `architecture.md` Decisions Log entry, "Live Site Traffic
Analytics — 2026-08-04") was run against a screenshot of the page and pasted
into this consultation project. Every finding in that audit was verified
against the live source before this prompt was written — some confirmed
exactly, some refuted with evidence, and one confirmed finding was root-caused
down to the exact lines responsible, which the original audit could not do
from a screenshot alone. See §2 and §9 for the full finding-by-finding
disposition.

**Before starting, run:**
```bash
git add -A && git commit -m "checkpoint: before live-traffic-ux-fix overnight prompt" --allow-empty
git checkout -b fix/live-traffic-ux
```

---

## 1. Goal

Fix one confirmed reliability bug and a set of confirmed UX gaps on the
already-shipped `/platform/traffic` (Live Traffic) page, without touching the
backend traffic pipeline (Redis presence, `TrafficHub`, `TrafficBroadcastService`,
`TrafficRollupJob`, GeoIP/UA parsing) built in the 2026-08-03/04 pass — that
pipeline is working as designed and is out of scope tonight. This is a
frontend reliability-and-polish pass, plus one small, targeted backend
validation gap found during verification (§5.7).

Applicable `CLAUDE.md` rules: #6 (industry benchmark — §8), #7 (Help sync —
§7, woven into each change below, not an appendix).

---

## 2. Audit disposition — what's confirmed, what's refuted, what's new

The audit's screenshot only showed the KPI row, "Who's here right now," the
trend chart, and three of the page's **four** breakdown cards. It never
mentions the **live visitor map** (`LiveVisitorMap`, Leaflet-based, ships
today) or the **"Top networks" (ISP) breakdown card** — both are real, shipped,
already documented in `helpContent.ts`'s `"issuer-live-traffic"` article and
`frontend/public/user-manual/index.html`. Most likely the screenshot was
cropped or taken before the page fully loaded past the fold. This prompt's
scope corrects for that: every fix below that applies to "a breakdown card"
applies to all four, and the map gets the same card-boundary and loading/error
treatment as its siblings.

**Confirmed, root-caused, fixed tonight:**
1. The "blank/broken box" on the three-now-four breakdown cards (§1/§3/§8 of
   the audit). **Root cause found, not guessed:** `LiveTrafficPage.tsx` never
   destructures `isError` from `useGetTrafficHistoryQuery` or
   `useGetTrafficBreakdownQuery` (lines 208–209 — only `isLoading` is read).
   On a failed request, RTK Query sets `isLoading` to `false` and leaves
   `data` `undefined` forever. Every render branch in the file is
   `xLoading || !x ? <skeleton> : ...` (lines 289, 305, 322, 338, 352) — so
   `!data` being permanently `true` after an error means the **loading
   skeleton renders forever**, indistinguishable from "still loading," with
   no error text, no icon, nothing. This is exactly what a static screenshot
   of a failed request looks like, and it matches the audit's own hypothesis
   ("stuck loading skeleton that never resolved") precisely. Fix: §5.1.
2. No live/connection-status indicator anywhere on the page (audit's Top
   Critical Issue #2). Confirmed — `useLiveTrafficHub` (`shared/hooks/useLiveTrafficHub.ts`)
   exposes no connection state at all; nothing in `LiveTrafficPage.tsx`
   reflects it. Fix: §5.2.
3. "Who's here right now" has no `Card` wrapper while its siblings do (audit
   §1). Confirmed — and the same is true of the **live visitor map** section,
   which the audit didn't see. Fix: §5.3.
4. Segmented control (Guests/Clients/Artists/Owners trend toggle) has no
   visible affordance on inactive options until hover (audit §4). Confirmed
   exactly: `LiveTrafficPage.tsx` lines 276–280 give the inactive state only
   `text-muted-foreground hover:text-foreground hover:bg-muted` — no
   resting-state background or border. Fix: §5.4.
5. Trend chart has no "insufficient data" guard (audit §8). Confirmed —
   `LineAreaChart.tsx` has no branch for `n < 2`; with one data point it plots
   a single dot at center with a degenerate area path, which is exactly what
   the audit's screenshot shows (and explains why only one x-axis label,
   "08-04," appeared — the feature shipped 2026-08-04, so as of the
   2026-08-05 screenshot there was at most one day of aggregated history).
   Fix: §5.5.
6. No date-range control (audit §3). Confirmed — `{ days: 30 }` is hardcoded
   at the two call sites (`LiveTrafficPage.tsx` lines 208–209). Fix: §5.6.
7. **New finding, not in the original audit:** neither `GetTrafficHistoryQuery`
   nor `GetTrafficBreakdownQuery` clamps its `Days` parameter server-side
   (verified by reading both handlers in full) — unlike the sibling
   `GetMrrHistoryQuery` (`Math.Clamp(query.Months, 1, 24)`) and
   `GetAuditLogQuery`/`GetMyStudioAuditLogQuery` (`Math.Clamp(query.PageSize, 1, 100)`).
   The original 2026-08-03 prompt's §6.6 called for a "clamp 1–90" but it was
   never implemented. This becomes a real (low-severity) issue the moment
   §5.6 adds a frontend control that sends an explicit `days` value. Fix:
   §5.7.

**Investigated and refuted — no code change, stated here so the finding isn't
silently dropped:**
8. *A11y — text contrast.* The audit correctly hedged ("can't extract exact
   hex values from a screenshot... should be measured directly"). Measured
   directly from `frontend/src/index.css`'s `@theme` tokens: dark-mode
   `--color-muted-foreground` (`hsl(240 5% 64.9%)`) on `--color-background`
   (`hsl(240 10% 3.9%)`) computes to a **7.76:1** contrast ratio — exceeds
   even WCAG AAA (7:1), let alone AA (4.5:1). Light mode computes to **4.83:1**
   — passes AA with a small margin. Not a WCAG risk as coded. (The light-mode
   margin is thin enough that it's worth keeping in mind before any future
   token tweak, but nothing to fix today.)
9. *A11y — icon aria-labels.* The audit correctly flagged this as
   "needs verification, not confirmed failing." Verified: `HelpMenu.tsx` line
   100 has `aria-label="Open help menu"`; `NotificationBell.tsx` line 42 has
   `aria-label={unreadCount > 0 ? `View notifications, ${unreadCount} unread` : "View notifications"}`.
   Both already pass.
10. *Notification bell vs. Feedback nav badge "inconsistency."* Not a bug.
    `NotificationBell` shows a badge only when the signed-in issuer's own
    `unreadCount > 0`; the "Feedback" nav badge shows the platform-wide count
    of open feedback reports (`useGetFeedbackReportsQuery({ status: "Open" })`,
    `IssuerLayout.tsx` line 27). They are two different, correctly-wired
    counters that happened to both be visible in one screenshot — not a
    shared pattern that's out of sync with itself.

**Confirmed but deliberately not built tonight — flagged, see §3:**
11. Massive unused horizontal space / `max-w-4xl` cap (audit's Top Critical
    Issue #3).
12. Card-header pattern inconsistency (audit §6, longer-term rec #1).
13. No export / no manual refresh (audit §3).
14. Two similarly-styled "help" entry points (audit §4).

---

## 3. Decisions you must make explicit note of / flag, not silently assume

### 3.1 Container width (`max-w-4xl`) — NOT a one-off bug, flagged as an app-wide design-system question

The audit frames the empty right-hand space as an unfinished, one-off
mistake on this page. Verified this is false: `max-w-4xl` is the
**established convention across nearly every Issuer platform-admin page** —
`AuditLogPage.tsx` (`max-w-4xl`), `PlanManagementPage.tsx` (`max-w-4xl`),
`IssuerDashboardPage.tsx`/`IssuerStudioDetailPage.tsx`/`PlanEditPage.tsx`
(`max-w-3xl`), `IssuerStudioListPage.tsx`/`SubscriptionOversightPage.tsx`
(`max-w-5xl`), `HelpInsightsPage.tsx`/`IndustryReportsPage.tsx` (`max-w-2xl`).
Widening only `LiveTrafficPage` would make it the one inconsistent page in
the section, not the one fixed page in an otherwise-inconsistent section.

Wide-screen data density genuinely is a standard expectation for analytics
dashboards specifically (see §8), so the audit's underlying instinct is
reasonable — but whether to widen **the entire Issuer admin section** (a
design-system-level decision touching ~8 files) is Phi's call, not an
engineering one to make unilaterally inside a single-page bug-fix prompt.
**Not built tonight.** If Phi wants this, it should be its own overnight
prompt scoped to `frontend/src/features/platform/components/*.tsx` as a set,
with one new shared width token, not a one-off change here.

### 3.2 Card-header pattern unification — flagged, not built tonight

Three header treatments currently coexist on this one page: `KpiCard`
(icon + label + big number, no `CardTitle`), `Card`/`CardHeader`/`CardTitle`
(Traffic trend, all four breakdown cards), and a bare `<p className="text-sm font-medium">`
label with no card at all (fixed by §5.3, but still a third distinct pattern
even after that fix — a labeled `Card` with an icon-less `CardTitle` isn't
the same visual language as a `KpiCard`). Unifying this is a real
recommendation worth doing, but it's a component-library-level decision that
likely touches other issuer pages reusing `KpiCard` today
(`IssuerDashboardPage.tsx` per the 2026-08-04 Decisions Log entry) — out of
scope for a single-page fix prompt. **Flagged as a follow-up backlog item,
not built tonight; no spec sketch needed since §3.1 already covers the
sibling design-system question and the two should likely be scoped together.**

### 3.3 Export — flagged, not built tonight (do-not-build-blind)

No export/download exists for the trend or breakdown data. This is a real
gap against the benchmark (§8) but needs a product decision this prompt
doesn't make: CSV via a new backend endpoint, or client-side export of
already-fetched JSON? Given the underlying data is aggregate-only (no PII —
see the 2026-08-03 prompt's §3.2 privacy design), a client-side CSV export of
the already-loaded `history`/`breakdown` RTK Query cache data (no new
endpoint, no new backend surface) is the cheapest correct answer and is
recommended for a fast-follow — but is not decided or built here.

### 3.4 "Help Insights" naming ambiguity — flagged, partly Marketing's call

The audit is right that "?" (`HelpMenu`) and "Help Insights" (`IssuerLayout.tsx`
nav item, actually a help-search-analytics page, not user-facing help) sit
close together and could read as the same thing to a new admin. The
**structural** fact — two entry points that could be confused — is in scope
for this project to name. The **exact replacement wording** (e.g. "Search
Insights," "Help Analytics") is UX-copy/brand-voice polish, which per this
project's own scope rules belongs to Marketing's `ux-copy`-style work, not
decided here. **Not renamed tonight; flagged for Phi to route to Marketing or
approve a specific replacement string.**

---

## 4. Scope boundary — do not touch

- Any backend traffic-pipeline file from the 2026-08-03/04 build: `TrafficHub.cs`,
  `TrafficBroadcastService.cs`, `TrafficRollupJob.cs`, `GeoIpService.cs`,
  `UserAgentParserService.cs`, `RecordTrafficEventCommand.cs`,
  `PublicEndpoints.cs`'s beacon handler, `useTrafficBeacon.ts`. All working as
  designed; this is a read-side UX/reliability pass only.
- `TrafficEvent`/`TrafficDailyAggregate` entities, their configurations, and
  any migration — no schema change is needed for anything in this prompt.
- `GetLiveTrafficSnapshotQuery` and the live-presence Redis path — the
  snapshot query/hook already handles its error state correctly
  (`snapshotError`, `LiveTrafficPage.tsx` lines 226–230); it is not part of
  the bug in §2 finding 1.
- `docs/user-manual.html` (repo root, 1,700 lines) — confirmed stale/legacy
  again during this pass (`diff` against `frontend/public/user-manual/index.html`
  shows they materially diverge; the latter is 3,149 lines and contains the
  live, current `issuer-live-traffic` section). Do not edit it.
- Other Issuer pages' `max-w-*` containers, `KpiCard.tsx`/`KpiSkeleton`
  itself, and any card-header pattern elsewhere — see §3.1/§3.2, explicitly
  not touched tonight.
- `NotificationBell.tsx` / `IssuerLayout.tsx`'s Feedback badge — investigated,
  not a bug (§2 finding 10), no change.
- Any Stripe/billing file — unrelated.

---

## 5. Frontend + backend changes — exact files, current code, target behavior

### 5.1 Fix the eternal-skeleton-on-error bug (highest priority — this is the audit's #1 issue)

**File:** `frontend/src/features/platform/components/LiveTrafficPage.tsx`

Current (lines 206–209):
```tsx
const { data: snapshot, isLoading: snapshotLoading, isError: snapshotError } =
  useGetLiveTrafficSnapshotQuery();
const { data: history, isLoading: historyLoading } = useGetTrafficHistoryQuery({ days: 30 });
const { data: breakdown, isLoading: breakdownLoading } = useGetTrafficBreakdownQuery({ days: 30 });
```

Target: destructure `isError` for both, matching the existing `snapshotError`
naming convention:
```tsx
const { data: history, isLoading: historyLoading, isError: historyError } =
  useGetTrafficHistoryQuery({ days });
const { data: breakdown, isLoading: breakdownLoading, isError: breakdownError } =
  useGetTrafficBreakdownQuery({ days });
```
(`days` here is the new state from §5.6 — if §5.6 is implemented first in the
same pass, wire this against that state variable; if done standalone, keep
`{ days: 30 }` and just add the `isError` destructuring.)

Every `xLoading || !x ? <skeleton> : ...` branch in the render (trend chart,
all four breakdown cards) must become a three-way branch: loading → skeleton;
error → a real error state (see below); success-with-data → the existing
content. Add a small shared inline error state matching `snapshotError`'s
existing copy/style (lines 226–230, `text-center text-sm text-destructive`,
`role="alert"`), scoped per-card so one failed request doesn't blank the
whole page — e.g. for the trend chart:
```tsx
{historyLoading ? (
  <div className="h-[130px] rounded bg-muted animate-pulse" />
) : historyError ? (
  <p className="h-[130px] flex items-center justify-center text-xs text-destructive" role="alert">
    Couldn't load traffic trend — try refreshing.
  </p>
) : !history || history.dataPoints.length === 0 ? (
  <p className="h-[130px] flex items-center justify-center text-xs text-muted-foreground">
    No traffic data yet.
  </p>
) : (
  <TrendChart data={history.dataPoints} series={series} />
)}
```
Apply the same three-way pattern to all four breakdown `CardContent` blocks
(lines 303–361), each with its own `role="alert"` error line matching its
existing `emptyLabel` copy style (e.g. "Couldn't load country data — try
refreshing.").

**Also fix the map/table sections' contradictory state:** today, if
`snapshotError` is true, the top-of-page error banner renders (line 227) but
the map (line 248–252) and "Who's here right now" table (line 257–263) still
render their `animate-pulse` skeletons forever underneath it, because both
check only `snapshotLoading || !snapshot` with no error branch — producing an
error message sitting directly above two sections that look like they're
still loading. Target: both sections should render nothing (or a smaller
inline "—" placeholder) when `snapshotError` is true, since the top banner
already communicates the failure; do not duplicate the error message three
times on one page.

### 5.2 New shared primitive: live connection status

**File:** `frontend/src/shared/hooks/useLiveTrafficHub.ts` — current hook
takes `enabled: boolean` and returns nothing. Target: return a connection
state so the page can render it.

```ts
export type LiveConnectionState = "connecting" | "connected" | "reconnecting" | "disconnected";

export function useLiveTrafficHub(enabled: boolean): {
  connectionState: LiveConnectionState;
  lastUpdatedAt: number | null; // Date.now() at last TrafficSnapshotUpdated receipt
} {
  const [connectionState, setConnectionState] = useState<LiveConnectionState>("connecting");
  const [lastUpdatedAt, setLastUpdatedAt] = useState<number | null>(null);
  // ...existing token/dispatch setup unchanged...

  useEffect(() => {
    if (!enabled || !token) { setConnectionState("disconnected"); return; }
    const connection = new HubConnectionBuilder()
      /* ...unchanged... */
      .build();

    connection.onreconnecting(() => setConnectionState("reconnecting"));
    connection.onreconnected(() => setConnectionState("connected"));
    connection.onclose(() => setConnectionState("disconnected"));

    connection.on("TrafficSnapshotUpdated", (payload: LiveTrafficSnapshotResponse) => {
      dispatch(platformApi.util.updateQueryData("getLiveTrafficSnapshot", undefined, () => payload));
      setLastUpdatedAt(Date.now());
    });

    connection.start()
      .then(() => setConnectionState("connected"))
      .catch(() => setConnectionState("disconnected"));

    return () => { void connection.stop(); };
  }, [enabled, token, dispatch]);

  return { connectionState, lastUpdatedAt };
}
```
Keep the existing "no per-client `JoinX` call needed" comment (this hub's
single-group auto-join behavior is unchanged) but move it to sit above the
`useEffect`, still intact.

**New file:** `frontend/src/shared/components/LiveStatusBadge.tsx` (NEW,
shared primitive — deliberately generic so any other real-time page can adopt
it later, e.g. anything built on `useSupportHub`/`useScheduleHub`, per the
audit's own longer-term rec #3 — but this pass only wires it into
`LiveTrafficPage`, does not retrofit other pages):
```tsx
import { useEffect, useState } from "react";
import type { LiveConnectionState } from "@/shared/hooks/useLiveTrafficHub";

const STATE_COPY: Record<LiveConnectionState, { label: string; dot: string }> = {
  connected:    { label: "Live",         dot: "bg-emerald-500" },
  connecting:   { label: "Connecting…",  dot: "bg-muted-foreground" },
  reconnecting: { label: "Reconnecting…", dot: "bg-amber-500" },
  disconnected: { label: "Offline",      dot: "bg-red-500" },
};

function useRelativeSeconds(ts: number | null): string | null {
  const [, force] = useState(0);
  useEffect(() => {
    if (ts === null) return;
    const id = setInterval(() => force((n) => n + 1), 1000);
    return () => clearInterval(id);
  }, [ts]);
  if (ts === null) return null;
  const s = Math.max(0, Math.floor((Date.now() - ts) / 1000));
  return s < 5 ? "just now" : `${s}s ago`;
}

export function LiveStatusBadge({
  connectionState, lastUpdatedAt, onRefresh,
}: { connectionState: LiveConnectionState; lastUpdatedAt: number | null; onRefresh?: () => void }) {
  const copy = STATE_COPY[connectionState];
  const relative = useRelativeSeconds(lastUpdatedAt);

  return (
    <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
      <span className={`h-1.5 w-1.5 rounded-full ${copy.dot} ${connectionState === "connected" ? "animate-pulse" : ""}`}
            aria-hidden="true" />
      <span>{copy.label}</span>
      {relative && <span>· Updated {relative}</span>}
      {onRefresh && (
        <button type="button" onClick={onRefresh} className="ml-1 hover:text-foreground" aria-label="Refresh now">
          ↻
        </button>
      )}
    </div>
  );
}
```
Use the existing `KpiAccent` palette values already established in
`KpiCard.tsx` (`text-emerald-500`/`text-amber-500`/`text-red-500`) for the dot
colors above, so this introduces zero new color tokens.

Wire into `LiveTrafficPage.tsx`'s header (replacing the plain "X active now"
span at lines 218–222 — keep the count badge, add the status badge next to
it):
```tsx
const { connectionState, lastUpdatedAt } = useLiveTrafficHub(true);
// ...
<header className="...">
  <Activity className="h-5 w-5" aria-hidden="true" />
  <span className="font-semibold tracking-tight">Live Traffic</span>
  {snapshot && (
    <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground font-medium">
      {snapshot.totalActive} active now
    </span>
  )}
  <div className="ml-auto">
    <LiveStatusBadge
      connectionState={connectionState}
      lastUpdatedAt={lastUpdatedAt}
      onRefresh={() => { refetchSnapshot(); refetchHistory(); refetchBreakdown(); }}
    />
  </div>
</header>
```
(`refetchSnapshot`/`refetchHistory`/`refetchBreakdown` come from destructuring
the `refetch` function off each RTK Query hook call — standard RTK Query API,
no `useEffect` needed.)

### 5.3 Card boundary for "Who's here right now" and the live map

**File:** same, lines 246–264. Wrap both sections in `Card`/`CardHeader`/`CardTitle`,
matching the Traffic trend card's structure exactly (`pb-2` header, `pt-0`
content):
```tsx
<Card>
  <CardHeader className="pb-2"><CardTitle className="text-sm">Live visitor map</CardTitle></CardHeader>
  <CardContent className="pt-0">
    {snapshotLoading || !snapshot ? (
      <div className="h-[280px] rounded-md bg-muted animate-pulse" />
    ) : snapshotError ? null : (
      <LiveVisitorMap visitors={snapshot.visitors} />
    )}
  </CardContent>
</Card>

<Card>
  <CardHeader className="pb-2"><CardTitle className="text-sm">Who's here right now</CardTitle></CardHeader>
  <CardContent className="pt-0">
    {snapshotLoading || !snapshot ? (
      <div className="space-y-2">{[1, 2, 3].map((i) => <div key={i} className="h-8 rounded bg-muted animate-pulse" />)}</div>
    ) : snapshotError ? null : (
      <LiveVisitorTable visitors={snapshot.visitors} />
    )}
  </CardContent>
</Card>
```
Tighten `LiveVisitorTable`'s empty-state padding while touching this file —
change `py-12` to `py-8` (line 129) so the empty state doesn't reproduce the
"two large voids stacked back to back" the audit flagged (§2 of the audit),
now that it's inside a bordered card rather than floating in open page space.

### 5.4 Segmented-control affordance

**File:** same, lines 271–284. No existing shared toggle/segmented-control
component was found in `frontend/src/shared/components/ui/` that matches this
pattern (`toggle-switch.tsx` is a boolean on/off switch, not a multi-option
group) — this is a one-off inline pattern, fix it in place rather than
inventing a new shared component for a 4-option toggle used in exactly one
place today:

Current inactive-state classes (line 279):
```tsx
: "text-muted-foreground hover:text-foreground hover:bg-muted"
```
Target — give the resting state a visible background so it reads as
clickable before hover:
```tsx
: "bg-muted/60 text-muted-foreground hover:text-foreground hover:bg-muted"
```

### 5.5 Trend chart insufficient-data guard

**File:** `frontend/src/shared/components/charts/LineAreaChart.tsx`. This
component is also used by other pages per its own doc comment ("a third
feature... after MrrChart.tsx and RevenueTrendChart.tsx") — do **not** bake a
Live-Traffic-specific message into the shared component itself; add the
guard at the call site instead, so `MrrChart`/`RevenueTrendChart`'s own
(different, richer) variants are unaffected.

**File:** `frontend/src/features/platform/components/LiveTrafficPage.tsx`,
`TrendChart` wrapper (lines 165–175). Add a minimum-points guard before
rendering `LineAreaChart`:
```tsx
function TrendChart({ data, series }: { data: TrafficHistoryDataPoint[]; series: TrendSeriesKey }) {
  if (data.length < 2) {
    return (
      <p className="h-[130px] flex items-center justify-center text-center text-xs text-muted-foreground px-4">
        Not enough data yet — check back after a few days of traffic.
      </p>
    );
  }
  const labelEvery = Math.ceil(data.length / 8 || 1);
  return (
    <LineAreaChart
      data={data}
      valueOf={(d) => d[series]}
      labelOf={(d, i, total) => (i % labelEvery === 0 || i === total - 1 ? d.date.slice(5) : null)}
      ariaLabel="Traffic trend"
    />
  );
}
```

### 5.6 Date-range control (7 / 30 / 90 days)

**File:** same. Add state and a small control next to the existing series
toggle in the `CardHeader` (lines 267–286):
```tsx
const [days, setDays] = useState<7 | 30 | 90>(30);
// ...
<div className="flex items-center gap-1">
  {[7, 30, 90].map((d) => (
    <button key={d} type="button" onClick={() => setDays(d as 7 | 30 | 90)}
      className={`text-[11px] px-2 py-0.5 rounded transition-colors ${
        days === d ? "bg-primary text-primary-foreground" : "bg-muted/60 text-muted-foreground hover:text-foreground hover:bg-muted"
      }`}>
      {d}d
    </button>
  ))}
</div>
```
Update the `CardTitle` from the hardcoded `"Traffic trend (30 days)"` to
`` `Traffic trend (${days} days)` ``. Wire `days` into both
`useGetTrafficHistoryQuery({ days })` and `useGetTrafficBreakdownQuery({ days })`
(replacing the two hardcoded `{ days: 30 }` calls) so the same control governs
both the chart and all four breakdown cards — matches the audit's own framing
of "scope visible" (date range) applying to the whole page, not just the
chart.

### 5.7 Backend: clamp `Days` on both query handlers (small, targeted)

Found during verification (§2, finding 7), not in the original audit.

**File:** `Pena_e_Arte.Application/Platform/Queries/GetTrafficHistoryQuery.cs`,
line 21. Current:
```csharp
DateOnly since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-query.Days));
```
Target — clamp using the exact same pattern as the sibling
`GetMrrHistoryQuery.cs` (`Math.Clamp(query.Months, 1, 24)`):
```csharp
int days = Math.Clamp(query.Days, 1, 90);
DateOnly since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
```
(Use the clamped `days` in the response's `Days` field too, line 40, so the
response accurately reflects what was actually queried.)

**File:** `Pena_e_Arte.Application/Platform/Queries/GetTrafficBreakdownQuery.cs`,
lines 26–27. Same fix:
```csharp
int days = Math.Clamp(query.Days, 1, 90);
DateOnly sinceDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-days));
DateTime sinceTimestamp = DateTime.UtcNow.AddDays(-days);
```
(Use `days` in the response's `Days` field, line 66.)

This does not need a FluentValidation validator — matches the established
`Math.Clamp`-in-handler pattern used by `GetMrrHistoryQuery`/`GetAuditLogQuery`/
`GetMyStudioAuditLogQuery`, none of which use a validator for this either;
introducing one here alone would be inconsistent with the codebase's own
convention for this exact kind of bound.

---

## 6. Test requirements

**Frontend component tests** (`frontend/src/features/platform/__tests__/LiveTrafficPage.test.tsx`,
extend the existing suite — do not duplicate its existing MSW/SignalR-mock
setup, add new `it()` blocks and new `server.use()` overrides alongside it):
- History/breakdown request failure (`HttpResponse.json(..., { status: 500 })`
  for `/traffic/history` and separately for `/traffic/breakdown`) renders the
  new per-card error text (`role="alert"`), not an indefinite skeleton —
  assert with `queryByText`/`findByText` that the error copy appears and the
  `animate-pulse` skeleton div is gone (`queryByTestId` or a class-based query,
  whichever this test file's existing convention uses elsewhere).
- `LiveStatusBadge` renders "Live" once the mocked SignalR `connection.start()`
  resolves (already mocked to resolve in the existing test file's
  `@microsoft/signalr` mock, lines 41–54) — extend that mock's `on`/`start`
  stubs only if needed to trigger `onreconnecting`/`onclose`/`onreconnected`
  callbacks for the reconnect-state tests below.
- Simulating the mock connection's `onreconnecting`/`onreconnected`/`onclose`
  callbacks (call them directly on the mocked connection object returned by
  `build()`) updates the badge to "Reconnecting…"/"Live"/"Offline"
  respectively.
- Date-range buttons (7d/30d/90d) re-trigger `useGetTrafficHistoryQuery`/
  `useGetTrafficBreakdownQuery` with the new `days` value — assert via MSW
  request interception (`http.get(..., ({ request }) => { ... assert URL
  contains days=7 ...})`) that clicking "7d" actually changes the outgoing
  query param, not just local UI state.
- `TrendChart` with a 1-data-point `history` fixture renders "Not enough data
  yet" copy, not `LineAreaChart`.
- Segmented control and date-range buttons both render with the new
  `bg-muted/60` resting-state class on inactive options (a class-presence
  assertion, matching this test file's existing style for other className
  checks if any exist, otherwise a `toHaveClass` assertion is fine — this
  codebase doesn't have a strict convention against it here).
- "Who's here right now" and "Live visitor map" are each inside a rendered
  `Card` now — assert on the new `CardTitle` text presence as a simple proxy
  (`getByText("Live visitor map")` / `getByText("Who's here right now")`)
  rather than asserting DOM structure directly.

**Backend unit tests** (`tests/Pena_e_Arte.UnitTests/`, extend or add
alongside whatever `GetTrafficHistoryHandlerTests`/`GetTrafficBreakdownHandlerTests`
already exist from the 2026-08-03 build — read those files first to match
their existing fixture/mocking shape before adding new cases, do not
reinvent the test setup):
- `Days = 0`, `Days = -5`, and `Days = 500` all clamp to `1`/`1`/`90`
  respectively for both handlers; response's `Days` field reflects the
  clamped value, not the raw input.

---

## 7. Help-sync obligations (per change, not an appendix)

`frontend/public/user-manual/index.html` is the confirmed live/served copy
(§4); `docs/user-manual.html` is stale and must not be touched, per the
2026-08-03 prompt's own do-not-touch list and reconfirmed here.

1. **Live status badge (§5.2) — new user-visible affordance, needs Help
   coverage.**
   - `frontend/src/features/help/helpContent.ts`, the existing
     `"issuer-live-traffic"` article (around line 969): add one tip to the
     existing `tips` array:
     `"A pulsing 'Live' badge in the header confirms the real-time feed is connected — if it says 'Reconnecting…' or 'Offline,' the numbers on screen may be stale; use the ↻ button next to it to refresh manually."`
   - `frontend/public/user-manual/index.html`, the `#issuer-live-traffic`
     section (line 2741 area): add one sentence to the existing tip callout
     (line 2769), matching its existing style, covering the same point as
     above.
   - `frontend/src/features/help/tours/issuerTour.ts` — **no new step
     needed.** The existing "Live traffic" step (targeting
     `[data-tour="issuer-traffic-nav"]`) already introduces the page as a
     whole from the nav; the status badge is a small in-page affordance, not
     a new destination or workflow, so it doesn't warrant its own tour stop —
     stated explicitly per this project's own rule that a "no" verdict must
     be justified, not silently skipped.

2. **Date-range control (§5.6) — new user-visible control, needs Help
   coverage.**
   - `helpContent.ts`, same article: update the existing step
     `"Scroll down for the historical trend chart, top countries, device/browser breakdown, top pages, and top networks (ISP) over the last 30 days."`
     to:
     `"Scroll down for the historical trend chart, top countries, device/browser breakdown, top pages, and top networks (ISP) — use the 7d/30d/90d toggle above the chart to change the time window for all of them at once."`
   - `frontend/public/user-manual/index.html`, same section's step 5 (line
     2767): same wording update.
   - `issuerTour.ts` — **no new step needed**, same reasoning as above: this
     extends an element (the trend chart / breakdown row) the existing tour
     step already generically covers as "historical trend chart... over the
     last 30 days"; update that step's body text to drop the hardcoded "30
     days" phrasing so it doesn't go stale now that the window is
     user-selectable: change `"See who's on the site right now — guests and signed-in users by role, where they're browsing from, and trends over time."`
     (line 12 of `issuerTour.ts`) — this line doesn't mention "30 days" at
     all, so **no change needed there**; only `helpContent.ts`'s more
     detailed step text (which does say "over the last 30 days") needs the
     edit above.

3. **Error states (§5.1) and card-boundary/segmented-control/chart-guard
   polish (§5.3–§5.5) — no Help change needed.** None of these change what
   the page *does* or what a user can accomplish on it; they change how
   loading/failure/insufficient-data states look, and how already-documented
   controls are visually framed. Per CLAUDE.md rule #7's "only a change with
   zero user-visible surface" exception being the sole valid "no" — these
   technically have visible surface (an error message that didn't exist
   before), but the *content* of what's being communicated ("this failed,"
   "not enough data yet") needs no separate Help article; a help system
   explaining error toasts is not this app's convention anywhere else
   (`AuditLogPage`'s own `snapshotError`-style banners aren't documented in
   Help either — verified by checking `helpContent.ts` for any existing
   "if you see an error" language: none exists as a pattern to match).
   Stated explicitly rather than silently skipped.

---

## 8. Industry-standard benchmark note

Per `CLAUDE.md` rule #6's issuer-role clause (general B2B SaaS platform-admin
standard, not the vertical-booking-SaaS set — this page has no tenant-owner
equivalent, per the original 2026-08-03 prompt's §3.4/§9 finding, not
re-litigated here):

- **Connection-status indicators on real-time dashboards are a documented
  2025/2026 best practice, not a nice-to-have**: "when real-time connectivity
  drops, providing fallback data or notifying users with clear status
  indicators rather than leaving charts frozen or blank is essential"
  ([Smashing Magazine, "From Data To Decisions: UX Strategies For Real-Time
  Dashboards," 2025](https://www.smashingmagazine.com/2025/09/ux-strategies-real-time-dashboards/)).
  §5.2 directly addresses this — it was the audit's own Top Critical Issue #2
  and is independently corroborated by current industry writing on the
  category, not just this project's own judgment.
- **Scope visibility (date range) is called out specifically for
  traffic-tracking dashboards**: "make the user's current scope visible
  (Account, date range, segment)... particularly important for dashboards
  that track live traffic and other dynamic data" ([DAR Design, "B2B
  Dashboard Information Architecture in 2026"](https://dardesign.io/blog/b2b-dashboard-information-architecture-2026)).
  §5.6 directly addresses this.
- **A ~30s-or-faster auto-refresh cadence is treated as the modern baseline**
  for this category — this page already exceeds it (5s SignalR push, per the
  2026-08-03 build), so no change needed there; cited only to confirm the
  existing cadence choice remains current, not stale, one day after ship.
- **Export is a named pain point** ("teams spending 2-10 hours per week on
  manual exports" is cited as the #1 2026 industry pain point in B2B
  analytics contexts) — corroborates §3.3's flag that this is a real gap,
  reinforcing why it's named as a fast-follow rather than silently dropped,
  even though it isn't built tonight.

Sources: [Smashing Magazine — UX Strategies For Real-Time Dashboards](https://www.smashingmagazine.com/2025/09/ux-strategies-real-time-dashboards/),
[DAR Design — B2B Dashboard Information Architecture in 2026](https://dardesign.io/blog/b2b-dashboard-information-architecture-2026),
[HockeyStack — Complete Guide to B2B Web Analytics](https://www.hockeystack.com/blog-posts/b2b-web-analytics).

---

## 9. Constraints (restated in full, as required)

- **No new npm/NuGet packages.** Everything in §5 uses existing dependencies
  (React state, RTK Query's own `refetch`, Tailwind, lucide-react icons
  already imported in this file).
- **No `useEffect` for data fetching** — the one `useEffect` touched (§5.2)
  is connection-lifecycle management (SignalR `onreconnecting`/`onclose`
  callbacks), the same established exception class `useLiveTrafficHub`
  already used before this prompt (matches `conventions.md`'s existing
  carve-out, restated from the 2026-08-03 prompt's own Constraints section).
- **TypeScript strict, no `any`** — `LiveConnectionState` is an explicit
  union type; no new response/request shape needs a new interface since §5.6
  reuses the existing `{ days?: number }` query-arg shape already declared in
  `platformApi.ts`.
- **Explicit C# types, no unclear `var`** — §5.7's `int days = Math.Clamp(...)`.
- **No business logic in endpoints** — §5.7's clamp lives in the query
  handler, matching `GetMrrHistoryQuery`'s existing precedent exactly, not in
  `PlatformEndpoints.cs`.
- **Tenant isolation** — unaffected; no query-filter-bearing entity is
  touched by this prompt.
- **Every endpoint has `.RequireAuthorization()`** — unaffected; no new
  endpoint is added by this prompt, both touched handlers are already
  `IssuerOnly`-gated at the existing endpoint level.
- **Never log PII, structured logs only** — unaffected; no logging is added
  or changed by this prompt.
- **Tests ship with every change** — §6.

---

## 10. Final self-check / verification checklist (run before declaring done)

- [ ] `dotnet build` clean, `dotnet test` green (all new + existing suites,
      including the two new `Math.Clamp` handler tests).
- [ ] `pnpm build`, `pnpm test`, `pnpm lint` clean.
- [ ] No file outside §5's list was touched — diff reviewed against §4's
      do-not-touch list, specifically confirming no other Issuer page's
      `max-w-*` container was widened (§3.1) and `docs/user-manual.html` was
      not touched.
- [ ] Manually force a 500 on `/api/v1/platform/traffic/history` and
      `/api/v1/platform/traffic/breakdown` locally (e.g. temporarily via
      browser devtools request blocking) and confirm the page shows the new
      error text instead of an indefinite pulsing skeleton — this is the
      audit's #1 issue, verify it empirically, not just via the unit test.
    - Also confirm this is not just an artifact of the fix seeming to "want"
      to be present — earlier attempted this class of check without result;
      redo cleanly if the first pass was inconclusive.
- [ ] Manually stop/restart the backend while the page is open and confirm
      the `LiveStatusBadge` transitions `Live → Reconnecting… → Live` (or
      `Offline` if the backend stays down) — SignalR reconnect behavior only,
      does not fully validate from unit tests alone.
- [ ] Clicking 7d/30d/90d visibly changes both the trend chart's shape (or
      empty state) and all four breakdown cards' contents, and the `CardTitle`
      text updates to match.
- [ ] `helpContent.ts`, `frontend/public/user-manual/index.html` both updated
      per §7; `issuerTour.ts` confirmed deliberately unchanged with reasoning
      documented in the commit body.
- [ ] For audits/self-review: every checklist row here has a verdict, no
      blanks.

---

## 11. Final deliverable spec

**Code files (edited):** `frontend/src/features/platform/components/LiveTrafficPage.tsx`,
`frontend/src/shared/hooks/useLiveTrafficHub.ts`,
`Pena_e_Arte.Application/Platform/Queries/GetTrafficHistoryQuery.cs`,
`Pena_e_Arte.Application/Platform/Queries/GetTrafficBreakdownQuery.cs`.

**Code files (new):** `frontend/src/shared/components/LiveStatusBadge.tsx`,
plus new test cases in `frontend/src/features/platform/__tests__/LiveTrafficPage.test.tsx`
and the relevant backend unit test file(s) for the two query handlers.

**Docs files (edited):** `frontend/src/features/help/helpContent.ts`,
`frontend/public/user-manual/index.html`.

**Docs files (this consultation project's own follow-up, not tonight's
implementing session's job):** after the implementing session finishes, this
consultation project should review the actual diff and add a short
`architecture.md` Decisions Log entry under the existing "Live Site Traffic
Analytics — 2026-08-04" section (not a new top-level entry — this is a
same-feature follow-up fix, append a dated sub-note) covering: the
eternal-skeleton-on-error root cause, the new `LiveStatusBadge` shared
primitive and that it is deliberately not yet adopted by other real-time
pages, and the `Days` clamp fix.

**Commit message:**
```
fix(platform): live traffic page error states, live-status badge, date range

- Fix eternal-loading-skeleton-on-request-failure bug (root cause of the
  "blank breakdown card" UX issue) — history/breakdown queries now surface
  isError per-card instead of rendering an indefinite skeleton
- Add LiveStatusBadge shared primitive (connected/reconnecting/offline +
  "updated Xs ago" + manual refresh), wired into Live Traffic header
- Card-wrap "Who's here right now" and the live visitor map for visual
  consistency with the trend/breakdown cards
- Segmented control + new date-range toggle get a visible resting-state
  background instead of hover-only affordance
- Trend chart gets a <2-datapoint "not enough data yet" guard
- Clamp Days 1-90 on GetTrafficHistoryQuery/GetTrafficBreakdownQuery,
  matching GetMrrHistoryQuery's existing Math.Clamp precedent
- Help Menu article, standalone user manual updated; onboarding tour step
  deliberately left unchanged (reasoning in PR description)
```
