# Overnight Prompt — Referral Codes Page Overhaul (2026-06-18)

> **Scope:** Complete overhaul of `PlatformReferralPage.tsx` plus three new
> backend commands (Generate, Reactivate, Delete) and their endpoints.
>
> No new npm packages. No new NuGet packages.
> Update `docs/claude/architecture.md` whenever a new `IgnoreQueryFilters()`
> usage is introduced — the table is the canonical record.
> Commit after each numbered task.

---

## 0. Mandatory Reading (Do This First)

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/architecture.md   ← pay attention to IgnoreQueryFilters() Approved Usages table
```

Then read these source files:

```
frontend/src/features/platform/components/PlatformReferralPage.tsx
frontend/src/features/platform/__tests__/PlatformReferralPage.test.tsx
frontend/src/features/platform/platform.types.ts
frontend/src/features/platform/platformApi.ts
Pena_e_Arte.Contracts/Responses/PlatformReferralCodeResponse.cs
Pena_e_Arte.Contracts/Responses/ReferralCodeResponse.cs
Pena_e_Arte.Application/Platform/Commands/DeactivateReferralCodeCommand.cs
Pena_e_Arte.Application/Platform/Queries/GetPlatformReferralCodesQuery.cs
Pena_e_Arte.Application/Referrals/Commands/GenerateReferralCodeCommand.cs
Pena_e_Arte.API/Endpoints/PlatformEndpoints.cs
```

---

## 1. Backend — Three New Commands

### 1a. `IssuerGenerateReferralCodeCommand`

**File:** `Pena_e_Arte.Application/Platform/Commands/IssuerGenerateReferralCodeCommand.cs`

The existing `GenerateReferralCodeCommand` is OwnerOnly and tenant-scoped.
The issuer needs a cross-tenant equivalent that bypasses the tenant check and
returns `PlatformReferralCodeResponse` so the RTK Query `PlatformReferral` tag
cache updates without extra round trips.

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Commands;

public record IssuerGenerateReferralCodeCommand(Guid StudioId) : IRequest<PlatformReferralCodeResponse>;

public class IssuerGenerateReferralCodeHandler(
    IAppDbContext                                  db,
    ILogger<IssuerGenerateReferralCodeHandler>     logger)
    : IRequestHandler<IssuerGenerateReferralCodeCommand, PlatformReferralCodeResponse>
{
    // Character set matches GenerateReferralCodeCommand.Alphabet
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public async Task<PlatformReferralCodeResponse> Handle(
        IssuerGenerateReferralCodeCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #9 — issuer generates referral code
        // for any studio cross-tenant. See architecture.md.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        // Deactivate any existing active codes for this studio
        List<ReferralCode> existing = await db.ReferralCodes
            .IgnoreQueryFilters()
            .Where(r => r.StudioId == command.StudioId && r.IsActive)
            .ToListAsync(ct);
        foreach (ReferralCode old in existing)
            old.IsActive = false;

        string code = await GenerateUniqueCodeAsync(ct);

        ReferralCode referralCode = new()
        {
            StudioId    = command.StudioId,
            Code        = code,
            IsActive    = true,
            IsSingleUse = true,
        };

        db.ReferralCodes.Add(referralCode);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Issuer generated referral code {ReferralCodeId} for studio {StudioId}",
            referralCode.Id, command.StudioId);

        return new PlatformReferralCodeResponse(
            referralCode.Id,
            referralCode.StudioId,
            studio.Name,
            referralCode.Code,
            referralCode.IsActive,
            referralCode.IsSingleUse,
            referralCode.CreatedAt,
            referralCode.ExpiresAt,
            RedemptionCount: 0);
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string candidate = GenerateCode();
            bool taken = await db.ReferralCodes
                .IgnoreQueryFilters()
                .AnyAsync(r => r.Code == candidate, ct);
            if (!taken) return candidate;
        }
        throw new InvalidOperationException(
            "Unable to generate a unique referral code after 10 attempts.");
    }

    internal static string GenerateCode()
    {
        char[] chars = new char[8];
        byte[] bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(8);
        for (int i = 0; i < 8; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }
}

public class IssuerGenerateReferralCodeValidator
    : AbstractValidator<IssuerGenerateReferralCodeCommand>
{
    public IssuerGenerateReferralCodeValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
```

### 1b. `ReactivateReferralCodeCommand`

**File:** `Pena_e_Arte.Application/Platform/Commands/ReactivateReferralCodeCommand.cs`

Mirrors `DeactivateReferralCodeCommand`. Sets `IsActive = true` and deactivates
any other currently active code for the same studio (one-active-per-studio invariant).

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Commands;

public record ReactivateReferralCodeCommand(Guid ReferralCodeId) : IRequest;

public class ReactivateReferralCodeHandler(IAppDbContext db)
    : IRequestHandler<ReactivateReferralCodeCommand>
{
    public async Task Handle(ReactivateReferralCodeCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #10 — issuer reactivates any
        // studio's referral code cross-tenant. See architecture.md.
        Domain.Entities.ReferralCode code = await db.ReferralCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == command.ReferralCodeId, ct)
            ?? throw new NotFoundException(
                nameof(Domain.Entities.ReferralCode), command.ReferralCodeId);

        // Deactivate any other currently active codes for this studio first
        List<Domain.Entities.ReferralCode> others = await db.ReferralCodes
            .IgnoreQueryFilters()
            .Where(r => r.StudioId == code.StudioId && r.Id != code.Id && r.IsActive)
            .ToListAsync(ct);
        foreach (Domain.Entities.ReferralCode other in others)
            other.IsActive = false;

        code.IsActive = true;

        await db.SaveChangesAsync(ct);
    }
}

public class ReactivateReferralCodeValidator
    : AbstractValidator<ReactivateReferralCodeCommand>
{
    public ReactivateReferralCodeValidator()
    {
        RuleFor(x => x.ReferralCodeId).NotEmpty();
    }
}
```

### 1c. `DeleteReferralCodeCommand`

**File:** `Pena_e_Arte.Application/Platform/Commands/DeleteReferralCodeCommand.cs`

Codes that have been redeemed are historical records — they cannot be hard-deleted.
Only codes with zero redemptions may be deleted. Use `BusinessRuleViolationException`.

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Commands;

public record DeleteReferralCodeCommand(Guid ReferralCodeId) : IRequest;

public class DeleteReferralCodeHandler(IAppDbContext db)
    : IRequestHandler<DeleteReferralCodeCommand>
{
    public async Task Handle(DeleteReferralCodeCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #11 — issuer deletes any
        // studio's unredeemed referral code cross-tenant. See architecture.md.
        Domain.Entities.ReferralCode code = await db.ReferralCodes
            .IgnoreQueryFilters()
            .Include(r => r.Redemptions)
            .FirstOrDefaultAsync(r => r.Id == command.ReferralCodeId, ct)
            ?? throw new NotFoundException(
                nameof(Domain.Entities.ReferralCode), command.ReferralCodeId);

        if (code.Redemptions.Count > 0)
            throw new BusinessRuleViolationException(
                "Cannot delete a referral code that has been redeemed. Deactivate it instead.");

        db.ReferralCodes.Remove(code);
        await db.SaveChangesAsync(ct);
    }
}

public class DeleteReferralCodeValidator
    : AbstractValidator<DeleteReferralCodeCommand>
{
    public DeleteReferralCodeValidator()
    {
        RuleFor(x => x.ReferralCodeId).NotEmpty();
    }
}
```

Run `dotnet build` — must succeed.

**Commit:** `feat(referrals): IssuerGenerateReferralCodeCommand, ReactivateReferralCodeCommand, DeleteReferralCodeCommand`

---

## 2. Backend — Register Endpoints

**File:** `Pena_e_Arte.API/Endpoints/PlatformEndpoints.cs`

Add three new endpoint registrations inside `MapPlatformEndpoints` (the existing
group already has `.RequireAuthorization("IssuerOnly")` applied):

```csharp
// After the existing referral-codes endpoints:
group.MapPost("studios/{studioId:guid}/referral-codes", GenerateReferralCodeForStudio);
group.MapPatch("referral-codes/{id:guid}/reactivate",   ReactivateReferralCode);
group.MapDelete("referral-codes/{id:guid}",             DeleteReferralCode);
```

Add the three handler methods in the same file:

```csharp
private static async Task<IResult> GenerateReferralCodeForStudio(
    Guid              studioId,
    ISender           mediator,
    CancellationToken ct)
{
    PlatformReferralCodeResponse result =
        await mediator.Send(new IssuerGenerateReferralCodeCommand(studioId), ct);
    return Results.Ok(result);
}

private static async Task<IResult> ReactivateReferralCode(
    Guid              id,
    ISender           mediator,
    CancellationToken ct)
{
    await mediator.Send(new ReactivateReferralCodeCommand(id), ct);
    return Results.NoContent();
}

private static async Task<IResult> DeleteReferralCode(
    Guid              id,
    ISender           mediator,
    CancellationToken ct)
{
    await mediator.Send(new DeleteReferralCodeCommand(id), ct);
    return Results.NoContent();
}
```

Add the missing using statement to the file:
```csharp
using Pena_e_Arte.Application.Platform.Commands;
// (IssuerGenerateReferralCodeCommand, ReactivateReferralCodeCommand, DeleteReferralCodeCommand)
```

Run `dotnet build` — must succeed.

**Commit:** `feat(referrals): register issuer generate/reactivate/delete endpoints`

---

## 3. Backend — Update `architecture.md` IgnoreQueryFilters Table

**File:** `docs/claude/architecture.md`

Add usages #9, #10, #11 to the `IgnoreQueryFilters() Approved Usages` table:

```markdown
| 9  | `IssuerGenerateReferralCodeHandler` | Cross-tenant studio lookup + referral code generation for issuer | IssuerOnly |
| 10 | `ReactivateReferralCodeHandler`     | Cross-tenant referral code reactivation                          | IssuerOnly |
| 11 | `DeleteReferralCodeHandler`         | Cross-tenant referral code deletion (unredeemed only)            | IssuerOnly |
```

**Commit:** `docs(architecture): register IgnoreQueryFilters usages #9-11`

---

## 4. Backend — Unit Tests for New Commands

**New file:** `tests/Pena_e_Arte.UnitTests/Platform/IssuerGenerateReferralCodeHandlerTests.cs`

> Pattern: follow `DeactivateReferralCodeHandler` tests if they exist; otherwise
> follow `CreatePlanHandlerTests.cs` for the `FakeDbContext.Create()` pattern.

Write tests covering:
- `Handle_ValidStudio_ReturnsNewActiveCode`
- `Handle_DeactivatesExistingActiveCodesBeforeGenerating`
- `Handle_StudioNotFound_ThrowsNotFoundException`
- `Handle_NewCodeHasZeroRedemptionCount`

**New file:** `tests/Pena_e_Arte.UnitTests/Platform/ReactivateReferralCodeHandlerTests.cs`

Tests:
- `Handle_ValidCode_SetsIsActiveTrue`
- `Handle_DeactivatesOtherActiveCodes`
- `Handle_CodeNotFound_ThrowsNotFoundException`

**New file:** `tests/Pena_e_Arte.UnitTests/Platform/DeleteReferralCodeHandlerTests.cs`

Tests:
- `Handle_CodeWithNoRedemptions_DeletesSuccessfully`
- `Handle_CodeWithRedemptions_ThrowsBusinessRuleViolationException`
- `Handle_CodeNotFound_ThrowsNotFoundException`

Run `dotnet test` — all tests must pass.

**Commit:** `test(referrals): unit tests for issuer generate, reactivate, delete commands`

---

## 5. Frontend — Update `platformApi.ts`

**File:** `frontend/src/features/platform/platformApi.ts`

Add three mutations and update the export list:

```typescript
generateReferralCodeForStudio: builder.mutation<PlatformReferralCodeResponse, string>({
  query: (studioId) => ({
    url:    `platform/studios/${studioId}/referral-codes`,
    method: "POST",
  }),
  invalidatesTags: ["PlatformReferral", "PlatformStats"],
}),
reactivateReferralCode: builder.mutation<void, string>({
  query: (id) => ({
    url:    `platform/referral-codes/${id}/reactivate`,
    method: "PATCH",
  }),
  invalidatesTags: ["PlatformReferral"],
}),
deleteReferralCode: builder.mutation<void, string>({
  query: (id) => ({
    url:    `platform/referral-codes/${id}`,
    method: "DELETE",
  }),
  invalidatesTags: ["PlatformReferral"],
}),
```

Export the three hooks:

```typescript
export const {
  // ...existing exports...
  useGenerateReferralCodeForStudioMutation,
  useReactivateReferralCodeMutation,
  useDeleteReferralCodeMutation,
} = platformApi;
```

Run `pnpm --dir frontend tsc --noEmit` — must produce zero errors.

**Commit:** `feat(referrals): add generate/reactivate/delete mutations to platformApi`

---

## 6. Frontend — Full `PlatformReferralPage.tsx` Overhaul

Apply all changes in a single editing pass. The entire file needs rewriting.
Use this specification:

### 6a. New imports

```tsx
import { useState } from "react";
import {
  Check,
  ClipboardCopy,
  Loader2,
  Plus,
  Share2,
  Trash2,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Label } from "@/shared/components/ui/label";
import { useGetStudiosQuery } from "@/features/studios/studiosApi";
import {
  useGetPlatformReferralCodesQuery,
  useDeactivateReferralCodeMutation,
  useReactivateReferralCodeMutation,
  useDeleteReferralCodeMutation,
  useGenerateReferralCodeForStudioMutation,
} from "@/features/platform/platformApi";
import type { PlatformReferralCodeResponse } from "@/features/platform/platform.types";
```

### 6b. Date formatter (top-level helper)

```tsx
function fmt(date: string | Date): string {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}
```

### 6c. Skeleton component

```tsx
function ReferralCodeRowSkeleton() {
  return (
    <Card>
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-1.5 flex-1">
            <div className="flex items-center gap-2">
              <Skeleton className="h-4 w-24" />
              <Skeleton className="h-5 w-14 rounded-full" />
              <Skeleton className="h-5 w-16 rounded-full" />
            </div>
            <Skeleton className="h-3 w-56" />
          </div>
          <div className="flex items-center gap-1.5">
            <Skeleton className="h-7 w-7" />
            <Skeleton className="h-7 w-20" />
            <Skeleton className="h-7 w-14" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
```

### 6d. `ReferralCodeRow` — full replacement

```tsx
interface ReferralCodeRowProps {
  code: PlatformReferralCodeResponse;
}

function ReferralCodeRow({ code }: ReferralCodeRowProps) {
  const [copied,     setCopied]     = useState(false);
  const [deactivating, setDeactivating] = useState(false);
  const [reactivating, setReactivating] = useState(false);
  const [deleting,   setDeleting]   = useState(false);

  const [deactivate, { isLoading: deactivating_ }] = useDeactivateReferralCodeMutation();
  const [reactivate, { isLoading: reactivating_ }] = useReactivateReferralCodeMutation();
  const [deleteFn,   { isLoading: deleting_     }] = useDeleteReferralCodeMutation();

  async function handleCopy() {
    await navigator.clipboard.writeText(code.code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  async function handleDeactivate() {
    await deactivate(code.id).unwrap();
    setDeactivating(false);
  }

  async function handleReactivate() {
    await reactivate(code.id).unwrap();
    setReactivating(false);
  }

  async function handleDelete() {
    await deleteFn(code.id).unwrap();
    // Row will disappear from the list via tag invalidation
  }

  const anyExpanded = deactivating || reactivating || deleting;

  const statusClass = code.isActive
    ? "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300"
    : "bg-muted text-muted-foreground";

  return (
    <Card>
      <CardContent className="p-4 space-y-2">

        {/* ── Main row ─────────────────────────────────────────────── */}
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-0.5 min-w-0">
            <div className="flex items-center gap-2 flex-nowrap min-w-0">
              <span className="font-mono font-medium text-sm shrink-0">{code.code}</span>
              <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 ${statusClass}`}>
                {code.isActive ? "Active" : "Inactive"}
              </span>
              {code.isSingleUse && (
                <span className="text-xs px-1.5 py-0.5 rounded-full bg-muted
                                  text-muted-foreground shrink-0">
                  Single use
                </span>
              )}
            </div>
            <p className="text-xs text-muted-foreground">
              {code.studioName}
              {" · "}
              {code.redemptionCount} {code.redemptionCount === 1 ? "redemption" : "redemptions"}
              {" · "}
              Generated {fmt(code.createdAt)}
              {code.expiresAt && ` · Expires ${fmt(code.expiresAt)}`}
            </p>
          </div>

          {/* ── Action zone (consistent for ALL cards) ──────────── */}
          {!anyExpanded && (
            <div className="flex items-center gap-1.5 shrink-0">

              {/* Copy to clipboard */}
              <Button
                size="sm"
                variant="ghost"
                className="h-7 w-7 p-0"
                onClick={handleCopy}
                aria-label={`Copy referral code ${code.code}`}
                title={copied ? "Copied!" : "Copy code"}
              >
                {copied
                  ? <Check className="h-3.5 w-3.5 text-green-500" />
                  : <ClipboardCopy className="h-3.5 w-3.5" />}
              </Button>

              {/* Deactivate (active) OR Reactivate (inactive) */}
              {code.isActive ? (
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-7 text-xs text-muted-foreground"
                  onClick={() => setDeactivating(true)}
                  aria-label={`Deactivate referral code ${code.code}`}
                >
                  Deactivate
                </Button>
              ) : (
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-7 text-xs"
                  onClick={() => setReactivating(true)}
                  aria-label={`Reactivate referral code ${code.code}`}
                >
                  Reactivate
                </Button>
              )}

              {/* Delete */}
              <Button
                size="sm"
                variant="ghost"
                className="h-7 w-7 p-0 text-muted-foreground hover:text-destructive transition-colors"
                onClick={() => setDeleting(true)}
                aria-label={`Delete referral code ${code.code}`}
                title="Delete"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>

            </div>
          )}
        </div>

        {/* ── Deactivate confirmation ──────────────────────────────── */}
        {deactivating && (
          <div className="flex items-center gap-2 pt-1 border-t">
            <span className="text-xs text-muted-foreground">
              Deactivate code <strong className="font-mono">{code.code}</strong>?
            </span>
            <Button
              size="sm"
              variant="destructive"
              className="h-7 px-2 text-xs"
              disabled={deactivating_}
              onClick={handleDeactivate}
            >
              {deactivating_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, deactivate"}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={() => setDeactivating(false)}
            >
              Cancel
            </Button>
          </div>
        )}

        {/* ── Reactivate confirmation ──────────────────────────────── */}
        {reactivating && (
          <div className="flex items-center gap-2 pt-1 border-t">
            <span className="text-xs text-muted-foreground">
              Reactivate code <strong className="font-mono">{code.code}</strong>?
              {" "}Any other active code for this studio will be deactivated.
            </span>
            <Button
              size="sm"
              className="h-7 px-2 text-xs"
              disabled={reactivating_}
              onClick={handleReactivate}
            >
              {reactivating_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, reactivate"}
            </Button>
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs"
              onClick={() => setReactivating(false)}
            >
              Cancel
            </Button>
          </div>
        )}

        {/* ── Delete confirmation ──────────────────────────────────── */}
        {deleting && (
          <div className="pt-2 space-y-1 border-t">
            {code.redemptionCount > 0 ? (
              <p className="text-xs text-amber-600 dark:text-amber-400">
                This code has {code.redemptionCount}{" "}
                {code.redemptionCount === 1 ? "redemption" : "redemptions"} —
                it cannot be deleted. Deactivate it instead.
              </p>
            ) : (
              <p className="text-xs text-muted-foreground">
                Permanently delete code <strong className="font-mono">{code.code}</strong>?
                This cannot be undone.
              </p>
            )}
            <div className="flex items-center gap-2">
              {code.redemptionCount === 0 && (
                <Button
                  size="sm"
                  variant="destructive"
                  className="h-7 px-2 text-xs"
                  disabled={deleting_}
                  onClick={handleDelete}
                >
                  {deleting_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, delete"}
                </Button>
              )}
              <Button
                size="sm"
                variant="ghost"
                className="h-7 px-2 text-xs"
                onClick={() => setDeleting(false)}
              >
                Cancel
              </Button>
            </div>
          </div>
        )}

      </CardContent>
    </Card>
  );
}
```

### 6e. Generate Code inline form

This is a sub-component used inside `PlatformReferralPage`:

```tsx
interface GenerateFormProps {
  onClose: () => void;
}

function GenerateCodeForm({ onClose }: GenerateFormProps) {
  const [studioId, setStudioId] = useState("");
  const { data: studios = [] } = useGetStudiosQuery();
  const [generate, { isLoading }] = useGenerateReferralCodeForStudioMutation();

  async function handleGenerate() {
    if (!studioId) return;
    await generate(studioId).unwrap();
    onClose();
  }

  return (
    <Card className="mb-4">
      <CardContent className="p-4 space-y-3">
        <p className="text-xs font-medium">Generate Referral Code</p>
        <div className="space-y-1">
          <Label htmlFor="gen-studio" className="text-xs">Studio</Label>
          <select
            id="gen-studio"
            value={studioId}
            onChange={(e) => setStudioId(e.target.value)}
            className="h-8 w-full rounded-md border border-input bg-background px-2 text-xs"
          >
            <option value="">Select a studio…</option>
            {studios.map((s) => (
              <option key={s.id} value={s.id}>{s.name}</option>
            ))}
          </select>
        </div>
        <p className="text-xs text-muted-foreground">
          Generates an 8-character single-use code. Any existing active code
          for the selected studio will be deactivated.
        </p>
        <div className="flex gap-2">
          <Button
            size="sm"
            className="h-7 px-3 text-xs gap-1"
            disabled={isLoading || !studioId}
            onClick={handleGenerate}
          >
            {isLoading ? <Loader2 className="h-3 w-3 animate-spin" /> : "Generate"}
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="h-7 px-3 text-xs"
            onClick={onClose}
          >
            Cancel
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
```

### 6f. `PlatformReferralPage` — full replacement

```tsx
export function PlatformReferralPage() {
  const [generating,    setGenerating]   = useState(false);
  const [search,        setSearch]       = useState("");
  const [statusFilter,  setStatusFilter] = useState<"all" | "active" | "inactive">("all");

  const { data: codes, isLoading, isError } = useGetPlatformReferralCodesQuery();

  const q = search.trim().toLowerCase();

  const filtered = (codes ?? []).filter((c) => {
    const matchesSearch = !q ||
      c.code.toLowerCase().includes(q) ||
      c.studioName.toLowerCase().includes(q);
    const matchesStatus =
      statusFilter === "all" ||
      (statusFilter === "active"   &&  c.isActive) ||
      (statusFilter === "inactive" && !c.isActive);
    return matchesSearch && matchesStatus;
  });

  return (
    <div className="min-h-screen bg-background">

      {/* ── Sticky header ───────────────────────────────────────── */}
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Share2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Referral Codes</span>
        {codes && (
          <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full
                           bg-muted text-muted-foreground font-medium">
            {codes.length}
          </span>
        )}
        <Button
          size="sm"
          className="ml-auto h-7 text-xs gap-1"
          onClick={() => setGenerating((g) => !g)}
          aria-label="Generate new referral code"
        >
          <Plus className="h-3.5 w-3.5" />
          Generate Code
        </Button>
      </header>

      <main className="max-w-3xl mx-auto px-4 py-4 space-y-3">

        {/* ── Helper text ─────────────────────────────────────────── */}
        <p className="text-xs text-muted-foreground">
          Referral codes give studios a shareable link for recruiting new tenants.
          Each studio can have one active code at a time.
        </p>

        {/* ── Generate form (inline) ──────────────────────────────── */}
        {generating && (
          <GenerateCodeForm onClose={() => setGenerating(false)} />
        )}

        {/* ── Search + filter ─────────────────────────────────────── */}
        {!isLoading && !isError && codes && codes.length > 0 && (
          <div className="flex gap-2 flex-wrap items-center">
            <div className="relative flex-1 min-w-48">
              <input
                type="search"
                placeholder="Search by code or studio…"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                className="h-8 w-full rounded-md border border-input bg-background
                           px-3 text-xs placeholder:text-muted-foreground
                           focus:outline-none focus:ring-1 focus:ring-ring"
                aria-label="Search referral codes"
              />
            </div>
            <div className="flex gap-1">
              {(["all", "active", "inactive"] as const).map((s) => (
                <button
                  key={s}
                  onClick={() => setStatusFilter(s)}
                  className={`text-xs px-2.5 py-1 rounded-full border transition-colors capitalize ${
                    statusFilter === s
                      ? "bg-primary text-primary-foreground border-primary"
                      : "hover:bg-muted border-border"
                  }`}
                >
                  {s === "all"
                    ? `All (${codes.length})`
                    : s === "active"
                      ? `Active (${codes.filter((c) => c.isActive).length})`
                      : `Inactive (${codes.filter((c) => !c.isActive).length})`}
                </button>
              ))}
            </div>
          </div>
        )}

        {/* ── Loading ─────────────────────────────────────────────── */}
        {isLoading && (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => <ReferralCodeRowSkeleton key={i} />)}
          </div>
        )}

        {/* ── Error ───────────────────────────────────────────────── */}
        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load referral codes.
          </p>
        )}

        {/* ── Empty state ─────────────────────────────────────────── */}
        {!isLoading && !isError && filtered.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <Share2 className="h-10 w-10 text-muted-foreground/30" />
            <p className="text-sm text-muted-foreground">
              {codes?.length === 0
                ? "No referral codes yet."
                : "No codes match your search."}
            </p>
            {codes?.length === 0 && (
              <Button
                size="sm"
                variant="outline"
                className="gap-1.5 text-xs mt-1"
                onClick={() => setGenerating(true)}
              >
                <Plus className="h-3.5 w-3.5" />
                Generate first code
              </Button>
            )}
            {codes && codes.length > 0 && (
              <Button
                size="sm"
                variant="ghost"
                className="text-xs"
                onClick={() => { setSearch(""); setStatusFilter("all"); }}
              >
                Clear filters
              </Button>
            )}
          </div>
        )}

        {/* ── Code list ───────────────────────────────────────────── */}
        {!isLoading && !isError && filtered.map((code) => (
          <ReferralCodeRow key={code.id} code={code} />
        ))}

      </main>
    </div>
  );
}
```

Run `pnpm --dir frontend tsc --noEmit` — zero errors.
Run `pnpm --dir frontend lint` — zero errors.

**Commit:** `feat(referrals): copy clipboard, activate inactive codes, delete, generate form, search/filter, skeleton`

---

## 7. Frontend — Update Tests

**File:** `frontend/src/features/platform/__tests__/PlatformReferralPage.test.tsx`

### 7a. Add `studiosApi` to the test store

The new generate form uses `useGetStudiosQuery`, so `studiosApi` must be in
the store and an MSW handler registered:

```typescript
import { studiosApi } from "@/features/studios/studiosApi";
import type { StudioResponse } from "@/features/studios/studiosApi";

const STUDIOS: StudioResponse[] = [
  {
    id:                   "s1",
    name:                 "Ink Soul",
    slug:                 "ink-soul",
    city:                 "Porto",
    latitude:             41.1,
    longitude:            -8.6,
    showPlatformBranding: true,
    allowBrandingRemoval: false,
    trialExpiresAt:       new Date(Date.now() + 14 * 86_400_000).toISOString(),
    createdAt:            "2024-01-01T00:00:00Z",
    isActive:             true,
  },
];

// Add to server:
http.get("http://localhost/api/v1/studios", () => HttpResponse.json(STUDIOS)),

// Add to makeStore():
[studiosApi.reducerPath]: studiosApi.reducer,
// and add studiosApi.middleware to the middleware chain
```

### 7b. Update loading test

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

### 7c. Update badge casing tests

```typescript
// Before:
it("shows 'active' badge for active codes", async () => {
  ...
  const activeBadges = screen.getAllByText("active");
  ...
});

it("shows 'inactive' badge for inactive codes", async () => {
  ...
  expect(screen.getByText("inactive")).toBeInTheDocument();
});

it("shows 'single-use' badge for single-use codes", async () => {
  ...
  expect(screen.getByText("single-use")).toBeInTheDocument();
});

// After:
it("shows 'Active' badge (Title Case) for active codes", async () => {
  renderPage();
  await screen.findByText("INK2026");
  const activeBadges = screen.getAllByText("Active", { selector: "span" });
  expect(activeBadges.length).toBeGreaterThanOrEqual(2);
});

it("shows 'Inactive' badge (Title Case) for inactive codes", async () => {
  renderPage();
  await screen.findByText("OLD2025");
  expect(screen.getByText("Inactive", { selector: "span" })).toBeInTheDocument();
  expect(screen.queryByText("inactive", { selector: "span" })).not.toBeInTheDocument();
});

it("shows 'Single use' badge for single-use codes", async () => {
  renderPage();
  await screen.findByText("ROOTS1X");
  expect(screen.getByText("Single use")).toBeInTheDocument();
  expect(screen.queryByText("single-use")).not.toBeInTheDocument();
});
```

### 7d. Update count display test

```typescript
// Before:
it("shows the total count in the header", async () => {
  renderPage();
  await screen.findByText("INK2026");
  expect(screen.getByText("(3)")).toBeInTheDocument();
});

// After:
it("shows the total count as a styled badge in the header", async () => {
  renderPage();
  await screen.findByText("INK2026");
  // Count is now a styled <span> not plain "(3)" text
  expect(screen.getByText("3", { selector: "span" })).toBeInTheDocument();
  expect(screen.queryByText("(3)")).not.toBeInTheDocument();
});
```

### 7e. Update deactivate confirmation tests

```typescript
// Before (deactivate confirmation):
it("clicking Deactivate shows confirmation with Yes/No", async () => {
  ...
  expect(screen.getByText(/deactivate\?/i)).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /yes/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /no/i })).toBeInTheDocument();
});

// After:
it("clicking Deactivate shows confirmation naming the code", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("INK2026");

  const [firstBtn] = screen.getAllByRole("button", { name: /deactivate/i });
  await user.click(firstBtn);

  // Confirmation names the code
  expect(screen.getByText(/deactivate code/i)).toBeInTheDocument();
  expect(screen.getByText(/INK2026/)).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /yes, deactivate/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
});

it("clicking Cancel hides the deactivate confirmation", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("INK2026");

  const [firstBtn] = screen.getAllByRole("button", { name: /deactivate/i });
  await user.click(firstBtn);
  await user.click(screen.getByRole("button", { name: /cancel/i }));

  expect(screen.queryByText(/yes, deactivate/i)).not.toBeInTheDocument();
});

// Patch the API call test:
it("clicking Yes, deactivate calls PATCH referral-codes/:id/deactivate", async () => {
  const deactivateSpy = vi.fn();
  server.use(
    http.patch("http://localhost/api/v1/platform/referral-codes/ref-1/deactivate", () => {
      deactivateSpy();
      return new HttpResponse(null, { status: 204 });
    }),
  );

  const user = userEvent.setup();
  renderPage();
  await screen.findByText("INK2026");

  const [firstBtn] = screen.getAllByRole("button", { name: /deactivate/i });
  await user.click(firstBtn);
  await user.click(screen.getByRole("button", { name: /yes, deactivate/i }));

  await waitFor(() => expect(deactivateSpy).toHaveBeenCalledOnce());
});
```

### 7f. Update "Deactivate only for active codes" count

The test `deactivateBtns).toHaveLength(2)` asserts 2 buttons (ref-1 and ref-2
are active). That remains true — but now the button text says "Deactivate" and
the aria-label says `deactivate referral code INK2026`. The `name: /deactivate/i`
regex still matches. No count change needed; verify it still passes.

### 7g. Add new tests

```typescript
it("shows 'Deactivate' button for active and 'Reactivate' button for inactive codes", async () => {
  renderPage();
  await screen.findByText("INK2026");
  // 2 active codes → 2 Deactivate buttons
  expect(screen.getAllByRole("button", { name: /deactivate/i })).toHaveLength(2);
  // 1 inactive code → 1 Reactivate button
  expect(screen.getAllByRole("button", { name: /reactivate/i })).toHaveLength(1);
});

it("shows Delete button on every card", async () => {
  renderPage();
  await screen.findByText("INK2026");
  // All 3 codes have a delete button
  expect(screen.getAllByRole("button", { name: /delete/i })).toHaveLength(3);
});

it("delete confirmation warns about redemptions if code has been redeemed", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("INK2026"); // ref-1 has 5 redemptions

  const deleteBtns = screen.getAllByRole("button", { name: /delete referral code/i });
  await user.click(deleteBtns[0]); // first = ref-1 (5 redemptions)

  expect(screen.getByText(/cannot be deleted/i)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /yes, delete/i })).not.toBeInTheDocument();
});

it("delete confirmation for unredeemed code shows 'Yes, delete' button", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("OLD2025"); // ref-3 has 3 redemptions — still blocked
  // Use a seed with 0 redemptions to test this path
  server.use(
    http.get("http://localhost/api/v1/platform/referral-codes", () =>
      HttpResponse.json([
        { ...CODES[2], redemptionCount: 0 }, // old code, 0 redemptions
      ]),
    ),
  );
  cleanup();
  renderPage();
  await screen.findByText("OLD2025");

  const deleteBtn = screen.getByRole("button", { name: /delete referral code old2025/i });
  await user.click(deleteBtn);

  expect(screen.getByRole("button", { name: /yes, delete/i })).toBeInTheDocument();
});

it("clicking Reactivate shows confirmation", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("OLD2025");

  await user.click(screen.getByRole("button", { name: /reactivate referral code old2025/i }));

  expect(screen.getByRole("button", { name: /yes, reactivate/i })).toBeInTheDocument();
});

it("confirming Reactivate calls PATCH referral-codes/:id/reactivate", async () => {
  const reactivateSpy = vi.fn();
  server.use(
    http.patch("http://localhost/api/v1/platform/referral-codes/ref-3/reactivate", () => {
      reactivateSpy();
      return new HttpResponse(null, { status: 204 });
    }),
  );

  const user = userEvent.setup();
  renderPage();
  await screen.findByText("OLD2025");

  await user.click(screen.getByRole("button", { name: /reactivate referral code old2025/i }));
  await user.click(screen.getByRole("button", { name: /yes, reactivate/i }));

  await waitFor(() => expect(reactivateSpy).toHaveBeenCalledOnce());
});

it("shows search input when codes exist", async () => {
  renderPage();
  await screen.findByText("INK2026");
  expect(screen.getByRole("searchbox")).toBeInTheDocument();
});

it("filters codes by search term (code string)", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("INK2026");

  await user.type(screen.getByRole("searchbox"), "INK");

  expect(screen.getByText("INK2026")).toBeInTheDocument();
  expect(screen.queryByText("ROOTS1X")).not.toBeInTheDocument();
});

it("shows 'No codes match your search' when filtered to zero results", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("INK2026");

  await user.type(screen.getByRole("searchbox"), "ZZZNOMATCH");

  expect(screen.getByText(/no codes match your search/i)).toBeInTheDocument();
});

it("filter pills show count for each status", async () => {
  renderPage();
  await screen.findByText("INK2026");
  expect(screen.getByRole("button", { name: /^All \(3\)/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /^Active \(2\)/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /^Inactive \(1\)/i })).toBeInTheDocument();
});

it("Generate Code button appears in header", async () => {
  renderPage();
  expect(screen.getByRole("button", { name: /generate new referral code/i })).toBeInTheDocument();
});

it("clicking Generate Code expands the generate form", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("INK2026");

  await user.click(screen.getByRole("button", { name: /generate new referral code/i }));

  expect(screen.getByText(/generate referral code/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/studio/i)).toBeInTheDocument();
});

it("each code has a copy button with aria-label", async () => {
  renderPage();
  await screen.findByText("INK2026");
  expect(screen.getByRole("button", { name: /copy referral code INK2026/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /copy referral code ROOTS1X/i })).toBeInTheDocument();
  expect(screen.getByRole("button", { name: /copy referral code OLD2025/i })).toBeInTheDocument();
});

it("helper text is present below the header", async () => {
  renderPage();
  await screen.findByText("INK2026");
  expect(screen.getByText(/referral codes give studios/i)).toBeInTheDocument();
});
```

Run `pnpm test` — all tests must pass.

**Commit:** `test(referrals): update and expand test suite for full referral page overhaul`

---

## 8. Final Verification

1. `dotnet build` — zero errors.
2. `dotnet test` — all tests pass.
3. `pnpm --dir frontend tsc --noEmit` — zero TypeScript errors.
4. `pnpm --dir frontend lint` — zero errors.
5. `pnpm --dir frontend test` — all tests pass.
6. Visual checks:
   - Every card has the same action zone: [Copy] [Deactivate/Reactivate] [Delete]
   - Active badge reads "Active" (Title Case, green), Inactive reads "Inactive" (muted)
   - Single-use badge reads "Single use" (not "single-use")
   - Code strings render in monospace font
   - "Generate Code" button is in the header, right-aligned
   - Inline generate form expands/collapses correctly
   - Skeleton appears on load, not spinner
   - Date reads "15 Jun 2026" not "15/06/2026"
   - Header count is a styled badge chip, not "(3)"
   - Filter pills show "All (3) / Active (2) / Inactive (1)"
7. `git log --oneline -15` — confirm all commits present.

---

## Reference: Audit Issue → Task Map

| Audit Issue                                                  | Task    |
|--------------------------------------------------------------|---------|
| No "Generate Code" / create button — missing primary CTA     | 1a + 5 + 6e/f |
| Inactive codes are functional dead ends (no Reactivate)      | 1b + 5 + 6d |
| No Delete / Archive action                                   | 1c + 5 + 6d |
| No copy-to-clipboard button                                  | 6d      |
| Action asymmetry — active cards only have Deactivate         | 6d      |
| No confirmation on Deactivate                                | 6d ✓ (already had it — improved copy) |
| "Deactivate?" text doesn't name the code                     | 6d      |
| "(3)" is plain text, not a styled badge                      | 6f      |
| No helper text / subheading                                  | 6f      |
| `max-w-2xl` wastes 50% of wide screens                       | 6f      |
| Spinner instead of skeleton on load                          | 6c + 6f |
| Empty state has no CTA                                       | 6f      |
| No search / filter bar                                       | 6f      |
| "active" / "inactive" / "single-use" lowercase casing        | 6d      |
| Date format "15/06/2026" instead of "15 Jun 2026"            | 6b + 6d |
| "Created" → "Generated"                                      | 6d      |
| No `aria-label` on action buttons (a11y)                     | 6d      |
| Codes with redemptions should block delete (server + UI)     | 1c + 6d |
