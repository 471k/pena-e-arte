using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetMrrHistoryHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetMrrHistoryHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoSubscriptions_ReturnsZeroMrrForEveryMonth()
    {
        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(3), default);

        result.Should().HaveCount(3);
        result.Should().OnlyContain(p => p.Mrr == 0m);
    }

    [Fact]
    public async Task Handle_MonthsClamped_StaysWithinOneToTwentyFour()
    {
        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(999), default);

        result.Should().HaveCount(24);
    }

    [Fact]
    public async Task Handle_MonthlySubscription_ContributesFullPriceToCurrentMonth()
    {
        Studio studio = SeedStudio();
        Plan   plan   = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = studio.Id,
            PlanId           = plan.Id,
            BillingInterval  = BillingInterval.Monthly,
            Status           = SubscriptionStatus.Active,
            CreatedAt        = DateTime.UtcNow.AddDays(-5),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(25),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(1), default);

        result.Single().Mrr.Should().Be(49m);
    }

    [Fact]
    public async Task Handle_YearlySubscription_ContributesMonthlyEquivalent()
    {
        // Regression test for the confirmed pre-existing revenue-reporting bug — same
        // fix as GetPlatformStatsHandler's MRR calculation.
        Studio studio = SeedStudio();
        Plan   plan   = new() { Name = "Premium" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 79m });
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Yearly, Price = 790m });
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = studio.Id,
            PlanId           = plan.Id,
            BillingInterval  = BillingInterval.Yearly,
            Status           = SubscriptionStatus.Active,
            CreatedAt        = DateTime.UtcNow.AddDays(-5),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(360),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        List<MrrDataPointResponse> result = await CreateSut().Handle(new GetMrrHistoryQuery(1), default);

        result.Single().Mrr.Should().BeApproximately(790m / 12m, 0.01m);
    }

    private Studio SeedStudio()
    {
        Studio studio = new()
        {
            Name           = $"Studio-{Guid.NewGuid():N}"[..20],
            Slug           = Guid.NewGuid().ToString("N")[..20],
            City           = "Porto",
            OwnerEmail     = $"{Guid.NewGuid():N}@test.com",
            IsActive       = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        _db.Studios.Add(studio);
        return studio;
    }
}
