# Overnight Prompt — Industry Reports Page Overhaul (2026-06-18)

> **Scope:** Complete overhaul of `IndustryReportsPage.tsx` plus one new backend
> command (`TriggerIndustryReportCommand`) and its endpoint.
>
> No new npm packages. No new NuGet packages.
> Commit after each numbered task.

---

## 0. Mandatory Reading (Do This First)

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/architecture.md
```

Then read these source files:

```
frontend/src/features/platform/components/IndustryReportsPage.tsx
frontend/src/features/platform/__tests__/IndustryReportsPage.test.tsx
frontend/src/features/platform/platform.types.ts
frontend/src/features/platform/platformApi.ts
Pena_e_Arte.Contracts/Responses/IndustryReportSummaryResponse.cs
Pena_e_Arte.Application/Platform/Queries/GetIndustryReportsQuery.cs
Pena_e_Arte.Infrastructure/Jobs/IndustryReportJob.cs
Pena_e_Arte.Infrastructure/Services/JobScheduler.cs
Pena_e_Arte.Domain/Interfaces/IJobScheduler.cs
Pena_e_Arte.API/Endpoints/PlatformEndpoints.cs
```

---

## 1. Backend — Extend `IJobScheduler` With a Manual Trigger

**File:** `Pena_e_Arte.Domain/Interfaces/IJobScheduler.cs`

Add one method to the interface. Do not remove or rename existing methods:

```csharp
void TriggerIndustryReportNow();
```

Full interface after change:

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public interface IJobScheduler
{
    void ScheduleAppointmentReminder(Guid appointmentId, string type, DateTimeOffset enqueueAt);
    void ScheduleTrialExpiryWarning(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleTrialExpiry(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleGracePeriodEnd(Guid studioId, DateTimeOffset enqueueAt);
    void ScheduleDesignRevisionTimeout(Guid revisionId, DateTimeOffset enqueueAt);
    void TriggerIndustryReportNow();
}
```

**File:** `Pena_e_Arte.Infrastructure/Services/JobScheduler.cs`

Implement the new method. Add it at the end of the class body:

```csharp
public void TriggerIndustryReportNow() =>
    backgroundJobs.Enqueue<IndustryReportJob>(j => j.RunAsync(CancellationToken.None));
```

Run `dotnet build` — must succeed before continuing.

**Commit:** `feat(reports): add TriggerIndustryReportNow to IJobScheduler`

---

## 2. Backend — `TriggerIndustryReportCommand`

**New file:** `Pena_e_Arte.Application/Platform/Commands/TriggerIndustryReportCommand.cs`

The job is async and long-running (up to 5 minutes per `IndustryReportJob`).
The command enqueues it via Hangfire and returns `Unit` immediately.
The endpoint will return `202 Accepted`.

```csharp
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

public record TriggerIndustryReportCommand : IRequest;

public class TriggerIndustryReportHandler(
    IJobScheduler                          jobs,
    ILogger<TriggerIndustryReportHandler>  logger)
    : IRequestHandler<TriggerIndustryReportCommand>
{
    public Task Handle(TriggerIndustryReportCommand command, CancellationToken ct)
    {
        jobs.TriggerIndustryReportNow();
        logger.LogInformation("Industry report generation triggered by issuer");
        return Task.CompletedTask;
    }
}

// Required by the "no endpoint without a validator" rule, even with no properties.
public class TriggerIndustryReportValidator : AbstractValidator<TriggerIndustryReportCommand>
{
    // No properties to validate — validator satisfies the registration convention.
}
```

Run `dotnet build` — must succeed.

**Commit:** `feat(reports): TriggerIndustryReportCommand enqueues Hangfire job`

---

## 3. Backend — Register Endpoint

**File:** `Pena_e_Arte.API/Endpoints/PlatformEndpoints.cs`

Add one endpoint registration inside `MapPlatformEndpoints`, grouped with the
existing reports endpoints:

```csharp
group.MapPost("reports/industry/trigger", TriggerIndustryReport);
```

Add the handler method:

```csharp
private static async Task<IResult> TriggerIndustryReport(
    ISender           mediator,
    CancellationToken ct)
{
    await mediator.Send(new TriggerIndustryReportCommand(), ct);
    return Results.Accepted();
}
```

Add the missing using:

```csharp
using Pena_e_Arte.Application.Platform.Commands;
// (TriggerIndustryReportCommand — shares namespace with the others already imported)
```

Run `dotnet build` — must succeed.

**Commit:** `feat(reports): POST /platform/reports/industry/trigger endpoint`

---

## 4. Backend — Unit Test for `TriggerIndustryReportHandler`

**New file:** `tests/Pena_e_Arte.UnitTests/Platform/TriggerIndustryReportHandlerTests.cs`

Follow the pattern of other handler tests in the same directory.

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Domain.Interfaces;
using Xunit;

namespace Pena_e_Arte.UnitTests.Platform;

public class TriggerIndustryReportHandlerTests
{
    [Fact]
    public async Task Handle_CallsTriggerIndustryReportNow()
    {
        IJobScheduler scheduler = Substitute.For<IJobScheduler>();
        TriggerIndustryReportHandler sut = new(
            scheduler,
            NullLogger<TriggerIndustryReportHandler>.Instance);

        await sut.Handle(new TriggerIndustryReportCommand(), CancellationToken.None);

        scheduler.Received(1).TriggerIndustryReportNow();
    }

    [Fact]
    public async Task Handle_CompletesSuccessfully_WithoutThrow()
    {
        IJobScheduler scheduler = Substitute.For<IJobScheduler>();
        TriggerIndustryReportHandler sut = new(
            scheduler,
            NullLogger<TriggerIndustryReportHandler>.Instance);

        Exception? ex = await Record.ExceptionAsync(
            () => sut.Handle(new TriggerIndustryReportCommand(), CancellationToken.None));

        Assert.Null(ex);
    }
}
```

Run `dotnet test` — all tests must pass.

**Commit:** `test(reports): unit tests for TriggerIndustryReportHandler`

---

## 5. Frontend — Update `platformApi.ts`

**File:** `frontend/src/features/platform/platformApi.ts`

Add one mutation after `getIndustryReports`:

```typescript
triggerIndustryReport: builder.mutation<void, void>({
  query: () => ({
    url:    "platform/reports/industry/trigger",
    method: "POST",
  }),
  // No cache invalidation — the Hangfire job is async and takes minutes.
  // The page will show a "Queued" confirmation; the user refreshes manually.
}),
```

Add to the export:

```typescript
export const {
  // ...existing exports...
  useTriggerIndustryReportMutation,
} = platformApi;
```

Run `pnpm --dir frontend tsc --noEmit` — zero errors.

**Commit:** `feat(reports): add triggerIndustryReport mutation to platformApi`

---

## 6. Frontend — Full `IndustryReportsPage.tsx` Overhaul

Apply all changes in a single editing pass. The full file specification follows.

### 6a. New imports

```tsx
import { useState } from "react";
import { BarChart3, Check, Download, ExternalLink, Loader2, PlayCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  useGetIndustryReportsQuery,
  useTriggerIndustryReportMutation,
} from "@/features/platform/platformApi";
import type { IndustryReportSummary } from "@/features/platform/platform.types";
```

### 6b. Date helpers (top-level)

```tsx
function formatPeriod(period: string): string {
  const parts = period.split("-");
  if (parts.length === 2) {
    const [year, month] = parts;
    const date = new Date(parseInt(year, 10), parseInt(month, 10) - 1);
    return date.toLocaleDateString("en-GB", { month: "long", year: "numeric" });
  }
  return period;
}

function formatDate(date: string | Date): string {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

/** Returns "1 July 2026" (or whatever the next 1st is) */
function nextReportDate(): string {
  const now  = new Date();
  const next = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1));
  return next.toLocaleDateString("en-GB", { day: "numeric", month: "long", year: "numeric" });
}
```

### 6c. Skeleton component

Show 3 skeleton rows while loading. Each mirrors the shape of a `ReportRow`:

```tsx
function ReportRowSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div className="space-y-1.5 flex-1">
          <Skeleton className="h-4 w-28" />
          <Skeleton className="h-3 w-40" />
        </div>
        <div className="flex items-center gap-2">
          <Skeleton className="h-7 w-24" />
          <Skeleton className="h-7 w-20" />
        </div>
      </CardContent>
    </Card>
  );
}
```

### 6d. `ReportRow` — improved

```tsx
interface ReportRowProps {
  report: IndustryReportSummary;
}

function ReportRow({ report }: ReportRowProps) {
  const label = formatPeriod(report.period);

  return (
    <Card>
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div className="space-y-0.5 min-w-0">
          <span className="font-medium text-sm">{label}</span>
          <p className="text-xs text-muted-foreground">
            Generated {formatDate(report.generatedAt)}
            {" · "}
            <span className="font-mono text-[10px] uppercase tracking-wide text-muted-foreground/70">
              JSON
            </span>
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <Button
            size="sm"
            variant="outline"
            className="h-7 text-xs gap-1.5"
            asChild
          >
            <a
              href={report.downloadUrl}
              download={`industry-report-${report.period}.json`}
              aria-label={`Download ${label} industry report`}
            >
              <Download className="h-3.5 w-3.5" />
              Download
            </a>
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="h-7 text-xs gap-1.5 text-primary hover:text-primary"
            asChild
          >
            <a
              href={report.downloadUrl}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`Open ${label} industry report in new tab`}
            >
              Open
              <ExternalLink className="h-3.5 w-3.5" />
            </a>
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
```

### 6e. `GenerateTriggerButton` — header action with feedback states

```tsx
function GenerateTriggerButton() {
  const [queued, setQueued] = useState(false);
  const [trigger, { isLoading }] = useTriggerIndustryReportMutation();

  async function handleTrigger() {
    await trigger().unwrap();
    setQueued(true);
    // Reset the "Queued!" label after 4 seconds
    setTimeout(() => setQueued(false), 4000);
  }

  if (queued) {
    return (
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Check className="h-3.5 w-3.5 text-green-500" />
        Queued — report will appear shortly
      </div>
    );
  }

  return (
    <Button
      size="sm"
      variant="outline"
      className="h-7 text-xs gap-1.5"
      disabled={isLoading}
      onClick={handleTrigger}
      aria-label="Trigger industry report generation now"
    >
      {isLoading
        ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
        : <PlayCircle className="h-3.5 w-3.5" />}
      Generate Report
    </Button>
  );
}
```

### 6f. `IndustryReportsPage` — full replacement

```tsx
export function IndustryReportsPage() {
  const { data: reports, isLoading, isError } = useGetIndustryReportsQuery();

  return (
    <div className="min-h-screen bg-background">

      {/* ── Sticky header ───────────────────────────────────────── */}
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <BarChart3 className="h-5 w-5" aria-hidden="true" />
        <span className="font-semibold tracking-tight">Industry Reports</span>
        {reports && (
          <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full
                           bg-muted text-muted-foreground font-medium">
            {reports.length}
          </span>
        )}
        <div className="ml-auto">
          <GenerateTriggerButton />
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-4 space-y-3">

        {/* ── Helper text ─────────────────────────────────────────── */}
        <p className="text-xs text-muted-foreground">
          Anonymized platform-wide analytics — booking trends, trial conversion,
          and MRR growth. No PII, no studio-level identifiers. Reports generate
          automatically on the 1st of each month.
        </p>

        {/* ── Loading ─────────────────────────────────────────────── */}
        {isLoading && (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => <ReportRowSkeleton key={i} />)}
          </div>
        )}

        {/* ── Error ───────────────────────────────────────────────── */}
        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load reports.
          </p>
        )}

        {/* ── Empty state ─────────────────────────────────────────── */}
        {!isLoading && !isError && reports?.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-3 text-center">
            <BarChart3
              className="h-12 w-12 text-muted-foreground/25"
              aria-hidden="true"
            />
            <p className="font-medium text-sm">No reports yet</p>
            <p className="text-xs text-muted-foreground max-w-xs">
              Industry reports are generated automatically on the 1st of each
              month. The first report will appear here on{" "}
              <strong>{nextReportDate()}</strong>, or you can trigger one now
              using the Generate Report button above.
            </p>
          </div>
        )}

        {/* ── Report list ─────────────────────────────────────────── */}
        {!isLoading && !isError && reports?.map((report) => (
          <ReportRow key={report.period} report={report} />
        ))}

      </main>
    </div>
  );
}
```

Run `pnpm --dir frontend tsc --noEmit` — zero errors.
Run `pnpm --dir frontend lint` — zero errors.

**Commit:** `feat(reports): skeleton loader, proper empty state, Generate Report button, Download button, helper text, max-w-2xl`

---

## 7. Frontend — Update Tests

**File:** `frontend/src/features/platform/__tests__/IndustryReportsPage.test.tsx`

### 7a. Add `vi` import and MSW handler for trigger endpoint

```typescript
import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
```

Add MSW handler to `setupServer(...)`:

```typescript
http.post("http://localhost/api/v1/platform/reports/industry/trigger", () =>
  new HttpResponse(null, { status: 202 }),
),
```

### 7b. Update loading test — spinner → skeleton

```typescript
// Before:
it("shows a loading spinner while loading", () => {
  renderPage();
  expect(screen.getByText("Loading…")).toBeInTheDocument();
});

// After:
it("shows skeleton cards while loading, not a spinner", () => {
  renderPage();
  expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
});
```

### 7c. Update generated date format test

```typescript
// Before:
it("shows generated date for each report", async () => {
  renderPage();
  await screen.findByText("May 2026");
  // Generated 01/06/2026 (en-GB locale)
  expect(screen.getByText(/generated 01\/06\/2026/i)).toBeInTheDocument();
});

// After:
it("shows generated date in 'D Mon YYYY' format for each report", async () => {
  renderPage();
  await screen.findByText("May 2026");
  // formatDate uses { day: "numeric", month: "short", year: "numeric" }
  // → "1 Jun 2026"
  expect(screen.getByText(/generated 1 jun 2026/i)).toBeInTheDocument();
});
```

### 7d. Update Open link test — now has aria-label

```typescript
// Before:
const openLinks = screen.getAllByRole("link", { name: /open/i });

// After:
const openLinks = screen.getAllByRole("link", { name: /open .+ industry report in new tab/i });
```

### 7e. Update empty state test — new copy

```typescript
// Before:
it("shows empty state when no reports exist", async () => {
  ...
  expect(await screen.findByText(/no reports published yet/i)).toBeInTheDocument();
});

// After:
it("shows empty state when no reports exist", async () => {
  server.use(
    http.get("http://localhost/api/v1/platform/reports/industry", () =>
      HttpResponse.json([]),
    ),
  );
  renderPage();

  // New bold heading
  expect(await screen.findByText("No reports yet")).toBeInTheDocument();
  // Explanation paragraph references monthly schedule
  expect(screen.getByText(/generated automatically on the 1st of each month/i)).toBeInTheDocument();
  // Old copy must be gone
  expect(screen.queryByText(/no reports published yet/i)).not.toBeInTheDocument();
});
```

### 7f. Add new tests

```typescript
it("shows helper text below the header", async () => {
  renderPage();
  await screen.findByText("May 2026");
  expect(screen.getByText(/anonymized platform-wide analytics/i)).toBeInTheDocument();
});

it("shows Generate Report button in header", async () => {
  renderPage();
  expect(
    screen.getByRole("button", { name: /trigger industry report generation now/i })
  ).toBeInTheDocument();
});

it("shows report count badge in header when reports exist", async () => {
  renderPage();
  await screen.findByText("May 2026");
  // 2 reports → badge shows "2"
  expect(screen.getByText("2", { selector: "span" })).toBeInTheDocument();
});

it("shows Download button for each report", async () => {
  renderPage();
  await screen.findByText("May 2026");
  const downloadLinks = screen.getAllByRole("link", { name: /download .+ industry report/i });
  expect(downloadLinks).toHaveLength(2);
});

it("Download links have the correct download attribute", async () => {
  renderPage();
  await screen.findByText("May 2026");

  const [may, april] = screen.getAllByRole("link", { name: /download .+ industry report/i });
  expect(may).toHaveAttribute("download", "industry-report-2026-05.json");
  expect(april).toHaveAttribute("download", "industry-report-2026-04.json");
});

it("clicking Generate Report posts to trigger endpoint and shows 'Queued' confirmation", async () => {
  const triggerSpy = vi.fn();
  server.use(
    http.post("http://localhost/api/v1/platform/reports/industry/trigger", () => {
      triggerSpy();
      return new HttpResponse(null, { status: 202 });
    }),
  );

  const user = userEvent.setup();
  renderPage();
  await screen.findByText("May 2026");

  await user.click(
    screen.getByRole("button", { name: /trigger industry report generation now/i })
  );

  await waitFor(() => expect(triggerSpy).toHaveBeenCalledOnce());
  expect(await screen.findByText(/queued — report will appear shortly/i)).toBeInTheDocument();
  // The trigger button is gone while queued
  expect(
    screen.queryByRole("button", { name: /trigger industry report generation now/i })
  ).not.toBeInTheDocument();
});

it("renders period names as formatted month labels", async () => {
  renderPage();
  expect(await screen.findByText("May 2026")).toBeInTheDocument();
  expect(screen.getByText("April 2026")).toBeInTheDocument();
});

it("empty state mentions the next report date", async () => {
  server.use(
    http.get("http://localhost/api/v1/platform/reports/industry", () =>
      HttpResponse.json([]),
    ),
  );
  renderPage();
  await screen.findByText("No reports yet");
  // The page renders the next 1st-of-month dynamically.
  // Assert the structure is present; exact date depends on test runtime.
  expect(screen.getByText(/the first report will appear here on/i)).toBeInTheDocument();
});

it("shows 'JSON' label on each report row", async () => {
  renderPage();
  await screen.findByText("May 2026");
  const jsonLabels = screen.getAllByText("JSON");
  expect(jsonLabels).toHaveLength(2);
});
```

Run `pnpm test` — all tests must pass.

**Commit:** `test(reports): update and expand test suite for industry reports overhaul`

---

## 8. Final Verification

1. `dotnet build` — zero errors.
2. `dotnet test` — all tests pass.
3. `pnpm --dir frontend tsc --noEmit` — zero TypeScript errors.
4. `pnpm --dir frontend lint` — zero errors.
5. `pnpm --dir frontend test` — all tests pass.
6. Visual checks:
   - 3 skeleton rows visible while loading
   - Helper text visible below header on all states
   - Empty state shows large icon + "No reports yet" heading + dynamic next date
   - "Generate Report" button in header, right-aligned
   - After clicking Generate, "Queued — report will appear shortly" replaces button for ~4s
   - Report count badge in header when reports exist
   - Each report row shows formatted period ("May 2026"), "Generated 1 Jun 2026" date, "JSON" chip, Download button, Open link
   - `max-w-2xl` container (wider than before)
   - Date format is "1 Jun 2026" not "01/06/2026"
7. `git log --oneline -10` — confirm all commits present.

---

## Reference: Audit Issue → Task Map

| Audit Issue                                                   | Task   |
|---------------------------------------------------------------|--------|
| Spinner instead of skeleton on load                           | 6c + 6f |
| Empty state is a single passive sentence                      | 6f      |
| "No reports published yet." — wrong word "published", no timing | 6f   |
| No primary action on the page                                 | 1-3 + 6e + 6f |
| No "Generate Report" manual trigger button                    | 1-3 + 6e + 6f |
| No helper text / subtitle under page title                    | 6f      |
| No "when will a report appear" info                           | 6f      |
| No download button — only "Open" external link               | 6d      |
| `max-w-xl` too narrow                                         | 6f      |
| Date format "01/06/2026" instead of "1 Jun 2026"             | 6b + 6d |
| No aria-label on Open link (a11y)                             | 6d      |
| No aria-label on Generate Report button (a11y)                | 6e      |
| `aria-hidden` missing on decorative icons (a11y)              | 6e + 6f |
| No report count badge in header                               | 6f      |
| Missing file type indicator on report rows                    | 6d      |
