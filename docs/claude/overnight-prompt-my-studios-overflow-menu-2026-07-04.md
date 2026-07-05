# Overnight Prompt — My Studios: Overflow Menu + Leave + Notification Preferences
**Date:** 2026-07-04
**Scope:** Three new features on `MyStudiosPage` — a kebab overflow menu per card, "Leave Studio"
(full stack), and per-studio client notification preferences (full stack).

---

## Required Reading

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/conventions.md
```

Then read these files **before writing a single line of code**:

```
# Frontend — component and API shape
frontend/src/features/auth/components/MyStudiosPage.tsx
frontend/src/features/auth/__tests__/MyStudiosPage.test.tsx
frontend/src/features/auth/authApi.ts
frontend/src/features/notifications/components/NotificationPreferencesCard.tsx   ← visual pattern
frontend/src/features/notifications/notification.types.ts
frontend/src/features/notifications/notificationsApi.ts

# Backend — understand how tenant claims work before touching it
Pena_e_Arte.Domain/Interfaces/IIdentityService.cs
Pena_e_Arte.Infrastructure/Services/IdentityService.cs
Pena_e_Arte.Application/Auth/Commands/SwitchStudioCommand.cs   ← EnsureTenantClaimAsync pattern
Pena_e_Arte.Application/Auth/Queries/GetMyStudiosQuery.cs
Pena_e_Arte.API/Endpoints/AuthEndpoints.cs
Pena_e_Arte.API/Endpoints/NotificationEndpoints.cs
Pena_e_Arte.Application/Persistence/IAppDbContext.cs            ← add new DbSet here
```

---

## Overview

All three features share a single entry point: a `MoreVertical` (kebab) icon button that
opens a `DropdownMenu` on each studio card. The standalone external-link icon is removed and
replaced by a "View public profile" menu item.

```
┌─────────────────────────────────────────────────────┐
│  [avatar]  Alpha Ink             Active   Current  ⋮ │
│            Tirana                                     │
└─────────────────────────────────────────────────────┘
                                                   ↓ opens:
                              ┌─────────────────────────────────┐
                              │  ↗  View public profile          │
                              │  🔔  Manage notifications        │
                              │  ─────────────────────────────  │
                              │  🚪  Leave studio               │
                              └─────────────────────────────────┘
```

---

## Feature A — "Leave Studio" (Full Stack)

### A1 — `IIdentityService` — new method

**File:** `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs`

Add:
```csharp
/// <summary>
/// Removes the user's "tenant_id" claim for the given studio.
/// Also clears the active-tenant token if it matches the removed studio.
/// Idempotent — safe to call even if the user no longer holds that claim.
/// </summary>
Task RemoveTenantClaimAsync(Guid userId, Guid studioId, CancellationToken ct);
```

### A2 — `IdentityService` — implement the new method

**File:** `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`

Implement `RemoveTenantClaimAsync` following the same pattern as `EnsureTenantClaimAsync`.
Specifically:
1. Load the user via `_userManager.FindByIdAsync(userId.ToString())` — throw `NotFoundException` if null.
2. Remove the `tenant_id` claim whose value equals `studioId.ToString()` using
   `_userManager.RemoveClaimAsync(user, new Claim("tenant_id", studioId.ToString()))`.
3. Check whether the active-tenant token in `AspNetUserTokens` matches the removed studio
   (use the same token name/loginProvider that `IssueTokensForTenantAsync` writes).
   If so, delete it (use `_userManager.RemoveAuthenticationTokenAsync`).
4. No return value — any failure should throw an `InvalidOperationException` (or follow the
   error-handling convention for other Identity operations in this file).

### A3 — `LeaveStudioResponse` contract

**File:** `Pena_e_Arte.Contracts/Responses/LeaveStudioResponse.cs` (NEW)

```csharp
namespace Pena_e_Arte.Contracts.Responses;

/// <param name="IsLeavingActiveTenant">
/// True when the studio the user is leaving is currently their active (JWT-scoped) studio.
/// The client must log the user out and redirect to /discover in this case, since
/// their current token is now invalid for any tenant.
/// </param>
public record LeaveStudioResponse(bool IsLeavingActiveTenant);
```

### A4 — `LeaveStudioCommand`

**File:** `Pena_e_Arte.Application/Auth/Commands/LeaveStudioCommand.cs` (NEW)

```csharp
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record LeaveStudioCommand(Guid StudioId) : IRequest<LeaveStudioResponse>;

public class LeaveStudioHandler(
    IIdentityService             identity,
    ICurrentUser                 currentUser,
    ILogger<LeaveStudioHandler>  logger)
    : IRequestHandler<LeaveStudioCommand, LeaveStudioResponse>
{
    public async Task<LeaveStudioResponse> Handle(
        LeaveStudioCommand command, CancellationToken ct)
    {
        // Validate membership before attempting removal
        IReadOnlyList<Guid> tenantIds =
            await identity.GetTenantIdsAsync(currentUser.UserId, ct);

        if (!tenantIds.Contains(command.StudioId))
            throw new NotFoundException("Studio membership", command.StudioId);

        // Determine whether the user is leaving their currently active studio
        Guid? activeTenantId = await identity.GetActiveTenantIdAsync(currentUser.UserId, ct);
        bool isLeavingActiveTenant = activeTenantId == command.StudioId;

        await identity.RemoveTenantClaimAsync(currentUser.UserId, command.StudioId, ct);

        logger.LogInformation(
            "Client {@UserId} left studio {@StudioId} (was active tenant: {@WasActive})",
            currentUser.UserId, command.StudioId, isLeavingActiveTenant);

        return new LeaveStudioResponse(isLeavingActiveTenant);
    }
}

public class LeaveStudioValidator : AbstractValidator<LeaveStudioCommand>
{
    public LeaveStudioValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
```

**Important note on the Client record:** Do NOT delete the `Client` row from the database.
The studio retains appointment history, payment records, and consent forms. Leaving a studio
removes the user's claim-based access (they cannot switch back to it), but the data is
preserved. If a user switches back later, `SwitchStudioCommand` will find the existing
`Client` row instead of creating a new one — this is already handled by its `isNewMembership`
logic.

### A5 — Endpoint

**File:** `Pena_e_Arte.API/Endpoints/AuthEndpoints.cs`

Add to `MapAuthEndpoints`:
```csharp
group.MapDelete("/my-studios/{studioId:guid}", LeaveStudio)
     .RequireAuthorization("ClientOnly");
```

Add the handler method:
```csharp
private static async Task<IResult> LeaveStudio(
    Guid              studioId,
    ISender           mediator,
    CancellationToken ct)
{
    LeaveStudioResponse result = await mediator.Send(new LeaveStudioCommand(studioId), ct);
    return Results.Ok(result);
}
```

### A6 — Backend unit tests

**File:** `tests/Pena_e_Arte.UnitTests/Auth/LeaveStudioHandlerTests.cs` (NEW)

Write tests covering:
1. Successfully leaves a non-active studio → returns `IsLeavingActiveTenant = false`
2. Successfully leaves the active studio → returns `IsLeavingActiveTenant = true`
3. Throws `NotFoundException` when the user has no claim for the requested studioId

---

## Feature B — Per-Studio Client Notification Preferences (Full Stack)

### B1 — Domain entity

**File:** `Pena_e_Arte.Domain/Entities/ClientNotificationPreference.cs` (NEW)

```csharp
namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Records a client's opt-in/opt-out preference for a specific notification
/// type and channel from a specific studio.
/// No global query filter — scoped by (UserId, StudioId) in every query.
/// </summary>
public class ClientNotificationPreference
{
    public Guid   Id        { get; set; }
    public Guid   UserId    { get; set; }
    public Guid   StudioId  { get; set; }
    public string Type      { get; set; } = string.Empty;  // NotificationType value
    public string Channel   { get; set; } = string.Empty;  // "Email" | "Sms"
    public bool   IsEnabled { get; set; } = true;
}
```

**No EF Core global query filter** — this entity is dual-keyed by `(UserId, StudioId)`, not
by the active JWT tenant. Every query must filter manually on both columns.

### B2 — DbContext

**File:** `Pena_e_Arte.Application/Persistence/IAppDbContext.cs`

Add:
```csharp
DbSet<ClientNotificationPreference> ClientNotificationPreferences { get; }
```

**File:** `Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs`

Add the DbSet property and configure it in `OnModelCreating`:
```csharp
public DbSet<ClientNotificationPreference> ClientNotificationPreferences => Set<ClientNotificationPreference>();
```

In `OnModelCreating`:
```csharp
builder.Entity<ClientNotificationPreference>(b =>
{
    b.HasIndex(p => new { p.UserId, p.StudioId, p.Type, p.Channel }).IsUnique();
});
```

### B3 — Migration

```bash
cd "Pena e Arte"
dotnet ef migrations add AddClientNotificationPreferences --project Pena_e_Arte.Infrastructure
dotnet ef database update --project Pena_e_Arte.Infrastructure
```

Inspect the generated file to confirm a unique index on `(UserId, StudioId, Type, Channel)`.

### B4 — Contracts

**File:** `Pena_e_Arte.Contracts/Responses/ClientNotificationPreferencesResponse.cs` (NEW)

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record ClientNotificationPreferenceItem(
    string Type,
    string Channel,
    bool   IsEnabled);

public record ClientNotificationPreferencesResponse(
    IReadOnlyList<ClientNotificationPreferenceItem> Preferences);
```

**File:** `Pena_e_Arte.Contracts/Requests/UpdateClientNotificationPreferencesRequest.cs` (NEW)

```csharp
namespace Pena_e_Arte.Contracts.Requests;

public record UpdateClientNotificationPreferencesRequest(
    IReadOnlyList<ClientNotificationPreferenceItem> Preferences);

public record ClientNotificationPreferenceItem(string Type, string Channel, bool IsEnabled);
```

**Note:** The `ClientNotificationPreferenceItem` record name collides with the Responses
namespace. Prefix with `Update` in the Requests namespace or use a shared Contracts namespace
— follow the existing project convention for this (check how other commands handle shared
sub-types).

### B5 — Client-side notification types

Only the five notification types that are sent **to clients** (not the owner-facing ones):

```csharp
// Pena_e_Arte.Domain/Constants/ClientNotificationType.cs (NEW — optional, avoids magic strings)
public static class ClientNotificationType
{
    public const string AppointmentCreated   = "AppointmentCreated";
    public const string AppointmentConfirmed = "AppointmentConfirmed";
    public const string AppointmentCancelled = "AppointmentCancelled";
    public const string DepositCaptured      = "DepositCaptured";
    public const string PaymentRefunded      = "PaymentRefunded";

    public static readonly IReadOnlyList<string> All =
    [
        AppointmentCreated,
        AppointmentConfirmed,
        AppointmentCancelled,
        DepositCaptured,
        PaymentRefunded,
    ];
}
```

### B6 — Query

**File:** `Pena_e_Arte.Application/Auth/Queries/GetClientStudioNotificationPreferencesQuery.cs` (NEW)

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Queries;

public record GetClientStudioNotificationPreferencesQuery(Guid StudioId)
    : IRequest<ClientNotificationPreferencesResponse>;

public class GetClientStudioNotificationPreferencesHandler(
    IAppDbContext db,
    ICurrentUser  currentUser)
    : IRequestHandler<GetClientStudioNotificationPreferencesQuery, ClientNotificationPreferencesResponse>
{
    private static readonly string[] Channels = ["Email", "Sms"];

    public async Task<ClientNotificationPreferencesResponse> Handle(
        GetClientStudioNotificationPreferencesQuery query, CancellationToken ct)
    {
        // Load whatever the user has persisted for this studio
        List<Domain.Entities.ClientNotificationPreference> saved = await db
            .ClientNotificationPreferences
            .Where(p => p.UserId == currentUser.UserId && p.StudioId == query.StudioId)
            .ToListAsync(ct);

        // Build the full matrix — every type × channel — defaulting to enabled
        List<ClientNotificationPreferenceItem> result = [];

        foreach (string type in ClientNotificationType.All)
        {
            foreach (string channel in Channels)
            {
                bool isEnabled = saved
                    .FirstOrDefault(p => p.Type == type && p.Channel == channel)
                    ?.IsEnabled ?? true; // default: all enabled

                result.Add(new ClientNotificationPreferenceItem(type, channel, isEnabled));
            }
        }

        return new ClientNotificationPreferencesResponse(result);
    }
}
```

### B7 — Command

**File:** `Pena_e_Arte.Application/Auth/Commands/UpdateClientStudioNotificationPreferencesCommand.cs` (NEW)

```csharp
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record UpdateClientStudioNotificationPreferencesCommand(
    Guid                                            StudioId,
    IReadOnlyList<ClientNotificationPreferenceItem> Preferences)
    : IRequest<Unit>;

public class UpdateClientStudioNotificationPreferencesHandler(
    IAppDbContext db,
    ICurrentUser  currentUser)
    : IRequestHandler<UpdateClientStudioNotificationPreferencesCommand, Unit>
{
    public async Task<Unit> Handle(
        UpdateClientStudioNotificationPreferencesCommand command, CancellationToken ct)
    {
        List<Domain.Entities.ClientNotificationPreference> existing = await db
            .ClientNotificationPreferences
            .Where(p => p.UserId == currentUser.UserId && p.StudioId == command.StudioId)
            .ToListAsync(ct);

        foreach (ClientNotificationPreferenceItem pref in command.Preferences)
        {
            // Only persist client-facing types — ignore anything outside the allowed set
            if (!ClientNotificationType.All.Contains(pref.Type)) continue;

            Domain.Entities.ClientNotificationPreference? row = existing
                .FirstOrDefault(p => p.Type == pref.Type && p.Channel == pref.Channel);

            if (row is null)
            {
                db.ClientNotificationPreferences.Add(new Domain.Entities.ClientNotificationPreference
                {
                    UserId    = currentUser.UserId,
                    StudioId  = command.StudioId,
                    Type      = pref.Type,
                    Channel   = pref.Channel,
                    IsEnabled = pref.IsEnabled,
                });
            }
            else
            {
                row.IsEnabled = pref.IsEnabled;
            }
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public class UpdateClientStudioNotificationPreferencesValidator
    : AbstractValidator<UpdateClientStudioNotificationPreferencesCommand>
{
    public UpdateClientStudioNotificationPreferencesValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.Preferences).NotNull();
    }
}
```

### B8 — Endpoints

**File:** `Pena_e_Arte.API/Endpoints/AuthEndpoints.cs`

Add to `MapAuthEndpoints` alongside the existing my-studios routes:

```csharp
group.MapGet ("/my-studios/{studioId:guid}/notification-preferences",
    GetStudioNotificationPreferences).RequireAuthorization("ClientOnly");

group.MapPut ("/my-studios/{studioId:guid}/notification-preferences",
    UpdateStudioNotificationPreferences).RequireAuthorization("ClientOnly");
```

Handler methods:
```csharp
private static async Task<IResult> GetStudioNotificationPreferences(
    Guid              studioId,
    ISender           mediator,
    CancellationToken ct)
{
    ClientNotificationPreferencesResponse result =
        await mediator.Send(new GetClientStudioNotificationPreferencesQuery(studioId), ct);
    return Results.Ok(result);
}

private static async Task<IResult> UpdateStudioNotificationPreferences(
    Guid                                        studioId,
    UpdateClientNotificationPreferencesRequest  request,
    ISender                                     mediator,
    CancellationToken                           ct)
{
    await mediator.Send(
        new UpdateClientStudioNotificationPreferencesCommand(studioId, request.Preferences), ct);
    return Results.NoContent();
}
```

---

## Feature C — Frontend: Kebab Menu + Leave Dialog + Notification Sheet

### C1 — `authApi.ts` additions

Add these interfaces and endpoints to the existing `authApi` slice:

```ts
// ── Leave Studio ──────────────────────────────────────────────────────────────

export interface LeaveStudioResponse {
  isLeavingActiveTenant: boolean;
}

// ── Client notification preferences ──────────────────────────────────────────

export interface ClientNotificationPreferenceItem {
  type:      string;
  channel:   "Email" | "Sms";
  isEnabled: boolean;
}

export interface ClientNotificationPreferencesResponse {
  preferences: ClientNotificationPreferenceItem[];
}
```

Add to `tagTypes`:
```ts
tagTypes: ["MyStudios", "ClientStudioNotificationPreferences"],
```

Add to `endpoints`:
```ts
leaveStudio: builder.mutation<LeaveStudioResponse, { studioId: string }>({
  query: ({ studioId }) => ({
    url:    `auth/my-studios/${studioId}`,
    method: "DELETE",
  }),
  invalidatesTags: ["MyStudios"],
}),

getClientStudioNotificationPreferences: builder.query<
  ClientNotificationPreferencesResponse,
  { studioId: string }
>({
  query: ({ studioId }) => `auth/my-studios/${studioId}/notification-preferences`,
  providesTags: (_result, _err, { studioId }) => [
    { type: "ClientStudioNotificationPreferences", id: studioId },
  ],
}),

updateClientStudioNotificationPreferences: builder.mutation<
  void,
  { studioId: string; preferences: ClientNotificationPreferenceItem[] }
>({
  query: ({ studioId, preferences }) => ({
    url:    `auth/my-studios/${studioId}/notification-preferences`,
    method: "PUT",
    body:   { preferences },
  }),
  invalidatesTags: (_result, _err, { studioId }) => [
    { type: "ClientStudioNotificationPreferences", id: studioId },
  ],
}),
```

Export the new hooks in the destructured export at the bottom of `authApi.ts`:
```ts
export const {
  // ... existing hooks ...
  useLeaveStudioMutation,
  useGetClientStudioNotificationPreferencesQuery,
  useUpdateClientStudioNotificationPreferencesMutation,
} = authApi;
```

### C2 — `StudioNotificationSheet` component

**File:** `frontend/src/features/auth/components/StudioNotificationSheet.tsx` (NEW)

This is the client-facing notification preference panel, visually mirroring
`NotificationPreferencesCard.tsx` but scoped to a single studio:

```tsx
import { useEffect, useState } from "react";
import { Loader2, Save }       from "lucide-react";
import { toast }               from "sonner";
import { Button }              from "@/shared/components/ui/button";
import { ToggleSwitch }        from "@/shared/components/ui/toggle-switch";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle, SheetDescription,
} from "@/shared/components/ui/sheet";
import {
  useGetClientStudioNotificationPreferencesQuery,
  useUpdateClientStudioNotificationPreferencesMutation,
} from "@/features/auth/authApi";
import type { ClientNotificationPreferenceItem } from "@/features/auth/authApi";

type NotificationChannel = "Email" | "Sms";

const CLIENT_NOTIFICATION_TYPES: { value: string; label: string }[] = [
  { value: "AppointmentCreated",   label: "Appointment confirmed" },
  { value: "AppointmentConfirmed", label: "Appointment reminder" },
  { value: "AppointmentCancelled", label: "Appointment cancelled" },
  { value: "DepositCaptured",      label: "Deposit captured" },
  { value: "PaymentRefunded",      label: "Payment refunded" },
];

const CHANNELS: NotificationChannel[] = ["Email", "Sms"];
const CHANNEL_LABELS: Record<NotificationChannel, string> = { Email: "Email", Sms: "SMS" };

type PreferenceMap = Record<string, boolean>;

function prefKey(type: string, channel: string) { return `${type}:${channel}`; }

function buildMap(items: ClientNotificationPreferenceItem[]): PreferenceMap {
  const map: PreferenceMap = {};
  for (const item of items) {
    map[prefKey(item.type, item.channel)] = item.isEnabled;
  }
  return map;
}

interface Props {
  studioId:   string;
  studioName: string;
  open:       boolean;
  onClose:    () => void;
}

export function StudioNotificationSheet({ studioId, studioName, open, onClose }: Props) {
  const { data, isLoading } = useGetClientStudioNotificationPreferencesQuery(
    { studioId },
    { skip: !open },  // only fetch when sheet is open
  );
  const [update, { isLoading: saving }] = useUpdateClientStudioNotificationPreferencesMutation();

  const [local, setLocal] = useState<PreferenceMap>({});
  const [dirty, setDirty] = useState(false);

  useEffect(() => {
    if (data) {
      setLocal(buildMap(data.preferences));
      setDirty(false);
    }
  }, [data]);

  function toggle(type: string, channel: NotificationChannel) {
    const key = prefKey(type, channel);
    setLocal((prev) => ({ ...prev, [key]: !prev[key] }));
    setDirty(true);
  }

  async function handleSave() {
    const preferences: ClientNotificationPreferenceItem[] =
      CLIENT_NOTIFICATION_TYPES.flatMap(({ value: type }) =>
        CHANNELS.map((channel) => ({
          type,
          channel,
          isEnabled: local[prefKey(type, channel)] ?? true,
        }))
      );
    try {
      await update({ studioId, preferences }).unwrap();
      setDirty(false);
      toast.success("Notification preferences saved.");
      onClose();
    } catch {
      toast.error("Failed to save preferences.");
    }
  }

  return (
    <Sheet open={open} onOpenChange={(o) => !o && onClose()}>
      <SheetContent side="right" className="w-full sm:max-w-md flex flex-col">
        <SheetHeader>
          <SheetTitle>Notifications — {studioName}</SheetTitle>
          <SheetDescription>
            Control which notifications this studio sends you.
          </SheetDescription>
        </SheetHeader>

        <div className="flex-1 overflow-y-auto mt-4">
          {isLoading ? (
            <div className="flex items-center gap-2 text-muted-foreground text-sm py-8 justify-center">
              <Loader2 className="h-4 w-4 animate-spin" />
              Loading…
            </div>
          ) : (
            <div className="rounded-md border overflow-hidden">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b bg-muted/40">
                    <th className="text-left px-3 py-2 font-medium text-muted-foreground w-full">
                      Notification
                    </th>
                    {CHANNELS.map((ch) => (
                      <th key={ch} className="px-3 py-2 font-medium text-muted-foreground text-center whitespace-nowrap">
                        {CHANNEL_LABELS[ch]}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {CLIENT_NOTIFICATION_TYPES.map(({ value: type, label }, i) => (
                    <tr key={type} className={i % 2 === 0 ? undefined : "bg-muted/20"}>
                      <td className="px-3 py-2.5 text-foreground">{label}</td>
                      {CHANNELS.map((channel) => (
                        <td key={channel} className="px-3 py-2.5 text-center">
                          <ToggleSwitch
                            checked={local[prefKey(type, channel)] ?? true}
                            onChange={() => toggle(type, channel)}
                            aria-label={`${label} via ${CHANNEL_LABELS[channel]}`}
                          />
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        <div className="border-t pt-4 pb-2">
          <Button
            className="w-full gap-2"
            onClick={handleSave}
            disabled={saving || !dirty || isLoading}
          >
            {saving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
            Save preferences
          </Button>
        </div>
      </SheetContent>
    </Sheet>
  );
}
```

### C3 — Update `MyStudiosPage.tsx`

This is the main change. Rewrite `MyStudiosPage.tsx` in full incorporating:
- Kebab `DropdownMenu` per card (replaces the standalone `ExternalLink` link)
- Leave confirmation `AlertDialog` (per card)
- `StudioNotificationSheet` (per card)

**New imports to add:**
```tsx
import {
  MoreVertical, ExternalLink, Bell, LogOut,
  Building2, CheckCircle2, Loader2, Plus,
} from "lucide-react";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem,
  DropdownMenuSeparator, DropdownMenuTrigger,
} from "@/shared/components/ui/dropdown-menu";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";
import { StudioNotificationSheet } from "./StudioNotificationSheet";
import {
  useGetMyStudiosQuery,
  useSwitchStudioMutation,
  useLeaveStudioMutation,
} from "@/features/auth/authApi";
import { logout } from "@/features/auth/authSlice";
```

**Remove** the `ExternalLink` icon link from `StudioCard`'s action area (the one that was
Fix 5 in the previous prompt). The external-link is now in the dropdown menu.

**Update `StudioCard` props** to accept new callbacks:
```tsx
interface StudioCardProps {
  studio:          MyStudioResponse;
  isActive:        boolean;
  isSwitching:     boolean;
  onSwitch:        (studioId: string) => void;
  onLeave:         (studio: MyStudioResponse) => void;      // ← new
  onNotifications: (studio: MyStudioResponse) => void;      // ← new
}
```

**Updated action area in `StudioCard`** (right side of the card content):

```tsx
<div className="flex items-center gap-1 shrink-0">
  {/* Current badge (non-interactive) */}
  {isActive ? (
    <span
      className="inline-flex items-center gap-1 rounded-full px-2.5 py-1
                 text-xs font-medium bg-emerald-500/15 text-emerald-500 shrink-0"
      aria-label={`${studio.name} is your current studio`}
    >
      <CheckCircle2 className="h-3 w-3" aria-hidden />
      Current
    </span>
  ) : (
    <Button
      size="sm"
      variant="outline"
      onClick={() => onSwitch(studio.studioId)}
      disabled={isSwitching}
      className="text-xs gap-1.5"
      aria-label={`Switch to ${studio.name}`}
    >
      {isSwitching ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : null}
      Switch
    </Button>
  )}

  {/* Kebab overflow menu */}
  <DropdownMenu>
    <DropdownMenuTrigger asChild>
      <Button
        size="icon"
        variant="ghost"
        className="h-8 w-8"
        aria-label={`More options for ${studio.name}`}
      >
        <MoreVertical className="h-4 w-4" aria-hidden />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" className="w-48">
      <DropdownMenuItem asChild>
        <Link
          to={`/s/${studio.slug}`}
          className="flex items-center gap-2 cursor-pointer"
        >
          <ExternalLink className="h-4 w-4" aria-hidden />
          View public profile
        </Link>
      </DropdownMenuItem>
      <DropdownMenuItem
        onClick={() => onNotifications(studio)}
        className="flex items-center gap-2"
      >
        <Bell className="h-4 w-4" aria-hidden />
        Manage notifications
      </DropdownMenuItem>
      <DropdownMenuSeparator />
      <DropdownMenuItem
        onClick={() => onLeave(studio)}
        className="flex items-center gap-2 text-destructive focus:text-destructive"
      >
        <LogOut className="h-4 w-4" aria-hidden />
        Leave studio
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</div>
```

**Updated `MyStudiosPage` component** — add state for the leave dialog and notification sheet,
and the `handleLeave` function:

```tsx
export function MyStudiosPage() {
  useDocumentMeta({ title: "My Studios — Pena e Artë", canonical: "/my-studios" });

  const dispatch        = useAppDispatch();
  const currentTenantId = useAppSelector((s) => s.auth.tenantId);
  const navigate        = useNavigate();

  const { data: studios, isLoading, isError, refetch } = useGetMyStudiosQuery();
  const [switchStudio]    = useSwitchStudioMutation();
  const [leaveStudio]     = useLeaveStudioMutation();
  const [switchingId, setSwitchingId]                 = useState<string | null>(null);
  const [leaveTarget,  setLeaveTarget]                = useState<MyStudioResponse | null>(null);
  const [isLeaving, setIsLeaving]                     = useState(false);
  const [notifTarget, setNotifTarget]                 = useState<MyStudioResponse | null>(null);

  async function handleSwitch(studioId: string) { /* ... unchanged ... */ }

  async function handleLeave() {
    if (!leaveTarget) return;
    setIsLeaving(true);
    try {
      const result = await leaveStudio({ studioId: leaveTarget.studioId }).unwrap();
      toast.success(`Left ${leaveTarget.name}.`);
      if (result.isLeavingActiveTenant) {
        // Token is no longer valid for any tenant — log out and go to discover
        dispatch(logout());
        navigate("/discover", { replace: true });
      }
      // If not leaving active tenant, the studios list auto-refreshes via invalidatesTags
    } catch {
      toast.error("Couldn't leave the studio. Please try again.");
    } finally {
      setIsLeaving(false);
      setLeaveTarget(null);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      {/* ... header (unchanged from previous prompt) ... */}

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-3">
        {/* ... loading / error / empty / list states — pass new props to StudioCard ... */}
        {!isLoading && !isError && studios && studios.length > 0 && (
          <>
            {/* ... sub-header row unchanged ... */}
            {studios.map((studio) => (
              <StudioCard
                key={studio.studioId}
                studio={studio}
                isActive={studio.studioId === currentTenantId}
                isSwitching={switchingId === studio.studioId}
                onSwitch={handleSwitch}
                onLeave={setLeaveTarget}                // ← new
                onNotifications={setNotifTarget}        // ← new
              />
            ))}
          </>
        )}
      </main>

      {/* ── Leave confirmation dialog ── */}
      <AlertDialog
        open={leaveTarget !== null}
        onOpenChange={(open) => !open && setLeaveTarget(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Leave {leaveTarget?.name}?</AlertDialogTitle>
            <AlertDialogDescription>
              You will lose access to this studio&apos;s booking flow.
              Your appointment history and records are preserved — you can
              rejoin the studio at any time.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isLeaving}>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={handleLeave}
              disabled={isLeaving}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {isLeaving ? (
                <Loader2 className="h-3.5 w-3.5 animate-spin" />
              ) : (
                "Leave studio"
              )}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {/* ── Notification preferences sheet ── */}
      {notifTarget && (
        <StudioNotificationSheet
          studioId={notifTarget.studioId}
          studioName={notifTarget.name}
          open={notifTarget !== null}
          onClose={() => setNotifTarget(null)}
        />
      )}
    </div>
  );
}
```

Write the complete `MyStudiosPage.tsx` in full — do not patch; replace the whole file.

---

## Tests

### Update `MyStudiosPage.test.tsx`

The test file already has a robust mock setup. Add these new tests inside the existing
`describe("MyStudiosPage", ...)` block:

```ts
// ── Overflow menu ─────────────────────────────────────────────────────────────

it("renders a kebab menu button for each studio card", async () => {
  renderPage();
  await screen.findByText("Alpha Ink");
  const kebabs = screen.getAllByRole("button", { name: /more options/i });
  expect(kebabs).toHaveLength(2);
});

it("opens the dropdown with 'View public profile', 'Manage notifications', and 'Leave studio'", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Alpha Ink");
  await user.click(screen.getAllByRole("button", { name: /more options/i })[0]);
  expect(screen.getByRole("menuitem", { name: /view public profile/i })).toBeInTheDocument();
  expect(screen.getByRole("menuitem", { name: /manage notifications/i })).toBeInTheDocument();
  expect(screen.getByRole("menuitem", { name: /leave studio/i })).toBeInTheDocument();
});

// ── Leave studio ──────────────────────────────────────────────────────────────

it("opens a confirmation dialog when 'Leave studio' is clicked", async () => {
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Alpha Ink");
  await user.click(screen.getAllByRole("button", { name: /more options/i })[0]);
  await user.click(screen.getByRole("menuitem", { name: /leave studio/i }));
  expect(screen.getByRole("alertdialog")).toBeInTheDocument();
  expect(screen.getByText(/leave alpha ink/i)).toBeInTheDocument();
});

it("calls the leave-studio API with the correct studioId on confirm", async () => {
  let capturedUrl = "";
  server.use(
    http.delete("http://localhost/api/v1/auth/my-studios/:studioId", ({ params }) => {
      capturedUrl = params.studioId as string;
      return HttpResponse.json({ isLeavingActiveTenant: false });
    }),
  );
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Alpha Ink");
  await user.click(screen.getAllByRole("button", { name: /more options/i })[0]);
  await user.click(screen.getByRole("menuitem", { name: /leave studio/i }));
  await user.click(screen.getByRole("button", { name: /leave studio/i }));
  await vi.waitFor(() => expect(capturedUrl).toBe("studio-aaa"));
});

it("navigates to /discover when leaving the active tenant studio", async () => {
  server.use(
    http.delete("http://localhost/api/v1/auth/my-studios/:studioId", () =>
      HttpResponse.json({ isLeavingActiveTenant: true }),
    ),
  );
  const user = userEvent.setup();
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={["/my-studios"]}>
        <Routes>
          <Route path="/my-studios" element={<MyStudiosPage />} />
          <Route path="/discover"   element={<div>Discover Page</div>} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  await screen.findByText("Alpha Ink");
  await user.click(screen.getAllByRole("button", { name: /more options/i })[0]);
  await user.click(screen.getByRole("menuitem", { name: /leave studio/i }));
  await user.click(screen.getByRole("button", { name: /leave studio/i }));
  expect(await screen.findByText("Discover Page")).toBeInTheDocument();
});

// ── Manage notifications ──────────────────────────────────────────────────────

it("opens the notification preferences sheet when 'Manage notifications' is clicked", async () => {
  // Mock the notification preferences endpoint
  server.use(
    http.get(
      "http://localhost/api/v1/auth/my-studios/:studioId/notification-preferences",
      () => HttpResponse.json({ preferences: [] }),
    ),
  );
  const user = userEvent.setup();
  renderPage();
  await screen.findByText("Alpha Ink");
  await user.click(screen.getAllByRole("button", { name: /more options/i })[0]);
  await user.click(screen.getByRole("menuitem", { name: /manage notifications/i }));
  expect(await screen.findByRole("dialog")).toBeInTheDocument();
  expect(screen.getByText(/notifications — alpha ink/i)).toBeInTheDocument();
});
```

### New `StudioNotificationSheet.test.tsx`

**File:** `frontend/src/features/auth/__tests__/StudioNotificationSheet.test.tsx` (NEW)

Write tests covering:
1. Renders sheet title and studio name when `open={true}`
2. Fetches preferences when open (verify API called with correct studioId)
3. Renders all 5 notification type rows
4. Renders Email and SMS column headers
5. Toggle changes the toggle state and enables the Save button
6. Save button is disabled until a toggle is changed
7. Calls the PUT mutation with the updated preferences on Save
8. Shows toast success and closes the sheet on successful save
9. Shows toast error on save failure

---

## Verification

```bash
cd "Pena e Arte"
dotnet build
dotnet ef migrations add AddClientNotificationPreferences --project Pena_e_Arte.Infrastructure
dotnet ef database update --project Pena_e_Arte.Infrastructure
dotnet test --no-build
cd frontend
pnpm tsc --noEmit
pnpm test -- --testPathPattern="MyStudiosPage|StudioNotificationSheet|LeaveStudio"
```

All commands exit 0.

---

## Exit condition

All tests green, TypeScript clean. Then append to `docs/claude/architecture.md`:

```markdown
## My Studios — Overflow Menu, Leave Studio, Notification Preferences — 2026-07-04

### Features added
1. **Kebab overflow menu**: each studio card now has a `MoreVertical` `DropdownMenu` with three
   items: "View public profile" (Link to /s/{slug}), "Manage notifications" (opens Sheet),
   "Leave studio" (opens AlertDialog). The standalone external-link icon is removed.

2. **Leave Studio** (full stack):
   - `IIdentityService.RemoveTenantClaimAsync` — new method removes the tenant_id claim
     and clears the active-tenant token if it matches.
   - `LeaveStudioCommand` — validates membership, calls RemoveTenantClaimAsync, returns
     `IsLeavingActiveTenant` flag.
   - `LeaveStudioResponse` contract — single bool field.
   - `DELETE /api/v1/auth/my-studios/{studioId}` — ClientOnly.
   - Frontend: `AlertDialog` confirmation, then either refetch studios (non-active tenant)
     or `dispatch(logout()) → navigate("/discover")` (leaving the active studio).
   - The Client DB row is NOT deleted — data is retained for studio history.

3. **Per-studio client notification preferences** (full stack):
   - New domain entity: `ClientNotificationPreference (Id, UserId, StudioId, Type, Channel, IsEnabled)`
   - No global query filter — scoped by `(UserId, StudioId)` in all queries.
   - 5 client-facing notification types only: AppointmentCreated, AppointmentConfirmed,
     AppointmentCancelled, DepositCaptured, PaymentRefunded.
   - Default: all enabled (returned by GET even before the user saves anything, via
     client-side defaults in the handler).
   - `GET/PUT /api/v1/auth/my-studios/{studioId}/notification-preferences` — ClientOnly.
   - `StudioNotificationSheet`: right-side `Sheet` with toggle table, lazy-loaded (`skip: !open`),
     auto-closes on successful save.

### Files added
Backend:
- `Pena_e_Arte.Domain/Entities/ClientNotificationPreference.cs`
- `Pena_e_Arte.Domain/Constants/ClientNotificationType.cs`
- `Pena_e_Arte.Contracts/Responses/LeaveStudioResponse.cs`
- `Pena_e_Arte.Contracts/Responses/ClientNotificationPreferencesResponse.cs`
- `Pena_e_Arte.Contracts/Requests/UpdateClientNotificationPreferencesRequest.cs`
- `Pena_e_Arte.Application/Auth/Commands/LeaveStudioCommand.cs`
- `Pena_e_Arte.Application/Auth/Commands/UpdateClientStudioNotificationPreferencesCommand.cs`
- `Pena_e_Arte.Application/Auth/Queries/GetClientStudioNotificationPreferencesQuery.cs`
- Migration: AddClientNotificationPreferences

Backend modified:
- `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs` — RemoveTenantClaimAsync
- `Pena_e_Arte.Infrastructure/Services/IdentityService.cs` — implementation
- `Pena_e_Arte.Application/Persistence/IAppDbContext.cs` — new DbSet
- `Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs` — DbSet + index config
- `Pena_e_Arte.API/Endpoints/AuthEndpoints.cs` — DELETE + GET/PUT routes

Frontend added:
- `frontend/src/features/auth/components/StudioNotificationSheet.tsx`
- `frontend/src/features/auth/__tests__/StudioNotificationSheet.test.tsx`
- `tests/Pena_e_Arte.UnitTests/Auth/LeaveStudioHandlerTests.cs`

Frontend modified:
- `frontend/src/features/auth/authApi.ts` — 3 new endpoints + interfaces
- `frontend/src/features/auth/components/MyStudiosPage.tsx` — kebab menu, leave dialog, notif sheet
- `frontend/src/features/auth/__tests__/MyStudiosPage.test.tsx` — 5 new tests
```
