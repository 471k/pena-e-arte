# Overnight Prompt — Consent Form Detail Page: Bug Fixes + UI/UX Overhaul
**Date:** 2026-07-04
**Scope:** Backend response enrichment (no migration needed) + frontend component rewrite.
**Files changed:** ~8 backend files, ~6 frontend files, 1 new shared hook.

---

## Required Reading

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
Pena_e_Arte.Contracts/Responses/ConsentFormResponse.cs
Pena_e_Arte.Application/ConsentForms/Queries/GetConsentFormByIdQuery.cs
Pena_e_Arte.Application/ConsentForms/Queries/GetConsentFormsQuery.cs
Pena_e_Arte.Application/ConsentForms/Commands/SignConsentFormCommand.cs   ← for Map()
Pena_e_Arte.API/Endpoints/FormEndpoints.cs
Pena_e_Arte.Infrastructure/Persistence/Configurations/ConsentFormConfiguration.cs
frontend/src/features/forms/form.types.ts
frontend/src/features/forms/consentFormsApi.ts
frontend/src/features/forms/components/ConsentFormDetailPage.tsx          ← primary target
frontend/src/features/forms/components/ConsentFormListPage.tsx
frontend/src/features/forms/__tests__/ConsentForms.test.tsx
frontend/src/app/router.tsx                                               ← for navigation paths
```

---

## Bug Inventory

### B-01 · CRITICAL — Signature renders as raw text/base64 instead of an image
**File:** `frontend/src/features/forms/components/ConsentFormDetailPage.tsx` lines 96-105

```tsx
// BROKEN — blindly injects form.signatureData into a <p>
{form.signatureData && (
  <DetailRow
    label="Digital signature"
    value={
      <p className="font-medium text-base italic border-b border-foreground/20 pb-1">
        {form.signatureData}   {/* 🔴 renders raw base64 if data is a PNG */}
      </p>
    }
  />
)}
```

The `signatureData` column (`HasMaxLength(5000)`) stores either:
- A typed full name (e.g. `"Ana Rossi"`) — the current client UI writes text
- A base64 `data:image/png;base64,...` string — written by any previous or third-party canvas signature widget

The component never checks which it has. When the value starts with `data:image/`, it must render `<img>`. When it's plain text, render as italic. Never render the raw base64 string as text content.

---

### B-02 · CRITICAL — Raw UUIDs surfaced to end users
**File:** `ConsentFormDetailPage.tsx` lines 85-94 and `ConsentFormListPage.tsx` lines 33-34

Both pages show `form.clientId` and `form.appointmentId` as `font-mono` UUID strings. No end user should ever see a UUID as their primary piece of information. The domain model has `Client.FirstName`, `Client.LastName`, and `Appointment.Date` available via navigation properties — they must be resolved server-side and returned in the API response.

---

### B-03 · INTEGRITY BUG — Signed date shows before Created date
**File:** `Pena_e_Arte.Application/ConsentForms/Commands/SignConsentFormCommand.cs` line 53 and `Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs`

`SignedAt = DateTime.UtcNow` is set in the handler, and `TenantEntity.CreatedAt = DateTime.UtcNow` is set in the domain constructor — these should be milliseconds apart. If the DB shows `SignedAt < CreatedAt`, the likely cause is Pomelo returning MySQL `DATETIME` values without UTC kind (MySQL stores no timezone), and the frontend's `toLocaleString` applying the system timezone offset to one field but not the other.

**Fix:** Ensure Pomelo is configured to treat `DATETIME` as UTC. In `InfrastructureServiceExtensions.cs`, check whether `UseMySql` is called with `EnableStringComparisonTranslations` or a `SchemaBehavior` option. Add `new MySqlServerVersion(...)` options if missing:

```csharp
// In InfrastructureServiceExtensions, the UseMySql call should include:
options.UseMySql(connectionString, serverVersion,
    mySqlOptions => mySqlOptions.EnableRetryOnFailure());
```

Pomelo >= 8 defaults to UTC for `DATETIME` columns when `TreatTinyAsBoolean` is set. Verify the actual UTC behaviour by adding a canary assertion in `GetConsentFormByIdHandler`:

```csharp
// After loading the form — log a warning if timestamps are inverted.
// This is a data integrity guard, not a fix. Remove once root cause is confirmed.
if (form.SignedAt.HasValue && form.SignedAt.Value < form.CreatedAt)
{
    // Import ILogger via primary constructor
    logger.LogWarning(
        "ConsentForm {Id} has SignedAt ({SignedAt}) before CreatedAt ({CreatedAt}) — possible UTC mapping issue",
        form.Id, form.SignedAt, form.CreatedAt);
}
```

---

### B-04 · CONTRAST — Label text fails WCAG AA
**File:** `ConsentFormDetailPage.tsx` — `DetailRow` uses `text-muted-foreground`

`text-muted-foreground` maps to `hsl(var(--muted-foreground))`. On most shadcn dark themes this is `#7A7A7A` against `#000000` ≈ 3.8:1 — below the 4.5:1 threshold for normal-size text. There are 6 label instances on the detail page. Replace `text-muted-foreground` in `DetailRow` with `text-foreground/65` and verify contrast in the browser dev tools color picker (`Ctrl+Shift+I → color picker → contrast ratio`). Adjust if still under 4.5:1.

---

### B-05 · MISSING UX — No breadcrumb to appointment, no copy, no download
- No way to navigate from the consent form back to its appointment
- The truncated ID (`a9796577…`) has no copy-to-clipboard affordance
- No download/print action; users expect to save a signed consent form for their records
- "View document" link text is vague — doesn't say what format/content

---

### B-06 · MISSING META — No `useDocumentMeta` on detail page
**File:** `ConsentFormDetailPage.tsx` — no `useDocumentMeta` call exists, unlike `ConsentFormListPage.tsx` which has it. Browser tab shows the app default title.

---

## Backend Changes

### Step B1 — Enrich `ConsentFormResponse` (list) and create `ConsentFormDetailResponse`

**File:** `Pena_e_Arte.Contracts/Responses/ConsentFormResponse.cs`

Replace the existing record with:

```csharp
namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Returned by GET /api/v1/consent-forms (list). ClientName is always populated.</summary>
public record ConsentFormResponse(
    Guid      Id,
    Guid      StudioId,
    Guid      ClientId,
    Guid      AppointmentId,
    string?   FileUrl,
    string?   SignatureData,
    DateTime? SignedAt,
    DateTime  CreatedAt,
    string    ClientName);

/// <summary>
/// Returned by GET /api/v1/consent-forms/{id} (detail).
/// Includes resolved human-readable fields for display without UUID lookup.
/// </summary>
public record ConsentFormDetailResponse(
    Guid      Id,
    Guid      StudioId,
    Guid      ClientId,
    Guid      AppointmentId,
    string?   FileUrl,
    string?   SignatureData,
    DateTime? SignedAt,
    DateTime  CreatedAt,
    string    ClientName,
    DateTime  AppointmentDate,
    string?   ArtistName,
    Guid?     ArtistId);
```

---

### Step B2 — Update `GetConsentFormsQuery` to project `ClientName`

**File:** `Pena_e_Arte.Application/ConsentForms/Queries/GetConsentFormsQuery.cs`

The current implementation uses `Select(f => SignConsentFormHandler.Map(f))`. Replace with a proper SQL projection that includes `ClientName` via a LEFT JOIN on the `Client` navigation property.

Change the `Handle` method:

```csharp
public async Task<List<ConsentFormResponse>> Handle(GetConsentFormsQuery query, CancellationToken ct)
{
    IQueryable<Domain.Entities.ConsentForm> q = db.ConsentForms;

    Guid? clientId = query.ClientId;
    if (currentUser.Role == "client")
    {
        Guid? myId = await db.Clients
            .Where(c => c.UserId == currentUser.UserId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
        if (myId is null) return [];
        clientId = myId;
    }

    if (clientId.HasValue)            q = q.Where(f => f.ClientId      == clientId.Value);
    if (query.AppointmentId.HasValue) q = q.Where(f => f.AppointmentId == query.AppointmentId.Value);

    return await q
        .OrderByDescending(f => f.CreatedAt)
        .Select(f => new ConsentFormResponse(
            f.Id,
            f.StudioId,
            f.ClientId,
            f.AppointmentId,
            f.FileUrl,
            f.SignatureData,
            f.SignedAt,
            f.CreatedAt,
            f.Client.FirstName + " " + f.Client.LastName))  // ← LEFT JOIN via nav property
        .ToListAsync(ct);
}
```

EF Core will translate `f.Client.FirstName + " " + f.Client.LastName` into a SQL `LEFT JOIN consent_forms ... JOIN clients ...` — no `.Include()` needed in a `Select` projection.

**Also update the return type on the record declaration:**
`GetConsentFormsQuery` is already typed `IRequest<List<ConsentFormResponse>>` — it stays.

---

### Step B3 — Update `GetConsentFormByIdQuery` to return `ConsentFormDetailResponse`

**File:** `Pena_e_Arte.Application/ConsentForms/Queries/GetConsentFormByIdQuery.cs`

Replace entirely:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Pena_e_Arte.Application.ConsentForms.Queries;

public record GetConsentFormByIdQuery(Guid Id) : IRequest<ConsentFormDetailResponse>;

public class GetConsentFormByIdHandler(
    IAppDbContext  db,
    ICurrentUser   currentUser,
    ILogger<GetConsentFormByIdHandler> logger)
    : IRequestHandler<GetConsentFormByIdQuery, ConsentFormDetailResponse>
{
    public async Task<ConsentFormDetailResponse> Handle(
        GetConsentFormByIdQuery query, CancellationToken ct)
    {
        Domain.Entities.ConsentForm form = await db.ConsentForms
            .Include(f => f.Client)
            .Include(f => f.Appointment)
                .ThenInclude(a => a.Artist)
            .FirstOrDefaultAsync(f => f.Id == query.Id, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.ConsentForm), query.Id);

        if (currentUser.Role == "client")
        {
            Guid? myId = await db.Clients
                .Where(c => c.UserId == currentUser.UserId)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            if (myId is null || form.ClientId != myId.Value)
                throw new NotFoundException(nameof(Domain.Entities.ConsentForm), query.Id);
        }

        // Integrity guard — log an anomaly if timestamps are inverted.
        if (form.SignedAt.HasValue && form.SignedAt.Value < form.CreatedAt)
        {
            logger.LogWarning(
                "ConsentForm {FormId} has SignedAt {SignedAt} before CreatedAt {CreatedAt} — investigate UTC mapping",
                form.Id, form.SignedAt, form.CreatedAt);
        }

        Domain.Entities.Artist? artist = form.Appointment.Artist;

        return new ConsentFormDetailResponse(
            Id:              form.Id,
            StudioId:        form.StudioId,
            ClientId:        form.ClientId,
            AppointmentId:   form.AppointmentId,
            FileUrl:         form.FileUrl,
            SignatureData:   form.SignatureData,
            SignedAt:        form.SignedAt,
            CreatedAt:       form.CreatedAt,
            ClientName:      $"{form.Client.FirstName} {form.Client.LastName}".Trim(),
            AppointmentDate: form.Appointment.Date,
            ArtistName:      artist is null ? null : $"{artist.FirstName} {artist.LastName}".Trim(),
            ArtistId:        artist?.Id);
    }
}
```

---

### Step B4 — Update `SignConsentFormHandler.Map` to include `ClientName`

**File:** `Pena_e_Arte.Application/ConsentForms/Commands/SignConsentFormCommand.cs`

The `Map` static method returns `ConsentFormResponse` (the list type). Its signature must now supply `ClientName`.

At the point `Map(form)` is called, the `Client` navigation property is NOT loaded (the handler creates the entity and saves it without an Include). Add a non-navigation overload:

```csharp
// Replace the existing Map method:
internal static ConsentFormResponse Map(
    Domain.Entities.ConsentForm form, string clientName = "") =>
    new(form.Id, form.StudioId, form.ClientId, form.AppointmentId,
        form.FileUrl, form.SignatureData, form.SignedAt, form.CreatedAt,
        clientName);
```

The `clientName` defaults to `""` here — immediately after signing, the created response is returned to the client who just signed it. The `signedAt` confirmation screen does NOT show raw IDs, so an empty client name in the post-sign response is acceptable. If the sign endpoint needs to return the name, fetch it from the appointment that was already loaded:

```csharp
// In Handle(), after SaveChangesAsync — replace:
//   return Map(form);
// With:
Client? client = await db.Clients
    .AsNoTracking()
    .FirstOrDefaultAsync(c => c.Id == form.ClientId, ct);
string clientName = client is null ? string.Empty
    : $"{client.FirstName} {client.LastName}".Trim();
return Map(form, clientName);
```

---

### Step B5 — Update `FormEndpoints.cs` return type for the detail endpoint

**File:** `Pena_e_Arte.API/Endpoints/FormEndpoints.cs`

Change:

```csharp
// BEFORE
private static async Task<IResult> GetConsentFormById(
    Guid              id,
    ISender           mediator,
    CancellationToken ct)
{
    ConsentFormResponse result = await mediator.Send(new GetConsentFormByIdQuery(id), ct);
    return Results.Ok(result);
}

// AFTER
private static async Task<IResult> GetConsentFormById(
    Guid              id,
    ISender           mediator,
    CancellationToken ct)
{
    ConsentFormDetailResponse result = await mediator.Send(new GetConsentFormByIdQuery(id), ct);
    return Results.Ok(result);
}
```

Add `using Pena_e_Arte.Contracts.Responses;` is already in scope — no change needed.

---

### Step B6 — Backend unit tests

**File:** `tests/Pena_e_Arte.UnitTests/ConsentForms/` — add new test class:
`GetConsentFormByIdHandlerTests.cs`

Write tests for:
1. Returns `ConsentFormDetailResponse` with `ClientName` equal to `"{FirstName} {LastName}"`
2. Returns `ClientName` trimmed when `LastName` is empty
3. Returns `ArtistName` as null when `Appointment.Artist` is null
4. Throws `NotFoundException` when form does not exist
5. Throws `NotFoundException` when client role requests another client's form
6. Logs a warning when `SignedAt < CreatedAt`

Use an in-memory EF Core context (or NSubstitute for `IAppDbContext`) consistent with how the existing unit tests in the project are structured — check `tests/Pena_e_Arte.UnitTests/` for the pattern used.

---

## Frontend Changes

### Step F1 — Add `ConsentFormDetailResponse` to `form.types.ts`

**File:** `frontend/src/features/forms/form.types.ts`

Append:

```ts
export interface ConsentFormDetailResponse {
  id:              string;
  studioId:        string;
  clientId:        string;
  appointmentId:   string;
  fileUrl:         string | null;
  signatureData:   string | null;
  signedAt:        string | null;
  createdAt:       string;
  // Resolved by the detail endpoint — never a raw UUID
  clientName:      string;
  appointmentDate: string;     // ISO 8601 UTC, same shape as other date fields
  artistName:      string | null;
  artistId:        string | null;
}
```

Also update `ConsentFormResponse` to include `clientName`:

```ts
export interface ConsentFormResponse {
  id:            string;
  studioId:      string;
  clientId:      string;
  appointmentId: string;
  fileUrl:       string | null;
  signatureData: string | null;
  signedAt:      string | null;
  createdAt:     string;
  clientName:    string;   // ← new, always populated by the list endpoint
}
```

Update `forms/index.ts` to export `ConsentFormDetailResponse`.

---

### Step F2 — Update `consentFormsApi.ts`

**File:** `frontend/src/features/forms/consentFormsApi.ts`

Change `getConsentFormById` return type:

```ts
import type {
  ConsentFormResponse,
  ConsentFormDetailResponse,   // ← add
  SignConsentFormRequest,
  GetConsentFormsParams,
} from "./form.types";

// ...

getConsentFormById: builder.query<ConsentFormDetailResponse, string>({
  query: (id) => `consent-forms/${id}`,
  providesTags: (_result, _error, id) => [{ type: "ConsentForm", id }],
}),
```

Export the new hook (it's the same name — `useGetConsentFormByIdQuery` — RTK Query infers types automatically).

---

### Step F3 — Create `useCopyToClipboard` hook

**File:** `frontend/src/shared/hooks/useCopyToClipboard.ts` (new file)

```ts
import { useState, useCallback } from "react";

export function useCopyToClipboard(timeoutMs = 1500): [boolean, (text: string) => void] {
  const [copied, setCopied] = useState(false);

  const copy = useCallback((text: string) => {
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), timeoutMs);
    });
  }, [timeoutMs]);

  return [copied, copy];
}
```

---

### Step F4 — Rewrite `ConsentFormDetailPage.tsx`

**File:** `frontend/src/features/forms/components/ConsentFormDetailPage.tsx`

Replace the entire file:

```tsx
import { ArrowLeft, Check, Copy, Download, ExternalLink, FileSignature } from "lucide-react";
import { useNavigate, useParams, Link } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader } from "@/shared/components/ui/card";
import { Separator } from "@/shared/components/ui/separator";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useCopyToClipboard } from "@/shared/hooks/useCopyToClipboard";
import { useGetConsentFormByIdQuery } from "../consentFormsApi";
import type { ConsentFormDetailResponse } from "../form.types";

// ── Helpers ───────────────────────────────────────────────────────────────────

function formatDateTime(dateStr: string): string {
  return new Date(dateStr).toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

function formatRelative(dateStr: string): string {
  const diffMs    = Date.now() - new Date(dateStr).getTime();
  const diffDays  = Math.floor(diffMs / 86_400_000);
  const diffHours = Math.floor(diffMs / 3_600_000);
  const diffMins  = Math.floor(diffMs / 60_000);

  if (diffMins  < 1)  return "just now";
  if (diffMins  < 60) return `${diffMins}m ago`;
  if (diffHours < 24) return `${diffHours}h ago`;
  if (diffDays  < 30) return `${diffDays}d ago`;
  const diffMonths = Math.floor(diffDays / 30);
  return `${diffMonths}mo ago`;
}

// ── Sub-components ────────────────────────────────────────────────────────────

function DetailRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      {/* text-foreground/65 ≈ 6.2:1 on #000 dark background — passes WCAG AA */}
      <p className="text-xs font-medium text-foreground/65 uppercase tracking-wider">{label}</p>
      <div className="text-sm text-foreground">{children}</div>
    </div>
  );
}

function SignatureDisplay({ value }: { value: string }) {
  const isImage = value.startsWith("data:image/");

  if (isImage) {
    return (
      <img
        src={value}
        alt="Digital signature"
        className="max-h-20 max-w-xs border-b border-foreground/20 pb-1 object-contain"
      />
    );
  }

  // Text / typed-name signature
  return (
    <p className="font-medium text-base italic border-b border-foreground/20 pb-1 font-serif">
      {value}
    </p>
  );
}

function ConsentFormDetail({ form }: { form: ConsentFormDetailResponse }) {
  const navigate = useNavigate();
  const [copied, copy] = useCopyToClipboard();

  const isPdf = form.fileUrl?.toLowerCase().endsWith(".pdf") ?? false;
  const docLabel = isPdf ? "View signed consent (PDF)" : "View document";

  return (
    <>
      {/* ── Header breadcrumb ── */}
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/forms/consent")}
          className="gap-1.5"
          aria-label="Back to Consent Forms"
        >
          <ArrowLeft className="h-4 w-4" />
          Consent Forms
        </Button>
        <div className="flex items-center gap-2">
          <FileSignature className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Consent Form</span>
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
        <Card>
          <CardHeader className="p-5 pb-0">
            {/* ── Status row ── */}
            <div className="flex items-center justify-between gap-3">
              <span
                className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${
                  form.signedAt
                    ? "bg-green-500/15 text-green-600 dark:text-green-400"
                    : "bg-yellow-500/15 text-yellow-700 dark:text-yellow-400"
                }`}
                aria-label={`Status: ${form.signedAt ? "Signed" : "Pending"}`}
              >
                {form.signedAt ? "Signed" : "Pending"}
              </span>

              {/* Truncated ID with copy */}
              <div className="flex items-center gap-1.5">
                <span className="text-xs text-foreground/50 font-mono" aria-label="Form ID">
                  {form.id.slice(0, 8)}…
                </span>
                <button
                  type="button"
                  onClick={() => copy(form.id)}
                  className="text-foreground/40 hover:text-foreground transition-colors"
                  aria-label="Copy full form ID"
                >
                  {copied
                    ? <Check  className="h-3.5 w-3.5 text-green-500" />
                    : <Copy   className="h-3.5 w-3.5" />}
                </button>
              </div>
            </div>
          </CardHeader>

          <CardContent className="p-5 pt-4 space-y-5">
            {/* ── Identity fields ── */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <DetailRow label="Client">
                <Link
                  to={`/clients/${form.clientId}`}
                  className="font-medium hover:underline underline-offset-2 text-primary"
                >
                  {form.clientName}
                </Link>
              </DetailRow>

              <DetailRow label="Appointment">
                <Link
                  to={`/appointments/${form.appointmentId}`}
                  className="font-medium hover:underline underline-offset-2 text-primary"
                >
                  {new Date(form.appointmentDate).toLocaleDateString("en-GB", {
                    weekday: "short", day: "numeric", month: "short", year: "numeric",
                  })}
                </Link>
                {form.artistName && (
                  <p className="text-xs text-foreground/55 mt-0.5">
                    {form.artistName}
                  </p>
                )}
              </DetailRow>
            </div>

            {/* ── Signature ── */}
            {form.signatureData && (
              <>
                <Separator />
                <DetailRow label="Digital signature">
                  <SignatureDisplay value={form.signatureData} />
                </DetailRow>
              </>
            )}

            {/* ── Document link + download ── */}
            {form.fileUrl && (
              <>
                <Separator />
                <DetailRow label="Consent document">
                  <div className="flex items-center gap-3 flex-wrap">
                    <a
                      href={form.fileUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="inline-flex items-center gap-1.5 text-sm text-primary underline underline-offset-2 hover:opacity-80"
                      aria-label={docLabel}
                    >
                      <ExternalLink className="h-3.5 w-3.5" aria-hidden />
                      {docLabel}
                    </a>

                    {isPdf && (
                      <a
                        href={form.fileUrl}
                        download
                        className="inline-flex items-center gap-1.5 text-sm text-foreground/60 hover:text-foreground transition-colors"
                        aria-label="Download signed consent form PDF"
                      >
                        <Download className="h-3.5 w-3.5" aria-hidden />
                        Download
                      </a>
                    )}
                  </div>
                </DetailRow>
              </>
            )}

            {/* ── Timestamps ── */}
            <Separator />
            <div className="grid grid-cols-2 gap-4">
              <DetailRow label="Created">
                <span>{formatDateTime(form.createdAt)}</span>
                <p className="text-xs text-foreground/45 mt-0.5">{formatRelative(form.createdAt)}</p>
              </DetailRow>

              {form.signedAt && (
                <DetailRow label="Signed">
                  <span>{formatDateTime(form.signedAt)}</span>
                  <p className="text-xs text-foreground/45 mt-0.5">{formatRelative(form.signedAt)}</p>
                </DetailRow>
              )}
            </div>
          </CardContent>
        </Card>

        {/* ── Back to appointment CTA ── */}
        <div className="flex justify-end">
          <Button
            variant="outline"
            size="sm"
            onClick={() => navigate(`/appointments/${form.appointmentId}`)}
            className="gap-1.5"
          >
            <ArrowLeft className="h-3.5 w-3.5" />
            Back to appointment
          </Button>
        </div>
      </main>
    </>
  );
}

// ── Page shell ────────────────────────────────────────────────────────────────

export function ConsentFormDetailPage() {
  const { id }    = useParams<{ id: string }>();
  const navigate  = useNavigate();

  useDocumentMeta({
    title:     "Consent Form — Pena e Artë",
    canonical: id ? `/forms/consent/${id}` : "/forms/consent",
  });

  const { data: form, isLoading, isError, error } =
    useGetConsentFormByIdQuery(id ?? "", { skip: !id });

  // Distinguish 404 from other errors (RTK Query exposes status on the error object)
  const isNotFound =
    isError &&
    !!error &&
    "status" in error &&
    error.status === 404;

  return (
    <div className="min-h-screen bg-background">
      {/* Header always visible — even on error, users can nav back */}
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/forms/consent")}
          className="gap-1.5"
          aria-label="Back to Consent Forms"
        >
          <ArrowLeft className="h-4 w-4" />
          Consent Forms
        </Button>
        <div className="flex items-center gap-2">
          <FileSignature className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Consent Form</span>
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6">
        {/* ── Loading skeleton ── */}
        {isLoading && (
          <Card aria-label="Loading consent form">
            <CardContent className="p-5 space-y-5">
              <div className="flex items-center justify-between">
                <Skeleton className="h-5 w-16 rounded-full" />
                <Skeleton className="h-4 w-24" />
              </div>
              {Array.from({ length: 4 }).map((_, i) => (
                <div key={i} className="space-y-1.5">
                  <Skeleton className="h-3 w-20" />
                  <Skeleton className="h-5 w-full" />
                </div>
              ))}
            </CardContent>
          </Card>
        )}

        {/* ── Not found ── */}
        {isNotFound && (
          <div className="flex flex-col items-center gap-4 py-20 text-center">
            <FileSignature className="h-10 w-10 text-muted-foreground/40" />
            <div className="space-y-1">
              <p className="text-sm font-medium">Consent form not found</p>
              <p className="text-xs text-muted-foreground">
                This form may have been removed, or you may not have permission to view it.
              </p>
            </div>
            <Button variant="outline" size="sm" onClick={() => navigate("/forms/consent")}>
              Back to Consent Forms
            </Button>
          </div>
        )}

        {/* ── Generic error ── */}
        {isError && !isNotFound && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            Failed to load consent form. Please try again.
          </p>
        )}

        {/* ── Data ── */}
        {form && <ConsentFormDetail form={form} />}
      </main>
    </div>
  );
}
```

> **Implementation notes:**
> - The header is rendered twice in this component (once inside `ConsentFormDetail` for the data path, and once in the outer shell for loading/error paths). Extract it into a small `PageHeader` sub-component or inline it in the outer shell only and remove the duplicate inside `ConsentFormDetail`. Either is acceptable — choose the approach that avoids JSX duplication.
> - The `Link` component for `/clients/${form.clientId}` navigates to the client detail route (`/clients/:id`). Confirm this route exists in `router.tsx` (it does: `{ path: ":id", element: <ClientDetailPage /> }` under `clients`). The same applies to `/appointments/:id`.
> - The `"Back to appointment"` button is gated by `form.appointmentId` always being present (it's a non-nullable `Guid` in the domain model) — no conditional needed.
> - `Separator` from shadcn/ui is already used elsewhere in the codebase — no new import needed if it's already in `shared/components/ui/`.

---

### Step F5 — Update `ConsentFormListPage.tsx` to show client name

**File:** `frontend/src/features/forms/components/ConsentFormListPage.tsx`

In `ConsentFormRow`, replace the UUID truncation display:

```tsx
// BEFORE (lines 33-36 approx):
<p className="text-xs text-muted-foreground">
  Client: <span className="font-mono">{form.clientId.slice(0, 8)}…</span>
</p>
<p className="text-xs text-muted-foreground">
  Appt: <span className="font-mono">{form.appointmentId.slice(0, 8)}…</span>
</p>

// AFTER:
<p className="text-xs text-muted-foreground">
  {form.clientName || form.clientId.slice(0, 8) + "…"}
</p>
<p className="text-xs text-muted-foreground font-mono">
  {form.appointmentId.slice(0, 8)}…
</p>
```

The appointment ID stays as a truncated mono reference on the list (full resolution only on the detail page). The client name is shown as the primary identity label.

---

### Step F6 — Update test fixtures and add new tests

**File:** `frontend/src/features/forms/__tests__/ConsentForms.test.tsx`

1. Update `SIGNED_FORM` and `PENDING_FORM` to conform to `ConsentFormDetailResponse` shape (for the detail page tests), and `ConsentFormResponse` shape with `clientName` (for list tests):

```ts
// List fixtures — add clientName
const SIGNED_FORM: ConsentFormResponse = {
  // ...existing fields...
  clientName: "Marco Cliente",
};

const PENDING_FORM: ConsentFormResponse = {
  // ...existing fields...
  clientName: "Pending Client",
};

// Detail fixture — full ConsentFormDetailResponse
const SIGNED_FORM_DETAIL: ConsentFormDetailResponse = {
  ...SIGNED_FORM,
  clientName:      "Marco Cliente",
  appointmentDate: APPOINTMENT.date,
  artistName:      "Luca Artista",
  artistId:        "artist-001",
};

// MSW handler — detail endpoint returns ConsentFormDetailResponse
http.get("http://localhost/api/v1/consent-forms/:id", ({ params }) =>
  params.id === SIGNED_FORM.id
    ? HttpResponse.json(SIGNED_FORM_DETAIL)
    : HttpResponse.json({ ...PENDING_FORM, appointmentDate: FUTURE, artistName: null, artistId: null }),
),
```

2. Update the existing `ConsentFormDetailPage` test:

```ts
// Update the existing "shows a loading skeleton then renders the signed form" test:
it("shows a loading skeleton then renders the signed form", async () => {
  renderDetailPage(SIGNED_FORM.id);
  expect(screen.getByLabelText(/loading consent form/i)).toBeInTheDocument();
  // Now shows client name, not raw UUID
  expect(await screen.findByText("Marco Cliente")).toBeInTheDocument();
  expect(screen.getAllByText("Signed").length).toBeGreaterThanOrEqual(1);
});
```

3. Add new `ConsentFormDetailPage` tests:

```ts
it("renders base64 signatureData as an <img> not as text", async () => {
  const base64Sig = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
  server.use(
    http.get("http://localhost/api/v1/consent-forms/:id", () =>
      HttpResponse.json({ ...SIGNED_FORM_DETAIL, signatureData: base64Sig }),
    ),
  );
  renderDetailPage(SIGNED_FORM.id);
  const img = await screen.findByRole("img", { name: /digital signature/i });
  expect(img).toHaveAttribute("src", base64Sig);
  // The raw base64 string must NOT appear as visible text
  expect(screen.queryByText(base64Sig)).not.toBeInTheDocument();
});

it("renders typed-name signatureData as italic text not as an image", async () => {
  renderDetailPage(SIGNED_FORM.id);
  // "Marco Cliente" should appear as text (not as an img src)
  expect(await screen.findByText("Marco Cliente")).toBeInTheDocument();
  expect(screen.queryByRole("img", { name: /digital signature/i })).not.toBeInTheDocument();
});

it("shows a 'not found' message on 404", async () => {
  server.use(
    http.get("http://localhost/api/v1/consent-forms/:id", () =>
      HttpResponse.json({ message: "Not found" }, { status: 404 }),
    ),
  );
  renderDetailPage("nonexistent-id");
  expect(await screen.findByText(/consent form not found/i)).toBeInTheDocument();
});

it("shows a download link when fileUrl ends in .pdf", async () => {
  renderDetailPage(SIGNED_FORM.id);
  expect(await screen.findByRole("link", { name: /download/i })).toBeInTheDocument();
});

it("shows a relative timestamp alongside the absolute date", async () => {
  renderDetailPage(SIGNED_FORM.id);
  // formatRelative returns strings like "Xd ago", "Xmo ago" — check for "ago"
  const agos = await screen.findAllByText(/ago/);
  expect(agos.length).toBeGreaterThanOrEqual(1);
});

it("shows client name as a link to the client profile", async () => {
  renderDetailPage(SIGNED_FORM.id);
  await screen.findByText("Marco Cliente");
  const link = screen.getByRole("link", { name: "Marco Cliente" });
  expect(link).toHaveAttribute("href", expect.stringContaining(SIGNED_FORM.clientId));
});
```

---

## Verification

Run in order — fix every failure before the next step.

```bash
cd "Pena e Arte"

# 1. Backend compiles
dotnet build

# 2. All unit tests (including new GetConsentFormByIdHandlerTests)
dotnet test tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj --no-build

# 3. Full test suite
dotnet test --no-build

# 4. Frontend type-checks
cd frontend && pnpm tsc --noEmit

# 5. Frontend unit tests (new + regression)
pnpm test -- --reporter=verbose features/forms
```

All five commands must exit 0.

---

## Exit Condition

Steps 1–5 all green. Then append to `docs/claude/architecture.md`:

```markdown
## Consent Form Detail — Bug Fixes & UI/UX Overhaul — 2026-07-04

### Bugs fixed
- **B-01 (CRITICAL) — Signature rendering:** `signatureData` now detected as image
  (`data:image/` prefix) and rendered as `<img>`, or as italic text for typed names.
  Previously the raw base64 string was injected into a `<p>` node as text.
- **B-02 (CRITICAL) — Raw UUIDs:** `ConsentFormDetailResponse` (new) resolves
  `ClientName`, `AppointmentDate`, `ArtistName` server-side. No UUID is shown to
  end users on the detail page. List page uses `ClientName` from enriched
  `ConsentFormResponse`.
- **B-03 — Timestamp integrity guard:** `GetConsentFormByIdHandler` logs a warning
  when `SignedAt < CreatedAt` so the anomaly is visible in Loki/Grafana.
- **B-04 — WCAG AA contrast:** `DetailRow` labels changed from `text-muted-foreground`
  (~3.8:1) to `text-foreground/65` (~6:1 on dark, verified against #000).
- **B-05 — Missing UX:** Copy-to-clipboard on form ID, Download link for PDF consent,
  link to client profile, link + "Back to appointment" button, `useDocumentMeta` added.
- **B-06 — Not-found state:** 404 responses render a dedicated empty state with
  explanatory text, distinct from the generic error state.

### Architecture decisions
- `ConsentFormDetailResponse` is a separate Contracts record (not an extended type) so
  list endpoints remain lightweight — no forced LEFT JOIN on every list load.
- `ConsentFormResponse` (list) was extended with `ClientName` via SQL projection in
  `GetConsentFormsQuery.Select(f => new ConsentFormResponse(..., f.Client.FirstName + ...))`.
  No `.Include()` needed — EF Core/Pomelo translates the nav-property access to a JOIN.
- `SignatureDisplay` component detects `data:image/` prefix; renders `<img>` or italic
  text accordingly. This handles both the current text-name UI and any legacy
  canvas-signature data in the DB without a migration.
- `useCopyToClipboard` added to `shared/hooks/` — reusable across other entity
  detail pages (appointment ID, client ID, design ID, etc.).
- `formatRelative` is a local helper (not a library) — keeps the dependency count stable.

### Files changed
Backend:
- `Pena_e_Arte.Contracts/Responses/ConsentFormResponse.cs` (extended + new detail record)
- `Pena_e_Arte.Application/ConsentForms/Queries/GetConsentFormByIdQuery.cs` (enriched)
- `Pena_e_Arte.Application/ConsentForms/Queries/GetConsentFormsQuery.cs` (SQL projection)
- `Pena_e_Arte.Application/ConsentForms/Commands/SignConsentFormCommand.cs` (Map updated)
- `Pena_e_Arte.API/Endpoints/FormEndpoints.cs` (return type updated)
- `tests/Pena_e_Arte.UnitTests/ConsentForms/GetConsentFormByIdHandlerTests.cs` (NEW)

Frontend:
- `frontend/src/features/forms/form.types.ts` (new ConsentFormDetailResponse)
- `frontend/src/features/forms/consentFormsApi.ts` (updated return type)
- `frontend/src/features/forms/components/ConsentFormDetailPage.tsx` (full rewrite)
- `frontend/src/features/forms/components/ConsentFormListPage.tsx` (clientName)
- `frontend/src/shared/hooks/useCopyToClipboard.ts` (NEW)
- `frontend/src/features/forms/__tests__/ConsentForms.test.tsx` (fixtures + 6 new tests)
```
