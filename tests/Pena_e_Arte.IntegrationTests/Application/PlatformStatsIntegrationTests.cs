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

        Plan plan = new() { Name = "Pro" };
        plan.Prices.Add(new PlanPrice { Interval = BillingInterval.Monthly, Price = 49m });
        db.Plans.Add(plan);
        db.Subscriptions.Add(new Subscription
        {
            StudioId = activeStudio.Id,
            PlanId = plan.Id,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            TrialExpiresAt = DateTime.UtcNow.AddDays(30),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
        });
        await db.SaveChangesAsync();

        await using AppDbContext readDb = fixture.CreateDbContext(Guid.Empty);
        GetPlatformStatsHandler handler = new(readDb);

        PlatformStatsResponse result = await handler.Handle(new GetPlatformStatsQuery(), default);

        result.TotalStudios.Should().BeGreaterThanOrEqualTo(1);
        result.ActiveSubscriptions.Should().BeGreaterThanOrEqualTo(1);
        result.NewStudiosThisMonth.Should().BeGreaterThanOrEqualTo(2);
        result.Mrr.Should().BeGreaterThanOrEqualTo(49m);
        result.TrialConversionRate.Should().BeInRange(0, 1);
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
