# Conventions — Naming, Formatting, Patterns

> Load this file when unsure about naming, file placement,
> or code style in either the backend or frontend.

---

## C# Conventions

```csharp
// Classes, methods, properties — PascalCase
public class AppointmentHandler { }
public Task<AppointmentResponse> Handle() { }
public Guid StudioId { get; set; }

// Private fields — _camelCase
private readonly AppDbContext _db;
private readonly ICurrentTenant _tenant;

// Local variables, parameters — camelCase
var appointmentId = Guid.NewGuid();
async Task CreateAsync(CreateAppointmentCommand command)

// Constants — PascalCase
public const string DefaultPolicy = "ClientAndAbove";

// Async methods always suffixed with Async
public async Task<Result> CreateAppointmentAsync() { }

// Records for commands, queries, DTOs (immutable by default)
public record CreateAppointmentCommand(CreateAppointmentRequest Request)
    : IRequest<AppointmentResponse>;

// No regions. If a file needs regions, split it into smaller files.
// No commented-out code committed to the repo.
// var only when type is obvious from the right-hand side.
```

---

## TypeScript / React Conventions

```typescript
// Components — PascalCase, named export
export function AppointmentCard({ appointment }: AppointmentCardProps) {}

// Hooks — camelCase, use prefix
export function useAppointments() {}
export function useCurrentUser() {}

// Types and interfaces — PascalCase
interface AppointmentCardProps { appointment: AppointmentResponse; }
type Role = "client" | "artist" | "owner" | "issuer";

// Constants — SCREAMING_SNAKE_CASE
const MAX_SESSION_DURATION = 480;

// RTK Query hooks — use generated hooks directly, no wrappers
const { data, isLoading } = useGetAppointmentsQuery();

// No default exports for components (named exports only)
// No index barrel files that re-export everything — import directly
// No inline object/array creation in JSX props (causes re-renders)
```

---

## File Naming

| Type | Convention | Example |
|---|---|---|
| C# class | PascalCase.cs | `AppointmentHandler.cs` |
| C# interface | IPascalCase.cs | `ICurrentTenant.cs` |
| C# migration | EF generated | `20241201_AddDepositColumn.cs` |
| React component | PascalCase.tsx | `AppointmentCard.tsx` |
| React hook | useCamelCase.ts | `useCurrentUser.ts` |
| RTK Query slice | camelCaseApi.ts | `appointmentsApi.ts` |
| Redux slice | camelCaseSlice.ts | `authSlice.ts` |
| Utility | camelCase.ts | `formatCurrency.ts` |
| Type file | camelCase.types.ts | `appointment.types.ts` |

---

## HTTP Status Codes

Always return the correct code. Never return 200 for everything.

```
200 OK              successful GET, successful PUT
201 Created         successful POST that created a resource
204 No Content      successful DELETE
400 Bad Request     validation failure (FluentValidation returns this)
401 Unauthorized    no token or token invalid
403 Forbidden       token valid but role insufficient
404 Not Found       resource does not exist (or tenant-scoped and not found)
409 Conflict        slot already booked, duplicate record
422 Unprocessable   semantic validation failure
500 Internal        unexpected — never expose exception detail to client
```

---

## Git Conventions

```
feat:     new feature
fix:      bug fix
chore:    build, deps, config — no production code change
refactor: code change that is not a fix or feature
test:     adding or updating tests
docs:     documentation only
migrate:  database migration

Examples:
feat(appointments): add deposit forfeiture on no-show
fix(auth): refresh token not rotating on concurrent requests
migrate: add soft delete columns to clients table
```

Branch naming: `feat/appointment-deposits`, `fix/token-refresh`, `chore/update-deps`

---

## Testing Conventions

```csharp
// Test method naming: MethodName_Scenario_ExpectedResult
[Fact]
public async Task CreateAppointment_SlotAlreadyBooked_ThrowsSlotAlreadyBookedException()

// Arrange / Act / Assert — always separated with blank lines
[Fact]
public async Task CreateAppointment_ValidRequest_ReturnsCreatedAppointment()
{
    // Arrange
    var command = new CreateAppointmentCommand(...);

    // Act
    var result = await _handler.Handle(command, CancellationToken.None);

    // Assert
    result.Should().NotBeNull();
    result.Status.Should().Be(AppointmentStatus.Pending);
}
```

```typescript
// Frontend: describe + it blocks
describe("AppointmentCard", () => {
  it("shows cancelled badge when status is cancelled", () => {});
  it("does not show cancel button for client role", () => {});
});
```

---

## What to Never Do

**Backend**
- No business logic in endpoints — endpoints call MediatR only
- No EF Core queries in Domain layer
- No `Console.WriteLine` or `Debug.WriteLine`
- No `Thread.Sleep` — use `Task.Delay` or Hangfire scheduling
- No synchronous I/O in async methods

**Frontend**
- No `useEffect` for data fetching — use RTK Query
- No direct `store.getState()` outside Redux middleware
- No `console.log` committed to the repo
- No hardcoded API URLs — always from environment config
- No role checks in component render logic — use `usePermission` hook
