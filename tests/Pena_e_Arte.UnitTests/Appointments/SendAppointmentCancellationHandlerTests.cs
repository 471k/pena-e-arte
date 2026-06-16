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

public class SendAppointmentCancellationHandlerTests
{
    private readonly FakeDbContext        _db            = FakeDbContext.Create();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier    _realtime      = Substitute.For<IRealtimeNotifier>();

    private SendAppointmentCancellationHandler CreateSut() =>
        new(_db, _notifications, _realtime,
            NullLogger<SendAppointmentCancellationHandler>.Instance);

    private async Task<(Guid appointmentId, Guid studioId)> SeedData()
    {
        Guid studioId = Guid.NewGuid();

        Client client = new()
        {
            StudioId  = studioId,
            FirstName = "Ana",
            LastName  = "Silva",
            Email     = "ana@example.com",
        };
        _db.Clients.Add(client);

        Appointment appointment = new()
        {
            StudioId        = studioId,
            ArtistId        = Guid.NewGuid(),
            ClientId        = client.Id,
            Client          = client,
            Date            = DateTime.UtcNow.AddDays(3),
            EndDate         = DateTime.UtcNow.AddDays(3).AddHours(2),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Cancelled,
            DepositStatus   = DepositStatus.Paid,
            DepositAmount   = 50m,
        };
        _db.Appointments.Add(appointment);

        await _db.SaveChangesAsync();
        return (appointment.Id, studioId);
    }

    [Fact]
    public async Task Handle_ValidAppointment_SendsEmail()
    {
        (Guid appointmentId, _) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        await _notifications.Received(1)
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppointmentNotFound_DoesNotSendEmail()
    {
        await CreateSut().Handle(new SendAppointmentCancellationCommand(Guid.NewGuid()), default);

        await _notifications.DidNotReceive()
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailFails_DoesNotThrow()
    {
        (Guid appointmentId, _) = await SeedData();

        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP error"));

        Func<Task> act = () =>
            CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_ValidAppointment_WritesSuccessNotificationLog()
    {
        (Guid appointmentId, Guid studioId) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
        log.StudioId.Should().Be(studioId);
    }

    [Fact]
    public async Task Handle_EmailFails_WritesFailedNotificationLog()
    {
        (Guid appointmentId, _) = await SeedData();

        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP error"));

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidAppointment_SubjectContainsCancelled()
    {
        (Guid appointmentId, _) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains("Cancelled")),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidAppointment_PushesNotificationReceivedEvent()
    {
        (Guid appointmentId, Guid studioId) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        await _realtime.Received(1).NotifyStudioAsync(
            studioId, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
