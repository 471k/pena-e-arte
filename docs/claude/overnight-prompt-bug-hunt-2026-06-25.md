# Overnight Prompt — Critical Bug Hunt & Fix Loop
**Target codebase:** Pena e Artë — Multi-tenant Tattoo Studio SaaS  
**Scope:** Full codebase — backend (ASP.NET Core 10 / C#) + frontend (React 19 / TypeScript)  
**Goal:** Find every critical bug, fix it, run the full test suite, and loop until everything is green.  
**Produce:** A `docs/claude/bug-hunt-report-2026-06-25.md` fix log for morning review.

---

## Before You Start — Read First

1. Read `CLAUDE.md` (project root).
2. Read `docs/claude/backend.md`.
3. Read `docs/claude/frontend.md`.
4. Read `docs/claude/conventions.md`.
5. Read `docs/claude/architecture.md`.

Do not skip any of those files. The non-negotiable rules inside them define what counts as a bug.

---

## Phase 0 — Establish Baseline

Run these commands in order. Record every error and failure — do not fix anything yet.

```bash
# Backend build — catch compile errors and warnings
cd "Pena e Arte"
dotnet build --verbosity minimal 2>&1 | tee /tmp/build-backend.txt

# Backend tests — capture all failures
dotnet test --logger "console;verbosity=normal" 2>&1 | tee /tmp/test-backend.txt

# Frontend type check — strict mode, zero tolerance
cd frontend
pnpm tsc --noEmit 2>&1 | tee /tmp/typecheck.txt

# Frontend lint
pnpm lint 2>&1 | tee /tmp/lint.txt

# Frontend tests
pnpm test --run 2>&1 | tee /tmp/test-frontend.txt
```

Write the initial counts to the fix log now:
```
## Baseline (before fixes)
- Backend build: X errors, Y warnings
- Backend tests: X failed / Y passed
- TypeScript: X errors
- ESLint: X errors, Y warnings
- Frontend tests: X failed / Y passed
```

---

## Phase 1 — Backend Bug Scan

Work through each category. For every hit: **read the full file before deciding it is a bug** — context matters. Mark each confirmed bug with its file, line, severity, and root cause. Add it to the fix log.

### B-01 · `.First()` that can throw `InvalidOperationException`

A bare `.First()` on a LINQ/EF query throws when the sequence is empty. All query calls must use `.FirstOrDefaultAsync()` + a null-guard or `NotFoundException`.

```bash
grep -rn "\.First(" --include="*.cs" "Pena e Arte" | grep -v "OrDefault\|//\|\.test\."
```

For each hit: read the handler. If there is no null-check after the call that throws a domain exception, it is a **critical bug** — replace with `.FirstOrDefaultAsync(...)  ?? throw new NotFoundException(...)`.

---

### B-02 · Unprotected endpoints

Every endpoint except `/auth/*`, `/health`, and the public group must call `.RequireAuthorization("PolicyName")`. Check every endpoint file.

```bash
grep -rn "MapGet\|MapPost\|MapPut\|MapDelete\|MapPatch" --include="*.cs" "Pena e Arte/Pena_e_Arte.API/Endpoints" \
  | grep -v "RequireAuthorization\|AllowAnonymous"
```

Open each file to verify the group-level `.RequireAuthorization()` is not providing the missing cover before flagging. If a route has neither group-level nor per-route authorization, it is a **critical bug**.

---

### B-03 · PII in Serilog log statements

Logs must never include names, emails, phone numbers, or card data. `tenant_id`, `user_id`, `request_id` are allowed.

```bash
grep -rn "Log\.\|_logger\.\|logger\." --include="*.cs" "Pena e Arte" \
  | grep -iE "\{[^}]*(email|name|phone|address|card|password)[^}]*\}"
```

For each hit: replace the PII property with a safe ID (e.g. swap `{Email}` for `{UserId}`).

---

### B-04 · `IgnoreQueryFilters()` outside approved locations

Only these are approved:
- `PublicEndpoints.cs` — public-facing queries (noted with `// Approved:` comment)
- `RecordArtistView` in `PublicEndpoints.cs`
- Any handler whose class doc says `IssuerOnly`

```bash
grep -rn "IgnoreQueryFilters" --include="*.cs" "Pena e Arte" | grep -v "// Approved\|PublicEndpoints"
```

Read each hit in full context. Unapproved usage is a **critical tenant-isolation bug** — remove or add proper issuer-role guard.

---

### B-05 · Unbounded `ToListAsync()` on large tables

Any query that loads an entire table without `.Take(n)` is a latency and OOM risk.

```bash
grep -rn "\.ToListAsync" --include="*.cs" "Pena e Arte" | grep -v "\.Take\|\.Where\|\.Skip\|obj/"
```

Open the surrounding handler. If there is no `.Take(n)` or `.Where(...)` that limits the result set to a tenant-scoped subset (which is already bounded by the global query filter), note it. If the query is on a table that can grow without bound and has no practical ceiling (e.g. all notifications, all appointments with no date filter), add a sensible `.Take(200)` guard.

---

### B-06 · External service calls without try/catch

Stripe, Twilio, MailKit, and Redis calls must be wrapped in try/catch. An unhandled SDK exception will crash the request pipeline with a 500.

```bash
grep -rn "await.*stripe\.\|await.*twilio\.\|SmtpClient\|await.*Send.*Mail\|await.*redis" \
  --include="*.cs" "Pena e Arte" | grep -v "//\|obj/"
```

For each hit: read the method. If not inside a try/catch that catches `Exception` (or the specific SDK exception type) and either rethrows as a domain exception or logs and returns a graceful result, it is a **high-severity bug**. Wrap it.

---

### B-07 · Missing FluentValidation validators

Every command/query that accepts user-controlled input must have a registered `AbstractValidator<T>`. Check that validators exist **and** are registered in DI.

Step 1 — list all command/query files:
```bash
find "Pena e Arte/Pena_e_Arte.Application" -name "*Command*.cs" -o -name "*Query*.cs" \
  | grep -v obj/ | sort
```

Step 2 — for each command that mutates state, check for a matching `*Validator.cs`:
```bash
find "Pena e Arte/Pena_e_Arte.Application" -name "*Validator.cs" | grep -v obj/ | sort
```

Step 3 — open `Pena_e_Arte.API/Program.cs` (or wherever validators are registered via `AddFluentValidation` / assembly scan) and verify the scan covers all validator assemblies.

If a mutation command has no validator, create a minimal one with at least a `NotEmpty` rule on the primary key / required fields.

---

### B-08 · Null-forgiving operator overuse (`!`)

The null-forgiving operator (`!`) suppresses compiler null warnings. Every use should be justified.

```bash
grep -rn "!;" --include="*.cs" "Pena e Arte" | grep -v "//\|!=\|obj/"
grep -rn "!\." --include="*.cs" "Pena e Arte/Pena_e_Arte.Application" | grep -v "//\|!=\|obj/"
```

For each hit: read the surrounding code. If the `!` is on a value retrieved from the DB or from a `ClaimsPrincipal` without a prior null check, replace it with a proper null check and a domain exception. Legitimate uses (e.g. after an explicit `?? throw` on the same object) can stay — add a `// safe: guarded above` comment.

---

### B-09 · `GetPortfolioFeed` — missing default for `radiusKm`

In `PublicEndpoints.cs`, `GetPortfolioFeed` declares `radiusKm` as a required `double` parameter with no default. When `lat` and `lng` are null (global feed), `radiusKm` is irrelevant but still required. A client calling the global feed will get a 400 if it omits `radiusKm`.

**Fix:** Add a default value:
```csharp
private static async Task<IResult> GetPortfolioFeed(
    double?           lat,
    double?           lng,
    ISender           mediator,
    CancellationToken ct,
    double            radiusKm = 50,
    int               page     = 1,
    int               pageSize = 24)
```

Also add a validator or inline guard: `if (pageSize is < 1 or > 100) pageSize = 24;`

---

### B-10 · `GetNearbyStudios` — missing query-parameter validation

`lat`, `lng`, and `radiusKm` come in as raw query-string doubles. If a caller sends `lat=9999`, the bounding-box query still runs and returns no results — silent corruption. Add a guard:

```csharp
if (lat is < -90 or > 90 || lng is < -180 or > 180 || radiusKm is <= 0 or > 500)
    return Results.BadRequest("Invalid lat/lng/radiusKm.");
```

Add this at the top of the `GetNearbyStudios` endpoint handler before calling MediatR.

---

### B-11 · `SendAppointmentConfirmationCommand` — verify it exists and has try/catch

`ConfirmAppointmentCommand` calls `await sender.Send(new SendAppointmentConfirmationCommand(...), ct)` at line 39. Open `Pena_e_Arte.Application/Appointments/Commands/SendAppointmentConfirmationCommand.cs`. If it does not exist, the build is broken and that is the highest priority fix. If it exists, check that the email send inside it is in a try/catch — a failed email send should NOT roll back the appointment confirmation.

---

### B-12 · `DeletedAt` filter consistency

`Artist` entity has `DeletedAt` referenced in `RecordArtistView` (`.Where(a => a.Slug == slug && a.DeletedAt == null)`). Check that `DeletedAt` exists on the entity and that the global query filter also filters it out — otherwise there is a double-filter inconsistency, or worse, deleted artists still appear in public queries.

```bash
grep -rn "DeletedAt" --include="*.cs" "Pena e Arte" | grep -v obj/
```

Verify: the global query filter in `AppDbContext` includes `a.DeletedAt == null` for `Artist`. If it does, the explicit `&& a.DeletedAt == null` in `RecordArtistView` is redundant but harmless. If the global filter does NOT include it, add it to the context.

---

## Phase 2 — Frontend Bug Scan

### F-01 · `useEffect` used for data fetching

RTK Query is the only permitted data-fetching mechanism. `useEffect` is only permitted for browser side-effects (setting document title, initialising non-React libraries, SignalR connection lifecycle).

```bash
grep -rn "useEffect" --include="*.tsx" --include="*.ts" "Pena e Arte/frontend/src" \
  | grep -iv "test\|spec\|__tests__" \
  | grep -i "fetch\|api\|dispatch.*fetch\|axios\|http\|request\|query\|mutation"
```

Each hit is a **rule violation**. Replace with an RTK Query hook. If the `useEffect` is bridging to a query for an initialisation race, restructure using `skip` or `skipToken`.

---

### F-02 · TypeScript `any` violations

Strict mode prohibits `any`. Check all production source files (not test files — those are already using `eslint-disable` comments where justified).

```bash
grep -rn ": any\b\|as any\b\| any " \
  --include="*.tsx" --include="*.ts" \
  "Pena e Arte/frontend/src" \
  | grep -iv "__tests__\|\.test\.\|\.spec\.\|eslint-disable\|@ts-"
```

For each hit: replace with the correct type. If the type comes from an external library with no typings, use `unknown` and add a type guard, or use the library's own types.

---

### F-03 · Default exports on named components

Convention: no `export default` on React components (named exports only).

```bash
grep -rn "^export default function\|^export default " \
  --include="*.tsx" \
  "Pena e Arte/frontend/src" \
  | grep -iv "__tests__\|\.test\."
```

For each hit: convert to a named export. Update all import sites.

```bash
# Find import sites for a component named MyComponent:
grep -rn "import.*MyComponent\|from.*MyComponent" --include="*.tsx" --include="*.ts" "Pena e Arte/frontend/src"
```

---

### F-04 · `console.log` / `console.error` / `console.warn` in production paths

```bash
grep -rn "console\.\(log\|error\|warn\|info\|debug\)" \
  --include="*.tsx" --include="*.ts" \
  "Pena e Arte/frontend/src" \
  | grep -iv "__tests__\|\.test\.\|\.spec\."
```

Remove all console calls from production code. If the intent was to surface an error state to the user, replace with a Serilog-equivalent (the UI should show an error state, not log to the console).

---

### F-05 · `localStorage` accessed outside `authSlice`

`localStorage` is only permitted in `authSlice.ts` (for token persistence). Any other direct access is a data-storage rule violation.

```bash
grep -rn "localStorage\." \
  --include="*.tsx" --include="*.ts" \
  "Pena e Arte/frontend/src" \
  | grep -iv "authSlice\|__tests__\|\.test\."
```

For each hit: move the state to Redux store or React state as appropriate.

---

### F-06 · RTK Query cache tags — missing invalidation

Mutations that modify entities must invalidate the correct cache tags, otherwise stale data is shown after a write. Check every `useMutation` endpoint definition in all `*Api.ts` files.

```bash
grep -rn "providesTags\|invalidatesTags" \
  --include="*.ts" \
  "Pena e Arte/frontend/src"
```

Audit pattern:
- Every `query` endpoint that returns a list should `providesTags` with an entity type + list tag.
- Every `mutation` that creates, updates, or deletes should `invalidatesTags` with the matching type.
- If a mutation on entity A also affects a count shown in entity B's list (e.g. posting a review changes `ReviewCount` on the studio), the mutation must also invalidate B's tag.

Common gaps to check:
- `useCreateStudioReviewMutation` — does it invalidate the studio's public response tag?
- `useCreateArtistReviewMutation` — does it invalidate the artist's public response tag?
- Any appointment state change (confirm, complete, no-show) — does it invalidate `GetAppointments` and `GetMyAppointments`?

---

### F-07 · Missing `useEffect` cleanup for SignalR subscriptions

SignalR connections registered in a `useEffect` without a cleanup function will fire event handlers on unmounted components.

```bash
grep -rn "useSignalR\|connection\.on\|hubConnection" \
  --include="*.tsx" --include="*.ts" \
  "Pena e Arte/frontend/src" \
  | grep -iv "__tests__\|\.test\."
```

For each `connection.on(...)` that is not paired with a `connection.off(...)` in the same `useEffect` cleanup return, add the cleanup:

```typescript
useEffect(() => {
  connection.on("EventName", handler);
  return () => { connection.off("EventName", handler); };
}, [connection]);
```

---

### F-08 · Hardcoded API base URLs

The API base URL must come from `import.meta.env.VITE_API_URL`. Any hardcoded `http://localhost:5000` or `https://api.penaearte.com` in production source is a bug.

```bash
grep -rn "localhost:\|127\.0\.0\.1\|https://api\." \
  --include="*.tsx" --include="*.ts" \
  "Pena e Arte/frontend/src" \
  | grep -iv "__tests__\|\.test\.\|//\s"
```

---

## Phase 3 — Fix Protocol

After completing the scan, you have a list of confirmed bugs. Fix them in this priority order:

**P0 — Critical (fix immediately, do not proceed until these are resolved):**
1. Backend build failures (if any from Phase 0)
2. Missing `SendAppointmentConfirmationCommand` (B-11)
3. Any unapproved `IgnoreQueryFilters()` (B-04)
4. Any unprotected endpoint (B-02)

**P1 — High (fix before running the test loop):**
5. All `.First()` without null-guard (B-01)
6. PII in logs (B-03)
7. External service calls without try/catch (B-06)
8. TypeScript `any` violations (F-02)
9. `useEffect` data fetching (F-01)

**P2 — Medium (fix in the test loop):**
10. Missing validators (B-07)
11. Null-forgiving operators without justification (B-08)
12. `GetPortfolioFeed` parameter default (B-09)
13. `GetNearbyStudios` input validation (B-10)
14. RTK Query cache invalidation gaps (F-06)
15. SignalR cleanup (F-07)

**P3 — Low (fix after tests are green):**
16. Unbounded `ToListAsync` (B-05)
17. `console.log` statements (F-04)
18. `localStorage` outside authSlice (F-05)
19. Default exports on components (F-03)
20. Hardcoded URLs (F-08)

**For every fix:**
- Read the full file before editing.
- Make the minimal change that fixes the root cause.
- Do not refactor unrelated code.
- Add or update the test for the fixed behaviour (see Phase 4).
- Record the fix in the fix log.

---

## Phase 4 — Test-Fix Loop

This is the core loop. Run it until exit criteria are met.

### Loop Step 1 — Run the full test suite

```bash
# Backend
cd "Pena e Arte"
dotnet test --logger "console;verbosity=normal" 2>&1 | tee /tmp/test-backend-run.txt

# Frontend
cd "Pena e Arte/frontend"
pnpm test --run 2>&1 | tee /tmp/test-frontend-run.txt
```

### Loop Step 2 — Triage each failure

For every failing test:

a. **Read the test file** in full.  
b. **Read the implementation file** the test is covering.  
c. **Determine root cause:**
   - Is the implementation wrong? → Fix the implementation.
   - Is the test testing the wrong behaviour (genuinely incorrect expectation, not just outdated copy)? → Fix the test assertion with a comment explaining why.
   - Is a required file missing (e.g. a component that was planned but not yet created by a previous overnight prompt)? → Create a minimal stub that satisfies the test, or skip that test with `it.skip(...)` and note it in the fix log for follow-up.

d. **Never delete a test** to make it pass. If a test cannot pass because a feature does not exist yet, mark it `.skip` and add a `// TODO: un-skip after <PromptName> runs` comment.

### Loop Step 3 — Fix and re-run

Apply all fixes for the current failure batch, then go back to Loop Step 1.

### Exit Criteria

Do not stop until ALL of the following are true simultaneously:

- `dotnet build` exits 0 with 0 errors.
- `dotnet test` exits 0 (all tests pass or are explicitly `.Skip`-ped with a reason comment).
- `pnpm tsc --noEmit` exits 0.
- `pnpm lint` exits 0 (no errors; warnings are acceptable if they existed before your changes).
- `pnpm test --run` exits 0.

If after 5 full loop iterations a test is still failing and you cannot determine the root cause, mark it `.skip`, add a detailed comment, note it in the fix log under "Unresolved / Needs Human Review", and continue.

---

## Phase 5 — Final Verification

Once all tests pass, run one final clean check:

```bash
cd "Pena e Arte"

# Clean rebuild — no cached artifacts
dotnet clean && dotnet build --verbosity minimal

# Full test pass
dotnet test

# Frontend
cd frontend
pnpm tsc --noEmit
pnpm lint
pnpm test --run
```

If any step regresses, return to the loop.

---

## Phase 6 — Write the Fix Log

Create `docs/claude/bug-hunt-report-2026-06-25.md` with this structure:

```markdown
# Bug Hunt Report — 2026-06-25

## Summary
| Category | Found | Fixed | Skipped |
|---|---|---|---|
| P0 Critical | N | N | N |
| P1 High | N | N | N |
| P2 Medium | N | N | N |
| P3 Low | N | N | N |

## Baseline vs Final
| Metric | Before | After |
|---|---|---|
| Backend build errors | N | 0 |
| Backend test failures | N | 0 |
| TypeScript errors | N | 0 |
| ESLint errors | N | 0 |
| Frontend test failures | N | 0 |

## Fixed Bugs

### [P0] <Short description>
- **File:** `path/to/file.cs`, line N
- **Root cause:** ...
- **Fix:** ...
- **Test:** `path/to/test.cs` — test name

### [P1] <Short description>
...

## Unresolved / Needs Human Review

### <Issue title>
- **File:** `path/to/file.cs`
- **Description:** What the issue is and why it could not be fixed autonomously.
- **Recommendation:** What the developer should do.

## Tests Marked `.skip`

| Test file | Test name | Reason |
|---|---|---|
| ... | ... | Depends on <PromptName> not yet run |
```

---

## Hard Rules — Do Not Violate

1. **Never bypass the tenant global query filter** without an existing `// Approved:` comment. If in doubt, do not touch it.
2. **Never add a new NuGet or npm package.** All fixes must use what is already in the stack.
3. **Never remove a test** — only `.skip` with a comment.
4. **Never put business logic in an endpoint** — endpoints call MediatR only.
5. **Never log PII** — even in a new log statement you add while fixing a bug.
6. **Secrets never in source** — if you encounter a hardcoded secret while scanning, redact it immediately and note it as a P0.
7. **No `useEffect` for data fetching** — even as a "temporary" workaround.
8. **No `any`** — even in a new test helper you write.

---

## Completion Signal

When you are done, the final line of `bug-hunt-report-2026-06-25.md` must be:

```
## Status: COMPLETE — all tests green, build clean.
```

If there are unresolved issues:

```
## Status: PARTIAL — N issues require human review (see above).
```
