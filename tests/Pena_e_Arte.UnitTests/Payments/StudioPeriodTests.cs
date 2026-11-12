using FluentAssertions;
using Pena_e_Arte.Application.Payments;

namespace Pena_e_Arte.UnitTests.Payments;

/// <summary>
/// E05b-1 (O08): a date the owner picks is a studio-local date. Europe/Tirane is UTC+1 in winter and UTC+2 in
/// summer (2026: summer time from 29 Mar to 25 Oct), so local midnight is a different UTC hour on either side.
/// </summary>
public class StudioPeriodTests
{
    private const string Tirane = "Europe/Tirane";

    private static DateTime Utc(int y, int m, int d, int h) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WinterMonth_StartsAndEndsAtLocalMidnight_UtcPlusOne()
    {
        StudioPeriod p = StudioPeriod.Resolve(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), Tirane);

        p.FromUtc.Should().Be(Utc(2026, 10, 31, 23));
        p.ToUtcExclusive.Should().Be(Utc(2026, 11, 30, 23), "To includes the whole of 30 Nov, local");
        p.TimezoneId.Should().Be(Tirane);
    }

    [Fact]
    public void SummerDay_IsUtcPlusTwo()
    {
        StudioPeriod p = StudioPeriod.Resolve(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 1), Tirane);

        p.FromUtc.Should().Be(Utc(2026, 6, 30, 22));
        p.ToUtcExclusive.Should().Be(Utc(2026, 7, 1, 22));
    }

    [Fact]
    public void October_SpansTheEndOfSummerTime_EachEdgeUsesItsOwnOffset()
    {
        StudioPeriod p = StudioPeriod.Resolve(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Tirane);

        p.FromUtc.Should().Be(Utc(2026, 9, 30, 22), "1 Oct is still summer time, UTC+2");
        p.ToUtcExclusive.Should().Be(Utc(2026, 10, 31, 23), "1 Nov is winter time, UTC+1; a fixed offset would be an hour off");
    }

    [Fact]
    public void TheDaySummerTimeStarts_IsTwentyThreeHoursLong()
    {
        StudioPeriod p = StudioPeriod.Resolve(new DateOnly(2026, 3, 29), new DateOnly(2026, 3, 29), Tirane);

        p.FromUtc.Should().Be(Utc(2026, 3, 28, 23));
        p.ToUtcExclusive.Should().Be(Utc(2026, 3, 29, 22));
        (p.ToUtcExclusive - p.FromUtc).Should().Be(TimeSpan.FromHours(23));
    }

    [Fact]
    public void OpenEnds_StayOpen()
    {
        StudioPeriod p = StudioPeriod.Resolve(null, null, Tirane);

        p.FromUtc.Should().BeNull();
        p.ToUtcExclusive.Should().BeNull();
    }

    [Fact]
    public void NoTimezone_TreatsTheDatesAsUtc_AndSaysSo()
    {
        StudioPeriod p = StudioPeriod.Resolve(new DateOnly(2026, 11, 1), null, null);

        p.FromUtc.Should().Be(Utc(2026, 11, 1, 0));
        p.TimezoneId.Should().Be("UTC");
    }
}
