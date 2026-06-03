using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

[Collection("Database")]
public class TrialExpiryWarningJobTests(DatabaseFixture fixture)
{
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private TrialExpiryWarningJob CreateSut(AppDbContext db) =>
        new(_notifications, db, NullLogger<TrialExpiryWarningJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_ValidStudio_SendsEmailToOwner()
    {
        Guid studioId = await SeedStudio("owner@mystudio.com");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await _notifications.Received(1).SendEmailAsync(
            "owner@mystudio.com",
            Arg.Is<string>(s => s.Contains("48 hours")),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ValidStudio_WritesSuccessNotificationLog()
    {
        Guid studioId = await SeedStudio("owner@success.com");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        NotificationLog? log = await verify.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
        log.StudioId.Should().Be(studioId);
    }

    [Fact]
    public async Task ExecuteAsync_EmailFails_WritesFailedNotificationLog()
    {
        Guid studioId = await SeedStudio("owner@fail.com");

        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        NotificationLog? log = await verify.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_EmailFails_DoesNotThrow()
    {
        Guid studioId = await SeedStudio("owner@nothrow.com");

        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        Func<Task> act = () => CreateSut(db).ExecuteAsync(studioId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownStudioId_DoesNotSendOrThrow()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        Func<Task> act = () => CreateSut(db).ExecuteAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
        await _notifications.DidNotReceive().SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_EmailBodyContainsStudioName()
    {
        Guid studioId = await SeedStudio("owner@body.com", name: "Tinta Viva");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("Tinta Viva")),
            Arg.Any<CancellationToken>());
    }

    private async Task<Guid> SeedStudio(string ownerEmail, string name = "Test Studio")
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);

        Studio studio = new()
        {
            Name           = name,
            Slug           = ("s-" + Guid.NewGuid().ToString("N"))[..20],
            City           = "Porto",
            OwnerEmail     = ownerEmail,
            IsActive       = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(2)
        };
        ctx.Studios.Add(studio);

        ctx.Subscriptions.Add(new Subscription
        {
            StudioId         = studio.Id,
            Status           = SubscriptionStatus.Trialing,
            TrialExpiresAt   = studio.TrialExpiresAt,
            GracePeriodEnd   = studio.TrialExpiresAt.AddDays(7),
            CurrentPeriodEnd = studio.TrialExpiresAt
        });

        await ctx.SaveChangesAsync();
        return studio.Id;
    }
}
