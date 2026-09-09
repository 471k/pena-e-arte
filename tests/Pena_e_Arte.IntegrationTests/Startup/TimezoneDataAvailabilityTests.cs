using FluentAssertions;

namespace Pena_e_Arte.IntegrationTests.Startup;

// Cheapest possible guard against the "runtime image doesn't ship tzdata" risk (an Alpine
// base image swap would silently break every studio-local-time conversion — see
// TimezoneUtils.ToStudioLocal). Must run in CI on every future change, not just once.
public class TimezoneDataAvailabilityTests
{
    [Fact]
    public void FindSystemTimeZoneById_PlatformDefaultTimezone_DoesNotThrow()
    {
        Action act = () => TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane");

        act.Should().NotThrow();
    }
}
