# Overnight Prompt — Production Hardening: Deep Bug Hunt + Industry-Standard Features
**Scope:** Full stack audit from backend to frontend. Fix every bug found, then implement
the production-critical features that are standard for a SaaS platform in this industry.
No new NuGet or npm packages unless explicitly listed in this prompt.

---

## Read First

1. `CLAUDE.md`
2. `docs/claude/backend.md`
3. `docs/claude/frontend.md`
4. `docs/claude/database.md`
5. `docs/claude/architecture.md`
6. `docs/claude/conventions.md`

---

## Working Context — Known State

The app is a multi-tenant tattoo studio SaaS. What is confirmed built and working:
- Appointment booking, cancellation, confirmation, no-show, completion
- Deposit rules (card + cash, manual capture)
- Design approval workflow with SignalR
- Client profiles, portable profiles, tattoo records, body maps
- Session splits + payment ledger
- Notification system (Hangfire + MailKit + Twilio)
- Public portfolio pages (studio + artist + discover feed)
- Platform subscriptions + trial model + grace period
- Referral codes, QR codes, branding flags
- Issuer dashboard (KPIs, subscription oversight, plan CRUD)
- Reviews (studio, artist, portfolio image)
- Suspension banner (owner + artist + client roles)
- OAuth (Google + Apple) — from recent overnight prompt

---

## Phase 1 — Deep Bug Hunt

### HOW TO AUDIT

For every file in `Pena_e_Arte.Application/`, `Pena_e_Arte.Domain/`,
`Pena_e_Arte.Infrastructure/`, `Pena_e_Arte.API/`, and `frontend/src/`,
check the categories below. Write down every bug found before fixing any of them.
Categorise each bug P0 (data-loss / security) → P3 (cosmetic).
Fix in P0 → P3 order.

---

### B-SEC: Security Bugs

**B-SEC-01 — CORS is wide open**

Read `Pena_e_Arte.API/Program.cs`. The current CORS config is:
```csharp
policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
```
This is a **P0 security bug** for production. Fix:

```csharp
// Extensions/CorsExtensions.cs
public static class CorsExtensions
{
    public static IServiceCollection AddApiCors(
        this IServiceCollection services, IConfiguration config)
    {
        string[] allowedOrigins = config.GetSection("Cors:AllowedOrigins")
                                        .Get<string[]>() ?? [];
        services.AddCors(opt =>
            opt.AddDefaultPolicy(p =>
            {
                if (allowedOrigins.Length == 0)
                    // Dev-only fallback — never reaches production
                    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                else
                    p.WithOrigins(allowedOrigins)
                     .AllowAnyHeader()
                     .AllowAnyMethod()
                     .AllowCredentials();
            }));
        return services;
    }
}
```

Add to `appsettings.json`:
```json
"Cors": {
  "AllowedOrigins": []
}
```

Add to `appsettings.Development.json` (gitignored or not sensitive):
```json
"Cors": {
  "AllowedOrigins": [ "http://localhost:5173", "http://localhost:3000" ]
}
```

Replace `builder.Services.AddCors(...)` in `Program.cs` with
`builder.Services.AddApiCors(builder.Configuration)`.

**B-SEC-02 — No rate limiting on auth and public-write endpoints**

Read `Pena_e_Arte.API/Program.cs`. There is no call to `AddRateLimiter`.
This is a **P0** security gap — login, register, forgot-password, and the public
review-creation endpoints are open to brute force and spam.

Use ASP.NET Core 8+ built-in rate limiting (no new package):

```csharp
// Extensions/RateLimitingExtensions.cs
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(opt =>
        {
            opt.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Auth endpoints — tight: 10 req/min per IP
            opt.AddFixedWindowLimiter("auth", o =>
            {
                o.Window           = TimeSpan.FromMinutes(1);
                o.PermitLimit      = 10;
                o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                o.QueueLimit       = 0;
                o.AutoReplenishment = true;
            });

            // Public write endpoints (reviews, view counters) — 30 req/min per IP
            opt.AddFixedWindowLimiter("public-write", o =>
            {
                o.Window           = TimeSpan.FromMinutes(1);
                o.PermitLimit      = 30;
                o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                o.QueueLimit       = 0;
                o.AutoReplenishment = true;
            });
        });
        return services;
    }
}
```

Register in `Program.cs`: `builder.Services.AddApiRateLimiting();`
and `app.UseRateLimiter();` (before `app.UseAuthentication()`).

Apply to endpoints in `AuthEndpoints.cs`:
```csharp
group.MapPost("/login",          Login)
     .RequireRateLimiting("auth");
group.MapPost("/register",       Register)
     .RequireRateLimiting("auth");
group.MapPost("/forgot-password", ForgotPassword)
     .RequireRateLimiting("auth");
```

Apply to public write endpoints in `PublicEndpoints.cs`:
```csharp
// Review creation routes
group.MapPost("/studios/{slug}/reviews",          CreateStudioReview)
     .RequireAuthorization("ClientAndAbove")
     .RequireRateLimiting("public-write");
group.MapPost("/artists/{slug}/reviews",          CreateArtistReview)
     .RequireAuthorization("ClientAndAbove")
     .RequireRateLimiting("public-write");
group.MapPost("/portfolio/{imageId:guid}/reviews", CreatePortfolioImageReview)
     .RequireAuthorization("ClientAndAbove")
     .RequireRateLimiting("public-write");
group.MapPost("/artists/{slug}/view",              RecordArtistView)
     .AllowAnonymous()
     .RequireRateLimiting("public-write");
```

**B-SEC-03 — Hangfire dashboard exposed without environment check**

Read `Program.cs`. The `UseHangfireDashboard` call uses a path from config with a custom
auth filter. Verify `HangfireDashboardAuthFilter.cs` requires the `IssuerOnly` policy.
If it only checks `IsAuthenticated` without role, fix it to require `role == "issuer"`:

```csharp
public class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        HttpContext http = context.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true
            && http.User.IsInRole("issuer");
    }
}
```

**B-SEC-04 — No `Stripe-Signature` validation check present**

Read `Pena_e_Arte.API/Endpoints/BillingEndpoints.cs`. Confirm the Stripe webhook handlers
call `StripeClient.ConstructEvent` with the `Stripe-Signature` header before processing
the event body. If the signature check is missing or can be bypassed (e.g. the raw body
is read after `app.UseMiddleware` which consumes it), fix it:
- Ensure `app.UseBodyBuffering()` or `EnableRequestBodyRewindMiddleware` runs before routing
  so the raw bytes are available for Stripe HMAC verification.
- Never process a webhook event if `ConstructEvent` throws `StripeException`.

---

### B-LOG: Business Logic Bugs

**B-LOG-01 — `MarkNoShowCommand` does not forfeit the deposit (P0)**

Read `Pena_e_Arte.Application/Appointments/Commands/MarkNoShowCommand.cs`.
The handler sets `appointment.Status = AppointmentStatus.NoShow` but does NOT update
`appointment.DepositStatus` or the associated `Payment`.

`DepositStatus.Forfeited` exists in the enum. Use it.

Fix: after setting the no-show status, load the associated `Payment` and update both:

```csharp
appointment.Status        = AppointmentStatus.NoShow;
appointment.DepositStatus = DepositStatus.Forfeited;
appointment.UpdatedAt     = DateTime.UtcNow;

// Forfeit the linked payment record
Payment? payment = await db.Payments
    .FirstOrDefaultAsync(p =>
        p.AppointmentId == appointment.Id &&
        p.Status        != PaymentStatus.Refunded, ct);

if (payment is not null)
{
    payment.Status    = PaymentStatus.Paid;   // studio keeps the deposit
    payment.UpdatedAt = DateTime.UtcNow;
    // For card holds: the Stripe PaymentIntent was in manual-capture state;
    // capturing it here finalises the charge. Cash deposits already confirmed
    // are already Paid — no action needed.
    if (payment.Method == ClientPaymentMethod.Card
        && !string.IsNullOrEmpty(payment.StripePaymentIntentId)
        && payment.Status == PaymentStatus.Captured)
    {
        await stripePaymentService.CapturePaymentAsync(
            payment.StripePaymentIntentId, payment.Amount, ct);
    }
}
```

Inject `IStripePaymentService` into `MarkNoShowHandler`. Add the `AppointmentId` FK to
the `Payment` entity if it is not already there (check the entity file). If it is already
there, ensure EF Core config includes it.

Add a unit test: `MarkNoShowCommand_SetsDepositForfeited` and
`MarkNoShowCommand_ForfeitedPaymentStatusIsUpdated`.

**B-LOG-02 — `CompleteAppointmentCommand` does not capture the Stripe hold (P0)**

Read `Pena_e_Arte.Application/Appointments/Commands/CompleteAppointmentCommand.cs`.
It sets `AppointmentStatus.Completed` but does not capture the Stripe `PaymentIntent`
that is in manual-capture (`Captured`) state.

Fix: after setting `Completed`, load the payment and capture if card:

```csharp
appointment.Status    = AppointmentStatus.Completed;
appointment.UpdatedAt = DateTime.UtcNow;

Payment? payment = await db.Payments
    .FirstOrDefaultAsync(p =>
        p.AppointmentId == appointment.Id &&
        p.Status == PaymentStatus.Captured &&
        p.Method == ClientPaymentMethod.Card, ct);

if (payment is not null && !string.IsNullOrEmpty(payment.StripePaymentIntentId))
{
    await stripePaymentService.CapturePaymentAsync(
        payment.StripePaymentIntentId, payment.Amount, ct);
    payment.Status    = PaymentStatus.Paid;
    payment.UpdatedAt = DateTime.UtcNow;
}
```

Add `IStripePaymentService` injection. Also send the aftercare notification (see Feature P-08).

**B-LOG-03 — `CancelAppointmentCommand` does not cancel Hangfire reminder jobs (P1)**

Read `CancelAppointmentCommand.cs` and the `IJobScheduler` interface.
The appointment reminder jobs (48h and 24h before) are scheduled at booking time.
If the appointment is cancelled, those jobs still fire and send reminder emails/SMS to the
client — a confusing user experience.

Fix: extend `IJobScheduler` with a `CancelAppointmentJobs(Guid appointmentId)` method and
implement it in the Hangfire job scheduler to delete the queued jobs by their stored job IDs.

This requires storing the Hangfire job IDs at booking time:
1. Add `ReminderJobId48h string?` and `ReminderJobId24h string?` to the `Appointment` entity.
2. Migration: `AddAppointmentReminderJobIds` — two nullable VARCHAR(128) columns.
3. In `CreateAppointmentHandler`, store the returned job IDs from `IJobScheduler`:
   ```csharp
   (appointment.ReminderJobId48h, appointment.ReminderJobId24h) =
       jobs.ScheduleAppointmentReminder(appointment.Id, appointment.Date);
   ```
4. In `CancelAppointmentHandler`, call:
   ```csharp
   jobs.CancelAppointmentJobs(appointment.ReminderJobId48h, appointment.ReminderJobId24h);
   ```
5. In `IJobScheduler` implementation, use `BackgroundJob.Delete(jobId)`.

**B-LOG-04 — `CancelAppointmentCommand` does not refund deposits (P1)**

When an appointment is cancelled by the studio/artist, any `Captured` card payment should
be released (Stripe void/refund), and `CashPending` payments should be cleared.
The `DepositStatus` should be set to `Refunded` on cancel.

Fix: after cancelling the appointment:
```csharp
Payment? payment = await db.Payments
    .FirstOrDefaultAsync(p => p.AppointmentId == appointment.Id, ct);

if (payment is not null)
{
    if (payment.Method == ClientPaymentMethod.Card
        && !string.IsNullOrEmpty(payment.StripePaymentIntentId)
        && payment.Status == PaymentStatus.Captured)
    {
        await stripePaymentService.RefundPaymentAsync(payment.StripePaymentIntentId, ct);
        payment.Status = PaymentStatus.Refunded;
    }
    else if (payment.Status == PaymentStatus.CashPending)
    {
        payment.Status = PaymentStatus.Refunded;
    }

    payment.UpdatedAt         = DateTime.UtcNow;
    appointment.DepositStatus = DepositStatus.Refunded;
}
```

Add `RefundPaymentAsync` to `IStripePaymentService` if it does not exist.
**Note:** Whether to refund based on cancellation policy (within 48h vs. outside) is a
business decision. For now, full refund on studio/artist-initiated cancel. Implement a
`CancellationReason` enum (`ClientNoShow | StudioCancelled | ClientCancelled`) and add
a `CancellationReason?` field to `Appointment` so future cancellation policy logic has
context to work with.

**B-LOG-05 — Double-booking guard uses `EndDate` but booking form may not send it (P1)**

Read `CreateAppointmentCommand.cs`. The overlap check uses `appointment.EndDate` which is
derived from `req.Date.AddMinutes(req.DurationMinutes)`. Check the `CreateAppointmentRequest`
contract and validator: `DurationMinutes` must be required and at least 30 minutes.
If `DurationMinutes` is 0 or missing, `EndDate == Date` and the overlap check never triggers.

Fix in `CreateAppointmentValidator.cs`:
```csharp
RuleFor(x => x.Request.DurationMinutes)
    .GreaterThanOrEqualTo(30).WithMessage("Session must be at least 30 minutes.")
    .LessThanOrEqualTo(480).WithMessage("Session cannot exceed 8 hours.");
RuleFor(x => x.Request.Date)
    .GreaterThan(DateTime.UtcNow.AddMinutes(30))
    .WithMessage("Appointment must be at least 30 minutes in the future.");
```

**B-LOG-06 — `Artist.WorkingHours` — no availability enforcement (P1)**

Read `Artist.cs`. There is no concept of working hours. Clients can book on any day and any
time, even if the artist is not working on that day. This is a missing industry-standard
feature (see Feature P-05) — document it as a known gap and track it for implementation.

**B-LOG-07 — Review validation missing on the body length server-side (P2)**

Read all three review validators: `CreateStudioReviewValidator`, `CreateArtistReviewValidator`,
`CreatePortfolioImageReviewValidator`. Each should enforce:
- Rating: 1–5 inclusive
- Body: minimum 10 characters, maximum 2000 characters
- AuthorName: not empty, max 200 characters

Audit each validator and fix any that are missing these rules.

---

### B-DB: Database / Performance Bugs

**B-DB-01 — N+1 query in `GetPublicStudioQuery` (P1)**

Read `Pena_e_Arte.Application/Public/Queries/GetPublicStudioQuery.cs`.
If it loads `studio.Artists` without `.Include(s => s.Artists)` and then iterates to get
per-artist portfolio images, it will fire one query per artist. Fix with eager loading:
```csharp
.Include(s => s.Artists)
    .ThenInclude(a => a.Portfolio)
```

**B-DB-02 — Missing indexes on high-traffic columns (P2)**

Audit `AppDbContext`/entity configurations for missing composite indexes. The following
queries run on every page load and need proper indexes:

| Table | Index needed | Reason |
|---|---|---|
| `Appointments` | `(ArtistId, Date, EndDate, Status)` | Overlap check on every booking |
| `Reviews` | `(StudioId)`, `(ArtistId)`, `(PortfolioImageId)` | Public review aggregation |
| `PortfolioImages` | `(ArtistId)` | Feed and artist profile |
| `NotificationLogs` | `(StudioId, CreatedAt DESC)` | Notification list page |

Add via EF Core `HasIndex` in the entity configuration. Generate a migration:
`AddPerformanceIndexes`.

**B-DB-03 — `TattooRecord.PhotoUrls` missing cascade delete for R2 files (P2)**

Read `DeleteTattooRecordCommand.cs`. When a `TattooRecord` is deleted, any R2 photos
should be deleted from Cloudflare R2 storage. Check if the handler calls
`IFileService.DeleteAsync` for each URL in `PhotoUrls` before deleting the record.
If not, add the cleanup step.

Same pattern for `PortfolioImage` deletion — when an artist removes a portfolio image,
the R2 file should be deleted.

**B-DB-04 — Soft delete not applied to `CancelledAt` timestamp (P2)**

Read `Appointment.cs` and `TenantEntity.cs`. Cancelled appointments use
`Status = Cancelled` but `TenantEntity.DeletedAt` is not set. Verify that the global
query filter on `Appointments` is `a.DeletedAt == null` (excluding hard-deleted rows only)
and that `Status == Cancelled` appointments still appear in history queries.
If the filter accidentally hides cancelled appointments, fix the query filter.

---

### B-API: API Contract Bugs

**B-API-01 — `GetPortfolioFeed` `radiusKm` has no default value (P1)**

Read `PublicEndpoints.cs` — specifically the `GetPortfolioFeed` handler signature.
If `radiusKm` is declared as `double radiusKm` (non-nullable, no default), the global
feed (no `lat`/`lng`) still requires the caller to supply `radiusKm` or get a 400.

Fix:
```csharp
private static async Task<IResult> GetPortfolioFeed(
    double? lat, double? lng, double radiusKm = 50,
    int page = 1, int pageSize = 20, ...)
```

**B-API-02 — Missing pagination on artist list, client list, and notification list (P2)**

Read `GetArtistsQuery.cs`, `GetClientsQuery.cs`, `GetNotificationsQuery.cs`.
If any returns `List<T>` without `page`/`pageSize` parameters, it will break under load.
Add server-side pagination: `page = 1, pageSize = 25` defaults, max `pageSize = 100`.
Return a `PagedResponse<T>` wrapper with `{ items, page, pageSize, totalCount }`.

Update corresponding RTK Query endpoints and TypeScript types.

**B-API-03 — `AppointmentResponse` does not include `CancellationReason` (P3)**

After implementing `CancellationReason` in B-LOG-04, add it to `AppointmentResponse` and
the mapping in `CreateAppointmentHandler.Map()`.

---

### F-TS: Frontend Type Safety

**F-TS-01 — Audit for `any` usage across all TypeScript files**

Run: `grep -rn ": any\|as any\|<any>" frontend/src --include="*.ts" --include="*.tsx"`
Every hit (except `eslint-disable-next-line` lines in test files) must be fixed.
Replace with proper types or `unknown` + type guard.

**F-TS-02 — TypeScript `enum` check**

The project has `erasableSyntaxOnly: true`. Run:
`grep -rn "^enum\|^export enum" frontend/src --include="*.ts" --include="*.tsx"`
Any hits are bugs — replace with `const` objects + type aliases per conventions.

**F-TS-03 — Default exports on components**

Run: `grep -rn "^export default" frontend/src/features --include="*.tsx"`
Every hit is a bug. Convert to named export.

---

### F-STATE: Frontend State Bugs

**F-STATE-01 — `baseQuery` dispatches `setSessionExpired()` on 401 without attempting token refresh (P1)**

Read `frontend/src/shared/api/baseQuery.ts`. When a 401 is received, the current code
dispatches `setSessionExpired()` immediately. However, the API issues short-lived JWTs.
A 401 should trigger a token refresh attempt first. Only if refresh fails should it
dispatch `setSessionExpired()`.

Fix using RTK Query's `mutex` pattern (no new package — use the exported `Mutex` from
`@reduxjs/toolkit/query`):

```typescript
import { Mutex } from "@reduxjs/toolkit/query";
import { authApi } from "@/features/auth/authApi";
import { setCredentials } from "@/features/auth/authSlice";

const mutex = new Mutex();

export const baseQuery: BaseQueryFn<...> = async (args, api, extraOptions) => {
  await mutex.waitForUnlock();
  let result = await rawBaseQuery(args, api, extraOptions);

  if (result.error?.status === 401) {
    if (!mutex.isLocked()) {
      const release = await mutex.acquire();
      try {
        const { token: currentToken } = (api.getState() as RootState).auth;
        if (!currentToken) { api.dispatch(setSessionExpired()); return result; }

        // Attempt refresh
        const refreshResult = await api.dispatch(
          authApi.endpoints.refreshToken.initiate(undefined)
        );

        if ("data" in refreshResult && refreshResult.data) {
          api.dispatch(setCredentials(refreshResult.data));
          // Retry the original request
          result = await rawBaseQuery(args, api, extraOptions);
        } else {
          api.dispatch(setSessionExpired());
        }
      } finally {
        release();
      }
    } else {
      await mutex.waitForUnlock();
      result = await rawBaseQuery(args, api, extraOptions);
    }
  }

  // ... rest of 402/403 handlers unchanged
  return result;
};
```

Ensure `authApi` has a `refreshToken` endpoint that calls `POST /api/v1/auth/refresh`
and returns `AuthResponse`. If not, add it.

**F-STATE-02 — `useEffect` usage audit for data fetching**

Run: `grep -rn "useEffect" frontend/src/features --include="*.tsx" --include="*.ts"`
Any `useEffect` that makes an `axios`, `fetch`, or API call is a bug.
Only `useEffect` calls for browser side-effects (timers, DOM listeners, SignalR) are allowed.
Fix by moving data fetching to RTK Query hooks.

---

### F-UX: Frontend UX Bugs

**F-UX-01 — No React error boundary (P1)**

There is no `ErrorBoundary` component wrapping the app or individual routes.
A runtime render error in any component will crash the entire app.

Create `frontend/src/shared/components/ErrorBoundary.tsx`:
```tsx
import { Component, type ErrorInfo, type ReactNode } from "react";

interface Props  { children: ReactNode; fallback?: ReactNode; }
interface State  { hasError: boolean; error: Error | null; }

export class ErrorBoundary extends Component<Props, State> {
  state: State = { hasError: false, error: null };

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Structured log — do NOT log PII
    console.error("[ErrorBoundary]", error.message, info.componentStack);
  }

  render() {
    if (this.state.hasError) {
      return this.props.fallback ?? (
        <div role="alert" className="flex min-h-[40vh] flex-col items-center justify-center gap-3 text-center p-8">
          <p className="text-lg font-semibold text-destructive">Something went wrong</p>
          <p className="text-sm text-muted-foreground">
            {this.state.error?.message ?? "An unexpected error occurred."}
          </p>
          <button
            className="mt-2 rounded-md bg-primary px-4 py-2 text-sm text-primary-foreground"
            onClick={() => this.setState({ hasError: false, error: null })}
          >
            Try again
          </button>
        </div>
      );
    }
    return this.props.children;
  }
}
```

Wrap the app root in `main.tsx`:
```tsx
<ErrorBoundary>
  <Provider store={store}>
    <RouterProvider router={router} />
  </Provider>
</ErrorBoundary>
```

Also wrap each major route group in its layout component with a scoped `<ErrorBoundary>`.

**F-UX-02 — Missing `aria-label` on icon-only buttons across components (P2)**

Run: `grep -rn "<button\|<Button" frontend/src/features --include="*.tsx" -A 3 | grep -v "aria-label\|children\|>"`
Any interactive button with only an icon child and no `aria-label` or `aria-labelledby`
is an accessibility bug. Fix every occurrence.

**F-UX-03 — Loading skeletons missing on slow-loading pages (P2)**

Audit these pages for `isLoading` state handling:
- `ClientDetailPage.tsx` — tattoo record list
- `ArtistDetailPage.tsx` — artist card
- `PaymentListPage.tsx` — payment table
- `SchedulePage.tsx` — appointment calendar

If any shows a blank screen while loading (no skeleton), add `<Skeleton>` placeholders
using the existing `shadcn/ui` Skeleton component.

---

## Phase 2 — Production-Standard Features

Implement in the order listed. Each feature is atomic — complete it fully (domain →
application → infrastructure → API → frontend → tests) before starting the next.

---

### P-01: Rate Limiting (already defined in B-SEC-02 — implement here)

The `RateLimitingExtensions.cs` definition is in B-SEC-02. This item is to confirm
it is wired into `Program.cs` and applied to all auth + public-write endpoints.

Add a test: `AuthEndpoints_Login_Returns429_AfterTenAttempts` in the integration test
project.

---

### P-02: Health Checks — DB, Redis, and Stripe Connectivity

Read `Program.cs`. The current `AddHealthChecks()` registers no tagged checks.
Extend it:

```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(
        name:    "database",
        tags:    ["ready"])
    .AddRedis(
        redisConnectionString: builder.Configuration.GetConnectionString("Redis")!,
        name:    "redis",
        tags:    ["ready"]);
```

Add two health endpoints:
```csharp
// /health/live  — simple liveness probe (no DB/Redis check — used by K3s livenessProbe)
// /health/ready — full readiness with DB + Redis check (used by K3s readinessProbe)
app.MapHealthChecks("/health/live",  new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate        = check => check.Tags.Contains("ready"),
    ResponseWriter   = UIResponseWriter.WriteHealthCheckUIResponse, // built-in
});
```

Keep the original `/health` for backward compat.

No new packages — `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` and
`AspNetCore.HealthChecks.Redis` are **not** part of the no-new-packages rule — check if
they are already in the `.csproj` file. If not, do NOT add them; use a manual
health check instead:

```csharp
// Manual Redis health check (no extra package)
builder.Services.AddHealthChecks()
    .AddCheck("redis", () =>
    {
        try
        {
            IConnectionMultiplexer redis =
                app.Services.GetRequiredService<IConnectionMultiplexer>();
            return redis.IsConnected
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("Redis not connected");
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Redis error");
        }
    }, tags: ["ready"]);
```

---

### P-03: JWT Auto-Refresh (already defined in F-STATE-01 — implement here)

Fully implement the mutex-based refresh in `baseQuery.ts` and the `refreshToken` RTK
Query endpoint. Write a Vitest test that confirms a 401 triggers a refresh attempt
before dispatching `setSessionExpired`.

---

### P-04: Password Change for Authenticated Users

**Backend:**

Add to `IIdentityService`:
```csharp
Task<(bool Success, string[] Errors)> ChangePasswordAsync(
    Guid   userId,
    string currentPassword,
    string newPassword,
    CancellationToken ct);
```

Implement in `IdentityService.cs` using `UserManager<ApplicationUser>.ChangePasswordAsync`.

Create `Pena_e_Arte.Application/Auth/Commands/ChangePasswordCommand.cs`:
```csharp
public record ChangePasswordCommand(
    Guid   UserId,
    string CurrentPassword,
    string NewPassword) : IRequest;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(8)
            .Matches("[A-Z]").WithMessage("Must contain uppercase letter.")
            .Matches("[0-9]").WithMessage("Must contain a digit.");
        RuleFor(x => x.NewPassword)
            .NotEqual(x => x.CurrentPassword)
            .WithMessage("New password must differ from current password.");
    }
}
```

Add endpoint to `AuthEndpoints.cs`:
```csharp
group.MapPatch("/change-password", ChangePassword)
     .RequireAuthorization("ClientAndAbove");

private static async Task<IResult> ChangePassword(
    ChangePasswordRequest body,
    ClaimsPrincipal       user,
    ISender               mediator,
    CancellationToken     ct)
{
    Guid userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    await mediator.Send(new ChangePasswordCommand(userId, body.CurrentPassword, body.NewPassword), ct);
    return Results.NoContent();
}
```

**Frontend:**

Add to `authApi.ts`:
```typescript
changePassword: builder.mutation<void, ChangePasswordRequest>({
  query: (body) => ({ url: "auth/change-password", method: "PATCH", body }),
}),
```

Create `frontend/src/features/auth/components/ChangePasswordPage.tsx`:
- React Hook Form with `currentPassword`, `newPassword`, `confirmNewPassword` fields
- Password inputs (use existing `PasswordInput` shared component)
- `confirmNewPassword` client-side validation: must match `newPassword`
- On success: toast "Password changed successfully" and redirect to profile
- On `400` error: surface the server validation message inline

Add route to the relevant layouts (client, artist, owner all can change password):
`/account/password` → `<ChangePasswordPage />` within each layout.

Add link in `UserMenu.tsx` dropdown: "Change password" → `/account/password`.

Write unit test for `ChangePasswordValidator` (password strength rules) and a component
test for `ChangePasswordPage` (form submission, error display).

---

### P-05: Artist Working Hours / Availability

This feature prevents clients from booking outside an artist's set working hours and
prevents double-booking on days the artist is off.

**Domain:**

Create `Pena_e_Arte.Domain/Entities/ArtistSchedule.cs`:
```csharp
/// <summary>
/// One entry per working day-of-week. Artists without entries on a day are unavailable.
/// </summary>
public class ArtistSchedule : TenantEntity
{
    public Guid      ArtistId     { get; set; }
    public DayOfWeek DayOfWeek    { get; set; }
    public TimeSpan  StartTime    { get; set; }  // e.g. 09:00
    public TimeSpan  EndTime      { get; set; }  // e.g. 18:00
    public bool      IsAvailable  { get; set; } = true;

    public Artist Artist { get; set; } = null!;
}
```

Create `Pena_e_Arte.Domain/Entities/ArtistTimeOff.cs`:
```csharp
/// <summary>
/// A specific date range where the artist is unavailable (holiday, sick leave, etc.)
/// </summary>
public class ArtistTimeOff : TenantEntity
{
    public Guid     ArtistId  { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate   { get; set; }
    public string   Reason    { get; set; } = string.Empty; // max 200 chars

    public Artist Artist { get; set; } = null!;
}
```

Add to `Artist.cs`:
```csharp
public ICollection<ArtistSchedule> Schedule { get; set; } = [];
public ICollection<ArtistTimeOff>  TimeOff  { get; set; } = [];
```

Add to `IAppDbContext.cs`:
```csharp
DbSet<ArtistSchedule> ArtistSchedules { get; }
DbSet<ArtistTimeOff>  ArtistTimeOffs  { get; }
```

**Application — Commands:**

`SetArtistScheduleCommand` — replaces (upserts) the full weekly schedule for the current
artist. Body: `List<DayScheduleRequest>` where each item has `dayOfWeek`, `startTime`,
`endTime`, `isAvailable`. Validates start < end; max 14 hours per day.

`AddArtistTimeOffCommand` — adds a time-off block. Validates start < end; max 365 days.

`DeleteArtistTimeOffCommand` — removes a time-off block by ID.

**Application — Queries:**

`GetArtistScheduleQuery(Guid ArtistId)` → `List<ArtistScheduleResponse>`

`GetArtistTimeOffQuery(Guid ArtistId)` → `List<ArtistTimeOffResponse>`

`GetArtistAvailableSlotsQuery(Guid ArtistId, DateOnly Date, int DurationMinutes)`
→ `List<TimeSlotResponse>` — returns a list of available start times for a given day,
filtering out booked slots and respecting working hours. Slot interval: every 30 minutes.

**`CreateAppointmentHandler` — availability check:**

After the existing conflict check, add:

```csharp
// Check artist is working on the requested day
DayOfWeek day = req.Date.DayOfWeek;
ArtistSchedule? schedule = await db.ArtistSchedules
    .FirstOrDefaultAsync(s =>
        s.ArtistId    == req.ArtistId &&
        s.DayOfWeek   == day          &&
        s.IsAvailable == true, ct);

if (schedule is null)
    throw new BusinessRuleViolationException(
        "The artist is not available on the selected day.");

TimeSpan requestStart = req.Date.TimeOfDay;
TimeSpan requestEnd   = requestEnd.TimeOfDay;
if (requestStart < schedule.StartTime || requestEnd > schedule.EndTime)
    throw new BusinessRuleViolationException(
        "The appointment falls outside the artist's working hours.");

// Check time-off blocks
bool isTimeOff = await db.ArtistTimeOffs.AnyAsync(t =>
    t.ArtistId  == req.ArtistId &&
    t.StartDate <= req.Date      &&
    t.EndDate   >= req.Date, ct);

if (isTimeOff)
    throw new BusinessRuleViolationException(
        "The artist has time off on the selected date.");
```

**Note:** For studios that have not yet set their schedule, a missing schedule means
"unavailable". Studios should be prompted to set their schedule from the dashboard.
Add a `SetupChecklist` banner on the dashboard if `ArtistSchedules` is empty (see P-07).

**API:**

Add to `ArtistEndpoints.cs` (ArtistAndAbove — artist manages own schedule):
```
GET    /api/v1/artists/{id}/schedule          → GetArtistScheduleQuery
PUT    /api/v1/artists/{id}/schedule          → SetArtistScheduleCommand
GET    /api/v1/artists/{id}/time-off          → GetArtistTimeOffQuery
POST   /api/v1/artists/{id}/time-off          → AddArtistTimeOffCommand
DELETE /api/v1/artists/{id}/time-off/{timeOffId} → DeleteArtistTimeOffCommand
```

Add to `PublicEndpoints.cs` (AllowAnonymous — for booking widget):
```
GET /api/v1/public/artists/{slug}/available-slots?date=YYYY-MM-DD&duration=60
```

**Frontend:**

Create `frontend/src/features/artists/components/ArtistSchedulePage.tsx`:
- Weekly grid showing Mon–Sun with start/end time inputs per day + toggle
- "Time off" tab with date range picker (use shadcn DatePicker) and reason field
- Save via RTK Query `setArtistSchedule` mutation
- Add route `/artists/:id/schedule` (ArtistAndAbove)

Update `BookAppointmentForm.tsx`:
- After selecting artist and date, call `getArtistAvailableSlots` query
- Replace the free-form time input with a slot picker showing only available slots
- Show "No slots available" if the query returns empty

**Migration:** `AddArtistScheduleAndTimeOff`

---

### P-06: Aftercare Notification on Appointment Completion

When `CompleteAppointmentCommand` runs, send aftercare instructions to the client.

**Domain:**

Add `AftercareSentAt DateTime?` to `Appointment` entity.
Migration: `AddAppointmentAftercareSentAt`.

**Application:**

Create `SendAftercareNotificationCommand.cs`:
```csharp
public record SendAftercareNotificationCommand(Guid AppointmentId) : IRequest;

// Handler: loads appointment + client + studio, resolves notification preferences,
// sends via MailKit + Twilio per preference. Sets appointment.AftercareSentAt.
// Uses a standard aftercare template (HTML email + SMS fallback).
```

Standard aftercare template content (use this verbatim for the email):
- Keep the tattoo covered for 2–4 hours
- Gently wash with lukewarm water and fragrance-free soap
- Pat dry — never rub
- Apply a thin layer of unscented moisturizer 2–3 times daily for 2 weeks
- Avoid sun, pools, and soaking for 2 weeks
- Contact the studio if you notice redness, swelling, or discharge after 3 days

In `CompleteAppointmentHandler`, after saving:
```csharp
await sender.Send(new SendAftercareNotificationCommand(appointment.Id), ct);
```

**Frontend:**

In `AppointmentDetailPage.tsx`, show a badge "Aftercare sent ✓" if `aftercareSentAt` is
not null.

---

### P-07: Studio Setup Checklist on Dashboard

New studios often forget to configure deposit rules, artist schedules, or branding.
Add a "Complete your setup" card on `DashboardPage.tsx` that disappears when all items
are done.

**Checklist items:**

| # | Item | How to detect |
|---|---|---|
| 1 | At least one deposit rule configured | `depositRules.length > 0` |
| 2 | Studio contact info added (phone or Instagram) | `studio.phoneNumber \|\| studio.instagramHandle` |
| 3 | At least one artist with a schedule set | `artists.some(a => a.hasSchedule)` |
| 4 | Studio location set (lat + lng) | `studio.latitude !== null` |

Use data already fetched by the dashboard's existing RTK Query calls where possible.
Add `hasSchedule: boolean` to `ArtistResponse` (computed in `GetArtistsQuery` handler
by checking whether `ArtistSchedules` has any rows for the artist).

Render the checklist as a collapsible card using shadcn `Card` + `Collapsible` or just
a simple list of checkmarks. Auto-hide when all 4 items pass.

---

### P-08: SEO Meta Tags on Public Pages

Public pages need proper `<title>` and `<meta>` tags for search engine discoverability.
No new package — use direct DOM manipulation via a `usePageMeta` hook:

```typescript
// frontend/src/shared/hooks/usePageMeta.ts
export function usePageMeta(title: string, description?: string) {
  useEffect(() => {
    document.title = title;
    let meta = document.querySelector<HTMLMetaElement>('meta[name="description"]');
    if (!meta) {
      meta = document.createElement("meta");
      meta.name = "description";
      document.head.appendChild(meta);
    }
    meta.content = description ?? "";
    return () => { document.title = "Pena e Artë"; };
  }, [title, description]);
}
```

Apply to these public pages:

| Component | Title | Description |
|---|---|---|
| `StudioPortfolioPage` | `{studio.name} — Tattoo Studio` | `{studio.city} tattoo studio. Book your appointment online.` |
| `ArtistPortfolioPage` | `{artist.name} — Tattoo Artist` | `Tattoo artist in {studio.name}, {studio.city}. View portfolio and book online.` |
| `DiscoverPage` | `Discover Tattoo Artists — Pena e Artë` | `Browse tattoo artists and studios near you.` |
| `LoginPage` | `Sign In — Pena e Artë` | *(none needed)* |
| `EmbedPage` | `{studio.name} — Book Now` | *(none needed)* |

---

### P-09: `.ics` Calendar Export for Appointments

Allow clients and artists to download an appointment as an iCal file.
No new package — generate the `.ics` format manually (it is plain text).

**Backend:**

Add endpoint to `AppointmentEndpoints.cs`:
```csharp
group.MapGet("{id}/calendar", DownloadCalendarFile)
     .RequireAuthorization("ClientAndAbove");

private static async Task<IResult> DownloadCalendarFile(
    Guid            id,
    ISender         mediator,
    CancellationToken ct)
{
    string ics = await mediator.Send(new GetAppointmentIcsQuery(id), ct);
    return Results.File(
        Encoding.UTF8.GetBytes(ics),
        "text/calendar",
        $"appointment-{id}.ics");
}
```

Create `GetAppointmentIcsQuery.cs`:
```csharp
public record GetAppointmentIcsQuery(Guid AppointmentId) : IRequest<string>;

// Handler: loads appointment + artist name + studio name
// Returns a minimal valid VCALENDAR string:
/*
BEGIN:VCALENDAR
VERSION:2.0
PRODID:-//PenaEArte//Appointment//EN
BEGIN:VEVENT
UID:{appointment.Id}@penaearte.com
DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}
DTSTART:{appointment.Date:yyyyMMddTHHmmssZ}
DTEND:{appointment.EndDate:yyyyMMddTHHmmssZ}
SUMMARY:Tattoo appointment with {artistName} at {studioName}
DESCRIPTION:Deposit amount: {appointment.DepositAmount:C}
END:VEVENT
END:VCALENDAR
*/
```

**Frontend:**

Add a "Add to Calendar" button on `AppointmentDetailPage.tsx` and `AppointmentCard.tsx`:
```tsx
<a
  href={`/api/v1/appointments/${appointment.id}/calendar`}
  download={`appointment-${appointment.id}.ics`}
  className="..."
>
  Add to Calendar
</a>
```

Use an `<a>` tag with `download` — no RTK Query mutation needed for file downloads.

---

### P-10: Email Verification on Registration

**Backend:**

In `RegisterUserHandler.cs`, after creating the user, generate an email confirmation token
and send a verification email:

```csharp
string confirmationToken = await identity.GenerateEmailConfirmationTokenAsync(userId);
string confirmationUrl   = $"{config["App:BaseUrl"]}/verify-email?token={Uri.EscapeDataString(confirmationToken)}&userId={userId}";

await notifications.SendEmailAsync(
    req.Email,
    "Confirm your Pena e Artë account",
    EmailTemplates.EmailVerification(confirmationUrl),
    ct);
```

Add to `IIdentityService`:
```csharp
Task<string> GenerateEmailConfirmationTokenAsync(Guid userId);
Task<(bool Success, string[] Errors)> ConfirmEmailAsync(Guid userId, string token, CancellationToken ct);
```

Add endpoint to `AuthEndpoints.cs`:
```csharp
group.MapGet("/verify-email", VerifyEmail).AllowAnonymous();
// Add to AllowAnonymous exceptions table in architecture.md

private static async Task<IResult> VerifyEmail(
    Guid   userId, string token,
    ISender mediator, CancellationToken ct)
{
    await mediator.Send(new ConfirmEmailCommand(userId, token), ct);
    return Results.Redirect("/login?verified=true");
}
```

**Gate:** Decide whether to block login until email is verified or just send the email
for trust purposes. For now: **do not block login** — send the email, mark `EmailConfirmed`
when clicked, and show a banner "Please verify your email" until confirmed.

**Frontend:**

In `LoginPage.tsx`, if `?verified=true` is in the URL, show a toast: "Email verified!
You can now log in."

In `DashboardPage.tsx` / relevant layout, if `user.emailConfirmed === false`, show a
dismissable inline banner: "Please verify your email. [Resend verification email]"

Add `emailConfirmed: boolean` to `AuthResponse` and `User` type in the auth slice.
Add `resendVerificationEmail: builder.mutation` endpoint in `authApi.ts`.

---

## Phase 3 — Test Coverage

For every bug fixed and every feature implemented, write tests.

**Backend unit tests** (in `Pena_e_Arte.UnitTests/`):
- `MarkNoShowHandler_SetsDepositForfeited`
- `MarkNoShowHandler_CardPayment_CapturesStripeHold`
- `CompleteAppointmentHandler_CapturesStripeHold_WhenPaymentCaptured`
- `CancelAppointmentHandler_SetsDepositRefunded_WhenCashPending`
- `CancelAppointmentHandler_CancelsHangfireJobs`
- `CreateAppointmentHandler_ThrowsWhenArtistNotWorkingOnDay`
- `CreateAppointmentHandler_ThrowsWhenAppointmentOutsideWorkingHours`
- `CreateAppointmentHandler_ThrowsWhenArtistOnTimeOff`
- `ChangePasswordHandler_ThrowsWhenCurrentPasswordWrong`
- `GetAppointmentIcsHandler_ReturnsValidIcsString`

**Frontend tests** (in `frontend/src/`):
- `ErrorBoundary.test.tsx` — renders fallback on thrown render error
- `baseQuery.test.ts` — 401 triggers refresh before `setSessionExpired`
- `ChangePasswordPage.test.tsx` — form validation, success toast, server error display
- `ArtistSchedulePage.test.tsx` — schedule grid renders, save mutation called
- `BookAppointmentForm.test.tsx` — slot picker shows only available times
- `usePageMeta.test.ts` — sets `document.title` and meta description

---

## Phase 4 — Test-Fix Loop

Run the full test suite. For any failure, fix the cause and re-run. Do not mark this
session complete until all tests pass.

```bash
cd "Pena e Arte"

# 1. Backend build
dotnet build --verbosity minimal
# Fix any compilation error before continuing

# 2. Backend tests
dotnet test
# Fix any test failure. Re-run after each fix.

# 3. Frontend type check
cd frontend && pnpm tsc --noEmit
# Fix every type error

# 4. Lint
pnpm lint
# Fix every lint error

# 5. Frontend tests
pnpm test --run
# Fix every test failure. Re-run after each fix.
```

All five must exit 0 before this prompt is considered complete.

---

## Phase 5 — Architecture Docs Update

After all fixes and features are implemented:

1. Update `docs/claude/architecture.md`:
   - **Decisions Log**: add entries for CORS config, rate limiting, ArtistSchedule, JWT refresh
   - **Feature Module Map**: add rows for P-04 (Password Change), P-05 (Artist Availability), P-06 (Aftercare), P-10 (Email Verification)
   - **AllowAnonymous Exceptions**: add `GET /api/v1/auth/verify-email`
   - **IgnoreQueryFilters Approved Usages**: check if `RegisterUserHandler`'s `IgnoreQueryFilters` call for client linking is already documented (add it if not)

2. Write a `fix-log.md` in `docs/claude/` listing every bug fixed with:
   - Bug ID (e.g. B-LOG-01)
   - One-line description
   - File(s) changed
   - Status (Fixed | Deferred | Won't Fix with reason)

---

## Hard Rules

1. **No new NuGet or npm packages** except: if `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` or
   `AspNetCore.HealthChecks.Redis` are already in the `.csproj`, use them. Otherwise use the manual approach shown.
2. **No business logic in endpoints** — all new endpoints call MediatR only.
3. **No `IgnoreQueryFilters()` without an `// Approved:` comment** — all new usages must be documented.
4. **No PII in logs** — `AuthorName`, `Email`, and customer names must never appear in Serilog statements.
5. **TypeScript strict mode** — no `any`, no default component exports, no TypeScript `enum`.
6. **No `useEffect` for data fetching** — RTK Query only.
7. **Every new endpoint needs a FluentValidation validator** — no exceptions.
8. **Every new command/query needs a unit test** — no exceptions.
