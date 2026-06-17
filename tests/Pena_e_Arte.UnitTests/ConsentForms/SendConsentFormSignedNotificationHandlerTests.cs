using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConsentForms;

public class SendConsentFormSignedNotificationHandlerTests
{
    private readonly FakeDbContext        _db            = FakeDbContext.Create();
    private readonly IEmailRenderer       _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier    _realtime      = Substitute.For<IRealtimeNotifier>();

    public SendConsentFormSignedNotificationHandlerTests() =>
        _emailRenderer
            .RenderConsentFormSigned(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns("<html>signed</html>");

    private SendConsentFormSignedNotificationHandler CreateSut() =>
        new(_db, _emailRenderer, _notifications, _realtime,
            NullLogger<SendConsentFormSignedNotificationHandler>.Instance);

    private async Task<(Guid formId, Studio studio)> SeedData()
    {
        Studio studio = new() { Name = "Test Studio", Slug = "test", OwnerEmail = "owner@test.com" };
        _db.Studios.Add(studio);

        Client client = new()
        {
            StudioId  = studio.Id,
            FirstName = "Ana",
            LastName  = "Silva",
            Email     = "ana@test.com",
        };
        _db.Clients.Add(client);

        Appointment appointment = new()
        {
            StudioId        = studio.Id,
            ArtistId        = Guid.NewGuid(),
            ClientId        = client.Id,
            Date            = DateTime.UtcNow.AddDays(3),
            EndDate         = DateTime.UtcNow.AddDays(3).AddHours(2),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Confirmed,
            DepositStatus   = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);

        ConsentForm form = new()
        {
            StudioId      = studio.Id,
            ClientId      = client.Id,
            AppointmentId = appointment.Id,
            Client        = client,
            Appointment   = appointment,
            SignatureData = "data:image/png;base64,abc",
            SignedAt      = DateTime.UtcNow,
        };
        _db.ConsentForms.Add(form);
        await _db.SaveChangesAsync();
        return (form.Id, studio);
    }

    [Fact]
    public async Task Handle_ValidInput_SendsEmailToStudioOwner()
    {
        (Guid formId, Studio studio) = await SeedData();

        await CreateSut().Handle(new SendConsentFormSignedNotificationCommand(formId), default);

        await _notifications.Received(1)
            .SendEmailAsync(studio.OwnerEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EntityNotFound_DoesNotSendEmail()
    {
        await CreateSut().Handle(new SendConsentFormSignedNotificationCommand(Guid.NewGuid()), default);

        await _notifications.DidNotReceive()
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailFails_DoesNotThrow()
    {
        (Guid formId, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        Func<Task> act = () => CreateSut().Handle(new SendConsentFormSignedNotificationCommand(formId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_EmailFails_WritesFailedNotificationLog()
    {
        (Guid formId, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await CreateSut().Handle(new SendConsentFormSignedNotificationCommand(formId), default);

        NotificationLog? log = await _db.NotificationLogs.FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidInput_WritesSuccessNotificationLog()
    {
        (Guid formId, Studio studio) = await SeedData();

        await CreateSut().Handle(new SendConsentFormSignedNotificationCommand(formId), default);

        NotificationLog? log = await _db.NotificationLogs.FirstOrDefaultAsync();
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
        log.StudioId.Should().Be(studio.Id);
        log.RecipientType.Should().Be(NotificationRecipientType.Studio);
        log.Channel.Should().Be(NotificationChannel.Email);
    }

    [Fact]
    public async Task Handle_ValidInput_PushesNotificationReceivedEvent()
    {
        (Guid formId, Studio studio) = await SeedData();

        await CreateSut().Handle(new SendConsentFormSignedNotificationCommand(formId), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(studio.Id, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
