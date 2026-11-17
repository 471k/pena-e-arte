using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

[Collection("Database")]
public class TrialExpiryJobTests(DatabaseFixture fixture)
{
    private readonly ListLogger<TrialExpiryJob> _logger = new();

    private TrialExpiryJob CreateSut(AppDbContext db) => new(db, _logger);

    [Fact]
    public async Task ExecuteAsync_TrialingSubscription_TransitionsToGracePeriod()
    {
        Guid studioId = await SeedSubscription(SubscriptionStatus.Trialing);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Subscription? sub = await verify.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == studioId);
        sub!.Status.Should().Be(SubscriptionStatus.GracePeriod);
        _logger.Messages(LogLevel.Information).Should().ContainSingle()
            .Which.Should().Be($"TrialExpiryJob {studioId}: trial ended, moved to GracePeriod");
    }

    [Fact]
    public async Task ExecuteAsync_GracePeriodSubscription_DoesNotChangeStatus()
    {
        Guid studioId = await SeedSubscription(SubscriptionStatus.GracePeriod);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Subscription? sub = await verify.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == studioId);
        sub!.Status.Should().Be(SubscriptionStatus.GracePeriod);
        // Scheduled once per trial: the skip line is what says it ran and chose not to act.
        _logger.Messages(LogLevel.Information).Should().ContainSingle()
            .Which.Should().Be($"TrialExpiryJob {studioId}: skipped, status is GracePeriod");
    }

    [Fact]
    public async Task ExecuteAsync_ActiveSubscription_DoesNotChangeStatus()
    {
        Guid studioId = await SeedSubscription(SubscriptionStatus.Active);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Subscription? sub = await verify.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == studioId);
        sub!.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownStudioId_DoesNotThrow()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        Func<Task> act = () => CreateSut(db).ExecuteAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_OnlyTargetsSpecifiedStudio_OtherTrialingSubscriptionsUnchanged()
    {
        Guid targetStudio = await SeedSubscription(SubscriptionStatus.Trialing);
        Guid otherStudio = await SeedSubscription(SubscriptionStatus.Trialing);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(targetStudio);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Subscription? otherSub = await verify.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == otherStudio);
        otherSub!.Status.Should().Be(SubscriptionStatus.Trialing);
    }

    private async Task<Guid> SeedSubscription(SubscriptionStatus status)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);

        Studio studio = new()
        {
            Name = $"Studio {Guid.NewGuid():N}"[..20],
            Slug = $"tj-{Guid.NewGuid():N}"[..20],
            City = "Porto",
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(-1)
        };
        ctx.Studios.Add(studio);
        await ctx.SaveChangesAsync();

        ctx.Subscriptions.Add(new Subscription
        {
            StudioId = studio.Id,
            Status = status,
            TrialExpiresAt = DateTime.UtcNow.AddDays(-1),
            GracePeriodEnd = DateTime.UtcNow.AddDays(6),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1)
        });
        await ctx.SaveChangesAsync();
        return studio.Id;
    }
}
