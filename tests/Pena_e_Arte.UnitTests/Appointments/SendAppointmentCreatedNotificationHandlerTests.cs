using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class SendAppointmentCreatedNotificationHandlerTests
{
    private readonly FakeDbContext        _db            = FakeDbContext.Create();
    private readonly IEmailRenderer       _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier    _realtime      = Substitute.For<IRealtimeNotifier>();

    public SendAppointmentCreatedNotificationHandlerTests()
    {
        _emailRenderer
            .RenderAppointmentCreatedClient(
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns("<html>client</html>");
        _emailRenderer
            .RenderAppointmentCreatedStudio(
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns("<html>studio</html>");
    }

    private SendAppointmentCreatedNotificationHandler CreateSut() =>
        new(_db, _emailRenderer, _notifications, _realtime,
            NullLogger<SendAppointmentCreatedNotificationHandler>.Instance);

    private async Task<(Guid appointmentId, Studio studio, Client client)> SeedData(string? phone = null)
    {
        Studio studio = new() { Name = "Test Studio", Slug = "test", OwnerEmail = "owner@test.com" };
        _db.Studios.Add(studio);

        Client client = new()
        {
            StudioId  = studio.Id,
            FirstName = "Ana",
            LastName  = "Silva",
            Email     = "ana@test.com",
            Phone     = phone,
        };
        _db.Clients.Add(client);

        Appointment appointment = new()
        {
            StudioId        = studio.Id,
            ArtistId        = Guid.NewGuid(),
            ClientId        = client.Id,
            Client          = client,
            Date            = DateTime.UtcNow.AddDays(3),
            EndDate         = DateTime.UtcNow.AddDays(3).AddHours(2),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return (appointment.Id, studio, client);
    }

    [Fact]
    public async Task Handle_ValidInput_SendsClientEmail()
    {
        (Guid appointmentId, _, Client client) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        await _notifications.Received(1)
            .SendEmailAsync(client.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidInput_SendsStudioEmail()
    {
        (Guid appointmentId, Studio studio, _) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        await _notifications.Received(1)
            .SendEmailAsync(studio.OwnerEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppointmentNotFound_DoesNotSendEmail()
    {
        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(Guid.NewGuid()), default);

        await _notifications.DidNotReceive()
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailFails_DoesNotThrow()
    {
        (Guid appointmentId, _, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        Func<Task> act = () => CreateSut().Handle(
            new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_EmailFails_WritesFailedNotificationLog()
    {
        (Guid appointmentId, _, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidInput_WritesClientEmailSuccessLog()
    {
        (Guid appointmentId, Studio studio, Client client) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.RecipientType == NotificationRecipientType.Client
                                   && n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
        log.StudioId.Should().Be(studio.Id);
        log.RecipientId.Should().Be(client.Id);
    }

    [Fact]
    public async Task Handle_ValidInput_WritesStudioEmailSuccessLog()
    {
        (Guid appointmentId, Studio studio, _) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.RecipientType == NotificationRecipientType.Studio);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
        log.RecipientId.Should().Be(studio.Id);
    }

    [Fact]
    public async Task Handle_ClientHasPhone_SendsSms()
    {
        (Guid appointmentId, _, _) = await SeedData(phone: "+351912345678");

        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        await _notifications.Received(1)
            .SendSmsAsync("+351912345678", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClientHasNoPhone_DoesNotSendSms()
    {
        (Guid appointmentId, _, _) = await SeedData(phone: null);

        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        await _notifications.DidNotReceive()
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SmsFails_DoesNotThrow()
    {
        (Guid appointmentId, _, _) = await SeedData(phone: "+351912345678");
        _notifications
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Twilio down"));

        Func<Task> act = () => CreateSut().Handle(
            new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_SmsFails_WritesFailedSmsLog()
    {
        (Guid appointmentId, _, _) = await SeedData(phone: "+351912345678");
        _notifications
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Twilio down"));

        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        NotificationLog? smsLog = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Sms);
        smsLog.Should().NotBeNull();
        smsLog!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidInput_PushesNotificationReceivedEvent()
    {
        (Guid appointmentId, Studio studio, _) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCreatedNotificationCommand(appointmentId), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(studio.Id, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
