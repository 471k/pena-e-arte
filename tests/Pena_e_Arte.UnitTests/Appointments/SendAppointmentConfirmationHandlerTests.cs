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

public class SendAppointmentConfirmationHandlerTests
{
    private readonly FakeDbContext       _db            = FakeDbContext.Create();
    private readonly IEmailRenderer      _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier    _realtime      = Substitute.For<IRealtimeNotifier>();

    public SendAppointmentConfirmationHandlerTests()
    {
        _emailRenderer
            .RenderAppointmentConfirmation(
                Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>(),
                Arg.Any<string?>(), Arg.Any<bool>())
            .Returns("<html>confirmation</html>");
    }

    private SendAppointmentConfirmationHandler CreateSut() =>
        new(_db, _emailRenderer, _notifications, _realtime,
            NullLogger<SendAppointmentConfirmationHandler>.Instance);

    private async Task<(Guid appointmentId, Studio studio)> SeedData(bool showBranding)
    {
        Studio studio = new()
        {
            Name = "Test Studio",
            Slug = "test-studio",
            City = "Lisboa",
        };
        if (!showBranding) studio.UpdateBranding(false);
        _db.Studios.Add(studio);

        Client client = new()
        {
            StudioId  = studio.Id,
            FirstName = "Ana",
            LastName  = "Silva",
            Email     = "ana@example.com",
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
            Status          = AppointmentStatus.Confirmed,
            DepositStatus   = DepositStatus.Paid,
            DepositAmount   = 50m,
        };
        _db.Appointments.Add(appointment);

        await _db.SaveChangesAsync();
        return (appointment.Id, studio);
    }

    [Fact]
    public async Task Handle_StudioFlagTrue_PassesShowBrandingTrue()
    {
        (Guid appointmentId, _) = await SeedData(showBranding: true);

        await CreateSut().Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        _emailRenderer.Received(1).RenderAppointmentConfirmation(
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>(),
            Arg.Any<string?>(), showBranding: true);
    }

    [Fact]
    public async Task Handle_StudioFlagFalse_PassesShowBrandingFalse()
    {
        (Guid appointmentId, _) = await SeedData(showBranding: false);

        await CreateSut().Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        _emailRenderer.Received(1).RenderAppointmentConfirmation(
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>(),
            Arg.Any<string?>(), showBranding: false);
    }

    [Fact]
    public async Task Handle_ValidAppointment_SendsEmail()
    {
        (Guid appointmentId, _) = await SeedData(showBranding: true);

        await CreateSut().Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        await _notifications.Received(1)
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppointmentNotFound_DoesNotSendEmail()
    {
        await CreateSut().Handle(new SendAppointmentConfirmationCommand(Guid.NewGuid()), default);

        await _notifications.DidNotReceive()
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailFails_DoesNotThrow()
    {
        (Guid appointmentId, _) = await SeedData(showBranding: true);

        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP error")));

        Func<Task> act = () =>
            CreateSut().Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_ValidAppointment_WritesSuccessNotificationLog()
    {
        (Guid appointmentId, Studio studio) = await SeedData(showBranding: true);

        await CreateSut().Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
        log.StudioId.Should().Be(studio.Id);
    }

    [Fact]
    public async Task Handle_EmailFails_WritesFailedNotificationLog()
    {
        (Guid appointmentId, _) = await SeedData(showBranding: true);

        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP error"));

        await CreateSut().Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidAppointment_PushesNotificationReceivedEvent()
    {
        (Guid appointmentId, Studio studio) = await SeedData(showBranding: true);

        await CreateSut().Handle(new SendAppointmentConfirmationCommand(appointmentId), default);

        await _realtime.Received(1).NotifyStudioAsync(
            studio.Id, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
