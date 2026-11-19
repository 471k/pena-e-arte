# Backend Instructions — ASP.NET Core 10

> Load this file when working on anything in `Pena_e_Arte.API`,
> `Pena_e_Arte.Application`, `Pena_e_Arte.Domain`, or
> `Pena_e_Arte.Infrastructure`.

---

## Project Layout

```
Pena_e_Arte.API/
├── Endpoints/              one static class per feature (AppointmentEndpoints.cs)
├── Middleware/             TenantMiddleware.cs, ExceptionMiddleware.cs
├── Extensions/             ServiceCollectionExtensions per concern
└── Program.cs              minimal, just wires extensions

Pena_e_Arte.Application/
├── Appointments/
│   ├── Commands/           CreateAppointmentCommand + Handler
│   ├── Queries/            GetAppointmentsQuery + Handler
│   └── Validators/         CreateAppointmentValidator (FluentValidation)
├── Clients/
├── Studios/
├── Billing/
├── Designs/
└── Notifications/

Pena_e_Arte.Domain/
├── Entities/               pure C# classes, no EF attributes here
├── Enums/                  Role.cs, AppointmentStatus.cs
├── Interfaces/             ICurrentTenant, ICurrentUser, repositories
└── Exceptions/             DomainException base + specifics

Pena_e_Arte.Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs
│   ├── Configurations/     IEntityTypeConfiguration per entity
│   └── Migrations/
├── Services/
│   ├── Stripe*/            Stripe Billing + aggregator card payment (no Connect)
│   ├── MailKit/            email templates
│   └── *.cs                Twilio, R2, Hangfire, Identity, etc.
├── Hubs/                   SignalR hubs
├── Jobs/                   Hangfire job classes
└── Caching/                Redis wrappers

Pena_e_Arte.Contracts/
├── Requests/               input DTOs
└── Responses/              output DTOs
```

---

## Endpoint Pattern

Always use Minimal API. Group by feature. One file per feature group.

```csharp
// Endpoints/AppointmentEndpoints.cs
public static class AppointmentEndpoints
{
    public static void MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/appointments")
            .RequireAuthorization();

        group.MapGet("/",    GetAppointments).RequireAuthorization("ArtistAndAbove");
        group.MapPost("/",   CreateAppointment).RequireAuthorization("ClientAndAbove");
        group.MapDelete("{id}", CancelAppointment).RequireAuthorization("ArtistAndAbove");
    }

    private static async Task<IResult> CreateAppointment(
        CreateAppointmentRequest request,
        ISender mediator,
        CancellationToken ct)
    {
        var result = await mediator.Send(new CreateAppointmentCommand(request), ct);
        return Results.Created($"/api/v1/appointments/{result.Id}", result);
    }
}
```

---

## MediatR Pattern (CQRS)

One command/query per use case. Handler in the same folder.

```csharp
// Application/Appointments/Commands/CreateAppointmentCommand.cs
public record CreateAppointmentCommand(CreateAppointmentRequest Request)
    : IRequest<AppointmentResponse>;

public class CreateAppointmentHandler
    : IRequestHandler<CreateAppointmentCommand, AppointmentResponse>
{
    private readonly AppDbContext _db;
    private readonly ICurrentTenant _tenant;

    public CreateAppointmentHandler(AppDbContext db, ICurrentTenant tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<AppointmentResponse> Handle(
        CreateAppointmentCommand command, CancellationToken ct)
    {
        // business logic here
    }
}
```

---

## FluentValidation Pattern

One validator per command/request. Registered automatically via assembly scan.

```csharp
public class CreateAppointmentValidator
    : AbstractValidator<CreateAppointmentCommand>
{
    public CreateAppointmentValidator()
    {
        RuleFor(x => x.Request.ArtistId).NotEmpty();
        RuleFor(x => x.Request.Date).GreaterThan(DateTime.UtcNow);
        RuleFor(x => x.Request.DurationMinutes).InclusiveBetween(30, 480);
    }
}
```

Register the pipeline behavior in `Program.cs` once:
```csharp
builder.Services.AddTransient(
    typeof(IPipelineBehavior<,>),
    typeof(ValidationBehavior<,>));
```

---

## RBAC Policies

Defined once in `Extensions/AuthorizationExtensions.cs`. Never redefine inline.

```csharp
options.AddPolicy("ClientAndAbove",  p => p.RequireRole("client","artist","owner","admin"));
options.AddPolicy("ArtistAndAbove",  p => p.RequireRole("artist","owner","admin"));
options.AddPolicy("OwnerOnly",       p => p.RequireRole("owner","admin"));
options.AddPolicy("AdminOnly",       p => p.RequireRole("admin"));
```

---

## Tenant Middleware

Runs before authorization. Extracts and validates `tenant_id` from JWT claim.
Sets `ICurrentTenant` in DI scope.

```csharp
// Middleware/TenantMiddleware.cs
public class TenantMiddleware
{
    public async Task InvokeAsync(HttpContext context, ICurrentTenant tenant)
    {
        var tenantId = context.User.FindFirstValue("tenant_id");
        if (tenantId is not null) tenant.SetTenant(tenantId);
        await _next(context);
    }
}
```

---

## SignalR Hubs

One hub per domain. Clients join groups by `studioId`.

```csharp
// Infrastructure/Hubs/ScheduleHub.cs
[Authorize]
public class ScheduleHub : Hub
{
    public async Task JoinStudio(string studioId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"studio:{studioId}");

    public async Task LeaveStudio(string studioId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"studio:{studioId}");
}
```

Push from anywhere via `IHubContext<ScheduleHub>`:
```csharp
await _hub.Clients.Group($"studio:{studioId}")
    .SendAsync("AppointmentCreated", response, ct);
```

---

## Error Handling

Use `ExceptionMiddleware` globally. Never try/catch in handlers unless
you're doing domain-specific recovery.

```csharp
// Domain/Exceptions/DomainException.cs
public abstract class DomainException(string message) : Exception(message);
public class SlotAlreadyBookedException()
    : DomainException("The selected time slot is no longer available.");
```

ExceptionMiddleware maps exception types to HTTP status codes centrally.

---

## Cash Payment Pattern

Cash payments do not touch any external service. They follow a two-step manual
confirmation flow entirely within the domain.

```
1. Client books → DeclareCashDepositCommand
   Creates Payment { Method = Cash, Status = CashPending }
   No Stripe call. No external request.

2. Owner/artist confirms receipt → ConfirmCashDepositCommand
   Sets Payment.Status = Paid, Payment.PaidAt = UtcNow
   Sets Appointment.DepositStatus = Paid
   Sets Payment.CashConfirmedByUserId = currentUser.UserId

3. Admin activates cash subscription → ActivateSubscriptionManuallyCommand
   Sets Subscription.Status = Active, CurrentPeriodEnd = UtcNow + 1 month
   Uses IgnoreQueryFilters() — AdminOnly command
```

Cash fields on `Payment`:
- `Method` — `ClientPaymentMethod.Cash`
- `Status` — starts as `CashPending`, becomes `Paid` after confirmation
- `CashNote` — optional freetext (max 500 chars)
- `CashConfirmedByUserId` — set on confirmation (not on declaration)

Card (`Stripe`) fields remain null for cash payments.
Never call `IStripePaymentService` from cash payment handlers.

---

## Serilog Convention

```csharp
Log.Information("Appointment created {@AppointmentId} for tenant {@TenantId}",
    appointment.Id, _tenant.StudioId);
```

Always use structured properties with `@`. Never string interpolation in logs.
The request middleware enriches every log with `tenant_id`, `user_id`,
`request_id` automatically — do not add them manually per log line.

## Clocks

Two defect classes, each with its own fix. Name the class in the PR so the fix matches it.

1. **Time of day read from the wall clock.** Code or a test reads `DateTime.UtcNow` and its answer
   depends on *when* it runs: "now + 3 h" crosses the studio's midnight, a fixture date passes the
   year end, `AddMonths` clamps on the 29th-31st. Fix: inject `TimeProvider` (registered as
   `TimeProvider.System`) and read `GetUtcNow()`; tests use a fixed instant. The nightly
   `Clock sweep` workflow (`.github/workflows/clock-sweep.yml`) finds this class by running the
   suites with the clock faked to midnights, month/year end, DST changes and a leap day.
2. **A duration timed with the wall clock.** Code measures "how long has passed" or "wait until
   now + X" with `DateTime.UtcNow`. A wall-clock step (NTP, a VM resume) stretches or cuts the
   wait. `TimeProvider.GetUtcNow()` does **not** fix this; it is the same wall clock behind an
   abstraction. Fix: the monotonic clock, `TimeProvider.GetTimestamp()` + `GetElapsedTime()`
   (or `Stopwatch`), and `Task.Delay(interval, timeProvider, ct)`. First member:
   `RedisConnectionWait` (fixed 19 Nov 2026). The sweep does not find this class (it keeps the
   monotonic clock real), so look for it in review.
