using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class PlatformStatsIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetPlatformStats_WithMixedStudios_ReturnsCorrectCounts()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        Studio activeStudio = SeedStudio(isActive: true);
        Studio suspendedStudio = SeedStudio(isActive: false);
        db.Studios.AddRange(activeStudio, suspendedStudio);
        await db.SaveChangesAsync();

        Plan plan = new() { Name = $"Pro-{Guid.NewGuid():N}" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        Plan freePlan = new() { Name = $"Free-{Guid.NewGuid():N}" };
        freePlan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 0m });
        db.Plans.AddRange(plan, freePlan);

        Studio freeStudio = SeedStudio(isActive: true);
        Studio pastDueStudio = SeedStudio(isActive: true);
        Studio churningStudio = SeedStudio(isActive: true);
        db.Studios.AddRange(freeStudio, pastDueStudio, churningStudio);
        await db.SaveChangesAsync();

        db.Subscriptions.AddRange(
            new Subscription
            {
                StudioId = activeStudio.Id,
                PlanId = plan.Id,
                BillingInterval = BillingInterval.Monthly,
                Status = SubscriptionStatus.Active,
                TrialExpiresAt = DateTime.UtcNow.AddDays(30),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            },
            new Subscription
            {
                StudioId = freeStudio.Id,
                PlanId = freePlan.Id,
                BillingInterval = BillingInterval.Monthly,
                Status = SubscriptionStatus.Active,
                TrialExpiresAt = DateTime.UtcNow.AddDays(-30),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            },
            new Subscription
            {
                StudioId = pastDueStudio.Id,
                PlanId = plan.Id,
                BillingInterval = BillingInterval.Monthly,
                Status = SubscriptionStatus.PastDue,
                TrialExpiresAt = DateTime.UtcNow.AddDays(-30),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(-2),
            },
            new Subscription
            {
                StudioId = churningStudio.Id,
                PlanId = plan.Id,
                BillingInterval = BillingInterval.Monthly,
                Status = SubscriptionStatus.Active,
                CancelAtPeriodEnd = true,
                TrialExpiresAt = DateTime.UtcNow.AddDays(-30),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
            });
        await db.SaveChangesAsync();

        await using AppDbContext readDb = fixture.CreateDbContext(Guid.Empty);
        GetPlatformStatsHandler handler = new(readDb);

        PlatformStatsResponse result = await handler.Handle(new GetPlatformStatsQuery(), default);

        result.TotalStudios.Should().BeGreaterThanOrEqualTo(1);
        result.ActiveSubscriptions.Should().BeGreaterThanOrEqualTo(1);
        result.NewStudiosThisMonth.Should().BeGreaterThanOrEqualTo(2);
        result.Mrr.Should().BeGreaterThanOrEqualTo(49m + 49m); // activeStudio + churningStudio (still billing until period end)
        result.TrialConversionRate.Should().BeInRange(0, 1);

        // D4/D5/D6: the new revenue-reporting figures.
        result.PayingStudios.Should().BeGreaterThanOrEqualTo(2); // activeStudio + churningStudio — freeStudio excluded (0 price)
        result.AtRiskMrr.Should().BeGreaterThanOrEqualTo(49m);   // pastDueStudio
        result.ScheduledChurnMrr.Should().BeGreaterThanOrEqualTo(49m); // churningStudio
    }

    private static Studio SeedStudio(bool isActive) => new()
    {
        Name = $"Stats Studio {Guid.NewGuid():N}"[..30],
        Slug = Guid.NewGuid().ToString("N")[..20],
        City = "Porto",
        OwnerEmail = $"{Guid.NewGuid():N}@test.com",
        IsActive = isActive,
        TrialExpiresAt = DateTime.UtcNow.AddDays(14),
    };
}
