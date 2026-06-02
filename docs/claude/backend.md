# Backend Instructions — ASP.NET Core 10

> Load this file when working on anything in `src/TattooStudio.API`,
> `TattooStudio.Application`, `TattooStudio.Domain`, or
> `TattooStudio.Infrastructure`.

---

## Project Layout

```
TattooStudio.API/
├── Endpoints/              one static class per feature (AppointmentEndpoints.cs)
├── Middleware/             TenantMiddleware.cs, ExceptionMiddleware.cs
├── Extensions/             ServiceCollectionExtensions per concern
└── Program.cs              minimal, just wires extensions

TattooStudio.Application/
├── Appointments/
│   ├── Commands/           CreateAppointmentCommand + Handler
│   ├── Queries/            GetAppointmentsQuery + Handler
│   └── Validators/         CreateAppointmentValidator (FluentValidation)
├── Clients/
├── Studios/
├── Billing/
├── Designs/
└── Notifications/

TattooStudio.Domain/
├── Entities/               pure C# classes, no EF attributes here
├── Enums/                  Role.cs, AppointmentStatus.cs
├── Interfaces/             ICurrentTenant, ICurrentUser, repositories
└── Exceptions/             DomainException base + specifics

TattooStudio.Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs
│   ├── Configurations/     IEntityTypeConfiguration per entity
│   └── Migrations/
├── Services/               Stripe, Twilio, MailKit, R2 implementations
├── Hubs/                   SignalR hubs
├── Jobs/                   Hangfire job classes
└── Caching/                Redis wrappers

TattooStudio.Contracts/
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
options.AddPolicy("ClientAndAbove",  p => p.RequireRole("client","artist","owner","issuer"));
options.AddPolicy("ArtistAndAbove",  p => p.RequireRole("artist","owner","issuer"));
options.AddPolicy("OwnerOnly",       p => p.RequireRole("owner","issuer"));
options.AddPolicy("IssuerOnly",      p => p.RequireRole("issuer"));
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

## Serilog Convention

```csharp
Log.Information("Appointment created {@AppointmentId} for tenant {@TenantId}",
    appointment.Id, _tenant.StudioId);
```

Always use structured properties with `@`. Never string interpolation in logs.
The request middleware enriches every log with `tenant_id`, `user_id`,
`request_id` automatically — do not add them manually per log line.
