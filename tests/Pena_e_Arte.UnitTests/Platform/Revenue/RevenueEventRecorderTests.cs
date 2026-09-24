using FluentAssertions;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform.Revenue;

public class RevenueEventRecorderTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private static Subscription NewSubscription(SubscriptionStatus status = SubscriptionStatus.Active) => new()
    {
        StudioId = Guid.NewGuid(),
        PlanId = Guid.NewGuid(),
        Status = status,
        BillingInterval = BillingInterval.Monthly,
        BilledUnitAmount = 59m,
        BilledQuantity = 1,
        BilledCurrency = "eur",
    };

    [Fact]
    public async Task Record_UnchangedAmount_AmountType_AddsNoRow()
    {
        Subscription sub = NewSubscription();

        RevenueEventRecorder.Record(_db, sub, 59m, 59m, RevenueEventType.Expansion, "test", null);
        await _db.SaveChangesAsync();

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RevenueEventType.PastDue)]
    [InlineData(RevenueEventType.Recovered)]
    [InlineData(RevenueEventType.Paused)]
    [InlineData(RevenueEventType.Resumed)]
    public async Task Record_UnchangedAmount_StateOnlyType_StillAddsRow(RevenueEventType type)
    {
        Subscription sub = NewSubscription();

        RevenueEventRecorder.Record(_db, sub, 59m, 59m, type, "test", null);
        await _db.SaveChangesAsync();

        SubscriptionRevenueEvent row = _db.SubscriptionRevenueEvents.Single();
        row.Type.Should().Be(type);
        row.MrrBefore.Should().Be(59m);
        row.MrrAfter.Should().Be(59m);
    }

    [Fact]
    public async Task Record_CopiesSubscriptionFacts_AndUsesRealStripeEventIdWhenGiven()
    {
        Subscription sub = NewSubscription();

        RevenueEventRecorder.Record(_db, sub, 0m, 59m, RevenueEventType.New, "SomeHandler", "evt_123");
        await _db.SaveChangesAsync();

        SubscriptionRevenueEvent row = _db.SubscriptionRevenueEvents.Single();
        row.SubscriptionId.Should().Be(sub.Id);
        row.StudioId.Should().Be(sub.StudioId);
        row.PlanId.Should().Be(sub.PlanId);
        row.Interval.Should().Be(BillingInterval.Monthly);
        row.Source.Should().Be("SomeHandler");
        row.StripeEventId.Should().Be("evt_123");
    }

    [Fact]
    public async Task Record_NoStripeEventId_BuildsDeterministicSyntheticKeyFromOccurredAt()
    {
        Subscription sub = NewSubscription();
        DateTime at = new(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        RevenueEventRecorder.Record(_db, sub, 0m, 59m, RevenueEventType.New, "SomeHandler", null, at);
        await _db.SaveChangesAsync();

        _db.SubscriptionRevenueEvents.Single().StripeEventId
            .Should().Be($"SomeHandler:{sub.Id}:New:{at:O}");
    }

    [Fact]
    public void MrrBeforeActivation_OnlyCountsAnAlreadyBillingSubscription()
    {
        RevenueEventRecorder.MrrBeforeActivation(NewSubscription(SubscriptionStatus.Active)).Should().Be(59m);
        RevenueEventRecorder.MrrBeforeActivation(NewSubscription(SubscriptionStatus.Cancelled)).Should().Be(0m);
        RevenueEventRecorder.MrrBeforeActivation(NewSubscription(SubscriptionStatus.Trialing)).Should().Be(0m);
        RevenueEventRecorder.MrrBeforeActivation(NewSubscription(SubscriptionStatus.GracePeriod)).Should().Be(0m);
    }

    [Fact]
    public async Task RecordActivationAsync_FirstTime_IsNew_ThenReactivationAfterwards()
    {
        Subscription sub = NewSubscription(SubscriptionStatus.Cancelled);
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();

        await RevenueEventRecorder.RecordActivationAsync(_db, sub, 0m, 59m, "test", default);
        await _db.SaveChangesAsync();
        await RevenueEventRecorder.RecordActivationAsync(_db, sub, 0m, 59m, "test", default);
        await _db.SaveChangesAsync();

        _db.SubscriptionRevenueEvents.OrderBy(e => e.CreatedAt).Select(e => e.Type)
            .Should().Equal(RevenueEventType.New, RevenueEventType.Reactivation);
    }

    [Fact]
    public async Task RecordActivationAsync_AlreadyBilling_IsExpansionOrContractionNotNew()
    {
        Subscription up = NewSubscription();
        Subscription down = NewSubscription();
        _db.Subscriptions.AddRange(up, down);
        await _db.SaveChangesAsync();

        await RevenueEventRecorder.RecordActivationAsync(_db, up, 59m, 79m, "test", default);
        await RevenueEventRecorder.RecordActivationAsync(_db, down, 79m, 59m, "test", default);
        await _db.SaveChangesAsync();

        _db.SubscriptionRevenueEvents.Single(e => e.SubscriptionId == up.Id).Type.Should().Be(RevenueEventType.Expansion);
        _db.SubscriptionRevenueEvents.Single(e => e.SubscriptionId == down.Id).Type.Should().Be(RevenueEventType.Contraction);
    }

    [Fact]
    public async Task RecordActivationAsync_FreePlan_ZeroToZero_AddsNoRow()
    {
        Subscription sub = NewSubscription(SubscriptionStatus.Trialing);
        sub.BilledUnitAmount = 0m;

        await RevenueEventRecorder.RecordActivationAsync(_db, sub, 0m, 0m, "test", default);
        await _db.SaveChangesAsync();

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task LatestTypeAsync_ReturnsMostRecentByOccurredAtThenInsertion_OrNullWhenEmpty()
    {
        Subscription sub = NewSubscription();
        (await RevenueEventRecorder.LatestTypeAsync(_db, sub.Id, default)).Should().BeNull();

        RevenueEventRecorder.Record(_db, sub, 0m, 59m, RevenueEventType.New, "t", null, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        RevenueEventRecorder.Record(_db, sub, 59m, 59m, RevenueEventType.Paused, "t", null, new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc));
        await _db.SaveChangesAsync();

        (await RevenueEventRecorder.LatestTypeAsync(_db, sub.Id, default)).Should().Be(RevenueEventType.Paused);
    }
}
