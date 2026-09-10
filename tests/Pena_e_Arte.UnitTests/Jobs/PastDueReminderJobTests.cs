using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class PastDueReminderJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ILogger<PastDueReminderJob> _logger = Substitute.For<ILogger<PastDueReminderJob>>();

    private PastDueReminderJob CreateSut() => new(_db, _notifications, _logger);

    private async Task<Studio> SeedPastDueStudioAsync(
        int daysPastDue, bool dunningExcluded = false)
    {
        Studio studio = new()
        {
            Name = "Ink & Iron",
            Slug = $"ink-iron-{Guid.NewGuid():N}",
            City = "Porto",
            OwnerEmail = $"owner-{Guid.NewGuid():N}@example.com",
        };
        _db.Studios.Add(studio);

        Subscription subscription = new()
        {
            StudioId = studio.Id,
            Status = SubscriptionStatus.PastDue,
            PastDueSince = DateTime.UtcNow.AddDays(-daysPastDue),
            DunningExcludedManually = dunningExcluded,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-daysPastDue),
            GracePeriodEnd = DateTime.UtcNow.AddDays(7),
        };
        _db.Subscriptions.Add(subscription);

        await _db.SaveChangesAsync();
        return studio;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public async Task RunAsync_ExactDayMatch_SendsReminderEmail(int daysPastDue)
    {
        Studio studio = await SeedPastDueStudioAsync(daysPastDue);

        await CreateSut().RunAsync();

        await _notifications.Received(1).SendEmailAsync(
            studio.OwnerEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    public async Task RunAsync_NonThresholdDay_DoesNotSendReminderEmail(int daysPastDue)
    {
        await SeedPastDueStudioAsync(daysPastDue);

        await CreateSut().RunAsync();

        await _notifications.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_DunningExcludedManually_DoesNotSendEvenOnThresholdDay()
    {
        await SeedPastDueStudioAsync(3, dunningExcluded: true);

        await CreateSut().RunAsync();

        await _notifications.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NotPastDue_DoesNotSend()
    {
        Studio studio = new() { Name = "Active Studio", Slug = "active-studio", City = "Lisbon", OwnerEmail = "a@b.com" };
        _db.Studios.Add(studio);
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studio.Id,
            Status = SubscriptionStatus.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
            GracePeriodEnd = DateTime.UtcNow.AddDays(27),
        });
        await _db.SaveChangesAsync();

        await CreateSut().RunAsync();

        await _notifications.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ThresholdDay_WritesNotificationLog()
    {
        Studio studio = await SeedPastDueStudioAsync(1);

        await CreateSut().RunAsync();

        _db.NotificationLogs.Should().ContainSingle(n => n.StudioId == studio.Id && n.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_MultiplePastDueStudiosOnThresholdDays_SendsToEach()
    {
        Studio studioA = await SeedPastDueStudioAsync(1);
        Studio studioB = await SeedPastDueStudioAsync(7);

        await CreateSut().RunAsync();

        await _notifications.Received(1).SendEmailAsync(
            studioA.OwnerEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).SendEmailAsync(
            studioB.OwnerEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
