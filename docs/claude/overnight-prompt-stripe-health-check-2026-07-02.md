# Overnight Prompt — Stripe Health Check (P-02)
**Date:** 2026-07-02
**Scope:** Single focused feature. Complete it fully including test, then exit.

---

## Context

`/health/ready` already exposes two checks:
- `database` → `DatabaseHealthCheck.cs` — calls `db.Database.CanConnectAsync()`
- `redis`    → `RedisHealthCheck.cs`    — checks `redis.IsConnected`

Both follow the same pattern:
- Live in `Pena_e_Arte.API/Extensions/`
- Implement `IHealthCheck` via primary constructor injection
- Registered in `Program.cs` with `.AddCheck<T>(name, tags: ["ready"])`

The Stripe check is missing. Add it now.

---

## Required Reading

```
CLAUDE.md
docs/claude/backend.md
docs/claude/conventions.md
Pena_e_Arte.API/Extensions/DatabaseHealthCheck.cs     ← pattern to follow
Pena_e_Arte.API/Extensions/RedisHealthCheck.cs        ← pattern to follow
Pena_e_Arte.API/Program.cs                            ← where to register
Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs  ← where to add DI
```

---

## What to Build

### Step 1 — Register `Stripe.BalanceService` in DI

File: `Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs`

`BalanceService` is the correct probe: it is the Stripe API's lightest read endpoint
(`GET /v1/balance`), creates no resources, and confirms both API key validity AND
network reachability in one call. It is NOT yet registered.

Add it alongside the other Stripe singletons (around line 71–78):

```csharp
services.AddSingleton<Stripe.BalanceService>();
```

No other changes to this file.

---

### Step 2 — Create `StripeHealthCheck.cs`

File: `Pena_e_Arte.API/Extensions/StripeHealthCheck.cs`

Follow the exact same structure as `DatabaseHealthCheck.cs` and `RedisHealthCheck.cs`:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Stripe;

namespace Pena_e_Arte.API.Extensions;

public class StripeHealthCheck(BalanceService balanceService) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await balanceService.GetAsync(cancellationToken: cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return HealthCheckResult.Unhealthy("Stripe API key invalid or unauthorised", ex);
        }
        catch (StripeException ex)
        {
            return HealthCheckResult.Degraded("Stripe API error", ex);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Stripe unreachable", ex);
        }
    }
}
```

**Why three catch blocks:**
- `401 Unauthorized` → `Unhealthy`: the key is wrong. Payments WILL fail. This is
  a hard failure that requires operator action.
- Other `StripeException` → `Degraded`: Stripe is reachable but returned an API error
  (e.g. rate limit, temporary 5xx). Payments may still work; alert but don't panic.
- Any other `Exception` → `Unhealthy`: network unreachable, DNS failure, timeout.

---

### Step 3 — Register in `Program.cs`

File: `Pena_e_Arte.API/Program.cs`

Current state (lines 45–47):
```csharp
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis",    tags: ["ready"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);
```

Add the Stripe check:
```csharp
builder.Services.AddHealthChecks()
    .AddCheck<RedisHealthCheck>("redis",       tags: ["ready"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<StripeHealthCheck>("stripe",     tags: ["ready"]);
```

**Important — rate limiting concern:**
`GET /v1/balance` is cheap but Stripe's standard rate limit is 100 requests/second per
secret key. Kubernetes readiness probes default to polling every 10 seconds per pod.
With many pods this is still well within limits. No caching needed for now.

However, if the team later scales to a high pod count, add result caching via
`HealthCheckOptions.MaximumAge` on the `/health/ready` endpoint:
```csharp
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate  = check => check.Tags.Contains("ready"),
    // Add this if rate limits become a concern:
    // MaximumAge = TimeSpan.FromSeconds(30),
});
```
Leave the comment in place for future reference. Do NOT add `MaximumAge` now unless
the build or tests require it.

---

### Step 4 — Write the Unit Test

File: `tests/Pena_e_Arte.UnitTests/HealthChecks/StripeHealthCheckTests.cs`

Create this directory if it doesn't exist.

Use the same testing approach as existing service tests in the unit test project.
Mock `BalanceService` using NSubstitute (or Moq, whichever is already used — check
the `.csproj` of the unit test project before choosing).

Required test cases:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pena_e_Arte.API.Extensions;
using Stripe;
// + NSubstitute or Moq — match what's already in the project

namespace Pena_e_Arte.UnitTests.HealthChecks;

public class StripeHealthCheckTests
{
    // Test 1: Stripe responds successfully → Healthy
    // Arrange: mock balanceService.GetAsync() returns a Balance object
    // Act:     call CheckHealthAsync
    // Assert:  result.Status == HealthStatus.Healthy

    // Test 2: Stripe returns 401 → Unhealthy
    // Arrange: mock throws StripeException with HttpStatusCode.Unauthorized
    // Act:     call CheckHealthAsync
    // Assert:  result.Status == HealthStatus.Unhealthy
    // Assert:  result.Description contains "invalid or unauthorised" (case-insensitive)

    // Test 3: Stripe returns another StripeException (e.g. 429) → Degraded
    // Arrange: mock throws StripeException with HttpStatusCode.TooManyRequests
    // Act:     call CheckHealthAsync
    // Assert:  result.Status == HealthStatus.Degraded

    // Test 4: Network error (non-Stripe exception) → Unhealthy
    // Arrange: mock throws HttpRequestException
    // Act:     call CheckHealthAsync
    // Assert:  result.Status == HealthStatus.Unhealthy
    // Assert:  result.Description contains "unreachable" (case-insensitive)

    // Test 5: CancellationToken is respected
    // Arrange: mock throws OperationCanceledException (simulates timeout)
    // Act:     call CheckHealthAsync
    // Assert:  does NOT return Healthy
    //          (either throws or returns Unhealthy — match whichever the
    //           implementation does, but it must NOT swallow the cancellation)
}
```

Write each test as a real `[Fact]` or `[Theory]` (whichever style the project uses).
Do not leave stubs — implement every assertion.

**Note on mocking `BalanceService`:**
`Stripe.BalanceService` is not sealed and its `GetAsync` method is virtual, so it
can be mocked with NSubstitute or Moq directly. If the test project uses NSubstitute:
```csharp
BalanceService balanceSvc = Substitute.For<BalanceService>();
balanceSvc.GetAsync(Arg.Any<RequestOptions>(), Arg.Any<CancellationToken>())
          .Returns(new Balance());
```
If Moq:
```csharp
Mock<BalanceService> mock = new();
mock.Setup(s => s.GetAsync(It.IsAny<RequestOptions>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new Balance());
```

---

## Verification

Run in order. Fix any failure before moving to the next step.

```bash
cd "Pena e Arte"

# 1. Build compiles clean
dotnet build

# 2. All unit tests pass (including the new StripeHealthCheckTests)
dotnet test tests/Pena_e_Arte.UnitTests/Pena_e_Arte.UnitTests.csproj --no-build

# 3. Full test suite passes
dotnet test --no-build
```

If step 1 fails: read the compiler error, fix the root cause, re-run.
If step 2 fails on the new tests: diagnose the mock setup, fix, re-run.
Do not move on until all three steps are green.

---

## Exit Condition

All three commands above exit with code 0.

Then append to `docs/claude/architecture.md`:

```markdown
## P-02 Stripe Health Check — 2026-07-02

- Added `Stripe.BalanceService` to DI in `InfrastructureServiceExtensions.cs`
- Created `Pena_e_Arte.API/Extensions/StripeHealthCheck.cs`
- Registered as `"stripe"` with `tags: ["ready"]` in `Program.cs`
- `/health/ready` now probes DB, Redis, and Stripe before reporting ready
- Unit tests: `tests/Pena_e_Arte.UnitTests/HealthChecks/StripeHealthCheckTests.cs`
- Rate limit note: left comment in `Program.cs` about `MaximumAge` for high pod counts
```
