using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class BackfillRevenueLedgerHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private BackfillRevenueLedgerHandler CreateSut() =>
        new(_db, NullLogger<BackfillRevenueLedgerHandler>.Instance);

    private static readonly DateTime Signup = DateTime.UtcNow.AddMonths(-4);

    private async Task<Subscription> SeedSubscription(
        SubscriptionStatus status = SubscriptionStatus.Active,
        decimal? billedAmount = 59m,
        bool studioActive = true,
        BillingInterval interval = BillingInterval.Monthly)
    {
        Studio studio = new()
        {
            Name = $"Studio-{Guid.NewGuid():N}"[..20],
            Slug = Guid.NewGuid().ToString("N")[..20],
            City = "Porto",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive = studioActive,
            TrialExpiresAt = Signup,
        };
        _db.Studios.Add(studio);
        Plan plan = new() { Name = "Pro" };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        Subscription subscription = new()
        {
            StudioId = studio.Id,
            PlanId = plan.Id,
            Status = status,
            BillingInterval = interval,
            BilledUnitAmount = billedAmount,
            BilledQuantity = billedAmount is null ? null : 1,
            BilledCurrency = billedAmount is null ? null : "eur",
            CreatedAt = Signup.AddDays(-10),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
        };
        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return subscription;
    }

    [Fact]
    public async Task Handle_ActivePaidSubscriptionWithNoLedgerRow_WritesOneNewEventAtBillingWindowStart()
    {
        Subscription sub = await SeedSubscription();

        BackfillRevenueLedgerResponse result = await CreateSut().Handle(new BackfillRevenueLedgerCommand(), default);

        result.Should().Be(new BackfillRevenueLedgerResponse(1, 0, 0));
        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.SubscriptionId.Should().Be(sub.Id);
        ledgerEvent.Type.Should().Be(RevenueEventType.New);
        ledgerEvent.MrrBefore.Should().Be(0m);
        ledgerEvent.MrrAfter.Should().Be(59m);
        ledgerEvent.Source.Should().Be("Backfill");
        // Window start = the later of Subscription.CreatedAt and the studio's trial expiry.
        ledgerEvent.OccurredAt.Should().BeCloseTo(Signup, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_YearlySubscription_WritesMonthlyEquivalentMrr()
    {
        await SeedSubscription(billedAmount: 590m, interval: BillingInterval.Yearly);

        await CreateSut().Handle(new BackfillRevenueLedgerCommand(), default);

        _db.SubscriptionRevenueEvents.Single().MrrAfter.Should().BeApproximately(49.17m, 0.01m);
    }

    [Fact]
    public async Task Handle_SubscriptionThatAlreadyHasAnyLedgerRow_IsUntouched()
    {
        Subscription sub = await SeedSubscription();
        _db.SubscriptionRevenueEvents.Add(new SubscriptionRevenueEvent
        {
            SubscriptionId = sub.Id,
            StudioId = sub.StudioId,
            OccurredAt = DateTime.UtcNow.AddDays(-3),
            Type = RevenueEventType.Expansion,
            MrrBefore = 29m,
            MrrAfter = 59m,
            Source = "ChangePlanHandler",
            StripeEventId = "existing",
        });
        await _db.SaveChangesAsync();

        BackfillRevenueLedgerResponse result = await CreateSut().Handle(new BackfillRevenueLedgerCommand(), default);

        result.Should().Be(new BackfillRevenueLedgerResponse(0, 1, 0));
        _db.SubscriptionRevenueEvents.Single().Source.Should().Be("ChangePlanHandler");
    }

    [Theory]
    [InlineData(SubscriptionStatus.Cancelled)]
    [InlineData(SubscriptionStatus.Trialing)]
    [InlineData(SubscriptionStatus.GracePeriod)]
    [InlineData(SubscriptionStatus.PastDue)]
    public async Task Handle_SubscriptionNotCurrentlyActive_GetsNoEvent(SubscriptionStatus status)
    {
        await SeedSubscription(status);

        BackfillRevenueLedgerResponse result = await CreateSut().Handle(new BackfillRevenueLedgerCommand(), default);

        result.Created.Should().Be(0);
        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ActiveButZeroMrr_GetsNoEvent()
    {
        await SeedSubscription(billedAmount: 0m);

        await CreateSut().Handle(new BackfillRevenueLedgerCommand(), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Rerun_IsANoOp()
    {
        await SeedSubscription();

        await CreateSut().Handle(new BackfillRevenueLedgerCommand(), default);
        _db.ChangeTracker.Clear();
        BackfillRevenueLedgerResponse second = await CreateSut().Handle(new BackfillRevenueLedgerCommand(), default);

        second.Should().Be(new BackfillRevenueLedgerResponse(0, 1, 0));
        _db.SubscriptionRevenueEvents.Count().Should().Be(1);
    }

    [Fact]
    public async Task Handle_MixedPortfolio_LedgerMrrNowEqualsMrrRulesMrrNow()
    {
        await SeedSubscription(billedAmount: 59m);
        await SeedSubscription(billedAmount: 590m, interval: BillingInterval.Yearly);
        await SeedSubscription(billedAmount: 79m, studioActive: false); // suspended: New + Paused
        await SeedSubscription(SubscriptionStatus.PastDue, billedAmount: 49m);
        await SeedSubscription(SubscriptionStatus.Cancelled, billedAmount: 29m);

        await CreateSut().Handle(new BackfillRevenueLedgerCommand(), default);
        _db.ChangeTracker.Clear();

        DateTime now = DateTime.UtcNow;
        decimal fromLedger = RevenueLedgerRules.MrrAt(_db.SubscriptionRevenueEvents.ToList(), now);
        decimal fromRules = MrrRules.MrrAt(await MrrInputLoader.LoadAsync(_db, default), now, now);

        fromLedger.Should().Be(fromRules);
        fromRules.Should().BeGreaterThan(0m);
    }

    [Fact]
    public async Task Handle_SuspendedActiveSubscription_WritesNewThenPaused()
    {
        await SeedSubscription(studioActive: false);

        await CreateSut().Handle(new BackfillRevenueLedgerCommand(), default);

        _db.SubscriptionRevenueEvents.OrderBy(e => e.OccurredAt).ThenBy(e => e.CreatedAt).Select(e => e.Type)
            .Should().Equal(RevenueEventType.New, RevenueEventType.Paused);
    }

    [Fact]
    public async Task Handle_PopulatesTheCommandCountsTheAuditRowIsBuiltFrom()
    {
        await SeedSubscription();                                   // seeded
        await SeedSubscription(SubscriptionStatus.Cancelled);        // not billing
        BackfillRevenueLedgerCommand command = new();

        await CreateSut().Handle(command, default);

        command.Created.Should().Be(1);
        command.SkippedAlreadyInLedger.Should().Be(0);
        command.SkippedNotBilling.Should().Be(1);
    }
}
