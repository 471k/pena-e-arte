using FluentAssertions;
using Pena_e_Arte.Application.Common;

namespace Pena_e_Arte.UnitTests.Common;

public class TimezoneUtilsTests
{
    [Fact]
    public void ToStudioLocal_NonUtcTimezone_ConvertsAwayFromRawUtcValue()
    {
        // Deliberately far from UTC to make a bug (returning the raw UTC value unconverted)
        // obvious in a failing assertion.
        DateTime utc = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        DateTime local = TimezoneUtils.ToStudioLocal(utc, "America/New_York");

        local.Should().NotBe(utc);
        local.Hour.Should().Be(8); // EDT is UTC-4 in June
    }

    [Fact]
    public void ToStudioLocal_UnknownTimezone_FallsBackToUtcRatherThanThrowing()
    {
        DateTime utc = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

        DateTime result = TimezoneUtils.ToStudioLocal(utc, "Not/A_Real_Zone");

        result.Should().Be(utc);
    }
}
