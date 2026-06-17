# Overnight Master Prompt — 2026-06-17

> Paste this entire file into Claude Code and let it run unsupervised.
> Every task below is self-contained. Work top to bottom. Never skip a test step.
> The full session ends with `dotnet test && pnpm test` returning zero failures.

---

## 0. MANDATORY SETUP — Do This Before Anything Else

```
Read CLAUDE.md.
Read docs/claude/architecture.md.
Read docs/claude/frontend.md.
Read docs/claude/backend.md.
Read docs/claude/conventions.md.
```

Then establish a baseline — run both test suites and note which tests are
already failing before you touch any code:

```bash
cd "Pena e Arte"
dotnet test --no-build 2>&1 | tail -20
cd frontend && pnpm test --run 2>&1 | tail -40
```

Record the counts. Every task below must leave the suite in equal or better
shape than the baseline.

Branch for this session:

```bash
git checkout -b feat/overnight-2026-06-17
```

---

## TASK 1 — StudioPortfolioPage, ArtistPortfolioPage, SharedDesignPage
### (features/public/components/)

**What already exists:** The three components are implemented and routed.
The test files are missing. Run implementation + bug checks first, then write tests.

### 1-A  Open Graph + canonical meta tags (SP-02 gap #1)

Both `StudioPortfolioPage` and `ArtistPortfolioPage` only set `document.title`
via a `useEffect`. The spec requires full OG meta and a canonical link in `<head>`.

**Do NOT install React Helmet.** No new dependencies allowed.
Use the `react-helmet-async` package IF it is already in `package.json`.
If it is not present, implement meta injection using a tiny helper that
directly manipulates `document.head` (create/update `<meta>` and `<link>` DOM
nodes), wrapped in a `useEffect` cleanup pattern. Keep it in
`shared/utils/useDocumentMeta.ts`.

The hook signature:

```typescript
interface DocMeta {
  title: string;
  description?: string;
  ogImage?: string;
  canonical: string;
}

export function useDocumentMeta(meta: DocMeta): void;
```

It must:
- Set `document.title`
- Create/update `<meta name="description">` if description provided
- Create/update `<meta property="og:title">`, `og:description`, `og:image`
  (skip og:image if no ogImage)
- Create/update `<link rel="canonical" href={canonical}>`
- On unmount, restore the previous `document.title` and remove injected nodes
  (to avoid leaks between pages in tests)

Apply it in `StudioPortfolioPage`:

```
title:       `${studio.name} — Book a Tattoo on Pena e Artë`
description: studio.description ?? `Book your next tattoo at ${studio.name} in ${studio.city}.`
ogImage:     studio.coverImageUrl
canonical:   `https://penaearte.com/s/${studio.slug}`
```

Apply it in `ArtistPortfolioPage`:

```
title:       `${artist.name} — Tattoo Artist on Pena e Artë`
description: artist.bio ?? `View the portfolio of ${artist.name}.`
ogImage:     artist.portfolioImages?.[0]
canonical:   `https://penaearte.com/artist/${artist.slug}`
```

Remove the old `useEffect` that set `document.title` from both pages after
replacing it with `useDocumentMeta`.

### 1-B  Verify component implementations

Open each of the three files and check for:

- No `useEffect` for data fetching (RTK Query only).
- No `any` types.
- Named exports only (no default exports).
- `SharedDesignPage` must show "This link has expired or has been revoked" when
  the query returns null/404 (not a generic error). Check the current implementation
  handles this case explicitly. If the error state is missing or generic, fix it.
- `StudioPortfolioPage` "Book here" button must link to `/book?studio={slug}` if
  the user is authenticated or `/login?redirect=/book?studio={slug}` if not.
  Check if auth state is read from the Redux store to determine which URL to use.
  If the button always links to `/book`, fix it to be auth-aware using
  `useAppSelector((s) => s.auth.token)`.
- All loading states must show a spinner (Loader2 from lucide-react), not blank pages.

Fix any issues found before writing tests.

### 1-C  Write tests

Create the following test files:

**`frontend/src/features/public/__tests__/StudioPortfolioPage.test.tsx`**

Mock setup required:
```typescript
vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useParams: () => ({ slug: "test-studio" }) };
});
```

Also mock `../publicApi` using `vi.mock` to return controlled data.

Tests to write:
```
describe("StudioPortfolioPage") {
  it("renders studio name and city when data loads")
  it("renders cover image when coverImageUrl is present")
  it("renders artist cards for each artist in the list")
  it("shows loading spinner while fetching")
  it("shows 'Studio not found' when isError is true")
  it("renders 'Book here' CTA when showBookingCta is true")
  it("sets og:title meta tag with studio name")
  it("sets canonical link tag with correct slug URL")
}
```

**`frontend/src/features/public/__tests__/ArtistPortfolioPage.test.tsx`**

Same mock pattern. Tests:
```
describe("ArtistPortfolioPage") {
  it("renders artist name and bio when data loads")
  it("renders portfolio images when present")
  it("shows 'Artist not found' when isError is true")
  it("shows loading spinner while fetching")
  it("sets og:title meta tag with artist name")
  it("renders studio link back to /s/{studioSlug}")
}
```

**`frontend/src/features/public/__tests__/SharedDesignPage.test.tsx`**

Mock `react-router-dom` `useParams` to return `{ token: "abc123" }`.
Mock `../publicApi` `useGetSharedDesignQuery`.

Tests:
```
describe("SharedDesignPage") {
  it("renders design image when token is valid")
  it("renders studioName and 'Book your own tattoo' CTA")
  it("CTA links to /s/{studioSlug}")
  it("shows expiry message when data is null (token invalid/expired)")
  it("shows loading spinner while fetching")
}
```

**`frontend/src/shared/utils/__tests__/useDocumentMeta.test.ts`**

Test the hook in isolation using `renderHook` from `@testing-library/react`:
```
describe("useDocumentMeta") {
  it("sets document.title")
  it("creates og:title meta tag")
  it("creates og:description meta tag when description is provided")
  it("creates og:image meta tag when ogImage is provided")
  it("skips og:image when ogImage is undefined")
  it("creates canonical link tag")
  it("restores previous title on unmount")
  it("removes injected meta nodes on unmount")
}
```

Run `pnpm test --run` after writing all four test files. Fix any failures before
moving to Task 2.

---

## TASK 2 — StudioMapPage
### (features/map/components/StudioMapPage.tsx)

**What already exists:** The component is implemented and uses react-leaflet.
No test file exists.

### 2-A  Verify implementation

Check:
- `useGetStudioMapQuery` is imported from `@/features/studios` (not fetched via useEffect).
- Loading, error, and empty states are all handled.
- The Leaflet CSS import (`import "leaflet/dist/leaflet.css"`) is present — this
  is required for the map to render correctly.
- No `any` types, no default export.

Fix anything broken before writing tests.

### 2-B  Write tests

Create **`frontend/src/features/map/__tests__/StudioMapPage.test.tsx`**.

React-leaflet requires a DOM mock that Vitest/jsdom does not provide out of the
box. Use this mock pattern at the top of the test file:

```typescript
// Mock react-leaflet entirely — jsdom has no canvas/tile support
vi.mock("react-leaflet", () => ({
  MapContainer: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="map-container">{children}</div>
  ),
  TileLayer: () => <div data-testid="tile-layer" />,
  Marker: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="marker">{children}</div>
  ),
  Popup: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="popup">{children}</div>
  ),
}));

// Mock leaflet itself to avoid CSS/icon errors
vi.mock("leaflet", () => ({
  default: {},
  divIcon: () => ({}),
  icon: () => ({}),
}));
```

Also mock the studios API query. Check the actual import path used in
StudioMapPage — it imports from `@/features/studios`. Mock accordingly.

Tests:
```
describe("StudioMapPage") {
  it("renders the map container")
  it("renders a marker for each studio returned by the API")
  it("renders studio name in popup")
  it("shows loading indicator while isLoading is true")
  it("shows error message when isError is true")
  it("shows empty message when studios array is empty")
  it("renders Sign in and Register links in the header")
}
```

Run `pnpm test --run`. Fix all failures before moving to Task 3.

---

## TASK 3 — Shared Presentational Components
### (shared/components/DataTable, ReadOnlyBanner, SuspensionBanner,
###  SubscriptionGatedButton, UserChip)

**What already exists:** All five components exist with no direct unit tests.
Layout tests exercise `ReadOnlyBanner` and `SuspensionBanner` indirectly.

### 3-A  Verify DataTable implementation

Open `shared/components/DataTable.tsx`. Check:
- Renders table headers from `columns[].header`.
- Renders cell via `col.cell(row)` if provided, else falls back to `col.accessorKey`.
- Shows `emptyMessage` when `data` is empty.
- Applies `cursor-pointer` class and calls `onRowClick` when provided.
- No `any` types. The generic `T` must be constrained to `object` or `Record<string, unknown>` if needed.
- Named export only.

Fix any issues.

### 3-B  Verify ReadOnlyBanner, SuspensionBanner

Open both files. Check:
- They are purely presentational (no RTK Query, no Redux, no side effects).
- `ReadOnlyBanner` shows the correct copy referencing the grace period.
- `SuspensionBanner` shows the correct copy with a link to `/subscribe`.
- Named exports only. No `any`.

### 3-C  Verify SubscriptionGatedButton

Open `shared/components/SubscriptionGatedButton.tsx`. Check:
- Accepts `children`, `disabled` (from outside), and `tooltipText` props at minimum.
- When the button is disabled (subscription gated), it renders a shadcn/ui
  `Tooltip` wrapping the button with the tooltip message.
- When not disabled, renders the button normally without a tooltip wrapper.
- Uses shadcn/ui `Button` primitive internally.
- No `any`, named export.

If the tooltip is missing or the disabled state is not wired correctly, fix it.

### 3-D  Verify UserChip

Open `shared/components/UserChip.tsx`. Check:
- Renders user's display name or initials.
- Uses shadcn/ui `Avatar` for the image/initials display.
- No `any`, named export.

### 3-E  Write tests

**`frontend/src/shared/components/__tests__/DataTable.test.tsx`**

```
describe("DataTable") {
  it("renders column headers")
  it("renders a row for each data item")
  it("renders cell via accessorKey when no cell fn provided")
  it("renders cell via cell() fn when provided")
  it("shows emptyMessage when data is empty")
  it("calls onRowClick with the row when row is clicked")
  it("applies cursor-pointer class to rows when onRowClick is provided")
  it("does NOT apply cursor-pointer when onRowClick is absent")
}
```

**`frontend/src/shared/components/__tests__/ReadOnlyBanner.test.tsx`**

```
describe("ReadOnlyBanner") {
  it("renders without crashing")
  it("contains text about read-only / grace period")
  it("contains a link to /billing or /subscribe")
}
```

**`frontend/src/shared/components/__tests__/SuspensionBanner.test.tsx`**

```
describe("SuspensionBanner") {
  it("renders without crashing")
  it("contains text about suspension")
  it("contains a link to /subscribe")
}
```

**`frontend/src/shared/components/__tests__/SubscriptionGatedButton.test.tsx`**

```
describe("SubscriptionGatedButton") {
  it("renders children when not gated")
  it("renders a tooltip when gated (disabled=true)")
  it("button is disabled when gated")
  it("button is not disabled when not gated")
  it("tooltip text appears in the DOM when gated")
}
```

For the tooltip test: shadcn/ui Tooltip only renders the content on hover by
default. In tests, check that `TooltipContent` is rendered in the DOM (it may
be in a portal). If the tooltip is hidden until hover, use `userEvent.hover`
to reveal it, or check the tooltip trigger's `aria-label` attribute instead —
adapt to whatever implementation exists.

**`frontend/src/shared/components/__tests__/UserChip.test.tsx`**

```
describe("UserChip") {
  it("renders the user's display name")
  it("renders initials when no avatar image is provided")
  it("renders avatar image when avatarUrl is provided")
}
```

Run `pnpm test --run`. Fix all failures before moving to Task 4.

---

## TASK 4 — useCurrentUser + usePermission hooks
### (shared/hooks/)

**What already exists:** Both hooks are implemented. No tests.

### 4-A  Verify implementations

Open `shared/hooks/useCurrentUser.ts`. Check:
- It is a thin selector: `useAppSelector((s) => s.auth.user)` — or returns the
  whole auth state sub-object.
- Uses `useAppSelector` (typed hook), not raw `useSelector`.
- No side effects, no RTK Query calls.
- Named export.

Open `shared/hooks/usePermission.ts`. Check:
- Reads `s.auth.role` from the store.
- Implements a rank-based check: issuer ≥ owner ≥ artist ≥ client.
- Returns `true` if the current role meets or exceeds `requiredRole`.
- Exports the `hasPermission(role, required)` pure function separately so it
  can be unit-tested without React.
- Named export for both the hook and the pure function.

If `hasPermission` is not exported separately, refactor it to be exported.
The hook becomes:

```typescript
export function usePermission(requiredRole: Role): boolean {
  const role = useAppSelector((s) => s.auth.role);
  return hasPermission(role, requiredRole);
}
```

### 4-B  Write tests

**`frontend/src/shared/hooks/__tests__/useCurrentUser.test.ts`**

Use `renderHook` with a Redux `Provider`. Create a helper `makeStore(authState)`
that creates a configured store with a pre-set auth state.

```
describe("useCurrentUser") {
  it("returns null when not authenticated")
  it("returns the user object when authenticated")
  it("returns updated user after state changes")
}
```

**`frontend/src/shared/hooks/__tests__/usePermission.test.ts`**

Test the pure `hasPermission` function without React (no renderHook needed for
most cases):

```
describe("hasPermission (pure)") {
  it("issuer passes all role checks")
  it("owner passes owner, artist, client checks")
  it("owner fails issuer check")
  it("artist passes artist, client checks")
  it("artist fails owner check")
  it("client passes client check only")
  it("null role fails all checks")
}

describe("usePermission (hook)") {
  it("returns true when authenticated role meets requirement")
  it("returns false when authenticated role is insufficient")
  it("returns false when role is null (unauthenticated)")
}
```

Run `pnpm test --run`. Fix all failures before Task 5.

---

## TASK 5 — BookingWidget
### (features/booking/components/BookingWidget.tsx)

**What already exists:** The component is implemented. No tests.

### 5-A  Verify implementation

Open `features/booking/components/BookingWidget.tsx`. Check:
- Reads `studio.showPlatformBranding` from the RTK Query studio response.
- Renders the "Powered by Pena e Artë" footer **only** when
  `showPlatformBranding` is `true`.
- Does NOT use `useEffect` for data fetching.
- Named export only.
- No `any`.
- The branding footer must be an `<a>` tag pointing to `https://penaearte.com`
  with `target="_blank" rel="noopener noreferrer"`.

If the branding footer is hardcoded (always rendered), fix it to be conditional
on `studio.showPlatformBranding`.

### 5-B  Write tests

Create **`frontend/src/features/booking/__tests__/BookingWidget.test.tsx`**.

Mock all RTK Query hooks used by the widget. Provide a helper that renders
the widget with a given `studioId` prop inside a Redux `Provider` + Router
wrapper.

```
describe("BookingWidget") {
  it("renders the booking form for the given studioId")
  it("renders 'Powered by Pena e Artë' footer when showPlatformBranding is true")
  it("does NOT render branding footer when showPlatformBranding is false")
  it("branding footer links to https://penaearte.com")
  it("shows loading state while studio data is fetching")
}
```

Run `pnpm test --run`. Fix all failures before Task 6.

---

## TASK 6 — SP-02 Backend Gaps

### 6-A  RegisterStudioHandler slug collision (bug fix)

**Current behavior:** `RegisterStudioHandler` throws `BusinessRuleViolationException`
when a user-supplied slug is already taken.

**Required behavior (per architecture.md):** If the supplied slug is taken,
auto-append `-2`, `-3`, etc. until a unique slug is found. This is what
`CreateArtistHandler` already does correctly.

Locate `Application/Studios/Commands/RegisterStudioCommand.cs` (or wherever
the handler lives). Find the slug collision check. Change it from:

```csharp
// CURRENT (throws)
if (await _db.Studios.AnyAsync(s => s.Slug == request.Slug, ct))
    throw new BusinessRuleViolationException("The slug is already taken.");
```

To:

```csharp
// FIXED (auto-suffix loop, same pattern as CreateArtistHandler)
string slug = request.Slug;
int suffix = 2;
while (await _db.Studios.AnyAsync(s => s.Slug == slug, ct))
    slug = $"{request.Slug}-{suffix++}";
```

Use `slug` (the potentially-suffixed value) when creating the Studio entity.

**Update the existing test** in
`tests/Pena_e_Arte.UnitTests/Studios/RegisterStudioHandlerTests.cs`:

The test `Handle_DuplicateSlug_ThrowsBusinessRuleViolationException` must be
replaced with:

```csharp
[Fact]
public async Task Handle_SlugCollision_AppendsSuffixUntilUnique()
{
    // Arrange — pre-seed "my-studio" and "my-studio-2"
    _db.Studios.Add(new Studio { Name = "Existing",   Slug = "my-studio",   City = "Lisbon" });
    _db.Studios.Add(new Studio { Name = "Existing 2", Slug = "my-studio-2", City = "Lisbon" });
    await _db.SaveChangesAsync();

    // Act
    StudioResponse result = await CreateSut()
        .Handle(new RegisterStudioCommand(ValidRequest() with { Slug = "my-studio" }), default);

    // Assert — should land on "my-studio-3"
    result.Slug.Should().Be("my-studio-3");
    _db.Studios.Should().ContainSingle(s => s.Slug == "my-studio-3");
}
```

### 6-B  UpdateStudioSlugCommand — write missing tests

No tests exist for `UpdateStudioSlugCommand`. Find the handler in
`Application/Studios/Commands/` and create:

**`tests/Pena_e_Arte.UnitTests/Studios/UpdateStudioSlugHandlerTests.cs`**

Tests:
```csharp
// MethodName_Scenario_ExpectedResult pattern (see conventions.md)

Handle_ValidSlug_UpdatesSlugOnStudio
Handle_SlugAlreadyTaken_ThrowsBusinessRuleViolationException
Handle_SlugAlreadyChangedOnce_ThrowsBusinessRuleViolationException
  // (SlugLockedAt is already set → second change is rejected)
Handle_InvalidSlugFormat_FailsFluentValidation
  // (test via CreateSut() → send command with slug containing uppercase or spaces)
Handle_SlugTooLong_FailsFluentValidation
  // (slug > 60 chars → validation fails)
Handle_SlugUnchanged_DoesNotSetSlugLockedAt
  // if user submits the same slug that is already set, no change, no lock
```

Use the same `FakeDbContext` helper used by other unit tests in that folder.
Look at `RegisterStudioHandlerTests.cs` for the exact import and helper pattern.

Also create **`tests/Pena_e_Arte.IntegrationTests/Application/UpdateStudioSlugIntegrationTests.cs`**
(follow the pattern in `StudioHandlerIntegrationTests.cs`):

```csharp
UpdateStudioSlug_ValidSlug_Returns204
UpdateStudioSlug_AsArtistRole_Returns403
UpdateStudioSlug_DuplicateSlug_Returns409OrUnprocessable
UpdateStudioSlug_SecondChange_Returns409OrUnprocessable
```

### 6-C  Run backend tests

```bash
cd "Pena e Arte" && dotnet test
```

Fix all failures before moving to Task 7.

---

## TASK 7 — SP-05 Gap: PortableProfileToggle mounting verification

**Status to verify:** The gap report said `PortableProfileToggle` was never
mounted. A code check shows it IS now imported and rendered in `MyProfilePage.tsx`.
This task verifies the tests confirm the wiring.

Open `frontend/src/features/clients/__tests__/MyProfilePage.test.tsx`.

Check that it has a test covering:
- `PortableProfileToggle` is rendered when the profile data loads.
- The toggle calls `useUpdatePortableProfileOptInMutation` on change.

If these tests are missing or failing, add them. The mock setup for
`clientsApi` that the file already uses should cover the mutation.

Also open `frontend/src/features/clients/__tests__/PortableProfileToggle.test.tsx`
and verify it tests:
- Renders the toggle in the "opted in" state correctly.
- Renders the toggle in the "opted out" state correctly.
- Calls the mutation with `{ optIn: true }` when toggled on.
- Calls the mutation with `{ optIn: false }` when toggled off.
- Shows the warning text about other studios seeing tattoo history.

If the test file is incomplete, add the missing cases.

Run `pnpm test --run features/clients`. Fix failures.

---

## TASK 8 — SP-06 Gap: ShareDesignButton mounting verification

**Status to verify:** The gap report said `ShareDesignButton` was never mounted.
A code check shows it IS now imported and rendered in `DesignDetailPage.tsx`
(ArtistAndAbove view, line ~125).

Open `frontend/src/features/designs/__tests__/DesignDetailPage.test.tsx`.

Check that it has a test covering:
- `ShareDesignButton` is visible to artist role.
- `ShareDesignButton` is NOT visible to client role.

If these tests are missing, add them using the existing mock setup in that file.

Open `frontend/src/features/designs/__tests__/ShareDesignButton.test.tsx`.

Verify it tests:
- Button renders for artist/owner role.
- On click, calls `useCreateDesignShareTokenMutation`.
- Modal shows the share URL after mutation succeeds.
- Modal shows the "Revoke" button.
- Revoke button calls `useRevokeDesignShareTokenMutation`.

If any tests are missing or failing, add/fix them.

Run `pnpm test --run features/designs`. Fix failures.

---

## TASK 9 — Final Full Test Run + Bug Loop

Run both full test suites:

```bash
cd "Pena e Arte"
dotnet test 2>&1 | tee /tmp/backend-results.txt
cd frontend && pnpm test --run 2>&1 | tee /tmp/frontend-results.txt
```

For every failure:
1. Read the error message carefully.
2. Find the root cause (missing mock, wrong import path, implementation bug,
   wrong assertion).
3. Fix it.
4. Re-run only that test file to confirm the fix.
5. Do NOT move on until that file is green.

Repeat until both suites report 0 failures.

---

## TASK 10 — Lint + Type Check

```bash
cd "Pena e Arte/frontend"
pnpm lint
pnpm tsc --noEmit
```

Fix every lint error and every TypeScript error. The rules:
- No `any`. Use `unknown` and narrow, or use a proper type.
- No default exports on components.
- No `console.log`.
- No `useEffect` used for data fetching.
- TypeScript `strict: true` — no implicit `any`, no loose null checks.
- No TypeScript `enum` — use `as const` objects (see frontend.md).

```bash
cd "Pena e Arte"
dotnet build 2>&1 | grep -E "error|warning CS"
```

Fix any C# compiler errors or warnings about nullable references, unused
variables, or obsolete APIs.

---

## TASK 11 — Commit

Only after both test suites are green and lint passes:

```bash
cd "Pena e Arte"
git add -A
git commit -m "feat: implement and test public pages, shared components, hooks, and SP-02/05/06 gap fixes

- Add useDocumentMeta helper with OG + canonical tag injection
- Add OG/canonical meta to StudioPortfolioPage and ArtistPortfolioPage
- Fix auth-aware CTA on StudioPortfolioPage
- Fix SharedDesignPage expiry/revoked message
- Add tests: StudioPortfolioPage, ArtistPortfolioPage, SharedDesignPage
- Add tests: StudioMapPage (vi.mock for react-leaflet)
- Add tests: DataTable, ReadOnlyBanner, SuspensionBanner, SubscriptionGatedButton, UserChip
- Add tests: useCurrentUser, usePermission (hasPermission pure fn)
- Add tests: BookingWidget (branding footer conditional)
- Fix RegisterStudioHandler slug collision → auto-suffix instead of throw
- Update RegisterStudioHandlerTests for new collision behavior
- Add UpdateStudioSlugHandlerTests (unit + integration)
- Verify and complete MyProfilePage + PortableProfileToggle tests
- Verify and complete DesignDetailPage + ShareDesignButton tests"
```

---

## Rules to Follow Throughout This Session

1. **Never introduce new npm or NuGet packages.** The stack is final.
2. **No `any` in TypeScript.** No exceptions.
3. **No default exports on components.** Named exports only.
4. **No `useEffect` for data fetching.** RTK Query only.
5. **No TypeScript `enum`.** Use `as const` objects with type aliases.
6. **Every new test file must use `vi.mock` for external dependencies** (react-router
   hooks, RTK Query hooks, leaflet, SignalR). Never let a test hit the real network.
7. **After every task, run the test suite for the files you touched.** Do not
   accumulate failures.
8. **If a component implementation is wrong, fix the implementation first,
   then write the test.** Tests must test correct behavior, not bugs.
9. **C# naming:** PascalCase classes/methods, `_camelCase` private fields,
   `Arrange / Act / Assert` pattern with blank line separation.
10. **Serilog only.** No `Console.WriteLine` or `Debug.WriteLine`.
