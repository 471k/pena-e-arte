using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetMrrMovementsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetMrrMovementsHandler CreateSut() => new(_db);

    private static DateTime MonthStart(int monthsAgo)
    {
        DateTime now = DateTime.UtcNow;
        return new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-monthsAgo);
    }

    private void Add(Guid subscriptionId, RevenueEventType type, DateTime at, decimal before, decimal after) =>
        _db.SubscriptionRevenueEvents.Add(new SubscriptionRevenueEvent
        {
            SubscriptionId = subscriptionId,
            StudioId = Guid.NewGuid(),
            OccurredAt = at,
            Type = type,
            MrrBefore = before,
            MrrAfter = after,
            Source = "test",
            StripeEventId = Guid.NewGuid().ToString(),
        });

    [Fact]
    public async Task Handle_EmptyLedger_ReturnsZerosForEveryMonth()
    {
        List<MrrMovementsDataPointResponse> result = await CreateSut().Handle(new GetMrrMovementsQuery(3), default);

        result.Should().HaveCount(3);
        result.Should().OnlyContain(p => p.New == 0m && p.Expansion == 0m && p.Reactivation == 0m
                                         && p.Contraction == 0m && p.Churn == 0m && p.Net == 0m);
    }

    [Fact]
    public async Task Handle_MonthsClamped_StaysWithinOneToTwentyFour()
    {
        (await CreateSut().Handle(new GetMrrMovementsQuery(999), default)).Should().HaveCount(24);
        (await CreateSut().Handle(new GetMrrMovementsQuery(0), default)).Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_GrowthToPremiumAndBack_MatchesSpecWorkedExample()
    {
        // The spec Feb-May scenario, shifted to the last four calendar months:
        //   M-3 (Feb): Growth monthly 59 starts        -> New 59
        //   M-2 (Mar): upgrade to Premium 79            -> Expansion +20
        //   M-1 (Apr): nothing
        //   M-0 (May): scheduled downgrade lands        -> Contraction -20
        Guid sub = Guid.NewGuid();
        Add(sub, RevenueEventType.New, MonthStart(3).AddDays(1), 0m, 59m);
        Add(sub, RevenueEventType.Expansion, MonthStart(2).AddDays(9), 59m, 79m);
        Add(sub, RevenueEventType.Contraction, MonthStart(0), 79m, 59m);
        await _db.SaveChangesAsync();

        List<MrrMovementsDataPointResponse> result = await CreateSut().Handle(new GetMrrMovementsQuery(4), default);

        result.Select(p => p.Month).Should().Equal(
            MonthStart(3).ToString("yyyy-MM"), MonthStart(2).ToString("yyyy-MM"),
            MonthStart(1).ToString("yyyy-MM"), MonthStart(0).ToString("yyyy-MM"));
        result[0].Should().Match<MrrMovementsDataPointResponse>(p => p.New == 59m && p.Net == 59m);
        result[1].Should().Match<MrrMovementsDataPointResponse>(p => p.Expansion == 20m && p.Net == 20m);
        result[2].Net.Should().Be(0m);
        result[3].Should().Match<MrrMovementsDataPointResponse>(p => p.Contraction == -20m && p.Net == -20m);
    }

    [Fact]
    public async Task Handle_MonthsBeforeTheLedgerStarts_AreAllZeros()
    {
        Add(Guid.NewGuid(), RevenueEventType.New, MonthStart(1).AddDays(3), 0m, 49m);
        await _db.SaveChangesAsync();

        List<MrrMovementsDataPointResponse> result = await CreateSut().Handle(new GetMrrMovementsQuery(4), default);

        result[0].Net.Should().Be(0m);
        result[1].Net.Should().Be(0m);
        result[2].New.Should().Be(49m);
        result[3].Net.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_MixedMovementsInOneMonth_AreSummedPerType()
    {
        DateTime at = MonthStart(1).AddDays(5);
        Add(Guid.NewGuid(), RevenueEventType.New, at, 0m, 59m);
        Add(Guid.NewGuid(), RevenueEventType.New, at, 0m, 29m);
        Add(Guid.NewGuid(), RevenueEventType.Churn, at, 49m, 0m);
        Add(Guid.NewGuid(), RevenueEventType.Reactivation, at, 0m, 30m);
        Add(Guid.NewGuid(), RevenueEventType.Paused, at, 59m, 59m);   // state-only: must not appear
        await _db.SaveChangesAsync();

        MrrMovementsDataPointResponse month =
            (await CreateSut().Handle(new GetMrrMovementsQuery(2), default))[0];

        month.New.Should().Be(88m);
        month.Churn.Should().Be(-49m);
        month.Reactivation.Should().Be(30m);
        month.Net.Should().Be(88m - 49m + 30m);
    }
}
