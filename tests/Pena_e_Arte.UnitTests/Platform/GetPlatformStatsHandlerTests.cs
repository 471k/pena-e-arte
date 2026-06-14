using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetPlatformStatsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPlatformStatsHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoData_ReturnsZeroStats()
    {
        PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

        result.TotalStudios.Should().Be(0);
        result.ActiveSubscriptions.Should().Be(0);
        result.TrialStudios.Should().Be(0);
        result.GracePeriodStudios.Should().Be(0);
        result.Mrr.Should().Be(0);
        result.TrialConversionRate.Should().Be(0);
        result.NewStudiosThisMonth.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithStudios_CountsOnlyNonSuspendedInTotal()
    {
        SeedStudio(isActive: true);
        SeedStudio(isActive: false, trialExpiresAt: DateTime.UtcNow.AddDays(-30));
        await _db.SaveChangesAsync();

        PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

        result.TotalStudios.Should().Be(1);
        result.TrialStudios.Should().Be(1); // active studio, no sub, trial still running
    }

    [Fact]
    public async Task Handle_WithActiveAndTrialSubscriptions_CountsBoth()
    {
        Studio s1 = SeedStudio(isActive: true);
        Studio s2 = SeedStudio(isActive: true);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = s1.Id,
            Status           = SubscriptionStatus.Active,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(30),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = s2.Id,
            Status           = SubscriptionStatus.Trialing,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(7),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(7),
        });
        await _db.SaveChangesAsync();

        PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

        result.ActiveSubscriptions.Should().Be(1);
        result.TrialStudios.Should().Be(1);
    }

    [Fact]
    public async Task Handle_MrrCalculation_SumsOnlyActiveSubscriptionPlanPrices()
    {
        Studio active   = SeedStudio(isActive: true);
        Studio trialing = SeedStudio(isActive: true);
        Plan   plan     = new() { Name = "Pro", BillingInterval = BillingInterval.Monthly, PriceMonthly = 49m, PriceYearly = 490m };
        _db.Plans.Add(plan);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = active.Id,
            PlanId           = plan.Id,
            Status           = SubscriptionStatus.Active,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(30),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId         = trialing.Id,
            PlanId           = plan.Id,
            Status           = SubscriptionStatus.Trialing,
            TrialExpiresAt   = DateTime.UtcNow.AddDays(7),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(7),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

        result.Mrr.Should().Be(49m);
    }

    [Fact]
    public async Task Handle_TrialConversionRate_IsActiveOverActivePlusTrialPlusGrace()
    {
        Studio active = SeedStudio(isActive: true, trialExpiresAt: DateTime.UtcNow.AddDays(-30));
        Studio trial  = SeedStudio(isActive: true, trialExpiresAt: DateTime.UtcNow.AddDays(-30));
        Studio grace  = SeedStudio(isActive: true, trialExpiresAt: DateTime.UtcNow.AddDays(-30));
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription { StudioId = active.Id, Status = SubscriptionStatus.Active,      CurrentPeriodEnd = DateTime.UtcNow.AddDays(30) });
        _db.Subscriptions.Add(new Subscription { StudioId = trial.Id,  Status = SubscriptionStatus.Trialing,    CurrentPeriodEnd = DateTime.UtcNow.AddDays(7) });
        _db.Subscriptions.Add(new Subscription { StudioId = grace.Id,  Status = SubscriptionStatus.GracePeriod, CurrentPeriodEnd = DateTime.UtcNow.AddDays(7) });
        await _db.SaveChangesAsync();

        PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

        result.GracePeriodStudios.Should().Be(1);
        result.TrialConversionRate.Should().BeApproximately(1.0 / 3.0, 0.001);
    }

    [Fact]
    public async Task Handle_NewStudiosThisMonth_CountsOnlyCurrentCalendarMonth()
    {
        SeedStudio(isActive: true); // CreatedAt defaults to now
        _db.Studios.Add(new Studio
        {
            Name           = "Old Studio",
            Slug           = Guid.NewGuid().ToString("N")[..20],
            City           = "Porto",
            OwnerEmail     = "old@test.com",
            IsActive       = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(-60),
            CreatedAt      = DateTime.UtcNow.AddMonths(-2),
        });
        await _db.SaveChangesAsync();

        PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

        result.NewStudiosThisMonth.Should().Be(1);
    }

    private Studio SeedStudio(bool isActive, DateTime? trialExpiresAt = null)
    {
        Studio studio = new()
        {
            Name       = $"Studio-{Guid.NewGuid():N}"[..20],
            Slug       = Guid.NewGuid().ToString("N")[..20],
            City       = "Porto",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive   = isActive,
            TrialExpiresAt = trialExpiresAt ?? DateTime.UtcNow.AddDays(14),
        };
        _db.Studios.Add(studio);
        return studio;
    }
}
