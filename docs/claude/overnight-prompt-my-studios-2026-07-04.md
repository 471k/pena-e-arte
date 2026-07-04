# Overnight Prompt — Phase 2: My Studios Page + GET /auth/my-studios
**Date:** 2026-07-04
**Scope:** New backend query + frontend page for clients to see and switch between their studios.
**No migration needed.** No new entities — the feature is built on top of existing Identity claims.

---

## Context: What Already Exists (Phase 1)

Phase 1 is complete and live:

- `POST /api/v1/auth/switch-studio` (`SwitchStudioCommand`) — accepts `{ studioId }`, creates
  a `Client` row if the user is new to that studio, issues fresh JWT scoped to that studio.
- `IIdentityService.GetTenantIdsAsync(userId)` — returns every `studio_id` the user holds a
  `tenant_id` claim for (one per studio they've ever joined).
- `IIdentityService.IssueTokensForTenantAsync(userId, studioId)` — issues fresh tokens.
- `useSwitchStudioMutation` and `useEnsureActiveStudio` hook on the frontend.

**The gap:** There is no way for a client to list all the studios they belong to outside the
booking flow. Currently the only way to switch is by visiting `/s/{slug}` → clicking "Book".
Phase 2 adds a dedicated page and endpoint for this.

---

## Required Reading

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/architecture.md                                     ← check the Decisions Log
Pena_e_Arte.Application/Auth/Commands/SwitchStudioCommand.cs    ← understand existing switch logic
Pena_e_Arte.Domain/Interfaces/IIdentityService.cs               ← GetTenantIdsAsync signature
Pena_e_Arte.Infrastructure/Services/IdentityService.cs          ← see how claims are stored
Pena_e_Arte.API/Endpoints/AuthEndpoints.cs                      ← where to add the endpoint
frontend/src/features/auth/authApi.ts                           ← RTK Query slice to extend
frontend/src/features/auth/useEnsureActiveStudio.ts             ← pattern for credential dispatch
frontend/src/features/auth/authSlice.ts                         ← setCredentials action
frontend/src/layouts/ClientLayout.tsx                           ← NAV_ITEMS to update
frontend/src/app/router.tsx                                     ← where to add the route
```

---

## Architecture Decision

**`IsCurrentlyActive` is computed on the frontend, not returned by the server.**

The frontend already has the active `tenantId` in the Redux store (`state.auth.tenantId`).
Returning `IsCurrentlyActive` from the server would require an extra Identity claim lookup per
request and would go stale as soon as the client switches. Comparing `studio.studioId === tenantId`
in the component is cheaper, always fresh, and requires no cache invalidation.

The server returns `IsStudioActive` (from `Studio.IsActive`) which the frontend cannot know —
this tells the client whether the studio is active on the platform (not suspended/deactivated).

---

## Backend Changes

### Step B1 — Create `MyStudioResponse`

**File:** `Pena_e_Arte.Contracts/Responses/MyStudioResponse.cs` (new file)

```csharp
namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// One entry in the list returned by GET /api/v1/auth/my-studios.
/// Represents a studio the authenticated client belongs to.
/// IsCurrentlyActive is NOT included — the frontend computes it
/// by comparing StudioId against the tenantId in the stored JWT.
/// </summary>
public record MyStudioResponse(
    Guid    StudioId,
    string  Name,
    string  Slug,
    string  City,
    string? CoverImageUrl,
    bool    IsStudioActive);
```

---

### Step B2 — Create `GetMyStudiosQuery`

**File:** `Pena_e_Arte.Application/Auth/Queries/GetMyStudiosQuery.cs` (new file)

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Queries;

public record GetMyStudiosQuery : IRequest<List<MyStudioResponse>>;

public class GetMyStudiosHandler(
    IAppDbContext  db,
    IIdentityService identity,
    ICurrentUser   currentUser)
    : IRequestHandler<GetMyStudiosQuery, List<MyStudioResponse>>
{
    public async Task<List<MyStudioResponse>> Handle(
        GetMyStudiosQuery query, CancellationToken ct)
    {
        // All studios this user holds a tenant_id claim for
        IReadOnlyList<Guid> tenantIds =
            await identity.GetTenantIdsAsync(currentUser.UserId, ct);

        if (tenantIds.Count == 0) return [];

        // Studios are not themselves tenant-scoped (Studio IS the tenant) —
        // no IgnoreQueryFilters() needed here.
        List<Domain.Entities.Studio> studios = await db.Studios
            .Where(s => tenantIds.Contains(s.Id))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return studios
            .Select(s => new MyStudioResponse(
                s.Id, s.Name, s.Slug, s.City, s.CoverImageUrl, s.IsActive))
            .ToList();
    }
}
```

**No FluentValidation validator needed** — the query has no user-supplied parameters.
`currentUser.UserId` is resolved from the validated JWT.

---

### Step B3 — Add `GET /auth/my-studios` to `AuthEndpoints.cs`

**File:** `Pena_e_Arte.API/Endpoints/AuthEndpoints.cs`

Add a new route inside `MapAuthEndpoints`:

```csharp
group.MapGet("/my-studios", GetMyStudios).RequireAuthorization("ClientOnly");
```

Add the handler method (follow the existing static-method pattern in the file):

```csharp
private static async Task<IResult> GetMyStudios(
    ISender           mediator,
    CancellationToken ct)
{
    List<MyStudioResponse> result = await mediator.Send(new GetMyStudiosQuery(), ct);
    return Results.Ok(result);
}
```

Add the missing using at the top of the file:
```csharp
using Pena_e_Arte.Application.Auth.Queries;
```

**Policy rationale:** `"ClientOnly"` — the same policy used by `SwitchStudio`. Multi-studio
membership is a client-exclusive concept. Artists and owners belong to exactly one studio by
design. Issuers are cross-tenant administrators, not clients.

**No rate limiting** — reads from Identity claims, no writes, non-sensitive aggregate.

---

### Step B4 — Backend unit tests

**File:** `tests/Pena_e_Arte.UnitTests/Auth/GetMyStudiosHandlerTests.cs` (new file)

Check the project's existing unit test patterns (see other files in the same directory) to
confirm the mocking library in use (NSubstitute or Moq). Write these tests:

```
1. Returns empty list when user has no tenant_id claims
2. Returns studios ordered alphabetically by Name
3. Returns correct Name, Slug, City, CoverImageUrl values from the Studio entity
4. IsStudioActive = true when Studio.IsActive = true
5. IsStudioActive = false when Studio.IsActive = false
6. Does NOT return studios the user has no claim for (security boundary)
7. Returns multiple studios correctly (client belongs to 3 studios)
8. Returns a single studio correctly (typical single-studio client)
```

Use in-memory EF Core or NSubstitute for `IAppDbContext` and `IIdentityService` as done in
the existing handler tests in the same folder.

---

## Frontend Changes

### Step F1 — Add `MyStudioResponse` interface and `getMyStudios` query to `authApi.ts`

**File:** `frontend/src/features/auth/authApi.ts`

Add the interface near the other response types:

```ts
export interface MyStudioResponse {
  studioId:       string;
  name:           string;
  slug:           string;
  city:           string;
  coverImageUrl:  string | null;
  isStudioActive: boolean;
}
```

Add `"MyStudios"` to `tagTypes`:

```ts
export const authApi = createApi({
  reducerPath: "authApi",
  baseQuery,
  tagTypes: ["MyStudios"],          // ← add
  endpoints: (builder) => ({
    // ...existing endpoints...

    getMyStudios: builder.query<MyStudioResponse[], void>({
      query: () => "auth/my-studios",
      providesTags: ["MyStudios"],
    }),

    switchStudio: builder.mutation<SwitchStudioResponse, SwitchStudioRequest>({
      query: (body) => ({ url: "auth/switch-studio", method: "POST", body }),
      // Invalidate so that IsStudioActive reflects any status change, but note:
      // isCurrentlyActive is computed client-side from Redux, so no refetch is
      // needed purely for switching — only for IsStudioActive changes.
      invalidatesTags: ["MyStudios"],
    }),
  }),
});
```

Export the new hook at the bottom:

```ts
export const {
  // ...existing hooks...
  useGetMyStudiosQuery,
  useSwitchStudioMutation,
} = authApi;
```

---

### Step F2 — Create `MyStudiosPage.tsx`

**File:** `frontend/src/features/auth/components/MyStudiosPage.tsx` (new file)

```tsx
import { Building2, CheckCircle2, ExternalLink, Loader2 } from "lucide-react";
import { Link, useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { setCredentials } from "@/features/auth/authSlice";
import { decodeToken } from "@/shared/utils/jwt";
import { useGetMyStudiosQuery, useSwitchStudioMutation } from "@/features/auth/authApi";
import type { MyStudioResponse } from "@/features/auth/authApi";
import { useState } from "react";

// ── Helpers ───────────────────────────────────────────────────────────────────

function StudioAvatar({ name, coverImageUrl }: { name: string; coverImageUrl: string | null }) {
  if (coverImageUrl) {
    return (
      <img
        src={coverImageUrl}
        alt={name}
        className="h-12 w-12 rounded-md object-cover shrink-0"
      />
    );
  }

  // Initials monogram — same approach as StudioPortfolioPage
  const initials = name
    .split(" ")
    .map((w) => w[0])
    .join("")
    .toUpperCase()
    .slice(0, 2);

  return (
    <div className="h-12 w-12 rounded-md bg-primary/10 text-primary flex items-center justify-center text-sm font-semibold shrink-0">
      {initials}
    </div>
  );
}

// ── Studio card ───────────────────────────────────────────────────────────────

interface StudioCardProps {
  studio:        MyStudioResponse;
  isActive:      boolean;
  isSwitching:   boolean;
  onSwitch:      (studioId: string) => void;
}

function StudioCard({ studio, isActive, isSwitching, onSwitch }: StudioCardProps) {
  return (
    <Card
      className={`transition-colors ${
        isActive ? "ring-2 ring-primary ring-offset-2 ring-offset-background" : ""
      }`}
    >
      <CardContent className="p-4">
        <div className="flex items-start gap-3">
          <StudioAvatar name={studio.name} coverImageUrl={studio.coverImageUrl} />

          <div className="flex-1 min-w-0 space-y-0.5">
            <div className="flex items-center gap-2 flex-wrap">
              <p className="text-sm font-semibold truncate">{studio.name}</p>
              {isActive && (
                <span className="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium bg-primary/15 text-primary">
                  <CheckCircle2 className="h-3 w-3" aria-hidden />
                  Active
                </span>
              )}
              {!studio.isStudioActive && (
                <span className="inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium bg-destructive/10 text-destructive">
                  Suspended
                </span>
              )}
            </div>
            <p className="text-xs text-muted-foreground">{studio.city}</p>
          </div>

          <div className="flex items-center gap-2 shrink-0">
            <Link
              to={`/s/${studio.slug}`}
              aria-label={`View ${studio.name} portfolio`}
              className="text-muted-foreground hover:text-foreground transition-colors"
              title="View portfolio"
            >
              <ExternalLink className="h-4 w-4" />
            </Link>

            {isActive ? (
              <Button size="sm" variant="outline" disabled className="gap-1.5 text-xs">
                <CheckCircle2 className="h-3.5 w-3.5" />
                Current
              </Button>
            ) : (
              <Button
                size="sm"
                variant="outline"
                onClick={() => onSwitch(studio.studioId)}
                disabled={isSwitching}
                className="text-xs gap-1.5"
                aria-label={`Switch to ${studio.name}`}
              >
                {isSwitching ? (
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                ) : null}
                Switch
              </Button>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function MyStudiosPage() {
  useDocumentMeta({ title: "My Studios — Pena e Artë", canonical: "/my-studios" });

  const dispatch        = useAppDispatch();
  const currentTenantId = useAppSelector((s) => s.auth.tenantId);
  const navigate        = useNavigate();

  const { data: studios, isLoading, isError, refetch } = useGetMyStudiosQuery();
  const [switchStudio]    = useSwitchStudioMutation();
  const [switchingId, setSwitchingId] = useState<string | null>(null);

  async function handleSwitch(studioId: string) {
    setSwitchingId(studioId);
    try {
      const response = await switchStudio({ studioId }).unwrap();
      const decoded  = decodeToken(response.accessToken);
      dispatch(setCredentials({ ...decoded, refreshToken: response.refreshToken }));
      // isCurrentlyActive updates instantly via tenantId change in Redux.
      // Show a confirmation toast and optionally navigate to the booking page.
      toast.success(
        response.isNewMembership
          ? "Joined studio — welcome!"
          : "Studio switched successfully."
      );
      navigate("/book", { replace: true });
    } catch {
      toast.error("Couldn't switch studios. Please try again.");
    } finally {
      setSwitchingId(null);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Building2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">My Studios</span>
        {studios && studios.length > 0 && (
          <span className="text-xs text-muted-foreground ml-1">
            ({studios.length})
          </span>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-3">
        {/* ── Loading ── */}
        {isLoading && (
          <div className="space-y-3" aria-label="Loading studios">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-20 w-full rounded-lg" />
            ))}
          </div>
        )}

        {/* ── Error ── */}
        {isError && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            Failed to load your studios.{" "}
            <button type="button" className="underline" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        )}

        {/* ── Empty ── */}
        {!isLoading && !isError && studios?.length === 0 && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <Building2 className="h-10 w-10 text-muted-foreground/50" />
            <div className="space-y-1">
              <p className="text-sm font-medium">No studios yet</p>
              <p className="text-xs text-muted-foreground">
                Visit a studio's page and tap "Book" to join.
              </p>
            </div>
            <Button size="sm" variant="outline" onClick={() => navigate("/discover")}>
              Discover studios
            </Button>
          </div>
        )}

        {/* ── List ── */}
        {!isLoading && !isError && studios && studios.length > 0 && (
          <>
            <p className="text-xs text-muted-foreground px-1">
              {studios.length === 1
                ? "You belong to one studio."
                : `You belong to ${studios.length} studios. Tap "Switch" to change your active studio.`}
            </p>

            {studios.map((studio) => (
              <StudioCard
                key={studio.studioId}
                studio={studio}
                isActive={studio.studioId === currentTenantId}
                isSwitching={switchingId === studio.studioId}
                onSwitch={handleSwitch}
              />
            ))}
          </>
        )}
      </main>
    </div>
  );
}
```

---

### Step F3 — Export from the auth feature index

**File:** `frontend/src/features/auth/index.ts`

If an `index.ts` exists in `frontend/src/features/auth/`, add:
```ts
export { MyStudiosPage } from "./components/MyStudiosPage";
```

If no `index.ts` exists, check how other auth components (`LoginPage`, `ClientRegisterPage`) are
imported in `router.tsx` — import directly from the component file path rather than an index.

---

### Step F4 — Add route to `router.tsx`

**File:** `frontend/src/app/router.tsx`

Import `MyStudiosPage` at the top (match the import style used for other client-only pages):

```ts
import { MyStudiosPage } from "@/features/auth/components/MyStudiosPage";
// — or from the auth index if one exists:
import { MyStudiosPage } from "@/features/auth";
```

Add the route inside the authenticated `AppLayout` children, after the `book` route (client-centric
routes are grouped at the top):

```tsx
// After the `book` route block:
{
  path: "my-studios",
  element: <RoleGuard allowedRoles={[Role.Client]} />,
  children: [
    { index: true, element: <ErrorBoundary><MyStudiosPage /></ErrorBoundary> },
  ],
},
```

---

### Step F5 — Add "My Studios" to `ClientLayout.tsx`

**File:** `frontend/src/layouts/ClientLayout.tsx`

Add `Building2` to the Lucide import line:
```ts
import {
  CalendarDays, Palette, FileText, ScrollText, User, PenLine, Building2,
} from "lucide-react";
```

Add a new entry to `NAV_ITEMS`. Place it after "Book Appointment" and before "My Designs"
so it's adjacent to the context-setting items:

```ts
const NAV_ITEMS = [
  { label: "Book Appointment", shortLabel: "Book",    href: "/book",         icon: <CalendarDays className="h-4 w-4" /> },
  { label: "My Studios",       shortLabel: "Studios", href: "/my-studios",   icon: <Building2    className="h-4 w-4" /> },
  { label: "My Designs",       shortLabel: undefined, href: "/designs",      icon: <Palette      className="h-4 w-4" /> },
  { label: "Intake Forms",     shortLabel: undefined, href: "/forms/intake", icon: <FileText     className="h-4 w-4" /> },
  { label: "Consent Forms",    shortLabel: undefined, href: "/forms/consent",icon: <ScrollText   className="h-4 w-4" /> },
  { label: "My Profile",       shortLabel: undefined, href: "/clients/me",   icon: <User         className="h-4 w-4" /> },
];
```

---

### Step F6 — Frontend tests

**File:** `frontend/src/features/auth/__tests__/MyStudiosPage.test.tsx` (new file)

Check the test setup pattern from a nearby test file (e.g. `ConsentForms.test.tsx`) for the
MSW server + store + render helper conventions. Write:

```ts
// ── Seed data ───────────────────────────────────────────────────────────────

const STUDIO_A: MyStudioResponse = {
  studioId:       "studio-aaa",
  name:           "Alpha Ink",
  slug:           "alpha-ink",
  city:           "Tirana",
  coverImageUrl:  null,
  isStudioActive: true,
};

const STUDIO_B: MyStudioResponse = {
  studioId:       "studio-bbb",
  name:           "Beta Art",
  slug:           "beta-art",
  city:           "Durrës",
  coverImageUrl:  "https://r2.example.com/beta.jpg",
  isStudioActive: true,
};

const SUSPENDED_STUDIO: MyStudioResponse = {
  studioId:       "studio-ccc",
  name:           "Closed Ink",
  slug:           "closed-ink",
  city:           "Vlorë",
  coverImageUrl:  null,
  isStudioActive: false,   // ← suspended
};
```

Write these tests:

```
Rendering
  1. Shows loading skeleton while fetching
  2. Shows each studio's name and city
  3. Shows an initials monogram when coverImageUrl is null
  4. Shows a cover image when coverImageUrl is present
  5. Shows a "Suspended" badge for isStudioActive=false studios

Active studio indicator (currentTenantId = "studio-aaa")
  6. Shows "Current" button (disabled) on the studio matching the active tenantId
  7. Shows "Switch" button (enabled) on studios that don't match the active tenantId
  8. Does not show "Active" badge on non-active studios

Studio switching
  9.  Calls the switch-studio API with the correct studioId on button click
  10. Dispatches setCredentials with the decoded token after a successful switch
  11. Navigates to /book after a successful switch
  12. Shows a toast error on switch failure (500 response)

Edge cases
  13. Shows the empty state when the API returns an empty array
  14. Shows the error state and a "Try again" button when the API returns 500
  15. Shows the correct count in the header when multiple studios are returned
```

MSW handler for the test suite:
```ts
http.get("http://localhost/api/v1/auth/my-studios", () =>
  HttpResponse.json([STUDIO_A, STUDIO_B]),
),
http.post("http://localhost/api/v1/auth/switch-studio", () =>
  HttpResponse.json({
    accessToken:    "fake.jwt.token",
    refreshToken:   "fake-refresh",
    isNewMembership: false,
    tokenType:      "Bearer",
  }),
),
```

For the `preloadedState` in `makeStore`, set `auth.tenantId = "studio-aaa"` so that test 6-8
correctly identify `STUDIO_A` as the active one.

You'll need to mock `decodeToken` to return a predictable payload when called with `"fake.jwt.token"`:

```ts
// At the top of the test file:
vi.mock("@/shared/utils/jwt", () => ({
  decodeToken: () => ({
    user:     { id: "u-001", email: "test@test.com" },
    token:    "fake.jwt.token",
    tenantId: "studio-bbb",  // switched to STUDIO_B
    role:     "client",
  }),
}));
```

---

## Verification

Run in order. Fix every failure before the next step.

```bash
cd "Pena e Arte"

# 1. Backend compiles
dotnet build

# 2. New handler unit tests
dotnet test tests/Pena_e_Arte.UnitTests/ --filter "GetMyStudios" --no-build

# 3. Full unit test suite
dotnet test tests/Pena_e_Arte.UnitTests/ --no-build

# 4. Full test suite (including integration tests)
dotnet test --no-build

# 5. Frontend type-checks (no TypeScript errors)
cd frontend && pnpm tsc --noEmit

# 6. Frontend unit tests for the new page
pnpm test -- --reporter=verbose features/auth/__tests__/MyStudiosPage
```

All six commands must exit 0.

---

## Exit Condition

Steps 1–6 all green. Then append to `docs/claude/architecture.md`:

```markdown
## Multi-Studio Plan — Phase 2: My Studios Page — 2026-07-04

### What was added
- `GET /api/v1/auth/my-studios` (`ClientOnly`, no rate limit) — returns all studios
  the authenticated client holds a `tenant_id` Identity claim for, ordered by name.
  Returns `MyStudioResponse[]` (StudioId, Name, Slug, City, CoverImageUrl, IsStudioActive).
- `GetMyStudiosQuery` + `GetMyStudiosHandler` — reads tenant IDs from `IIdentityService.GetTenantIdsAsync`
  then fetches `Studio` rows. Studios are not tenant-scoped (no IgnoreQueryFilters needed).
- `MyStudiosPage` at `/my-studios` (client-only route) — lists studio cards with
  cover image/initials monogram, city, active ring, switch button, and a link to the public portfolio.
- `Building2` nav item added to `ClientLayout` between "Book Appointment" and "My Designs".

### Key decisions
- **IsCurrentlyActive computed on the frontend**, not the server. The Redux store already holds
  `auth.tenantId`. Comparing `studio.studioId === tenantId` in the component is cheaper,
  always fresh, and eliminates cache-invalidation overhead after switching.
- **Navigate to /book after switch** — clears the user's mental model to "I'm now in a new studio"
  and lands them on the most immediately useful page. No full-page reload needed; stale RTK Query
  cache for the old tenant will be replaced by fresh fetches triggered by the new page.
- **ClientOnly policy** — consistent with the existing SwitchStudio endpoint. Artists and owners
  each belong to exactly one studio; this feature is meaningless for them.
- **No validator needed** — `GetMyStudiosQuery` takes no user-supplied parameters.

### Files added/changed
Backend:
- `Pena_e_Arte.Contracts/Responses/MyStudioResponse.cs` (NEW)
- `Pena_e_Arte.Application/Auth/Queries/GetMyStudiosQuery.cs` (NEW)
- `Pena_e_Arte.API/Endpoints/AuthEndpoints.cs` (GET /auth/my-studios added)
- `tests/Pena_e_Arte.UnitTests/Auth/GetMyStudiosHandlerTests.cs` (NEW — 8 tests)

Frontend:
- `frontend/src/features/auth/authApi.ts` (MyStudioResponse interface + getMyStudios query)
- `frontend/src/features/auth/components/MyStudiosPage.tsx` (NEW)
- `frontend/src/features/auth/index.ts` (export added if index exists)
- `frontend/src/app/router.tsx` (/my-studios route added)
- `frontend/src/layouts/ClientLayout.tsx` (Building2 nav item added)
- `frontend/src/features/auth/__tests__/MyStudiosPage.test.tsx` (NEW — 15 tests)

### Feature map entry
Add to Feature Module Map:
| 23 | Multi-Studio Client View | No new entity (`Studio` + Identity claims) | `IIdentityService.GetTenantIdsAsync` | Per-user, cross-tenant |
```
