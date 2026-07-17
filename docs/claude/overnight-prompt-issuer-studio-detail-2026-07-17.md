# Overnight Prompt — Issuer Studio Detail Page: UX Audit + Summary Endpoint
**Date:** 2026-07-17
**Files changed:** 8 (3 new, 5 modified)
**Type:** Full-stack — frontend rewrite + new backend read-only endpoint

---

## Context

`IssuerStudioDetailPage.tsx` was audited and has several UX issues. This prompt fixes
every issue with the required specificity. Read the current source files before writing
a single line of code — the complete current implementations are in the project.

---

## Phase 0 — Required Reading

Read each file below in full before touching any code.

```
frontend/src/features/platform/components/IssuerStudioDetailPage.tsx
frontend/src/features/platform/__tests__/IssuerStudioDetailPage.test.tsx
frontend/src/features/platform/platform.types.ts
frontend/src/features/platform/platformApi.ts
frontend/src/features/studios/studiosApi.ts
Pena_e_Arte.Domain/Entities/Studio.cs
Pena_e_Arte.Application/Persistence/IAppDbContext.cs
Pena_e_Arte.Application/Platform/Queries/GetPlatformSubscriptionsQuery.cs
Pena_e_Arte.API/Endpoints/PlatformEndpoints.cs
Pena_e_Arte.Contracts/Responses/PlatformStatsResponse.cs   ← for record shape reference
docs/claude/conventions.md
docs/claude/architecture.md
```

Key things to confirm during reading:

1. Does `Studio` entity have an `OwnerEmail` or `OwnerId` field? If yes, the summary query
   can join directly. If no, use claim-based lookup (see Phase 2).
2. Does `IAppDbContext` expose a `DbSet<Client>` and `DbSet<Appointment>`? Confirm their
   type names (needed for `CountAsync` in the summary query).
3. Does `IAppDbContext` expose `DbSet<ApplicationUser>` or a way to query Identity users?
   If not, inject `UserManager<ApplicationUser>` in the summary handler.
4. Check `IgnoreQueryFilters` usage numbering in `architecture.md` — the new query must
   add the next sequential comment.

---

## Phase 1 — Frontend: Seven Targeted Fixes

All fixes are in `frontend/src/features/platform/components/IssuerStudioDetailPage.tsx`.
The complete file is provided verbatim at the end of this section. Do not diverge from it.

### Fix 1 — Subscription status field: pill badge instead of plain text

**Current (line ~241):**
```tsx
<div>
  <span className="text-muted-foreground">Subscription status</span>
  <p>{STATUS_LABELS[badgeStatus]}</p>
</div>
```

**Replace with:**
```tsx
<div>
  <span className="text-muted-foreground">Subscription status</span>
  <div className="mt-0.5">
    <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${STATUS_CLASSES[badgeStatus]}`}>
      {STATUS_LABELS[badgeStatus]}
    </span>
  </div>
</div>
```

Rationale: The card header already uses this exact pill pattern for `badgeStatus`. Showing the
same state as plain white text in the field below creates two visual languages for one semantic
state. Reuse the component — one pill, everywhere.

---

### Fix 2 — Grid column balance + conditional fields section

**Current problem:** The data grid has conditional rows (`Renews` in left col, `Trial expiry`
in right col) that render independently, creating uneven column heights.

**New structure:**
- The main grid is always exactly 3 rows × 2 cols: `Slug/City`, `Registered/Platform branding`,
  `Plan/Subscription-status-pill`.
- Conditional fields (`Renews`, `Trial expiry`) move to a separate `border-t pt-3` section
  below the fixed grid, also using a 2-col grid.
- This section only renders when at least one conditional field is present.

```tsx
{/* Fixed 3×2 grid — always rendered */}
<div className="grid grid-cols-2 gap-x-6 gap-y-1.5 text-xs">
  <div>
    <span className="text-muted-foreground">Slug</span>
    <p className="font-mono">{studio.slug}</p>
  </div>
  <div>
    <span className="text-muted-foreground">City</span>
    <p>{studio.city}</p>
  </div>
  <div>
    <span className="text-muted-foreground">Registered</span>
    <p>{fmt(studio.createdAt)}</p>
  </div>
  <div>
    <span className="text-muted-foreground">Platform branding</span>
    <p>{studio.showPlatformBranding ? "Shown" : "Hidden"}</p>
  </div>
  <div>
    <span className="text-muted-foreground">Plan</span>
    <p>
      {subStatus === "Trialing"
        ? "In Trial"
        : subStatus === "NoSubscription"
        ? "None"
        : (sub?.planName ?? "—")}
    </p>
  </div>
  {/* Fix 1 applied here — pill badge */}
  <div>
    <span className="text-muted-foreground">Subscription status</span>
    <div className="mt-0.5">
      <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${STATUS_CLASSES[badgeStatus]}`}>
        {STATUS_LABELS[badgeStatus]}
      </span>
    </div>
  </div>
</div>

{/* Conditional fields — only if at least one is present */}
{(trialDate || (sub?.currentPeriodEnd && sub.status === "Active")) && (
  <div className="border-t pt-3 grid grid-cols-2 gap-x-6 gap-y-1.5 text-xs">
    {sub?.currentPeriodEnd && sub.status === "Active" && (
      <div>
        <span className="text-muted-foreground">Renews</span>
        <p>{fmt(sub.currentPeriodEnd)}</p>
      </div>
    )}
    {/* Empty left col if only trial is showing */}
    {trialDate && !(sub?.currentPeriodEnd && sub.status === "Active") && <div />}
    {trialDate && (
      <div>
        <span className="text-muted-foreground">Trial expiry</span>
        <p className="flex items-center gap-1.5 flex-wrap">
          {fmt(trialDate)}
          {trialExpired && (
            <span className="inline-flex items-center px-1.5 py-0.5 rounded-full text-[10px] font-medium bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300">
              Expired
            </span>
          )}
        </p>
      </div>
    )}
    {/* Empty right col if only renews is showing */}
    {!trialDate && sub?.currentPeriodEnd && sub.status === "Active" && <div />}
  </div>
)}
```

---

### Fix 3 — "View public portfolio" visually separated from data fields

**Current:** The link sits inside `CardContent` with only `mt-2` separating it from the last
data row. It reads as another data pair visually.

**Replace with:**
```tsx
<div className="border-t pt-3">
  <a
    href={`/s/${studio.slug}`}
    target="_blank"
    rel="noopener noreferrer"
    className="inline-flex items-center gap-1.5 text-xs text-primary hover:underline"
  >
    <ExternalLink className="h-3.5 w-3.5" />
    View public portfolio
  </a>
</div>
```

The `border-t` gives a clear visual break. The `text-primary` colour (already present in the
current code) is correct. The icon size is bumped from `h-3` to `h-3.5` to match the text cap.

---

### Fix 4 — "Suspend" → "Suspend Studio" (and "Reactivate" → "Reactivate Studio")

Everywhere the button label reads "Suspend" or "Reactivate" (the initial action buttons, NOT
the confirm-step text which reads "Suspend this studio?"), rename to be specific:

```tsx
{/* Before */}
<><PauseCircle className="h-3.5 w-3.5" /> Suspend</>
<><PlayCircle  className="h-3.5 w-3.5" /> Reactivate</>

{/* After */}
<><PauseCircle className="h-3.5 w-3.5" /> Suspend Studio</>
<><PlayCircle  className="h-3.5 w-3.5" /> Reactivate Studio</>
```

The confirm-panel text "Suspend this studio?" / "Reactivate this studio?" stays unchanged —
it already names the object.

---

### Fix 5 — Consequence copy in destructive confirm panels

#### Suspend confirm panel — add helper text:

```tsx
{confirmPlatform && (
  <div className="flex flex-col gap-1.5 pt-2 border-t">
    <p className="text-xs font-medium text-muted-foreground">
      {confirmPlatform === "suspend" ? "Suspend this studio?" : "Reactivate this studio?"}
    </p>
    {confirmPlatform === "suspend" && (
      <p className="text-xs text-muted-foreground">
        This immediately hides the studio from Discover and blocks all owner and artist logins.
      </p>
    )}
    <div className="flex items-center gap-2 mt-0.5">
      <Button
        size="sm"
        variant={confirmPlatform === "suspend" ? "destructive" : "default"}
        className="h-8 px-3 text-xs"
        disabled={suspending || unsuspending}
        onClick={executePlatform}
      >
        {(suspending || unsuspending)
          ? <Loader2 className="h-3 w-3 animate-spin" />
          : "Confirm"}
      </Button>
      <Button size="sm" variant="ghost" className="h-8 px-3 text-xs"
        onClick={() => setConfirmPlatform(null)}>
        Cancel
      </Button>
    </div>
  </div>
)}
```

#### Cancel subscription confirm panel — add helper text:

```tsx
{confirming && (
  <div className="flex flex-col gap-1.5 pt-2 border-t">
    <p className="text-xs text-destructive font-medium">Cancel subscription permanently?</p>
    <p className="text-xs text-muted-foreground">
      Billing ends immediately. Studio data is retained and the studio can re-subscribe at any time.
    </p>
    <div className="flex items-center gap-2 mt-0.5">
      <Button
        size="sm" variant="destructive" className="h-7 px-2 text-xs"
        disabled={cancelling_} onClick={handleCancel}
      >
        {cancelling_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Confirm"}
      </Button>
      <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
        onClick={() => setConfirming(false)}>Back</Button>
    </div>
  </div>
)}
```

---

### Fix 6 — Touch targets: main action buttons h-8 → h-9

The initial action buttons (Extend trial, Activate, Suspend Studio, Reactivate Studio, Cancel
Subscription) are currently `h-8` (32 px). Change to `h-9` (36 px). The inline confirm-step
buttons (`h-7`, `h-8`) stay at their current size.

---

### Fix 7 — Wider layout with desktop two-column grid

**Current `<main>` className:**
```tsx
<main className="max-w-3xl mx-auto px-4 py-6 space-y-4">
  <Card>...</Card>  {/* Studio Info */}
  <Card>...</Card>  {/* Actions */}
</main>
```

**New `<main>` className and structure:**
```tsx
<main className="max-w-5xl mx-auto px-4 py-6">
  <div className="grid lg:grid-cols-[1fr_288px] gap-4 lg:gap-6 lg:items-start">

    {/* Left column */}
    <div className="space-y-4">
      <Card>...</Card>  {/* Studio Info — with Fixes 1–3 applied */}
      <Card>...</Card>  {/* Studio Overview — new card from Phase 2 */}
    </div>

    {/* Right column */}
    <div>
      <Card>...</Card>  {/* Actions — with Fixes 4–6 applied */}
    </div>

  </div>
</main>
```

On mobile (`< lg`), the grid collapses to a single column: Info, Overview, Actions (in that
order — the DOM order naturally produces this since left column comes first in markup).

---

## Phase 2 — Full-Stack: Studio Overview (Owner + Metrics)

This adds a new read-only `GET /api/v1/platform/studios/{studioId}/summary` endpoint that
returns the studio owner's contact info plus three usage counters.

### 2a — Read these domain files first

Before writing the handler, read:
```
Pena_e_Arte.Domain/Entities/Studio.cs
Pena_e_Arte.Application/Persistence/IAppDbContext.cs
Pena_e_Arte.Domain/Entities/ApplicationUser.cs (or wherever ApplicationUser is defined)
```

**Decision gate:**

- If `Studio` has `OwnerEmail: string` or `OwnerId: string` → use it directly. The
  `RegisterStudioRequest` has `ownerEmail`, so there is a chance the domain entity stores
  it. If so, the handler is trivial.
- If `Studio` does not have owner identity → find the owner via Identity claims:
  query `AspNetUserClaims` (via `UserManager` or `IAppDbContext.Set<IdentityUserClaim<string>>()`)
  for users who have `ClaimType == "tenant_id"` and `ClaimValue == studioId.ToString()`,
  then cross-reference with `AspNetUserRoles` to find the one with role name `"owner"`.

Pick whichever path the domain structure supports. Document the chosen approach in a
one-line code comment in the handler.

### 2b — Contract: `IssuerStudioSummaryResponse.cs`

Create `Pena_e_Arte.Contracts/Responses/IssuerStudioSummaryResponse.cs`:

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record IssuerStudioSummaryResponse(
    string OwnerEmail,
    string OwnerDisplayName,
    int    ArtistCount,
    int    ClientCount,
    int    AppointmentCount
);
```

### 2c — Application query: `GetIssuerStudioSummaryQuery.cs`

Create `Pena_e_Arte.Application/Platform/Queries/GetIssuerStudioSummaryQuery.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetIssuerStudioSummaryQuery(Guid StudioId) : IRequest<IssuerStudioSummaryResponse>;

public class GetIssuerStudioSummaryHandler(
    IAppDbContext db,
    UserManager<ApplicationUser> userManager)
    : IRequestHandler<GetIssuerStudioSummaryQuery, IssuerStudioSummaryResponse>
{
    public async Task<IssuerStudioSummaryResponse> Handle(
        GetIssuerStudioSummaryQuery query,
        CancellationToken ct)
    {
        string studioIdStr = query.StudioId.ToString();

        // ── Owner lookup ──────────────────────────────────────────────────────────
        // [DECISION: fill in one of the two approaches after reading Studio entity]
        //
        // Approach A (Studio.OwnerId exists):
        //   var studio = await db.Studios.IgnoreQueryFilters()
        //       .FirstOrDefaultAsync(s => s.Id == query.StudioId, ct);
        //   ApplicationUser? owner = studio?.OwnerId is not null
        //       ? await userManager.FindByIdAsync(studio.OwnerId)
        //       : null;
        //
        // Approach B (claim-based):
        //   var tenantUserIds = await db.Set<IdentityUserClaim<string>>()
        //       .Where(c => c.ClaimType == "tenant_id" && c.ClaimValue == studioIdStr)
        //       .Select(c => c.UserId)
        //       .ToListAsync(ct);
        //   var ownerRole = await db.Set<IdentityRole>()
        //       .Where(r => r.NormalizedName == "OWNER")
        //       .Select(r => r.Id)
        //       .FirstOrDefaultAsync(ct);
        //   var ownerUserId = await db.Set<IdentityUserRole<string>>()
        //       .Where(ur => tenantUserIds.Contains(ur.UserId) && ur.RoleId == ownerRole)
        //       .Select(ur => ur.UserId)
        //       .FirstOrDefaultAsync(ct);
        //   ApplicationUser? owner = ownerUserId is not null
        //       ? await userManager.FindByIdAsync(ownerUserId)
        //       : null;

        // ── Counts ───────────────────────────────────────────────────────────────
        // IgnoreQueryFilters approved: usage #N — issuer cross-tenant studio summary,
        // IssuerOnly. See architecture.md. Increment N to next unused number.

        int clientCount = await db.Clients
            .IgnoreQueryFilters()
            .Where(c => c.StudioId == query.StudioId)
            .CountAsync(ct);

        int appointmentCount = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == query.StudioId)
            .CountAsync(ct);

        // Artist count — query users with role "artist" AND tenant_id = studioId claim.
        // Use Approach A or B above as appropriate once you've read the entity.
        // If the project has an Artist entity in IAppDbContext (e.g. db.Artists), prefer:
        //   int artistCount = await db.Artists.IgnoreQueryFilters()
        //       .Where(a => a.StudioId == query.StudioId).CountAsync(ct);
        // Otherwise fall back to the role+claim cross-join.
        int artistCount = 0; // Replace with actual query after reading db context.

        string ownerEmail       = owner?.Email           ?? "—";
        string ownerDisplayName = owner is null
            ? "—"
            : $"{owner.FirstName} {owner.LastName}".Trim() is { Length: > 0 } n ? n : ownerEmail;

        return new IssuerStudioSummaryResponse(
            ownerEmail,
            ownerDisplayName,
            artistCount,
            clientCount,
            appointmentCount);
    }
}
```

**Implementation note on `owner` variable:** The two decision-gate approaches above define
`owner` in different scopes. Use whichever approach the domain supports, then reference
`owner` in the return statement. Do not leave the placeholder comment in the shipped code —
implement the correct approach and leave a one-line comment naming the approach.

### 2d — Endpoint: add to `PlatformEndpoints.cs`

Add inside `MapPlatformEndpoints`, after the existing `group.MapGet("stats", GetStats)` line:

```csharp
group.MapGet("studios/{studioId:guid}/summary", GetStudioSummary);
```

Add the handler method at the end of the static class:

```csharp
private static async Task<IResult> GetStudioSummary(
    Guid              studioId,
    ISender           mediator,
    CancellationToken ct)
{
    IssuerStudioSummaryResponse result =
        await mediator.Send(new GetIssuerStudioSummaryQuery(studioId), ct);
    return Results.Ok(result);
}
```

Add the required using for the new query class if not already present:
```csharp
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
```

### 2e — Update `architecture.md`

Add the next sequential entry to the `IgnoreQueryFilters` usage log in
`docs/claude/architecture.md`:

```markdown
| #N | GetIssuerStudioSummaryHandler | Cross-tenant: client + appointment counts for a single studio. IssuerOnly. |
```

Replace `#N` with the actual next number after reading the existing log.

### 2f — Frontend: add to `platform.types.ts`

Append to `frontend/src/features/platform/platform.types.ts`:

```typescript
export interface IssuerStudioSummaryResponse {
  ownerEmail:       string;
  ownerDisplayName: string;
  artistCount:      number;
  clientCount:      number;
  appointmentCount: number;
}
```

### 2g — Frontend: add RTK Query endpoint to `platformApi.ts`

Add to the `tagTypes` array: `"IssuerStudioSummary"`.

Add inside `endpoints: (builder) => ({`:

```typescript
getIssuerStudioSummary: builder.query<IssuerStudioSummaryResponse, string>({
  query: (studioId) => `platform/studios/${studioId}/summary`,
  providesTags: (_result, _err, studioId) => [{ type: "IssuerStudioSummary", id: studioId }],
}),
```

Add the generated hook to the export at the bottom:
```typescript
export const {
  // ... existing hooks ...
  useGetIssuerStudioSummaryQuery,
} = platformApi;
```

Also add `IssuerStudioSummaryResponse` to the import from `./platform.types`:
```typescript
import type {
  MrrDataPoint,
  PlatformStatsResponse,
  PlatformSubscriptionResponse,
  PlatformReferralCodeResponse,
  IndustryReportSummary,
  IssuerStudioSummaryResponse,   // ← add
} from "./platform.types";
```

### 2h — Frontend: Studio Overview card in `IssuerStudioDetailPage.tsx`

Add the query call alongside the existing queries (near the top of the component function):

```tsx
const {
  data: summary,
  isLoading: summaryLoading,
} = useGetIssuerStudioSummaryQuery(studioId!, { skip: !studioId });
```

Add the Studio Overview card in the left column, after the Studio Info card:

```tsx
{/* ── Studio Overview Card ─────────────────────────────────────────── */}
<Card>
  <CardHeader className="pb-2">
    <CardTitle className="text-sm">Studio Overview</CardTitle>
  </CardHeader>
  <CardContent className="pt-0">
    {summaryLoading ? (
      <div className="space-y-2">
        <Skeleton className="h-4 w-full" />
        <Skeleton className="h-4 w-2/3" />
        <Skeleton className="h-4 w-1/2" />
      </div>
    ) : summary ? (
      <div className="space-y-3">
        {/* Owner */}
        <div className="text-xs space-y-0.5">
          <p className="text-[10px] text-muted-foreground font-medium uppercase tracking-wider">
            Owner
          </p>
          <p className="font-medium">{summary.ownerDisplayName}</p>
          {summary.ownerEmail !== "—" && (
            <a
              href={`mailto:${summary.ownerEmail}`}
              className="text-primary hover:underline"
            >
              {summary.ownerEmail}
            </a>
          )}
          {summary.ownerEmail === "—" && (
            <p className="text-muted-foreground">{summary.ownerEmail}</p>
          )}
        </div>

        {/* Metrics */}
        <div className="border-t pt-3 grid grid-cols-3 text-center gap-2">
          <div>
            <p className="text-base font-semibold tabular-nums">{summary.artistCount}</p>
            <p className="text-[10px] text-muted-foreground mt-0.5">Artists</p>
          </div>
          <div>
            <p className="text-base font-semibold tabular-nums">{summary.clientCount}</p>
            <p className="text-[10px] text-muted-foreground mt-0.5">Clients</p>
          </div>
          <div>
            <p className="text-base font-semibold tabular-nums">{summary.appointmentCount}</p>
            <p className="text-[10px] text-muted-foreground mt-0.5">Appts</p>
          </div>
        </div>
      </div>
    ) : (
      <p className="text-xs text-muted-foreground">Summary unavailable.</p>
    )}
  </CardContent>
</Card>
```

Add the import at the top of `IssuerStudioDetailPage.tsx`:
```tsx
import { useGetIssuerStudioSummaryQuery } from "@/features/platform/platformApi";
```

---

## Phase 3 — Tests

### 3a — Existing tests

Read `IssuerStudioDetailPage.test.tsx` before writing any new tests. Do not change the
existing 6 tests — they should all still pass after the changes above. Verify:

- `"shows Suspend button for active studios"` queries `getByRole("button", { name: /suspend/i })`.
  Regex `/suspend/i` still matches "Suspend Studio" — **this test still passes without changes**.
- `"renders Active badge"` queries `getAllByText("Active")`. The pill badge in the Studio Info
  card and the card header badge both render the text "Active" — this test still passes.

### 3b — New tests to add

Append the following tests to the existing `describe("IssuerStudioDetailPage", ...)` block.

Add to `IssuerStudioDetailPage.test.tsx`:

```typescript
// ── Fix 1: Subscription status pill ──────────────────────────────────────────

it("renders subscription status as a pill badge, not plain text", async () => {
  renderPage();
  await screen.findAllByText("Ink Soul");
  // Find the subscription status label
  const label = screen.getByText("Subscription status");
  // The value sibling must be a <span> with rounded-full class (pill), not a plain <p>
  const field = label.closest("div")!;
  const pill  = field.querySelector("span.rounded-full");
  expect(pill).not.toBeNull();
  expect(pill?.textContent).toBe("Active");
});

// ── Fix 4: Button labels ──────────────────────────────────────────────────────

it("suspend button is labelled 'Suspend Studio'", async () => {
  renderPage();
  await screen.findAllByText("Ink Soul");
  expect(screen.getByRole("button", { name: /suspend studio/i })).toBeInTheDocument();
});

// ── Fix 5: Consequence copy ───────────────────────────────────────────────────

it("suspend confirm panel shows consequence copy", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findAllByText("Ink Soul");
  await user.click(screen.getByRole("button", { name: /suspend studio/i }));
  expect(await screen.findByText(/immediately hides the studio from discover/i)).toBeInTheDocument();
});

// ── Fix 3: View public portfolio link ────────────────────────────────────────

it("'View public portfolio' renders as an <a> link pointing to the studio's public page", async () => {
  renderPage();
  await screen.findAllByText("Ink Soul");
  const link = screen.getByRole("link", { name: /view public portfolio/i });
  expect(link).toHaveAttribute("href", "/s/ink-soul");
  expect(link).toHaveAttribute("target", "_blank");
});

// ── Phase 2: Studio Overview card ────────────────────────────────────────────

// Extend the MSW server with the new endpoint.
// Add this handler to the setupServer(...) call at the top of the test file:
//   http.get("http://localhost/api/v1/platform/studios/s1/summary", () =>
//     HttpResponse.json({
//       ownerEmail:       "owner@ink-soul.test",
//       ownerDisplayName: "Maria Silva",
//       artistCount:      3,
//       clientCount:      47,
//       appointmentCount: 129,
//     })
//   ),
// Then add the following tests:

it("Studio Overview card renders owner email", async () => {
  renderPage();
  expect(await screen.findByText("owner@ink-soul.test")).toBeInTheDocument();
});

it("Studio Overview card renders artist, client, and appointment counts", async () => {
  renderPage();
  expect(await screen.findByText("3")).toBeInTheDocument();   // artistCount
  expect(screen.getByText("47")).toBeInTheDocument();          // clientCount
  expect(screen.getByText("129")).toBeInTheDocument();         // appointmentCount
});
```

**Note on adding the summary handler to `setupServer`:** The three new tests above depend on
it. Add the `http.get("...summary", ...)` handler to the `setupServer(...)` call at the top
of the test file alongside the existing three handlers. The existing tests remain unaffected
because they do not assert on summary data.

Also add `userEvent` import if not already present:
```typescript
import userEvent from "@testing-library/user-event";
```

### 3c — Backend unit tests

Create `tests/Pena_e_Arte.UnitTests/Platform/GetIssuerStudioSummaryHandlerTests.cs`:

```csharp
// Test the handler with an in-memory DB (follow the existing unit test pattern in the project).
// Tests to cover:
// 1. Returns correct artist, client, and appointment counts for a known studioId.
// 2. Returns OwnerEmail "—" and OwnerDisplayName "—" when no owner is found.
// 3. Returns zero counts when the studio has no associated data.
// Follow the existing handler test pattern in the UnitTests project for EF Core setup.
```

---

## Phase 4 — Quality Gates

Run each check. All must pass before this prompt is considered complete.

```bash
# Frontend
pnpm --filter frontend test -- --reporter=verbose 2>&1 | grep -E "(PASS|FAIL|✓|✗)"
pnpm --filter frontend lint 2>&1 | grep -E "error|warning" | head -20

# Backend
dotnet build Pena_e_Arte.sln 2>&1 | grep -E "error|warning" | grep -v "^Build succeeded"
dotnet test tests/Pena_e_Arte.UnitTests/ 2>&1 | tail -5
```

Fix any errors before completing. Do not mark this prompt done while tests are failing.

---

## Phase 5 — Forbidden Actions

- Do not add any new npm package or NuGet package.
- Do not modify `STATUS_CLASSES` or `STATUS_LABELS` constants — reuse them as-is.
- Do not add `IgnoreQueryFilters()` to any query that isn't IssuerOnly.
- Do not add business logic in the endpoint — it calls `mediator.Send` only.
- Do not use `any` in TypeScript or `var` for non-obvious types in C#.
- Do not add a `FluentValidation` validator for this query — it has no user input, only a
  route parameter that is already typed as `:guid` in the route constraint.

---

## Completion Checklist

- [ ] Fix 1 — Subscription status pill badge (no plain text in status field)
- [ ] Fix 2 — Grid column balance (3-row fixed + conditional section)
- [ ] Fix 3 — "View public portfolio" visually separated with border-t
- [ ] Fix 4 — "Suspend Studio" / "Reactivate Studio" labels
- [ ] Fix 5 — Consequence copy in both destructive confirm panels
- [ ] Fix 6 — h-9 touch targets on main action buttons
- [ ] Fix 7 — max-w-5xl + lg:grid-cols-[1fr_288px] layout
- [ ] `IssuerStudioSummaryResponse.cs` contract created
- [ ] `GetIssuerStudioSummaryQuery.cs` handler created (correct approach for owner lookup)
- [ ] `PlatformEndpoints.cs` — new GET route registered
- [ ] `architecture.md` — IgnoreQueryFilters usage log updated
- [ ] `platform.types.ts` — `IssuerStudioSummaryResponse` added
- [ ] `platformApi.ts` — `getIssuerStudioSummary` query + hook added
- [ ] Studio Overview card renders in page (left column, below Info card)
- [ ] `IssuerStudioDetailPage.test.tsx` — 3 new MSW handler + 7 new tests
- [ ] `GetIssuerStudioSummaryHandlerTests.cs` — 3 backend unit tests
- [ ] All tests pass (`pnpm test`, `dotnet test`)
- [ ] No TypeScript errors (`pnpm lint`)
- [ ] No C# build errors (`dotnet build`)
