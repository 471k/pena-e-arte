using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetRevenueRetentionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetRevenueRetentionHandler CreateSut() => new(_db);

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
    public async Task Handle_EmptyLedger_ReturnsNullRates()
    {
        RevenueRetentionResponse result = await CreateSut().Handle(new GetRevenueRetentionQuery(), default);

        result.GrossRevenueRetention.Should().BeNull();
        result.NetRevenueRetention.Should().BeNull();
        result.StartMrr.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_OnlyNewSubscriptionsThisMonth_StartMrrZero_ReturnsNullRates()
    {
        Add(Guid.NewGuid(), RevenueEventType.New, MonthStart(0), 0m, 59m);
        await _db.SaveChangesAsync();

        RevenueRetentionResponse result = await CreateSut().Handle(new GetRevenueRetentionQuery(), default);

        result.GrossRevenueRetention.Should().BeNull();
        result.NetRevenueRetention.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoMovementThisMonth_BothRatesAreOneHundredPercent()
    {
        Add(Guid.NewGuid(), RevenueEventType.New, MonthStart(3), 0m, 59m);
        Add(Guid.NewGuid(), RevenueEventType.New, MonthStart(2), 0m, 100m);
        await _db.SaveChangesAsync();

        RevenueRetentionResponse result = await CreateSut().Handle(new GetRevenueRetentionQuery(), default);

        result.StartMrr.Should().Be(159m);
        result.GrossRevenueRetention.Should().Be(1.0);
        result.NetRevenueRetention.Should().Be(1.0);
    }

    [Fact]
    public async Task Handle_ContractionChurnAndExpansionThisMonth_ComputesGrrAndNrrAgainstStartMrr()
    {
        Guid contracts = Guid.NewGuid();
        Guid churns = Guid.NewGuid();
        Guid expands = Guid.NewGuid();
        Add(contracts, RevenueEventType.New, MonthStart(3), 0m, 59m);
        Add(churns, RevenueEventType.New, MonthStart(3), 0m, 100m);
        Add(expands, RevenueEventType.New, MonthStart(3), 0m, 50m);

        Add(contracts, RevenueEventType.Contraction, MonthStart(0), 59m, 49m);   // -10
        Add(churns, RevenueEventType.Churn, MonthStart(0), 100m, 0m);            // -100
        Add(expands, RevenueEventType.Expansion, MonthStart(0), 50m, 80m);       // +30
        await _db.SaveChangesAsync();

        RevenueRetentionResponse result = await CreateSut().Handle(new GetRevenueRetentionQuery(), default);

        result.StartMrr.Should().Be(209m);
        result.GrossRevenueRetention.Should().BeApproximately((209.0 - 10 - 100) / 209.0, 1e-9);
        result.NetRevenueRetention.Should().BeApproximately((209.0 + 30 - 10 - 100) / 209.0, 1e-9);
    }

    [Fact]
    public async Task Handle_NewAndReactivationAndSameMonthNewcomers_AreExcludedFromRetention()
    {
        Guid existing = Guid.NewGuid();
        Guid newcomer = Guid.NewGuid();
        Add(existing, RevenueEventType.New, MonthStart(2), 0m, 100m);

        Add(newcomer, RevenueEventType.New, MonthStart(0), 0m, 40m);                // new: excluded
        Add(newcomer, RevenueEventType.Expansion, MonthStart(0), 40m, 60m);         // not an existing customer: excluded
        Add(Guid.NewGuid(), RevenueEventType.Reactivation, MonthStart(0), 0m, 30m); // reactivation: excluded
        await _db.SaveChangesAsync();

        RevenueRetentionResponse result = await CreateSut().Handle(new GetRevenueRetentionQuery(), default);

        result.StartMrr.Should().Be(100m);
        result.GrossRevenueRetention.Should().Be(1.0);
        result.NetRevenueRetention.Should().Be(1.0);
    }
}
