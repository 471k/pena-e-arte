# Overnight Prompt — Suspension 403 Fix
**Reported bug:** Artists and clients whose studio is suspended get HTTP 403 on every API call and
see "Failed to load appointments / clients / designs / intake forms. Please try again." instead of
a meaningful suspension message.  
**Secondary goal:** Display role-appropriate messaging when studio access is unavailable.

---

## Diagnosis (read this fully before touching any file)

### What the backend does correctly
`TenantMiddleware` calls `IsStudioActiveAsync`. When the studio is inactive it throws
`TenantSuspendedException`, which `ExceptionMiddleware` maps to HTTP 403 with a JSON body:

```json
{ "status": 403, "message": "This studio account has been suspended. Please contact support." }
```

The single exemption is `GET /api/v1/studios/me` — it passes through so the owner can read
`isActive: false` and the frontend can render the suspension banner.

### Why the owner layout works
`OwnerLayout` calls `useGetMyStudioQuery()`. That request hits the exempted `/studios/me` endpoint,
succeeds (200), returns `{ ..., isActive: false }`, and `SuspensionBanner` renders because it
checks `studio?.isActive === false`.

### Why artist and client layouts break
`ArtistLayout` has no call to `useGetMyStudioQuery()` and no `SuspensionBanner` at all.
`ClientLayout` is the same.

When `luis.rodrigues@dark-canvas.test` (artist role) logs in:
1. Every API call → 403 from suspension middleware.
2. `baseQuery.ts` has a handler for 401 (session expired) and 402 (read-only). **It has no
   handler for 403.** The error falls through as a generic RTK Query error.
3. Pages render their `isError` branch: "Failed to load. Please try again."

### The 403 ambiguity problem
The current 403 response body has no `code` field, so the frontend cannot distinguish
`TenantSuspendedException` (studio suspended) from `ForbiddenException` (wrong role).
We must add a machine-readable discriminator to the suspension response.

---

## Files to Change

| # | File | Change |
|---|------|--------|
| 1 | `Pena_e_Arte.API/Middleware/ExceptionMiddleware.cs` | Add `code` field to JSON body for `TenantSuspendedException` |
| 2 | `frontend/src/features/ui/uiSlice.ts` | Add `studioSuspended` state + actions |
| 3 | `frontend/src/shared/api/baseQuery.ts` | Intercept 403 `STUDIO_SUSPENDED`, dispatch new action |
| 4 | `frontend/src/shared/components/SuspensionBanner.tsx` | Read from Redux; accept `role` prop |
| 5 | `frontend/src/layouts/ArtistLayout.tsx` | Add `<SuspensionBanner role="artist" />` |
| 6 | `frontend/src/layouts/ClientLayout.tsx` | Add `<SuspensionBanner role="client" />` |
| 7 | `frontend/src/shared/hooks/useSuspensionAwareError.ts` | New hook — role-aware error message |
| 8 | Key page components | Use the hook for their `isError` display |
| 9 | `layouts/__tests__/ArtistLayout.test.tsx` | Add suspension banner tests |
| 10 | `layouts/__tests__/ClientLayout.test.tsx` | Add suspension banner tests |
| 11 | `shared/components/__tests__/SuspensionBanner.test.tsx` | Add Redux-driven tests |

---

## Step 1 — Backend: `ExceptionMiddleware.cs`

Read the file first. The current `HandleExceptionAsync` method writes:
```csharp
await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = statusCode, message }));
```

Replace the `TenantSuspendedException` branch so it emits a `code` field:

```csharp
private async Task HandleExceptionAsync(HttpContext context, Exception ex)
{
    (int statusCode, string message, string? code) = ex switch
    {
        BadHttpRequestException             => (400,  "Invalid or missing request body.", (string?)null),
        JsonException                       => (400,  "Invalid JSON in request body.",    null),
        ValidationException ve              => (422,  string.Join("; ", ve.Errors.Select(e => e.ErrorMessage)), null),
        NotFoundException                   => (404,  ex.Message,  null),
        ConflictException                   => (409,  ex.Message,  null),
        SlotAlreadyBookedException          => (409,  ex.Message,  null),
        DbUpdateException { InnerException: MySqlException { Number: 1062 } }
                                            => (409,  "This action was already completed by another request. Refresh and try again.", null),
        DesignAlreadyApprovedException      => (409,  ex.Message,  null),
        ConsentFormAlreadySignedException   => (409,  ex.Message,  null),
        ForbiddenException                  => (403,  ex.Message,  null),
        TenantSuspendedException            => (403,  ex.Message,  "STUDIO_SUSPENDED"),
        SubscriptionRequiredException       => (402,  ex.Message,  null),
        BusinessRuleViolationException      => (422,  ex.Message,  null),
        ServiceUnavailableException         => (503,  ex.Message,  null),
        UnauthorizedAccessException         => (401,  ex.Message,  null),
        StripeException stripeEx            => (502,  stripeEx.StripeError?.Message ?? stripeEx.Message, null),
        _                                   => (500,  "An unexpected error occurred.", null),
    };

    if (statusCode == 500)
        logger.LogError(ex, "Unhandled exception");

    context.Response.StatusCode  = statusCode;
    context.Response.ContentType = "application/json";

    object body = code is not null
        ? new { status = statusCode, message, code }
        : new { status = statusCode, message };

    await context.Response.WriteAsync(JsonSerializer.Serialize(body));
}
```

**Why the `code` field only on `TenantSuspendedException`:** A plain `ForbiddenException` (wrong
role, accessing another tenant's data) should NOT get a code. We only need the discriminator for
suspension so the frontend can act on it globally.

---

## Step 2 — Frontend: `uiSlice.ts`

Read the current file. Add `studioSuspended` to `UiState` and add corresponding actions.
Also add `extraReducers` to clear it on logout (the auth `logout` action):

```typescript
import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { logout } from "@/features/auth/authSlice";

interface UiState {
  readOnlyError:    string | null;
  sessionExpired:   boolean;
  studioSuspended:  boolean;
}

const uiSlice = createSlice({
  name: "ui",
  initialState: {
    readOnlyError:   null,
    sessionExpired:  false,
    studioSuspended: false,
  } as UiState,
  reducers: {
    setReadOnlyError: (state, { payload }: PayloadAction<string>) => {
      state.readOnlyError = payload;
    },
    clearReadOnlyError: (state) => {
      state.readOnlyError = null;
    },
    setSessionExpired: (state) => {
      state.sessionExpired = true;
    },
    clearSessionExpired: (state) => {
      state.sessionExpired = false;
    },
    setStudioSuspended: (state) => {
      state.studioSuspended = true;
    },
    clearStudioSuspended: (state) => {
      state.studioSuspended = false;
    },
  },
  extraReducers: (builder) => {
    // Reset suspension flag when the user logs out so the next user
    // (e.g. the owner logging in on the same device) sees a clean state.
    builder.addCase(logout, (state) => {
      state.studioSuspended = false;
      state.readOnlyError   = null;
      state.sessionExpired  = false;
    });
  },
});

export const {
  setReadOnlyError, clearReadOnlyError,
  setSessionExpired, clearSessionExpired,
  setStudioSuspended, clearStudioSuspended,
} = uiSlice.actions;

export default uiSlice.reducer;
```

**Note:** Before adding the `logout` import, read `frontend/src/features/auth/authSlice.ts` to
confirm the exact name of the exported action. If it is named differently (e.g. `logoutAction`),
use that name.

---

## Step 3 — Frontend: `baseQuery.ts`

Read the current file. Add a 403 handler that checks for `code: "STUDIO_SUSPENDED"`:

```typescript
import { fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { BaseQueryFn, FetchArgs, FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { RootState } from "@/app/store";
import {
  setReadOnlyError,
  setSessionExpired,
  setStudioSuspended,
} from "@/features/ui/uiSlice";

const rawBaseQuery = fetchBaseQuery({
  baseUrl: "/api/v1/",
  prepareHeaders: (headers, { getState }) => {
    const { token, tenantId } = (getState() as RootState).auth;
    if (token)    headers.set("Authorization", `Bearer ${token}`);
    if (tenantId) headers.set("X-Tenant-Id", tenantId);
    return headers;
  },
});

export const baseQuery: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> =
  async (args, api, extraOptions) => {
    const result = await rawBaseQuery(args, api, extraOptions);

    if (result.error?.status === 401) {
      api.dispatch(setSessionExpired());
      return result;
    }

    if (result.error?.status === 402) {
      const data = result.error.data as { message?: string } | undefined;
      const message = data?.message ?? "Your studio is in read-only mode.";
      api.dispatch(setReadOnlyError(message));
      return result;
    }

    if (result.error?.status === 403) {
      const data = result.error.data as { code?: string } | undefined;
      if (data?.code === "STUDIO_SUSPENDED") {
        api.dispatch(setStudioSuspended());
      }
      // Plain 403 (wrong role / wrong tenant) — no global side effect;
      // individual pages handle it in their isError branch.
    }

    return result;
  };
```

---

## Step 4 — Frontend: `SuspensionBanner.tsx`

Read the current file. Replace it entirely so it:
- Still accepts `studio?: StudioResponse` (keeps OwnerLayout working without any changes there)
- Also reads `studioSuspended` from Redux (for ArtistLayout / ClientLayout)
- Accepts a `role` prop for role-specific copy

```typescript
import { ShieldX } from "lucide-react";
import { Link } from "react-router-dom";
import { useAppSelector } from "@/app/hooks";
import type { StudioResponse } from "@/features/studios/studiosApi";

type SuspensionBannerProps = {
  studio?: StudioResponse;
  role?:   "owner" | "artist" | "client";
};

export function SuspensionBanner({ studio, role = "owner" }: SuspensionBannerProps) {
  const studioSuspended = useAppSelector((s) => s.ui.studioSuspended);

  // Owner layout supplies studio data; artist/client rely on Redux state.
  const isSuspended = studio?.isActive === false || studioSuspended;
  if (!isSuspended) return null;

  const message =
    role === "artist"
      ? "Your studio's account has been suspended by the platform. Contact your studio owner or platform support to resolve this."
      : role === "client"
      ? "This studio's account has been suspended. Your bookings and records are safe, but access is temporarily unavailable. Contact the studio for assistance."
      : "Your studio has been suspended by the platform administrator. Contact support or reactivate your subscription to resolve this.";

  return (
    <div
      role="alert"
      aria-live="polite"
      className="flex items-center gap-3 px-4 py-2.5 bg-red-500/10 border-b border-red-500/30 text-red-700 dark:text-red-400 text-sm"
    >
      <ShieldX className="h-4 w-4 shrink-0" aria-hidden="true" />
      <span className="flex-1">
        {message}
        {role === "owner" && (
          <>
            {" "}
            <a
              href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL ?? "support@penaearte.com"}`}
              className="font-medium underline underline-offset-4"
            >
              Contact support
            </a>{" "}
            or{" "}
            <Link to="/subscribe" className="font-medium underline underline-offset-4">
              reactivate your subscription
            </Link>
            .
          </>
        )}
      </span>
    </div>
  );
}
```

**Important:** The existing `SuspensionBanner` tests reference `role="link"` with name
`/reactivate your subscription/i`. The reactivate link now only renders for `role="owner"`.
Update those tests (see Step 11).

---

## Step 5 — Frontend: `ArtistLayout.tsx`

Read the current file. Add the `SuspensionBanner` import and render it above the `ReadOnlyBanner`:

```typescript
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
```

Inside the returned JSX, insert above `<ReadOnlyBanner />`:

```tsx
<SuspensionBanner role="artist" />
<ReadOnlyBanner />
```

No other changes to this file.

---

## Step 6 — Frontend: `ClientLayout.tsx`

Same change as Step 5 but with `role="client"`:

```typescript
import { SuspensionBanner } from "@/shared/components/SuspensionBanner";
```

```tsx
<SuspensionBanner role="client" />
<ReadOnlyBanner />
```

---

## Step 7 — New Hook: `useSuspensionAwareError.ts`

Create `frontend/src/shared/hooks/useSuspensionAwareError.ts`:

```typescript
import { useAppSelector } from "@/app/hooks";

/**
 * Returns a display-ready error message.
 * When the studio is suspended, returns a suspension-specific message instead
 * of the generic "Failed to load" text, so users understand why data is unavailable.
 *
 * @param isError - the `isError` boolean from any RTK Query hook
 * @param genericMessage - fallback message when error is unrelated to suspension
 */
export function useSuspensionAwareError(
  isError:        boolean,
  genericMessage: string,
): string | null {
  const isSuspended = useAppSelector((s) => s.ui.studioSuspended);

  if (!isError) return null;

  if (isSuspended) {
    return "Studio access is suspended. Your data is safe — access will be restored once the studio reactivates their subscription.";
  }

  return genericMessage;
}
```

---

## Step 8 — Update Page-Level Error States

The console errors show 403s on these pages. Update their `isError` branch to use the hook.
Read each file before editing.

### 8A — Pages to update

Find every page component that currently has a pattern like:
```tsx
if (isError) return <p>Failed to load [X]. Please try again.</p>;
```

The minimum set from the reported console errors:
- `frontend/src/features/appointments/SchedulePage.tsx`
- `frontend/src/features/dashboard/DashboardPage.tsx`
- `frontend/src/features/artists/ArtistListPage.tsx`
- `frontend/src/features/clients/ClientListPage.tsx`
- `frontend/src/features/designs/DesignListPage.tsx`
- `frontend/src/features/forms/IntakeForms.tsx`
- `frontend/src/features/payments/PaymentListPage.tsx`

Also run this to find any others:
```bash
grep -rn "Failed to load\|Please try again" \
  --include="*.tsx" \
  "Pena e Arte/frontend/src/features" \
  | grep -v "__tests__"
```

### 8B — Pattern to apply in each page

Import the hook:
```typescript
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
```

Replace:
```tsx
if (isError) return <p className="text-sm text-destructive">Failed to load appointments. Please try again.</p>;
```

With:
```tsx
const errorMessage = useSuspensionAwareError(isError, "Failed to load appointments. Please try again.");
if (errorMessage) {
  return (
    <p className="text-sm text-destructive p-4" role="alert">
      {errorMessage}
    </p>
  );
}
```

Adapt the string in the second argument to match the existing message for each page.

### 8C — `DashboardPage.tsx` special case

The dashboard shows multiple sections that each call their own RTK Query hooks (appointments,
payments, artists). Each section has its own `isError`. Read the file — the reported console errors
show `CashPendingSection` rendering despite 403s.

For the dashboard: apply the hook in each subsection's error branch independently. Do NOT
gate the entire dashboard render on a single error — some sections may succeed while others fail.

---

## Step 9 — Tests: `ArtistLayout.test.tsx`

Read the current test file. The `makeStore` function currently accepts `{ readOnlyError }`.
Extend it to also accept `studioSuspended` and update the preloaded state.

Add these new tests at the bottom of the existing `describe("ArtistLayout")` block:

```typescript
it("SuspensionBanner is hidden when studio is not suspended", () => {
  renderLayout({ studioSuspended: false });
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

it("SuspensionBanner is visible when studioSuspended is true in ui state", () => {
  renderLayout({ studioSuspended: true });
  expect(screen.getByRole("alert")).toBeInTheDocument();
  expect(screen.getByText(/studio's account has been suspended/i)).toBeInTheDocument();
});

it("SuspensionBanner shows artist-role copy (not owner reactivation link)", () => {
  renderLayout({ studioSuspended: true });
  expect(screen.queryByRole("link", { name: /reactivate your subscription/i })).not.toBeInTheDocument();
  expect(screen.getByText(/contact your studio owner/i)).toBeInTheDocument();
});
```

Update `makeStore` to accept and wire `studioSuspended`:

```typescript
type StoreOverrides = {
  readOnlyError?:   string | null;
  studioSuspended?: boolean;
};

function makeStore(overrides: StoreOverrides = {}) {
  return configureStore({
    // ... same reducers ...
    preloadedState: {
      auth: { ... } as any,
      ui: {
        readOnlyError:   overrides.readOnlyError   ?? null,
        sessionExpired:  false,
        studioSuspended: overrides.studioSuspended ?? false,
      },
    },
  });
}
```

---

## Step 10 — Tests: `ClientLayout.test.tsx`

Same pattern as Step 9. Extend `StoreOverrides`, update `makeStore`, add three new tests:

```typescript
it("SuspensionBanner is hidden when studio is not suspended", () => {
  renderLayout({ studioSuspended: false });
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

it("SuspensionBanner is visible when studioSuspended is true in ui state", () => {
  renderLayout({ studioSuspended: true });
  expect(screen.getByRole("alert")).toBeInTheDocument();
  expect(screen.getByText(/studio.*suspended/i)).toBeInTheDocument();
});

it("SuspensionBanner shows client-role copy mentioning studio contact", () => {
  renderLayout({ studioSuspended: true });
  expect(screen.getByText(/contact the studio/i)).toBeInTheDocument();
  expect(screen.queryByRole("link", { name: /reactivate your subscription/i })).not.toBeInTheDocument();
});
```

---

## Step 11 — Tests: `SuspensionBanner.test.tsx`

Read the current test file. Two existing tests will now fail because `role` defaults to `"owner"`:
- The reactivate link test still passes (it renders for `role="owner"` which is the default).

Add these new tests to cover the Redux-driven path and role variants:

```typescript
// Add these imports at the top:
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import uiReducer from "@/features/ui/uiSlice";
import authReducer from "@/features/auth/authSlice";

function makeStoreWithSuspension(suspended: boolean) {
  return configureStore({
    reducer: { auth: authReducer, ui: uiReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: null, token: null, tenantId: null, role: null } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: suspended },
    },
  });
}

function renderWithStore(props: Parameters<typeof SuspensionBanner>[0], suspended: boolean) {
  const store = makeStoreWithSuspension(suspended);
  render(
    <Provider store={store}>
      <MemoryRouter>
        <SuspensionBanner {...props} />
      </MemoryRouter>
    </Provider>,
  );
}

// ── New tests ──────────────────────────────────────────────────────────────────

it("renders when studioSuspended is true in Redux state (no studio prop)", () => {
  renderWithStore({}, true);
  expect(screen.getByRole("alert")).toBeInTheDocument();
});

it("does not render when studioSuspended is false and no studio prop", () => {
  renderWithStore({}, false);
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

it("renders artist-role copy when role='artist'", () => {
  renderWithStore({ role: "artist" }, true);
  expect(screen.getByText(/contact your studio owner/i)).toBeInTheDocument();
  expect(screen.queryByRole("link", { name: /reactivate your subscription/i })).not.toBeInTheDocument();
});

it("renders client-role copy when role='client'", () => {
  renderWithStore({ role: "client" }, true);
  expect(screen.getByText(/contact the studio/i)).toBeInTheDocument();
  expect(screen.queryByRole("link", { name: /reactivate your subscription/i })).not.toBeInTheDocument();
});

it("renders owner reactivation link when role='owner' (default)", () => {
  renderWithStore({ studio: SUSPENDED_STUDIO }, false);
  expect(screen.getByRole("link", { name: /reactivate your subscription/i })).toBeInTheDocument();
});
```

**Important:** The existing tests that use `renderBanner()` (no Provider) will fail because the
updated `SuspensionBanner` now calls `useAppSelector`. Wrap those tests with a Provider or convert
`renderBanner` to use `renderWithStore`. Do whichever is less disruptive to the test structure.

---

## Step 12 — Backend tests: `ExceptionMiddlewareTests.cs`

Read `tests/Pena_e_Arte.IntegrationTests/Middleware/ExceptionMiddlewareTests.cs`. Find or add a
test that verifies the suspension response body includes `code: "STUDIO_SUSPENDED"`:

```csharp
[Fact]
public async Task TenantSuspendedException_Returns403WithSuspendedCode()
{
    // Arrange — throw TenantSuspendedException through the middleware
    // (use the existing test setup pattern from the file)
    
    // Act
    HttpResponseMessage response = await /* call the test endpoint */;
    
    // Assert
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    string body = await response.Content.ReadAsStringAsync();
    using JsonDocument doc = JsonDocument.Parse(body);
    Assert.Equal("STUDIO_SUSPENDED", doc.RootElement.GetProperty("code").GetString());
}
```

Follow the exact test structure pattern already used in that file.

---

## Step 13 — Test-Fix Loop

After all changes, run the full test suite and loop until clean:

```bash
# Backend
cd "Pena e Arte"
dotnet build --verbosity minimal
dotnet test

# Frontend
cd "Pena e Arte/frontend"
pnpm tsc --noEmit
pnpm test --run
```

For every failing test:
1. Read the test file.
2. Read the implementation.
3. Fix the root cause (never delete a test).
4. Re-run tests.

Repeat until `dotnet test` and `pnpm test --run` both exit 0.

---

## Hard Rules

1. **Do not change the HTTP 403 status code** — only add the `code` field to the body for
   `TenantSuspendedException`. All existing 403-producing paths that are NOT suspension remain
   unchanged.
2. **Do not add new npm or NuGet packages.**
3. **No `any` in TypeScript** — type the `result.error.data` cast explicitly.
4. **No `useEffect` for data fetching** — `useSuspensionAwareError` is a hook, not a data
   fetcher; it only reads Redux state.
5. **The `OwnerLayout` must not change** other than the fact that `SuspensionBanner` internally
   now also reads from Redux. Its existing `studio={studio}` prop usage stays as-is.
6. **Do not log PII** — no new log statements that include user email, name, or tenant name.

---

## Expected Outcome

After this fix:

- **Artist `luis.rodrigues@dark-canvas.test`** logs into suspended Dark Canvas:
  - Red suspension banner appears at top of `ArtistLayout` with: "Your studio's account has been
    suspended by the platform. Contact your studio owner or platform support to resolve this."
  - Pages that fail to load show: "Studio access is suspended. Your data is safe — access will be
    restored once the studio reactivates their subscription."
  - No more "Failed to load. Please try again." for suspension-caused 403s.

- **Owner `owner@dark-canvas.test`** — behaviour unchanged. The `SuspensionBanner` still shows
  via the `studio.isActive === false` path from `useGetMyStudioQuery()`, with the full owner copy
  including the reactivate link.

- **Any client** registered with a suspended studio sees the client-specific banner and suspension
  error message instead of generic failures.

- **Non-suspended studios** — zero change in behaviour. The `studioSuspended` flag in Redux
  starts as `false` and only becomes `true` if the API actually returns 403 + `STUDIO_SUSPENDED`.
  Normal 403 errors (wrong role, accessing another tenant's data) do not set it.
