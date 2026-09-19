# Overnight Master Prompt — P1 Backlog, Group 1 ("Ready Now")

**Date:** 2026-09-09
**Mode:** Fully autonomous. No user present. Run until every phase exits clean.
**Run with:** `claude --dangerously-skip-permissions`
**Before starting:** `git add -A && git commit -m "chore: pre-P1-group1 checkpoint"` then
`git checkout -b feat/p1-group1-csv-style-pwa-2026-09-09`

---

## Context — read this before anything else

Source: `docs/claude/p1-backlog-master-build-spec-2026-09-09.md` (the full 19-item backlog,
re-verified against source 2026-09-09) and its own audit,
`docs/claude/audit-p1-backlog-2026-09-09.md`. This prompt builds exactly the three items from
that backlog's "Group 1 — ready now" bucket that survived a second, deeper source-verification
pass done specifically to prepare this prompt:

1. **CSV data export** (backlog item 13 / report §D23) — owner-only CSV export for clients,
   appointments, and revenue.
2. **Booking widget — style selection upfront** (backlog item 17 / report §A3) — a structured
   tattoo-style field on the booking form, replacing "style buried in the freeform description."
3. **Installable PWA** (backlog item 7 / report §B19) — manifest + service worker so the app
   installs to a home screen and loads its shell offline.

**Item 6 (Saved Payment Method) was in the original Group 1 and is deliberately NOT in this
prompt.** While preparing this spec, source verification turned up
`Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs`'s own doc comment and
`docs/claude/architecture.md`'s Decisions Log (`IPaymentProvider replaces IStripePaymentService
— 2026-07-31`): Stripe-aggregator card processing (`IStripePaymentService`) was deleted outright
— not migrated — because Stripe isn't currently available for the platform's Albania-registered
entity (Amendment A, Article 4(g) exposure). Flow A (client card deposits) now runs on
`NullPaymentProvider`, which fails closed by design until a replacement provider ("POK") is
chosen and integrated — a separate, later, not-yet-scoped ticket. Building Stripe Setup
Intents / card-on-file tonight would be built on a payment path that cannot function end-to-end
today and would very likely need to be redone once POK lands. This is exactly the "do not build
blind" case the source project exists to catch — confirmed with the user; item 6 waits.

**Everything below was re-verified against live source while writing this prompt, not
transcribed from the backlog spec's prose.** Where source disagreed with the backlog spec's
assumptions, this prompt follows source and says so inline.

Each phase is independent — no phase depends on another's output. Build in the order below
(lowest-risk first), but if one phase hits an unresolvable blocker, skip it, note why in the
final deliverable, and continue to the next.

---

## Required reading

```
CLAUDE.md                       — all 7 non-negotiable rules, especially #6/#7
docs/claude/architecture.md     — Feature Module Map; Decisions Log (read the
                                   "IPaymentProvider replaces IStripePaymentService" entry
                                   so you understand why item 6 is out of scope, and don't
                                   accidentally touch payment code while in this area);
                                   AllowAnonymous Exceptions table (Phase 2 touches a guest
                                   surface, though it adds no new anonymous endpoint)
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/conventions.md
```

---

## Constraints (identical to every prior overnight prompt in this repo)

- **No new npm or NuGet packages.** This was explicitly checked for Phase 3 (PWA) — the backlog
  spec's original proposal (`vite-plugin-pwa`) is rejected; Phase 3 hand-rolls the manifest and
  service worker instead. Phase 1 (CSV) has no CSV-writer NuGet package in this repo today
  (confirmed: no `CsvHelper` reference anywhere) — write CSV by hand with proper escaping, do not
  add one.
- No `useEffect` for data fetching. Approved exceptions as documented in every prior prompt.
- TypeScript strict mode, no `any`. Explicit C# types, no unclear `var`.
- No business logic in endpoints — MediatR only. Every command/query with request parameters has
  a FluentValidation validator.
- Tenant isolation via EF Core global query filters everywhere. None of these three phases need
  `IgnoreQueryFilters()` — if you find yourself reaching for it in any of them, stop and re-read
  the relevant section below, you've likely misread the requirement.
- Every endpoint has `.RequireAuthorization()` with the correct policy. Phase 1's three new
  endpoints and Phase 2's field addition to an existing endpoint are the only endpoint-surface
  changes tonight — no new `AllowAnonymous` route.
- Never log PII. Structured logs only (Serilog). No secrets in source.
- Every backend change ships with unit + integration tests. Every frontend change ships with
  component tests covering loading/error/empty states at minimum.
- **CLAUDE.md rule #7 (Help sync) applies to every phase** — each phase below states exactly
  what to update, including the phases where the honest answer is "nothing, and here's why."
- **Do not build blind.** If you discover an open product/business question in any phase that
  isn't already resolved below, stop building that specific sub-item, note it explicitly in the
  final deliverable, and move on — don't guess.

---

# PHASE 1 — CSV Data Export

Backlog item 13 / report §D23. Owner-only CSV export for clients, appointments, and revenue.

## Design decisions (pre-resolved — do not re-litigate)

- **Three typed endpoints, not one generic `/exports/{entity}` route.** The backlog spec
  proposed `GET /api/v1/exports/{entity}?format=csv` with a runtime string switch. This repo's
  actual convention is one typed MediatR query per action, one endpoint per route, grouped by
  feature (`ClientEndpoints.cs`, `AppointmentEndpoints.cs`, `ReportEndpoints.cs`) — a generic
  string-keyed entity switch doesn't fit that shape and would be the first non-conforming
  pattern in the API layer. Build three typed endpoints instead, one per feature file, each with
  its own MediatR query and validator.
- **Route shape mirrors the existing `.ics` precedent, not a `?format=` query param.**
  `AppointmentEndpoints.cs` already has `GET {id:guid}/calendar.ics` returning
  `Results.Content(icsContent, "text/calendar; charset=utf-8")` — the file-format extension
  lives in the path, not a query string. Match that: `export.csv`, not `?format=csv`.
- **Revenue export is a payment-level ledger, not the aggregated summary.** `ReportsPage.tsx`
  already shows `GetRevenueSummaryQuery`'s aggregated monthly-trend + per-artist breakdown — a
  CSV of that same small aggregate isn't what an owner handing data to an accountant needs. Build
  the revenue export as one row per `Payment` (date paid, client, artist, appointment date,
  amount, retained amount, status, method), reusing `Payment.RetainedAmount()` (already used by
  `GetRevenueSummaryHandler`) for the retained-amount column.
- **No `IgnoreQueryFilters()` anywhere in this phase** — all three exports are owner-scoped to
  the caller's own studio via the existing global query filter, exactly like the backlog spec
  itself calls out (distinct from the admin cross-tenant audit-log export, which this is not).
- **Buffered response via `Results.Content`, batched DB read via `AsAsyncEnumerable()`.** True
  chunked HTTP streaming (`Results.Stream` with an incremental writer) is more machinery than
  this app's actual scale needs — a single tattoo studio's client/appointment/payment history is
  hundreds to low thousands of rows, not millions — and no existing endpoint in this codebase
  does it. Match the existing `GetIcs` precedent (`Results.Content`, a single string result) but
  build that string by iterating the query with `AsAsyncEnumerable()` and appending to a
  `StringBuilder`, rather than `ToListAsync()`-ing the whole table first — this avoids holding two
  full in-memory copies (the EF entities and the CSV string) for the largest studios, at
  negligible extra complexity. If a studio's actual data volume ever makes this insufficient,
  that's a real streaming migration, not a change to make speculatively tonight.
- **UTF-8 BOM prefix.** Client/artist names in this app can contain Albanian diacritics — prefix
  every CSV response with `﻿` so Excel (the overwhelmingly likely consumer, per the backlog
  spec's own framing of "hand to an accountant") detects UTF-8 correctly instead of mis-rendering
  non-ASCII characters.
- **Optional `from`/`to` on Appointments and Revenue, no date range on Clients.** Mirrors
  `GetRevenueSummaryQuery`'s existing nullable-`DateTime?` pattern. Clients aren't date-bounded
  (a client "belongs to" the studio indefinitely; there's no natural date axis to filter by for
  an export whose whole point is a full roster).

## Backend

### 1-A: Shared CSV escaping helper — new file

`Pena_e_Arte.Application/Common/CsvUtils.cs`:

```csharp
using System.Text;

namespace Pena_e_Arte.Application.Common;

/// <summary>
/// Minimal hand-rolled CSV writer — this repo has no CsvHelper (or equivalent) NuGet
/// package and CLAUDE.md's "no new NuGet packages" rule applies to overnight prompts.
/// RFC 4180-shaped: fields containing a comma, quote, or newline are wrapped in quotes,
/// with internal quotes doubled. Shared by every CSV export query so escaping logic
/// exists in exactly one place.
/// </summary>
public static class CsvUtils
{
    public const string Bom = "﻿";

    public static string EscapeField(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        bool needsQuoting = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuoting) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    public static void AppendRow(StringBuilder sb, params string?[] fields)
    {
        sb.AppendLine(string.Join(',', fields.Select(EscapeField)));
    }
}
```

### 1-B: Clients export

New file `Pena_e_Arte.Application/Clients/Queries/ExportClientsCsvQuery.cs`:

```csharp
public record ExportClientsCsvQuery : IRequest<string>;

public class ExportClientsCsvHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<ExportClientsCsvQuery, string>
{
    public async Task<string> Handle(ExportClientsCsvQuery query, CancellationToken ct)
    {
        StringBuilder sb = new();
        sb.Append(CsvUtils.Bom);
        CsvUtils.AppendRow(sb, "First Name", "Last Name", "Email", "Phone", "Assigned Artist",
            "Client Since", "Marketing Opt-In");

        IQueryable<Client> clientsQuery = db.Clients
            .AsNoTracking()
            .Include(c => c.Artist)
            .Where(c => c.StudioId == tenant.StudioId)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName);

        await foreach (Client c in clientsQuery.AsAsyncEnumerable().WithCancellation(ct))
        {
            CsvUtils.AppendRow(sb,
                c.FirstName, c.LastName, c.Email, c.Phone,
                c.Artist is null ? "" : $"{c.Artist.FirstName} {c.Artist.LastName}",
                c.CreatedAt.ToString("yyyy-MM-dd"),
                c.MarketingOptIn ? "Yes" : "No");
        }

        return sb.ToString();
    }
}
```

Check `Artist` entity for its actual name fields before assuming `FirstName`/`LastName` (verify —
don't guess; `Client.FirstName`/`LastName` are confirmed but `Artist`'s shape wasn't re-checked
while writing this prompt).

No validator needed — this query takes no parameters.

### 1-C: Appointments export

New file `Pena_e_Arte.Application/Appointments/Queries/ExportAppointmentsCsvQuery.cs`, same
shape:

```csharp
public record ExportAppointmentsCsvQuery(DateTime? From, DateTime? To) : IRequest<string>;
```

Columns: Date, End Time, Duration (min), Artist, Client, Status, Deposit Status, Deposit Amount,
Cancellation Reason. Join `Client`/`Artist` for names the same way as 1-B. Filter
`a.Date >= From` / `a.Date <= To` when provided, matching `GetRevenueSummaryQuery`'s existing
optional-range pattern — read that handler once more here for the exact null-coalescing shape to
mirror.

Validator (new file, `Pena_e_Arte.Application/Appointments/Validators/ExportAppointmentsCsvValidator.cs`):
`To` must be `>= From` when both are provided — same rule any other date-range query in this
codebase should already be enforcing; if you find an existing validator doing this for a
different query, copy its exact FluentValidation shape rather than inventing a new one.

### 1-D: Revenue export

New file `Pena_e_Arte.Application/Reports/Queries/ExportRevenueCsvQuery.cs`:

```csharp
public record ExportRevenueCsvQuery(DateTime? From, DateTime? To) : IRequest<string>;
```

Columns: Paid At, Client, Artist, Appointment Date, Amount, Retained Amount, Status, Method,
Provider. Source: `db.Payments.Where(p => p.Status == PaymentStatus.Paid || p.Status ==
PaymentStatus.Refunded)` (same inclusion rule `GetRevenueSummaryHandler` already uses and
explains in its own comment — a partially-refunded payment still has `Status == Refunded` but a
nonzero `RetainedAmount()`), filtered by `PaidAt` against `From`/`To` when provided. Join through
`Payment.Appointment`/`Payment.Client`/`Payment.Appointment.Artist` for the display columns —
confirm the exact navigation path compiles; `Payment.Appointment` is confirmed to exist, but
whether `Artist` is reachable directly from there or needs a separate lookup wasn't re-verified
while writing this prompt.

Validator: same `To >= From` shape as 1-C.

### 1-E: Endpoints

`Pena_e_Arte.API/Endpoints/ClientEndpoints.cs` — add inside the existing `MapClientEndpoints`
group:

```csharp
group.MapGet("/export.csv", ExportClientsCsv).RequireAuthorization("OwnerOnly");
```

```csharp
private static async Task<IResult> ExportClientsCsv(ISender mediator, CancellationToken ct)
{
    string csv = await mediator.Send(new ExportClientsCsvQuery(), ct);
    return Results.Content(csv, "text/csv; charset=utf-8");
}
```

`Pena_e_Arte.API/Endpoints/AppointmentEndpoints.cs` — add to the existing group:

```csharp
group.MapGet("/export.csv", ExportAppointmentsCsv).RequireAuthorization("OwnerOnly");
```

with a `DateTime? from, DateTime? to` bound-from-query-string handler, same shape as
`GetAppointments`'s existing `from`/`to` params two lines above it in that file.

`Pena_e_Arte.API/Endpoints/ReportEndpoints.cs` — add to the existing group:

```csharp
group.MapGet("/revenue/export.csv", ExportRevenueCsv).RequireAuthorization("OwnerOnly");
```

Mirror `GetRevenueSummary`'s existing handler shape exactly (same `from`/`to` params) for the new
`ExportRevenueCsv` handler right below it.

**Route-collision check:** `ClientEndpoints.cs`'s existing `{clientId:guid}` route and the new
literal `/export.csv` route must not collide under ASP.NET Core's routing — a literal segment
should win over a `:guid`-constrained parameter automatically, but confirm this by hitting both
routes in an integration test (a GUID-shaped client id must still resolve to `GetClientById`, and
`export.csv` must still resolve to the new handler) rather than assuming.

## Frontend

### 1-F: Shared authenticated-download helper — new file

**Important, verified while writing this prompt, not assumed:** this app stores its JWT in
`localStorage`/`sessionStorage` (`authSlice.ts`) and `baseQuery.ts`'s `prepareHeaders` attaches
both `Authorization: Bearer <token>` and `X-Tenant-Id` from Redux state on every RTK Query
request. A plain `<a href={...} download>` link — which is what
`AppointmentDetailPage.tsx`'s existing "Add to Calendar (.ics)" link uses — does **not** carry
either header on a raw browser navigation. That existing `.ics` link is very likely broken today
for exactly this reason; **that's a separate, pre-existing bug, out of scope for tonight** (see
"Out of Scope" below) — do not copy its pattern, and do not fix it as a drive-by unless it's
trivial to also cover once the helper below exists.

New file `frontend/src/shared/utils/downloadAuthenticatedFile.ts`:

```typescript
import { store } from "@/app/store";

/** Fetches an API endpoint with the same auth headers RTK Query's baseQuery attaches
 *  (Authorization + X-Tenant-Id), then triggers a browser download of the response body
 *  under the given filename. For endpoints that return a file (CSV, etc.) rather than
 *  JSON, which RTK Query isn't a natural fit for. */
export async function downloadAuthenticatedFile(path: string, filename: string): Promise<void> {
  const { token, tenantId } = store.getState().auth;
  const headers: Record<string, string> = {};
  if (token)    headers.Authorization = `Bearer ${token}`;
  if (tenantId) headers["X-Tenant-Id"] = tenantId;

  const response = await fetch(`/api/v1/${path}`, { headers });
  if (!response.ok) throw new Error(`Download failed (${response.status})`);

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
```

Check `store.ts`'s actual `RootState.auth` shape before assuming `token`/`tenantId` field names —
`baseQuery.ts`'s `prepareHeaders` (read above) uses exactly those two, so it should match, but
confirm against the real `authSlice.ts` state shape, not just the destructuring in `baseQuery.ts`.

New test file `frontend/src/shared/utils/__tests__/downloadAuthenticatedFile.test.ts` — mock
`fetch` and the Redux store, assert the two headers are set, assert a non-OK response throws,
assert the blob/anchor/revoke sequence runs on success (jsdom supports `URL.createObjectURL`
after a minimal shim if it isn't already globally available in this test environment — check
`src/test/setup.ts` first for an existing shim before adding one).

### 1-G: "Export CSV" buttons

- `frontend/src/features/clients/components/ClientListPage.tsx` — add an "Export CSV" button in
  the header toolbar (next to the existing search/filter row), gated on
  `usePermission(Role.Owner)` (mirror this file's existing `usePermission(Role.Artist)` call for
  `canCreate` — same hook, different role), calling
  `downloadAuthenticatedFile("clients/export.csv", "clients.csv")` with a loading-state disable
  while the request is in flight and a `sonner` toast on failure (check how this file or a
  sibling already surfaces toasts — `sonner` is already a dependency, per `main.tsx`'s
  `<Toaster />`).
- `frontend/src/features/appointments/components/SchedulePage.tsx` — same button, same
  `Role.Owner` gate, calling `downloadAuthenticatedFile("appointments/export.csv", "appointments.csv")`.
  This page already imports `Role` and uses `useAppSelector` for role checks — mirror its
  existing pattern rather than introducing `usePermission` if this file's convention differs from
  `ClientListPage.tsx`'s.
- `frontend/src/features/reports/components/ReportsPage.tsx` — same button in the page header
  (this whole page is already `OwnerOnly`-routed per the Feature Module Map, so no additional
  role gate is needed here — confirm this against `router.tsx` before assuming), calling
  `downloadAuthenticatedFile("reports/revenue/export.csv", "revenue.csv")`.

## Tests

- Backend: handler tests for all three export queries (empty studio → header row only; a few
  rows → correct escaping of a name containing a comma and a quote; `From`/`To` filtering on
  Appointments/Revenue). Validator tests for the `To >= From` rule. Integration test confirming
  `OwnerOnly` — an artist or client token gets 403, not 200 with wrong data.
- Frontend: `downloadAuthenticatedFile` unit tests (above). Component tests on all three pages
  confirming the button is hidden for non-owners and calls the right path for owners (mock the
  helper, don't actually trigger a real download in jsdom).

## Help sync (rule #7)

Per the backlog spec's own call, and confirmed correct: brief additions to the existing owner
help entries for clients, appointments, and reports (find each by its `id` in
`helpContent.ts` — likely something in the `owner-clients-list`/`owner-schedule`/`owner-reports`
family based on this file's existing `client-*` naming convention; the owner-side ids weren't
individually re-verified while writing this prompt) — one added step each ("Click 'Export CSV' to
download a spreadsheet-ready copy"), not new standalone articles. Standalone manual: same three
sections in `frontend/public/user-manual/index.html`, one sentence each. No onboarding-tour
change — this is a small affordance on existing pages, not a new nav item or primary action,
matching the precedent the backlog spec itself cites for this exact judgment call (A10/A11-style
additions).

---

# PHASE 2 — Booking Widget: Style Selection Upfront

Backlog item 17 / report §A3.

## Design decisions (pre-resolved — do not re-litigate)

- **The new field lives on `BookingIntake`, not `Appointment`.** The backlog spec said "add
  ServiceType/Style ... to Appointment." Source disagrees: `BookingIntake` (one-to-one with
  `Appointment`) already exists specifically to hold "what I want done at this booking" —
  `TattooDescription`, `SafetyNotes`, `DesiredPlacement`, `ReferralSource` all live there, with
  an explicit doc comment explaining why this is kept separate from `Appointment` itself. A style
  field is exactly this category of data — it belongs in `BookingIntake` alongside
  `TattooDescription`, not bolted onto `Appointment`.
- **Reuse `TattooStyle`, correctly identified as constants, not an enum.** The backlog spec
  called it "the existing `TattooStyle` enum." It's actually
  `Pena_e_Arte.Domain/Constants/TattooStyle.cs` — a `static class` of eight `const string`
  values plus a `TattooStyle.All` list, backing `PortfolioImage.Style` (itself a `string?`, not
  an enum-typed column). Match that existing convention exactly: the new field is `string?`, its
  validator checks membership in `TattooStyle.All` (mirroring how `ReferralSource` is validated
  against `BookingContentValidationRules.ValidReferralSources` in `CreateAppointmentValidator`),
  not a new C# `enum`.
- **The field is optional, not required.** `ReferralSource` — the closest existing precedent on
  this same form — is optional. Style should be too: forcing a choice adds friction to the
  booking flow's already-required fields (`TattooDescription`), and a client who genuinely wants
  "surprise me, artist's choice" or doesn't know the terminology shouldn't be blocked from
  booking. If product later wants it required, that's a one-line validator change, not a reason
  to over-build tonight.
- **Do NOT source the field's options from `Artist.Specializations`.** The backlog spec said to
  prefill from "the chosen artist's Specializations, same data source `ArtistDetailPage.tsx`
  already displays." Checked against source: `Artist.Specializations` is a freeform
  `string?` (max 1000 chars, validated only for length in `CreateArtistValidator`/
  `UpdateArtistValidator`) — owner-typed text like "traditional, fine line, custom pieces," not a
  structured list of `TattooStyle` values. There is no reliable way to parse that into valid
  `TattooStyle.All` options, and attempting fuzzy text matching is fragile, out of scope, and not
  what this item needs. Source the Select's options from `TattooStyle.All` directly (via the
  shared frontend constant created in 2-D below) — the same fixed, canonical list `PortfolioImage`
  and the Discover page's style filter chips already use.
- **This single change covers both the authenticated and guest booking flows automatically, with
  no separate guest-side work needed.** `CreateGuestAppointmentRequest.Booking` is itself a
  `CreateAppointmentRequest` (`Booking.ClientId` is ignored) — the guest handler passes it
  straight through to the same internal creation logic the authenticated flow uses. On the
  frontend, `TattooIntakeFields.tsx` is already an explicitly shared component ("shared by the
  authenticated BookAppointmentForm and the guest checkout form," per its own doc comment) —
  adding the Style field there covers both `BookAppointmentForm.tsx` and the guest checkout form
  in one component edit. Do not touch the guest form separately; if you find yourself editing two
  form components for this field, stop, you've misread the shared-component structure.

## Backend

### 2-A: `BookingIntake.cs`

Add, right after `TattooDescription`:

```csharp
/// <summary>Optional tattoo style tag for this specific booking. Values are one of
/// TattooStyle's constants — same convention PortfolioImage.Style already uses. Null
/// means the client didn't specify (e.g. "artist's choice" / unsure).</summary>
public string? Style { get; set; }
```

### 2-B: Migration

`dotnet ef migrations add AddStyleToBookingIntake --project Pena_e_Arte.Infrastructure`. Strip
the BOM the tool adds before committing (per `feedback_windows_tooling_gotchas` /
`CONTRIBUTING.md` — CI's format-check rejects it, same step every prior migration in this repo
has needed).

### 2-C: `CreateAppointmentRequest.cs` (Contracts)

Add `Style` right after `TattooDescription`:

```csharp
public record CreateAppointmentRequest(
    Guid? ArtistId,
    Guid ClientId,
    DateTime Date,
    int DurationMinutes,
    string? Notes,
    string TattooDescription = "",
    string? Style = null,
    string? SafetyNotes = null,
    ...
```

**Positional record — every other file constructing a `CreateAppointmentRequest` (test fixtures,
the frontend request builder) needs its argument order checked, not just the two call sites named
below.** Grep for `new CreateAppointmentRequest(` and `CreateAppointmentRequest(` across both
`tests/` and any place a raw object literal builds this shape before considering this step done.

### 2-D: `CreateAppointmentCommand.cs` handler + `CreateAppointmentValidator.cs`

In the handler, inside the `appointment.Intake = new BookingIntake { ... }` object initializer,
add `Style = req.Style,` alongside the existing `TattooDescription = req.TattooDescription,` line.

In the `Map` method's `AppointmentResponse` constructor call, add `a.Intake?.Style` in the
corresponding position (see 2-E for where that lands in the response contract).

In `CreateAppointmentValidator.cs`, add right after the `TattooDescription` rule:

```csharp
RuleFor(x => x.Request.Style)
    .Must(s => s is null || TattooStyle.All.Contains(s))
    .WithMessage("Style must be one of: " + string.Join(", ", TattooStyle.All));
```

(mirrors the existing `ReferralSource` rule's exact shape three lines below where you're adding
this).

### 2-E: `AppointmentResponse.cs` (Contracts)

Add `Style` after `TattooDescription`:

```csharp
public record AppointmentResponse(
    ...
    string? TattooDescription = null,
    string? Style = null,
    string? SafetyNotes = null,
    ...
```

Positional record, same caution as 2-C — check every construction site, and update
`GetMyEarningsQuery`/`GetAppointmentsQuery`/any other handler that maps into `AppointmentResponse`
by position rather than through the shared `CreateAppointmentCommand.Map` helper (search for
`new AppointmentResponse(` across the whole `Pena_e_Arte.Application` tree — `Map` is confirmed
to be one call site but was not confirmed to be the only one while writing this prompt).

### 2-F: `CreateGuestAppointmentRequest` — confirm no change needed

`CreateGuestAppointmentRequest.Booking` is a `CreateAppointmentRequest`, so 2-C already covers the
guest path. Read `CreateGuestAppointmentCommand.cs` in full once more here to confirm nothing
between `req.Booking` and the eventual `CreateAppointmentCommand`-equivalent internal call
re-maps fields explicitly (i.e., confirm it's a genuine passthrough, not a field-by-field
reconstruction that would silently drop `Style`) before concluding this step is a no-op.

## Frontend

### 2-G: Extract the shared style-options constant — new file

`PortfolioFeed.tsx` already has a local `STYLES` constant whose 8 real entries are the frontend
mirror of `TattooStyle.All`, plus a `{value: "", label: "All"}` sentinel for its own "no filter"
chip. `TattooStyle.cs`'s own doc comment already says "keep in sync with STYLES constant in
PortfolioFeed.tsx" — meaning this duplication is already a known, named risk; fix it as part of
this change rather than creating a third copy.

New file `frontend/src/shared/constants/tattooStyles.ts`:

```typescript
/** Frontend mirror of Pena_e_Arte.Domain.Constants.TattooStyle.All — keep both in sync. */
export const TATTOO_STYLE_OPTIONS: ReadonlyArray<{ value: string; label: string }> = [
  { value: "traditional",     label: "Traditional"     },
  { value: "realism",         label: "Realism"         },
  { value: "blackwork",       label: "Blackwork"       },
  { value: "geometric",       label: "Geometric"       },
  { value: "watercolor",      label: "Watercolor"      },
  { value: "fineline",        label: "Fineline"        },
  { value: "neo-traditional", label: "Neo-Traditional" },
  { value: "japanese",        label: "Japanese"        },
];
```

Update `PortfolioFeed.tsx`'s local `STYLES` to `[{ value: "", label: "All" }, ...TATTOO_STYLE_OPTIONS]`,
importing the new constant instead of hand-listing the eight entries a second time. Run
`PortfolioFeed`'s existing tests after this change — the chip order/labels must be byte-identical
to before, this is a pure refactor of where the list lives, not a content change.

### 2-H: `tattooIntakeValidation.ts`

Add `style: string` to `TattooIntakeValues` (default `""`, matching `referralSource`'s existing
empty-string-means-unset convention on this same interface — not `string | null`, to stay
consistent with every other field on this type). No new validation function needed — the field is
optional, there's no "when X then Y is required" rule like `ReferralSourceOther`'s.

### 2-I: `TattooIntakeFields.tsx`

Add a Style `<Select>` between the existing "What are you looking to get done?" textarea and the
"How did you hear about us?" select (style is closer to "what," so it reads better placed there
than after the referral-source question):

```tsx
import { TATTOO_STYLE_OPTIONS } from "@/shared/constants/tattooStyles";

// ...

<div className="space-y-1.5">
  <FieldLabel htmlFor="style">Style (optional)</FieldLabel>
  <Select
    value={value.style}
    onValueChange={(v) => onChange({ ...value, style: v })}
  >
    <SelectTrigger id="style">
      <SelectValue placeholder="Select a style" />
    </SelectTrigger>
    <SelectContent>
      {TATTOO_STYLE_OPTIONS.map(({ value: v, label }) => (
        <SelectItem key={v} value={v}>{label}</SelectItem>
      ))}
    </SelectContent>
  </Select>
</div>
```

### 2-J: Wire the new field through to the request

`BookAppointmentForm.tsx`'s submit handler already spreads `intake.tattooDescription`,
`intake.referralSource`, etc. into the outgoing request object (around the existing
`tattooDescription: intake.tattooDescription,` line) — add `style: intake.style || null,` there,
and confirm the guest checkout form's equivalent submit handler does the same (it was not
re-verified while writing this prompt whether the guest form assembles its request the same
inline way or through a shared function — check both `BookAppointmentForm.tsx` and the guest
checkout form's submit handlers explicitly, don't assume symmetry from the shared
`TattooIntakeFields` component alone).

`appointmentsApi.ts` / `appointment.types.ts` — add `style?: string | null` to whatever local
TypeScript type mirrors `CreateAppointmentRequest`/`AppointmentResponse` on the frontend.

## Tests

- Backend: validator test (`Style` outside `TattooStyle.All` rejected, `null` and any valid
  value accepted). Handler test confirming `Style` persists onto `BookingIntake` and round-trips
  through `AppointmentResponse`. Existing `CreateAppointmentHandlerTests` and
  `CreateGuestAppointmentHandlerTests` should still pass unmodified except for any positional
  `AppointmentResponse`/`CreateAppointmentRequest` construction that now needs the extra
  argument — fix those, don't skip them.
- Frontend: `TattooIntakeFields.test.tsx` — new test asserting the Style select renders all 8
  options and calls `onChange` correctly. `BookAppointmentForm.test.tsx` and the guest form's
  equivalent test — assert `style` is included (or omitted as `null`) in the submitted payload.
  `PortfolioFeed.test.tsx` — must still pass unmodified after 2-G's refactor.

## Help sync (rule #7)

`helpContent.ts` — update the existing `client-book-appointment` article: add one line to its
`steps` array mentioning the optional style field, right after the existing "Describe what you're
looking to get done" step. No new `keywords` entries needed beyond maybe `"style"`/`"tattoo
style"` — check the existing `keywords` array first, it may already be broad enough.
Standalone manual — same one-line addition to whichever section documents the booking form
(search `user-manual/index.html` for the guest/client booking section by its existing anchor,
likely `#guest-book-appointment` or similar based on this file's naming pattern seen elsewhere —
confirm the actual anchor id before editing). No onboarding-tour change — this is a new field on
an existing step of an existing form, not a new page or primary action; if any tour step's
selector specifically targets the intake-fields area by a fragile selector that this change's
DOM insertion could break, verify and fix it, but don't add a new step.

---

# PHASE 3 — Installable PWA

Backlog item 7 / report §B19. Frontend-only. **No new npm package** — confirmed against
`frontend/package.json` while writing this prompt: no PWA-related dependency exists today, and
the user explicitly chose the hand-rolled approach over adding `vite-plugin-pwa`.

## Design decisions (pre-resolved — do not re-litigate)

- **Hand-written `manifest.json` + hand-written service worker, registered manually in
  `main.tsx`.** No build-plugin automation — write both files as static/plain assets and wire the
  registration call yourself. This is more code than `vite-plugin-pwa` would generate, in exchange
  for zero new dependencies, matching every other overnight prompt's standing constraint.
- **Cache the app shell only — never cache `/api/*` responses.** This app's data is
  real-time/multi-tenant-sensitive; a stale cached appointment list or client roster is worse
  than a network-error state. `verifier-gui`'s own notes already treat network-error states as
  correct, expected UI here — don't build around that assumption, build with it. The service
  worker's fetch handler must explicitly bypass its cache for any request whose path starts with
  `/api/` or `/hubs/` (the SignalR hub path) — pass those straight to the network, no
  interception.
- **Icons: reuse the existing brand mark, generate PNGs via a system tool if one is available on
  the build machine, hand-draw a solid-color fallback otherwise — either way, do not add an npm
  package to solve this.** `frontend/public/favicon.svg` is the only existing brand asset (no PNG
  icons exist today). A manifest's `icons` array needs raster PNGs (192×192 and 512×512 minimum)
  for reliable "Add to Home Screen" behavior across browsers, particularly iOS Safari, which
  doesn't reliably rasterize an SVG manifest icon itself. Check for `rsvg-convert`, `magick`
  (ImageMagick), or `inkscape` on the build machine (`command -v <tool>`) and convert
  `favicon.svg` to both sizes if one exists; if none is available, generate two solid
  background PNGs in the app's existing dark-purple theme color (check `tailwind.config`/
  `index.css` for the exact hex value already in use — don't invent a new brand color) with a
  simple centered mark, via any already-available means (Python's `Pillow`, if present in the
  environment — check with `python3 -c "import PIL"` first — or a minimal hand-written PNG
  encoder as a last resort). Either path is acceptable; what matters is two valid PNGs land at
  `frontend/public/icons/icon-192.png` and `frontend/public/icons/icon-512.png` without a new
  package appearing in `package.json`.
- **`display: standalone`, theme color matches the existing dark-purple palette** (same value
  used for `PortfolioFeed.tsx`'s active-chip background, `bg-violet-600` per the source read
  above, or whatever the actual computed hex is — check Tailwind's resolved value, don't guess a
  new one).

## Build

### 3-A: `frontend/public/manifest.json` — new file

```json
{
  "name": "TattooOS",
  "short_name": "TattooOS",
  "description": "Appointment booking and studio management for tattoo studios.",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#0a0a0f",
  "theme_color": "#7c3aed",
  "icons": [
    { "src": "/icons/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icons/icon-512.png", "sizes": "512x512", "type": "image/png" }
  ]
}
```

Verify `background_color`/`theme_color` against the app's actual resolved dark-mode background
and accent colors before shipping these placeholder hexes — read the Tailwind config / root CSS
custom properties, don't guess.

### 3-B: Link the manifest — `index.html`

Add `<link rel="manifest" href="/manifest.json">` and a `<meta name="theme-color" content="...">`
matching 3-A's `theme_color`, in the `<head>`.

### 3-C: Service worker — `frontend/public/sw.js` (new file)

Hand-written, Cache-API-based (no Workbox — that would be a new dependency too, even as a CDN
script it's not a locally-vetted asset and this app has no existing pattern of loading
third-party scripts from a CDN at runtime). Cache-first for the app shell's static build output,
network-only passthrough for `/api/` and `/hubs/`:

```javascript
const CACHE_NAME = "tattooos-shell-v1";
const SHELL_ASSETS = ["/", "/manifest.json"];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(SHELL_ASSETS))
  );
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(keys.filter((k) => k !== CACHE_NAME).map((k) => caches.delete(k)))
    )
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  const url = new URL(event.request.url);

  // Never intercept API calls or the SignalR hub — always hit the network.
  if (url.pathname.startsWith("/api/") || url.pathname.startsWith("/hubs/")) {
    return;
  }

  // Cache-first for everything else (the built app shell + static assets),
  // falling back to network and caching the result for next time.
  event.respondWith(
    caches.match(event.request).then((cached) => {
      if (cached) return cached;
      return fetch(event.request).then((response) => {
        if (response.ok) {
          const clone = response.clone();
          caches.open(CACHE_NAME).then((cache) => cache.put(event.request, clone));
        }
        return response;
      });
    })
  );
});
```

Bump `CACHE_NAME` (e.g. `-v2`) on any future change to this file so returning visitors pick up
the new shell rather than serving a stale cached one indefinitely — note this in a code comment
so a future session doesn't lose the convention.

### 3-D: Register it — `main.tsx`

```typescript
if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/sw.js").catch((err) => {
      console.error("Service worker registration failed:", err);
    });
  });
}
```

Place this after the existing render call, not before — don't block first paint on registration.
A raw `console.error` here is acceptable per this app's existing convention for
client-side-only, non-business-logic failures (check `ErrorBoundary.tsx`'s own error logging for
precedent before assuming — CLAUDE.md rule #5's "no console.log in production paths" is
Serilog/structured-logging-for-the-backend in intent; confirm whether any existing frontend code
already uses `console.error` for this class of non-critical client failure, and match it exactly
rather than assuming the rule extends to the frontend the same way).

### 3-E: `vite.config.ts`

Confirm `frontend/public/*` (manifest, icons, `sw.js`) is copied to the build output root as-is —
Vite does this automatically for anything in `public/` with no config change needed; verify by
running `pnpm build` and checking `dist/manifest.json`, `dist/sw.js`, and `dist/icons/*` exist
after the build, rather than assuming.

## Tests

- A test asserting `manifest.json` is valid JSON and has the required fields (`name`,
  `start_url`, `display`, `icons` with at least one valid entry) — a plain unit test reading and
  parsing the file, not a browser-level PWA audit (this repo has no Lighthouse CI step to hook
  into, and adding one is out of scope).
- A test for the service worker's fetch-handler routing logic is impractical to unit test
  meaningfully in `jsdom` (no real Service Worker execution context) — skip a unit test for
  `sw.js` itself, but do add a manual verification step to the build checklist below (open the
  built app, confirm the service worker registers in DevTools' Application tab, confirm an
  `/api/` request in the Network tab shows as going to the network, not "(ServiceWorker)").

## Help sync (rule #7)

None needed — matches the backlog spec's own correct call, same precedent as A10/A11: this is
infrastructure with no user-facing setting to document, not a new page or workflow.

---

## Out of Scope — flagged explicitly, not silently dropped

1. **Item 6 (Saved Payment Method)** — see Context above. Waits on the POK (or other) payment
   provider decision.
2. **`AppointmentDetailPage.tsx`'s "Add to Calendar (.ics)" link is very likely broken today** —
   discovered while designing Phase 1's authenticated-download helper (1-F). It's a plain
   `<a href download>` against an endpoint gated `RequireAuthorization("ClientAndAbove")`, and
   this app's auth (JWT in `localStorage`/`sessionStorage`, attached via `Authorization` +
   `X-Tenant-Id` headers on RTK-Query-managed requests only) gives a raw browser navigation
   neither header. This wasn't confirmed to actually 401 in a running instance — that's the first
   step of a real follow-up ticket, not something to fix as a drive-by here. If it turns out to
   already work (e.g. some other mechanism grants read access to this one route without the
   headers), the finding is void; if it doesn't work, `downloadAuthenticatedFile` (1-F) is the
   fix, applied to that link too.
3. **`Artist.Specializations` being freeform text, not structured `TattooStyle` values, may be
   worth its own decision later** (e.g. migrating it to a multi-select of `TattooStyle.All`
   values, enabling real client-facing filtering/matching by style) — noted while scoping Phase 2,
   genuinely a separate, larger product decision, not touched here.

---

# PHASE 4 — Backlog Carry-Forward

Update `docs/claude/p1-backlog-master-build-spec-2026-09-09.md` (add a dated addendum section —
do not fork a second document) to record:

- Move items 13, 17, and 7 from "Group 1" to "shipped 2026-09-09," each with a one-line pointer
  to the files touched (not a re-explanation).
- Item 6 stays in the backlog, now explicitly blocked on the POK/payment-provider decision rather
  than "ready now" — update its build-order grouping to reflect that (it belongs with the
  decision-gated items now, not Group 1).
- Note the two follow-up items surfaced in "Out of Scope" above (the `.ics` download's likely
  auth gap; `Artist.Specializations` structuring) as newly-identified, separately-scoped
  candidates — don't let them silently disappear.

---

## Final self-check before declaring done

```bash
dotnet build      # 0 errors, 0 warnings
dotnet test       # all green, including every new test above
cd frontend
pnpm tsc --noEmit # 0 TypeScript errors
pnpm lint
pnpm test --run   # all green, including every new test above
pnpm build        # 0 errors; confirm dist/manifest.json, dist/sw.js, dist/icons/* exist (3-E)
```

Plus, walk through each explicitly:

- Every new endpoint (`export.csv` ×3) has `RequireAuthorization("OwnerOnly")` — verified with an
  actual non-owner-token integration test, not just read from the code.
- No `IgnoreQueryFilters()` was added anywhere in Phases 1–3.
- `Style` round-trips end-to-end: submit a booking with a style through the actual
  `CreateAppointmentCommand` handler in a test, read it back off `AppointmentResponse`.
- The guest booking flow was actually exercised (not just assumed symmetric with the authenticated
  flow) — a `CreateGuestAppointmentCommand` test asserting `Style` survives the passthrough.
- Every positional record touched in Phase 2 (`CreateAppointmentRequest`, `AppointmentResponse`)
  had every construction site across `Application`, `API`, and `tests/` checked and updated, not
  just the two call sites this prompt named explicitly.
- The service worker's fetch handler was manually verified (per Phase 3's Tests section) to never
  intercept `/api/` or `/hubs/` traffic.
- `helpContent.ts`, the standalone manual, and (where applicable) onboarding-tour steps are
  updated for every phase that shipped a user-facing change — Phase 1 and Phase 2 both have
  real Help-sync obligations; Phase 3 correctly has none, stated explicitly rather than silently
  skipped.
- Every design decision stated as "pre-resolved" above was actually followed as specified — if
  live source didn't match an assumption this prompt made (the `Artist` entity's exact name
  fields in 1-B, the `AppointmentResponse` construction sites in 2-E, the frontend `auth` state
  shape in 1-F, the resolved theme-color hex in 3-A, or anything else flagged inline as "not
  re-verified while writing this prompt"), that deviation and what you did instead is called out
  explicitly in the final deliverable, not silently absorbed.

---

## Final Deliverable

Append a new section to `docs/claude/architecture.md`'s Decisions Log, matching the established
row format:

```markdown
| CSV export, booking-style field, installable PWA — P1 Group 1 (2026-09-09) | [what shipped per phase, files touched, any deviation from this prompt's stated assumptions] | Current vertical-booking-SaaS standard (CLAUDE.md rule 6) — CSV export and PWA installability are baseline expectations at this product tier (Fresha/Vagaro/Boulevard/GlossGenius-class); style-upfront booking closes a real UX gap where style was previously buried in freeform text. Item 6 deliberately deferred — see Context section of docs/claude/overnight-prompt-p1-group1-2026-09-09.md for why. |
```

Commit:

```
git add -A && git commit -m "feat: CSV export, booking style field, installable PWA — P1 backlog group 1"
```
