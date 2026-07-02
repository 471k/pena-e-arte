# Overnight Prompt — Feedback / Bug Report Feature
**Date:** 2026-07-02
**Scope:** Full-stack feature. Build it end-to-end, verify, then exit.

---

## Feature Summary

Artists and Owners can submit feedback to the platform team directly from within the
app — bug reports, feature requests, or general feedback. The issuer has a dedicated
inbox inside the Platform Admin section to review and manage every submission.

---

## Required Reading (do before touching any file)

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/architecture.md
docs/claude/conventions.md
```

---

## Constraints (apply everywhere)

- No new npm or NuGet packages.
- No `useEffect` for data fetching. RTK Query only.
- TypeScript strict mode. No `any`. No default exports on components.
- Every DB query on tenant data: EF Core global query filters must be in place.
  `FeedbackReport` is NOT tenant-scoped (the issuer reads across all studios) — no
  global query filter on it, same pattern as `Review`.
- Every endpoint must have `.RequireAuthorization()` with the correct policy.
- Never log PII. Logs include `request_id`, `tenant_id`, `user_id`.
- No business logic in endpoints — endpoints call MediatR only.
- Enums in TypeScript: use `as const` + type alias. No TypeScript `enum` keyword.
- `erasableSyntaxOnly: true` is on — no TypeScript enums anywhere.

---

# BACKEND

## Step B1 — Domain Entity

File: `Pena_e_Arte.Domain/Entities/FeedbackReport.cs`

```csharp
namespace Pena_e_Arte.Domain.Entities;

public class FeedbackReport
{
    private FeedbackReport() { }

    public Guid        Id              { get; private set; } = Guid.NewGuid();
    public Guid        StudioId        { get; private set; }
    public string      SubmitterUserId { get; private set; } = string.Empty;
    public string      SubmitterRole   { get; private set; } = string.Empty;
    public string      StudioName      { get; private set; } = string.Empty;
    public string      Type            { get; private set; } = string.Empty;
    public string      Title           { get; private set; } = string.Empty;
    public string      Body            { get; private set; } = string.Empty;
    public string      Status          { get; private set; } = FeedbackStatus.Open;
    public string?     IssuerNote      { get; private set; }
    public DateTime    CreatedAt       { get; private set; } = DateTime.UtcNow;
    public DateTime?   ResolvedAt      { get; private set; }

    public static FeedbackReport Create(
        Guid   studioId,
        string submitterUserId,
        string submitterRole,
        string studioName,
        string type,
        string title,
        string body)
    {
        return new FeedbackReport
        {
            StudioId        = studioId,
            SubmitterUserId = submitterUserId,
            SubmitterRole   = submitterRole,
            StudioName      = studioName,
            Type            = type,
            Title           = title.Trim(),
            Body            = body.Trim(),
        };
    }

    public void UpdateStatus(string status, string? issuerNote)
    {
        Status     = status;
        IssuerNote = issuerNote?.Trim();
        if (status is FeedbackStatus.Resolved or FeedbackStatus.Dismissed)
            ResolvedAt = DateTime.UtcNow;
        else
            ResolvedAt = null;
    }
}
```

File: `Pena_e_Arte.Domain/Enums/FeedbackStatus.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public static class FeedbackStatus
{
    public const string Open       = "Open";
    public const string Reviewing  = "Reviewing";
    public const string Resolved   = "Resolved";
    public const string Dismissed  = "Dismissed";
}
```

File: `Pena_e_Arte.Domain/Enums/FeedbackType.cs`

```csharp
namespace Pena_e_Arte.Domain.Enums;

public static class FeedbackType
{
    public const string BugReport      = "BugReport";
    public const string FeatureRequest = "FeatureRequest";
    public const string General        = "General";
}
```

---

## Step B2 — Contracts

File: `Pena_e_Arte.Contracts/Requests/SubmitFeedbackRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record SubmitFeedbackRequest(
    string Type,    // "BugReport" | "FeatureRequest" | "General"
    string Title,
    string Body);
```

File: `Pena_e_Arte.Contracts/Requests/UpdateFeedbackStatusRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record UpdateFeedbackStatusRequest(
    string  Status,      // "Open" | "Reviewing" | "Resolved" | "Dismissed"
    string? IssuerNote);
```

File: `Pena_e_Arte.Contracts/Responses/FeedbackReportResponse.cs`

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record FeedbackReportResponse(
    Guid      Id,
    string    Type,
    string    Title,
    string    Body,
    string    Status,
    string    StudioName,
    string    SubmitterRole,
    string?   IssuerNote,
    DateTime  CreatedAt,
    DateTime? ResolvedAt);
```

---

## Step B3 — Application Layer: Commands

**File:** `Pena_e_Arte.Application/Feedback/Commands/SubmitFeedbackCommand.cs`

```csharp
using MediatR;
using Microsoft.AspNetCore.Identity;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Pena_e_Arte.Application.Feedback.Commands;

public record SubmitFeedbackCommand(SubmitFeedbackRequest Request) : IRequest<FeedbackReportResponse>;

public class SubmitFeedbackHandler(
    IAppDbContext  db,
    ICurrentTenant tenant,
    ICurrentUser   user)
    : IRequestHandler<SubmitFeedbackCommand, FeedbackReportResponse>
{
    public async Task<FeedbackReportResponse> Handle(
        SubmitFeedbackCommand command, CancellationToken ct)
    {
        // Read studio name — use IgnoreQueryFilters only if needed; we already
        // have access to our own studio via the tenant filter.
        Domain.Entities.Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new InvalidOperationException("Studio not found for current tenant.");

        FeedbackReport report = FeedbackReport.Create(
            studioId:        tenant.StudioId,
            submitterUserId: user.UserId,
            submitterRole:   user.Role,
            studioName:      studio.Name,
            type:            command.Request.Type,
            title:           command.Request.Title,
            body:            command.Request.Body);

        db.FeedbackReports.Add(report);
        await db.SaveChangesAsync(ct);

        return new FeedbackReportResponse(
            report.Id,
            report.Type,
            report.Title,
            report.Body,
            report.Status,
            report.StudioName,
            report.SubmitterRole,
            report.IssuerNote,
            report.CreatedAt,
            report.ResolvedAt);
    }
}
```

**File:** `Pena_e_Arte.Application/Feedback/Commands/UpdateFeedbackStatusCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Feedback.Commands;

public record UpdateFeedbackStatusCommand(
    Guid                        Id,
    UpdateFeedbackStatusRequest Request) : IRequest<FeedbackReportResponse>;

public class UpdateFeedbackStatusHandler(IAppDbContext db)
    : IRequestHandler<UpdateFeedbackStatusCommand, FeedbackReportResponse>
{
    public async Task<FeedbackReportResponse> Handle(
        UpdateFeedbackStatusCommand command, CancellationToken ct)
    {
        Domain.Entities.FeedbackReport report = await db.FeedbackReports
            .FirstOrDefaultAsync(r => r.Id == command.Id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.FeedbackReport), command.Id);

        report.UpdateStatus(command.Request.Status, command.Request.IssuerNote);
        await db.SaveChangesAsync(ct);

        return new FeedbackReportResponse(
            report.Id,
            report.Type,
            report.Title,
            report.Body,
            report.Status,
            report.StudioName,
            report.SubmitterRole,
            report.IssuerNote,
            report.CreatedAt,
            report.ResolvedAt);
    }
}
```

---

## Step B4 — Application Layer: Queries

**File:** `Pena_e_Arte.Application/Feedback/Queries/GetFeedbackReportsQuery.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Feedback.Queries;

// type and status are optional filters ("" or null = all)
public record GetFeedbackReportsQuery(
    string? Type   = null,
    string? Status = null) : IRequest<List<FeedbackReportResponse>>;

public class GetFeedbackReportsHandler(IAppDbContext db)
    : IRequestHandler<GetFeedbackReportsQuery, List<FeedbackReportResponse>>
{
    public async Task<List<FeedbackReportResponse>> Handle(
        GetFeedbackReportsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.FeedbackReport> q = db.FeedbackReports
            .OrderByDescending(r => r.CreatedAt);

        if (!string.IsNullOrEmpty(query.Type))
            q = q.Where(r => r.Type == query.Type);

        if (!string.IsNullOrEmpty(query.Status))
            q = q.Where(r => r.Status == query.Status);

        return await q.Select(r => new FeedbackReportResponse(
            r.Id,
            r.Type,
            r.Title,
            r.Body,
            r.Status,
            r.StudioName,
            r.SubmitterRole,
            r.IssuerNote,
            r.CreatedAt,
            r.ResolvedAt))
        .ToListAsync(ct);
    }
}
```

---

## Step B5 — FluentValidation Validators

**File:** `Pena_e_Arte.Application/Feedback/Validators/SubmitFeedbackValidator.cs`

```csharp
using FluentValidation;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Feedback.Validators;

public class SubmitFeedbackValidator : AbstractValidator<SubmitFeedbackCommand>
{
    private static readonly string[] ValidTypes =
        [FeedbackType.BugReport, FeedbackType.FeatureRequest, FeedbackType.General];

    public SubmitFeedbackValidator()
    {
        RuleFor(x => x.Request.Type)
            .NotEmpty()
            .Must(t => ValidTypes.Contains(t))
            .WithMessage("Type must be BugReport, FeatureRequest, or General.");

        RuleFor(x => x.Request.Title)
            .NotEmpty()
            .MaximumLength(150)
            .WithMessage("Title is required and must be at most 150 characters.");

        RuleFor(x => x.Request.Body)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(2000)
            .WithMessage("Description must be between 10 and 2000 characters.");
    }
}
```

**File:** `Pena_e_Arte.Application/Feedback/Validators/UpdateFeedbackStatusValidator.cs`

```csharp
using FluentValidation;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Feedback.Validators;

public class UpdateFeedbackStatusValidator : AbstractValidator<UpdateFeedbackStatusCommand>
{
    private static readonly string[] ValidStatuses =
        [FeedbackStatus.Open, FeedbackStatus.Reviewing, FeedbackStatus.Resolved, FeedbackStatus.Dismissed];

    public UpdateFeedbackStatusValidator()
    {
        RuleFor(x => x.Request.Status)
            .NotEmpty()
            .Must(s => ValidStatuses.Contains(s))
            .WithMessage("Status must be Open, Reviewing, Resolved, or Dismissed.");

        RuleFor(x => x.Request.IssuerNote)
            .MaximumLength(1000)
            .When(x => x.Request.IssuerNote is not null);
    }
}
```

---

## Step B6 — IAppDbContext interface

File: `Pena_e_Arte.Application/Persistence/IAppDbContext.cs`

Add to the interface (check what's already there, add only the new line):
```csharp
DbSet<FeedbackReport> FeedbackReports { get; }
```

---

## Step B7 — AppDbContext: DbSet + configuration

File: `Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs`

**In the DbSet block**, after the `// --- Issuer-level` section add:

```csharp
// --- Platform feedback (no tenant filter — submitter has studioId, issuer reads all) ---
public DbSet<FeedbackReport> FeedbackReports => Set<FeedbackReport>();
```

**In `OnModelCreating`**, configure the entity (no query filter):

```csharp
builder.Entity<FeedbackReport>(b =>
{
    b.HasKey(r => r.Id);
    b.Property(r => r.SubmitterUserId).HasMaxLength(450).IsRequired();
    b.Property(r => r.SubmitterRole).HasMaxLength(20).IsRequired();
    b.Property(r => r.StudioName).HasMaxLength(200).IsRequired();
    b.Property(r => r.Type).HasMaxLength(30).IsRequired();
    b.Property(r => r.Status).HasMaxLength(20).IsRequired();
    b.Property(r => r.Title).HasMaxLength(150).IsRequired();
    b.Property(r => r.Body).HasMaxLength(2000).IsRequired();
    b.Property(r => r.IssuerNote).HasMaxLength(1000);
    b.HasIndex(r => r.StudioId);
    b.HasIndex(r => r.Status);
    b.HasIndex(r => r.CreatedAt);
    // NO HasQueryFilter — issuer reads across all studios
});
```

---

## Step B8 — EF Core Migration

```bash
cd "Pena e Arte"
dotnet ef migrations add AddFeedbackReports \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

Verify the generated migration:
- Table name: `FeedbackReports`
- All columns present with correct lengths
- No query filter applied (there should be no `.HasAnnotation("QueryFilter"...)` in the migration)
- Indexes on `StudioId`, `Status`, `CreatedAt`

---

## Step B9 — Endpoints

File: `Pena_e_Arte.API/Endpoints/FeedbackEndpoints.cs`

```csharp
using MediatR;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Application.Feedback.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class FeedbackEndpoints
{
    public static void MapFeedbackEndpoints(this IEndpointRouteBuilder app)
    {
        // Artist or Owner submits feedback
        app.MapPost("/api/v1/feedback", SubmitFeedback)
            .RequireAuthorization("ArtistAndAbove");

        RouteGroupBuilder platform = app.MapGroup("/api/v1/platform/feedback")
            .RequireAuthorization("IssuerOnly");

        platform.MapGet("",        GetFeedbackReports);
        platform.MapPatch("{id:guid}/status", UpdateFeedbackStatus);
    }

    private static async Task<IResult> SubmitFeedback(
        SubmitFeedbackRequest request,
        ISender               mediator,
        CancellationToken     ct)
    {
        FeedbackReportResponse result = await mediator.Send(new SubmitFeedbackCommand(request), ct);
        return Results.Created($"/api/v1/feedback/{result.Id}", result);
    }

    private static async Task<IResult> GetFeedbackReports(
        ISender           mediator,
        CancellationToken ct,
        string?           type   = null,
        string?           status = null)
    {
        List<FeedbackReportResponse> result =
            await mediator.Send(new GetFeedbackReportsQuery(type, status), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateFeedbackStatus(
        Guid                        id,
        UpdateFeedbackStatusRequest request,
        ISender                     mediator,
        CancellationToken           ct)
    {
        FeedbackReportResponse result =
            await mediator.Send(new UpdateFeedbackStatusCommand(id, request), ct);
        return Results.Ok(result);
    }
}
```

**Register in `Program.cs`:** add `app.MapFeedbackEndpoints();` after the existing
`app.Map*Endpoints()` calls.

---

## Step B10 — Backend Unit Tests

File: `tests/Pena_e_Arte.UnitTests/Feedback/SubmitFeedbackHandlerTests.cs`

Required test cases:
1. Happy path: handler creates `FeedbackReport`, persists it, returns correct `FeedbackReportResponse`.
2. `Type = "BugReport"` stored correctly.
3. `Title` and `Body` are trimmed before storage.
4. Studio not found → throws `InvalidOperationException`.

File: `tests/Pena_e_Arte.UnitTests/Feedback/UpdateFeedbackStatusHandlerTests.cs`

Required test cases:
1. Happy path: status updated, `IssuerNote` stored.
2. Resolving: `ResolvedAt` set.
3. Re-opening (status = "Open"): `ResolvedAt` cleared.
4. Report not found → throws `NotFoundException`.

File: `tests/Pena_e_Arte.UnitTests/Feedback/FeedbackValidatorTests.cs`

Required test cases for `SubmitFeedbackValidator`:
1. Valid request passes.
2. Empty `Type` fails.
3. Invalid `Type` value fails.
4. Empty `Title` fails.
5. `Title` > 150 chars fails.
6. `Body` < 10 chars fails.
7. `Body` > 2000 chars fails.

---

# FRONTEND

## Step F1 — Feature folder structure

Create `frontend/src/features/feedback/` with:
```
feedback/
  feedbackApi.ts
  feedback.types.ts
  components/
    FeedbackDialog.tsx
    FeedbackInboxPage.tsx
  index.ts
```

---

## Step F2 — Types

File: `frontend/src/features/feedback/feedback.types.ts`

```ts
export const FEEDBACK_TYPE = {
  BugReport:      "BugReport",
  FeatureRequest: "FeatureRequest",
  General:        "General",
} as const;
export type FeedbackType = (typeof FEEDBACK_TYPE)[keyof typeof FEEDBACK_TYPE];

export const FEEDBACK_STATUS = {
  Open:      "Open",
  Reviewing: "Reviewing",
  Resolved:  "Resolved",
  Dismissed: "Dismissed",
} as const;
export type FeedbackStatus = (typeof FEEDBACK_STATUS)[keyof typeof FEEDBACK_STATUS];

export interface FeedbackReportResponse {
  id:            string;
  type:          FeedbackType;
  title:         string;
  body:          string;
  status:        FeedbackStatus;
  studioName:    string;
  submitterRole: string;
  issuerNote:    string | null;
  createdAt:     string;
  resolvedAt:    string | null;
}

export interface SubmitFeedbackRequest {
  type:  FeedbackType;
  title: string;
  body:  string;
}

export interface UpdateFeedbackStatusRequest {
  status:     FeedbackStatus;
  issuerNote: string | null;
}
```

---

## Step F3 — RTK Query API slice

File: `frontend/src/features/feedback/feedbackApi.ts`

```ts
import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { RootState } from "@/app/store";
import type {
  FeedbackReportResponse,
  SubmitFeedbackRequest,
  UpdateFeedbackStatusRequest,
} from "./feedback.types";

export const feedbackApi = createApi({
  reducerPath: "feedbackApi",
  baseQuery: fetchBaseQuery({
    baseUrl: import.meta.env.VITE_API_URL,
    prepareHeaders: (headers, { getState }) => {
      const token = (getState() as RootState).auth.accessToken;
      if (token) headers.set("Authorization", `Bearer ${token}`);
      return headers;
    },
  }),
  tagTypes: ["Feedback"],
  endpoints: (builder) => ({
    submitFeedback: builder.mutation<FeedbackReportResponse, SubmitFeedbackRequest>({
      query: (body) => ({ url: "/api/v1/feedback", method: "POST", body }),
    }),
    getFeedbackReports: builder.query<
      FeedbackReportResponse[],
      { type?: string; status?: string }
    >({
      query: ({ type, status } = {}) => {
        const params = new URLSearchParams();
        if (type)   params.set("type",   type);
        if (status) params.set("status", status);
        return `/api/v1/platform/feedback?${params.toString()}`;
      },
      providesTags: ["Feedback"],
    }),
    updateFeedbackStatus: builder.mutation<
      FeedbackReportResponse,
      { id: string } & UpdateFeedbackStatusRequest
    >({
      query: ({ id, ...body }) => ({
        url: `/api/v1/platform/feedback/${id}/status`,
        method: "PATCH",
        body,
      }),
      invalidatesTags: ["Feedback"],
    }),
  }),
});

export const {
  useSubmitFeedbackMutation,
  useGetFeedbackReportsQuery,
  useUpdateFeedbackStatusMutation,
} = feedbackApi;
```

Register `feedbackApi` in the Redux store:
- Add `feedbackApi.reducerPath: feedbackApi.reducer` to the `store`'s `reducer` map.
- Add `feedbackApi.middleware` to `getDefaultMiddleware().concat(...)`.

Read `frontend/src/app/store.ts` to find the exact location and follow the existing
pattern used by other API slices.

---

## Step F4 — FeedbackDialog (Artist + Owner submit form)

File: `frontend/src/features/feedback/components/FeedbackDialog.tsx`

This is a `Dialog` (shadcn/ui) that opens when the user clicks a trigger button.
The trigger is NOT part of this component — it receives `open` and `onOpenChange` as props,
allowing the caller to control it.

```tsx
import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Loader2, CheckCircle } from "lucide-react";
import { toast } from "sonner";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/shared/components/ui/dialog";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { Input } from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { Controller } from "react-hook-form";
import { cn } from "@/shared/utils/cn";
import { FEEDBACK_TYPE } from "../feedback.types";
import { useSubmitFeedbackMutation } from "../feedbackApi";

const schema = z.object({
  type:  z.enum(["BugReport", "FeatureRequest", "General"]),
  title: z.string().min(1, "Title is required").max(150, "Max 150 characters"),
  body:  z.string().min(10, "Please describe in at least 10 characters").max(2000, "Max 2000 characters"),
});
type FormValues = z.infer<typeof schema>;

interface Props {
  open:         boolean;
  onOpenChange: (open: boolean) => void;
}

export function FeedbackDialog({ open, onOpenChange }: Props) {
  const [submitted, setSubmitted] = useState(false);
  const [submitFeedback, { isLoading }] = useSubmitFeedbackMutation();

  const {
    register,
    control,
    handleSubmit,
    watch,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { type: "BugReport", title: "", body: "" },
  });

  const bodyLength = watch("body").length;

  async function onSubmit(values: FormValues) {
    try {
      await submitFeedback(values).unwrap();
      setSubmitted(true);
      reset();
    } catch {
      toast.error("Failed to submit. Please try again.");
    }
  }

  function handleClose(open: boolean) {
    if (!open) {
      setSubmitted(false);
      reset();
    }
    onOpenChange(open);
  }

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Send Feedback</DialogTitle>
          <DialogDescription>
            Report a bug, request a feature, or share your thoughts.
            Our team reviews every submission.
          </DialogDescription>
        </DialogHeader>

        {submitted ? (
          <div className="flex flex-col items-center gap-3 py-6 text-center">
            <CheckCircle className="h-10 w-10 text-green-500" />
            <p className="text-sm font-medium">Thank you for your feedback!</p>
            <p className="text-xs text-muted-foreground">
              We've received your message and will review it soon.
            </p>
            <Button size="sm" onClick={() => handleClose(false)} className="mt-2">
              Close
            </Button>
          </div>
        ) : (
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-1">
            {/* Type */}
            <div className="space-y-1.5">
              <Label htmlFor="type">Type</Label>
              <Controller
                control={control}
                name="type"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="type">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={FEEDBACK_TYPE.BugReport}>🐛 Bug Report</SelectItem>
                      <SelectItem value={FEEDBACK_TYPE.FeatureRequest}>✨ Feature Request</SelectItem>
                      <SelectItem value={FEEDBACK_TYPE.General}>💬 General Feedback</SelectItem>
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            {/* Title */}
            <div className="space-y-1.5">
              <Label htmlFor="title">Title</Label>
              <Input
                id="title"
                placeholder="Brief summary"
                disabled={isLoading}
                {...register("title")}
                className={cn(errors.title && "border-destructive")}
              />
              {errors.title && (
                <p className="text-xs text-destructive">{errors.title.message}</p>
              )}
            </div>

            {/* Body */}
            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <Label htmlFor="body">Description</Label>
                <span className={cn(
                  "text-xs",
                  bodyLength > 1800 ? "text-amber-500" : "text-muted-foreground"
                )}>
                  {bodyLength}/2000
                </span>
              </div>
              <Textarea
                id="body"
                rows={5}
                placeholder="Describe the issue or idea in detail…"
                disabled={isLoading}
                {...register("body")}
                className={cn("resize-none", errors.body && "border-destructive")}
              />
              {errors.body && (
                <p className="text-xs text-destructive">{errors.body.message}</p>
              )}
            </div>

            <div className="flex justify-end gap-2 pt-1">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => handleClose(false)}
                disabled={isLoading}
              >
                Cancel
              </Button>
              <Button type="submit" size="sm" disabled={isLoading}>
                {isLoading && <Loader2 className="h-4 w-4 animate-spin mr-1.5" />}
                Send Feedback
              </Button>
            </div>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
```

---

## Step F5 — FeedbackButton: add to ArtistLayout + OwnerLayout

Create a small `FeedbackButton` component that lives in the header of both layouts.
It sits alongside `NotificationBell` and `UserMenu` in the `ml-auto` section.

Do NOT add it to the nav items list. Add it as a dedicated icon button in the header.

**Pattern to follow:**
```tsx
// In ArtistLayout.tsx and OwnerLayout.tsx
// 1. Add import:
import { useState } from "react";
import { MessageSquareMore } from "lucide-react";
import { FeedbackDialog } from "@/features/feedback";
import { Button } from "@/shared/components/ui/button";

// 2. Inside the layout function, add state:
const [feedbackOpen, setFeedbackOpen] = useState(false);

// 3. In the header, in the ml-auto flex div (alongside NotificationBell):
<Button
  variant="ghost"
  size="icon"
  className="h-8 w-8"
  onClick={() => setFeedbackOpen(true)}
  title="Send feedback"
  aria-label="Send feedback"
>
  <MessageSquareMore className="h-4 w-4" />
</Button>
<NotificationBell />
<UserMenu onLogout={handleLogout} />
<FeedbackDialog open={feedbackOpen} onOpenChange={setFeedbackOpen} />
```

Apply this exact pattern to BOTH `ArtistLayout.tsx` and `OwnerLayout.tsx`.

---

## Step F6 — FeedbackInboxPage (Issuer)

File: `frontend/src/features/feedback/components/FeedbackInboxPage.tsx`

The issuer views all feedback submissions, can filter them, and update their status.

**Layout:**
- Header with title "Feedback Inbox" + total count badge
- Filter bar: type chips (All / Bug Reports / Feature Requests / General) +
  status chips (All / Open / Reviewing / Resolved / Dismissed)
- Sorted list of feedback cards (newest first)
- Each card: expandable detail view with status controls

**Card (collapsed):**
```
[BugReport badge] "Title text here..."   [Studio Name]   [artist/owner]   2026-07-01
[Open badge]
```

**Card (expanded — click to toggle):**
```
Full description body text
────────────────────────────────
[Issuer note textarea]
[Open] [Reviewing] [Resolved] [Dismissed] buttons
```

**Status badge colours:**
- Open      → `bg-blue-500/15 text-blue-600`
- Reviewing → `bg-amber-500/15 text-amber-600`
- Resolved  → `bg-green-500/15 text-green-600`
- Dismissed → `bg-muted text-muted-foreground`

**Type badge colours:**
- BugReport      → `bg-red-500/15 text-red-600`
- FeatureRequest → `bg-purple-500/15 text-purple-600`
- General        → `bg-sky-500/15 text-sky-600`

**Type label display:**
- `"BugReport"` → `"Bug Report"`
- `"FeatureRequest"` → `"Feature Request"`
- `"General"` → `"General"`

**Behaviour:**
- Status update: call `updateFeedbackStatus({ id, status, issuerNote })` on button click.
  Show `Loader2` spinner on the active button. On success: show `toast.success("Updated.")`.
  On error: `toast.error("Failed to update.")`.
- Issuer note is a `<Textarea>` within the card — it saves TOGETHER with the status
  on the button click (not auto-saved). This avoids excessive API calls.
- If there are no submissions matching the filter: empty state with
  `MessageSquareMore` icon and "No feedback yet." copy.
- Loading: 5 skeleton cards.
- Error: "Failed to load feedback." with retry button.
- `useDocumentMeta({ title: "Feedback Inbox — Pena e Artë" })` called at top.

Full implementation should be ~200 lines. Build it completely — no stubs.

---

## Step F7 — Issuer router + nav

**`router.tsx`:**

1. Add import:
   ```ts
   import { FeedbackInboxPage } from "@/features/feedback";
   ```

2. Inside the `platform` route's `children` array, add:
   ```ts
   { path: "feedback", element: <ErrorBoundary><FeedbackInboxPage /></ErrorBoundary> },
   ```

**`IssuerLayout.tsx`:**

Add to `NAV_ITEMS`:
```ts
import { MessageSquare } from "lucide-react";

// In NAV_ITEMS array, after "Reports":
{ label: "Feedback", href: "/platform/feedback", icon: <MessageSquare className="h-4 w-4" /> },
```

Add an unread-count badge if there are Open submissions. To do this, call
`useGetFeedbackReportsQuery({ status: "Open" })` in `IssuerLayout` and show a count
badge on the nav item if `openItems.length > 0`:

```tsx
// In IssuerLayout:
const { data: openFeedback } = useGetFeedbackReportsQuery({ status: "Open" });
const openCount = openFeedback?.length ?? 0;
```

Then in the nav render, for the "Feedback" item specifically, show a badge:
```tsx
{label === "Feedback" && openCount > 0 && (
  <span className="ml-auto min-w-[1.25rem] rounded-full bg-destructive px-1 py-0.5 text-[10px] font-medium text-destructive-foreground text-center">
    {openCount > 99 ? "99+" : openCount}
  </span>
)}
```

---

## Step F8 — Feature index

File: `frontend/src/features/feedback/index.ts`

```ts
export { FeedbackDialog }     from "./components/FeedbackDialog";
export { FeedbackInboxPage }  from "./components/FeedbackInboxPage";
export { feedbackApi,
         useSubmitFeedbackMutation,
         useGetFeedbackReportsQuery,
         useUpdateFeedbackStatusMutation } from "./feedbackApi";
export * from "./feedback.types";
```

---

## Step F9 — Frontend Unit Tests

File: `frontend/src/features/feedback/components/FeedbackDialog.test.tsx`

Required tests:
1. Renders with type selector, title input, body textarea, cancel + submit buttons.
2. Type selector defaults to "Bug Report".
3. Empty title: shows "Title is required" on submit.
4. Body under 10 chars: shows validation error on submit.
5. Valid form: calls `submitFeedback` mutation.
6. Submit loading: button shows spinner.
7. Success: shows "Thank you" confirmation view.
8. Success: "Close" button calls `onOpenChange(false)`.
9. Close: resets form back to default state.
10. Mutation error: shows `toast.error`.

File: `frontend/src/features/feedback/components/FeedbackInboxPage.test.tsx`

Required tests:
1. Renders loading skeletons while `isLoading`.
2. Renders feedback card list on success.
3. Bug report badge shown for BugReport type.
4. Status badge shown correctly for each status.
5. Filter by type chip: changes RTK Query params.
6. Filter by status chip: changes RTK Query params.
7. Expand card on click: shows full body + note textarea + status buttons.
8. Status update: calls `updateFeedbackStatus` on button click.
9. Status update success: shows success toast.
10. Empty state: renders when `data.length === 0`.
11. Error state: renders retry button.

---

## Verification

Run in order. Fix every failure before proceeding.

```bash
cd "Pena e Arte"

# 1. Backend compiles
dotnet build

# 2. Migration was created and is valid
dotnet ef migrations list \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API

# 3. All backend tests pass
dotnet test --no-build

# 4. Frontend compiles with no TypeScript errors
cd frontend && pnpm build

# 5. All frontend tests pass
pnpm test
```

---

## Exit Condition

All five commands above exit with code 0.

Then append to `docs/claude/architecture.md`:

```markdown
## Feedback / Bug Report Feature — 2026-07-02

### What was built
- `FeedbackReport` domain entity (non-tenant, issuer reads cross-studio)
- `FeedbackType` and `FeedbackStatus` domain constants
- MediatR: `SubmitFeedbackCommand`, `UpdateFeedbackStatusCommand`, `GetFeedbackReportsQuery`
- FluentValidation: `SubmitFeedbackValidator`, `UpdateFeedbackStatusValidator`
- Migration: `AddFeedbackReports`
- Endpoints:
  - `POST /api/v1/feedback` (ArtistAndAbove)
  - `GET /api/v1/platform/feedback?type=&status=` (IssuerOnly)
  - `PATCH /api/v1/platform/feedback/{id}/status` (IssuerOnly)
- `FeedbackDialog` component in `ArtistLayout` + `OwnerLayout` header
- `FeedbackInboxPage` at `/platform/feedback` (IssuerOnly)
- IssuerLayout: "Feedback" nav item with Open-count badge
- `feedbackApi` RTK Query slice registered in store

### Architecture decisions
- `FeedbackReport` is NOT a `TenantEntity` — no EF Core global query filter.
  `SubmitFeedbackHandler` reads `StudioId` from `ICurrentTenant` and stores it.
  `GetFeedbackReportsHandler` queries across all studios without `IgnoreQueryFilters()`
  because no filter is registered for this entity.
- `FeedbackDialog` is controlled (open/onOpenChange props) — callers own state.
  This avoids prop-drilling the dialog into deeply nested components.
- Issuer note is submitted alongside status update (not auto-saved) to keep API calls
  intentional and predictable.
```
