using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class SendStudioRegisteredNotificationHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();

    public SendStudioRegisteredNotificationHandlerTests()
    {
        _appSettings.BaseUrl.Returns("https://app.tattooos.co");
        _emailRenderer.RenderStudioRegisteredAdmin(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(),
            Arg.Any<DateTime>(), Arg.Any<bool>(), Arg.Any<string>())
            .Returns("<html>admin notice</html>");
    }

    private SendStudioRegisteredNotificationHandler CreateSut() =>
        new(_db, _emailRenderer, _notifications, _identity, _realtime, _appSettings,
            NullLogger<SendStudioRegisteredNotificationHandler>.Instance);

    private Studio SeedStudio()
    {
        Studio studio = new()
        {
            Name = "Ink & Soul",
            Slug = "ink-and-soul",
            City = "Tirana",
            OwnerEmail = "owner@ink-and-soul.test",
            Nipt = "L01234567A",
        };
        _db.Studios.Add(studio);
        return studio;
    }

    [Fact]
    public async Task Handle_StudioNotFound_DoesNotThrowAndSendsNoEmail()
    {
        Func<Task> act = () => CreateSut().Handle(
            new SendStudioRegisteredNotificationCommand(Guid.NewGuid()), default);

        await act.Should().NotThrowAsync();
        await _notifications.DidNotReceiveWithAnyArgs()
            .SendEmailAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Handle_NoAdminAccounts_DoesNotThrowAndSendsNoEmail()
    {
        Studio studio = SeedStudio();
        await _db.SaveChangesAsync();
        _identity.GetEmailsInRoleAsync("admin", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());

        Func<Task> act = () => CreateSut().Handle(
            new SendStudioRegisteredNotificationCommand(studio.Id), default);

        await act.Should().NotThrowAsync();
        await _notifications.DidNotReceiveWithAnyArgs()
            .SendEmailAsync(default!, default!, default!, default);
        _db.NotificationLogs.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OneAdminAccountEmailSucceeds_WritesSuccessfulLog()
    {
        Studio studio = SeedStudio();
        await _db.SaveChangesAsync();
        _identity.GetEmailsInRoleAsync("admin", Arg.Any<CancellationToken>())
            .Returns(new[] { "admin@pena-arte.test" });

        await CreateSut().Handle(new SendStudioRegisteredNotificationCommand(studio.Id), default);

        await _notifications.Received(1).SendEmailAsync(
            "admin@pena-arte.test", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        NotificationLog log = _db.NotificationLogs.Should().ContainSingle().Subject;
        log.RecipientType.Should().Be(NotificationRecipientType.Admin);
        log.RecipientId.Should().Be(studio.Id);
        log.StudioId.Should().Be(studio.Id);
        log.IsSuccess.Should().BeTrue();

        await _realtime.Received(1).NotifyAdminsAsync(
            "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AllAdminEmailsThrow_WritesFailedLog()
    {
        Studio studio = SeedStudio();
        await _db.SaveChangesAsync();
        _identity.GetEmailsInRoleAsync("admin", Arg.Any<CancellationToken>())
            .Returns(new[] { "admin1@pena-arte.test", "admin2@pena-arte.test" });
        _notifications.SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("send failed"));

        await CreateSut().Handle(new SendStudioRegisteredNotificationCommand(studio.Id), default);

        NotificationLog log = _db.NotificationLogs.Should().ContainSingle().Subject;
        log.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OneOfTwoAdminEmailsFails_WritesSuccessfulLog()
    {
        Studio studio = SeedStudio();
        await _db.SaveChangesAsync();
        _identity.GetEmailsInRoleAsync("admin", Arg.Any<CancellationToken>())
            .Returns(new[] { "admin1@pena-arte.test", "admin2@pena-arte.test" });
        _notifications.SendEmailAsync(
            "admin1@pena-arte.test", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("send failed"));

        await CreateSut().Handle(new SendStudioRegisteredNotificationCommand(studio.Id), default);

        await _notifications.Received(1).SendEmailAsync(
            "admin2@pena-arte.test", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        NotificationLog log = _db.NotificationLogs.Should().ContainSingle().Subject;
        log.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_MultipleAdminAccounts_SendsToEveryOneAndWritesSingleLog()
    {
        Studio studio = SeedStudio();
        await _db.SaveChangesAsync();
        _identity.GetEmailsInRoleAsync("admin", Arg.Any<CancellationToken>())
            .Returns(new[] { "admin1@pena-arte.test", "admin2@pena-arte.test", "admin3@pena-arte.test" });

        await CreateSut().Handle(new SendStudioRegisteredNotificationCommand(studio.Id), default);

        await _notifications.Received(3).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.NotificationLogs.Should().ContainSingle();
    }
}
