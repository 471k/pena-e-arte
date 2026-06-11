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
        result.SuspendedStudios.Should().Be(0);
        result.MonthlyRecurringRevenue.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithStudios_CountsCorrectly()
    {
        Studio active = SeedStudio(isActive: true);
        Studio suspended = SeedStudio(isActive: false);
        await _db.SaveChangesAsync();

        PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

        result.TotalStudios.Should().Be(2);
        result.SuspendedStudios.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithActiveAndTrialSubscriptions_CountsBoth()
    {
        Studio s1 = SeedStudio(isActive: true);
        Studio s2 = SeedStudio(isActive: true);
        await _db.SaveChangesAsync();

        _db.Subscriptions.Add(new Subscription
        {
            StudioId       = s1.Id,
            Status         = SubscriptionStatus.Active,
            TrialExpiresAt = DateTime.UtcNow.AddDays(30),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
        });
        _db.Subscriptions.Add(new Subscription
        {
            StudioId       = s2.Id,
            Status         = SubscriptionStatus.Trialing,
            TrialExpiresAt = DateTime.UtcNow.AddDays(7),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(7),
        });
        await _db.SaveChangesAsync();

        PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

        result.ActiveSubscriptions.Should().Be(1);
        result.TrialStudios.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WithReferralCodes_CountsActiveOnly()
    {
        Studio studio = SeedStudio(isActive: true);
        await _db.SaveChangesAsync();

        _db.ReferralCodes.Add(new ReferralCode { StudioId = studio.Id, Code = "CODE0001", IsActive = true });
        _db.ReferralCodes.Add(new ReferralCode { StudioId = studio.Id, Code = "CODE0002", IsActive = false });
        await _db.SaveChangesAsync();

        PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

        result.TotalReferralCodes.Should().Be(2);
        result.ActiveReferralCodes.Should().Be(1);
    }

    private Studio SeedStudio(bool isActive)
    {
        Studio studio = new()
        {
            Name       = $"Studio-{Guid.NewGuid():N}"[..20],
            Slug       = Guid.NewGuid().ToString("N")[..20],
            City       = "Porto",
            OwnerEmail = $"{Guid.NewGuid():N}@test.com",
            IsActive   = isActive,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        _db.Studios.Add(studio);
        return studio;
    }
}
