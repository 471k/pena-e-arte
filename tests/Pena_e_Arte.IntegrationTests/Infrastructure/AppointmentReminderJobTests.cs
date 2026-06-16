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
public class AppointmentReminderJobTests(DatabaseFixture fixture)
{
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier    _realtime      = Substitute.For<IRealtimeNotifier>();

    private AppointmentReminderJob CreateSut(AppDbContext db) =>
        new(_notifications, db, _realtime, NullLogger<AppointmentReminderJob>.Instance);

    [Fact]
    public async Task SendReminderAsync_ValidAppointment_SendsEmail()
    {
        (Guid appointmentId, _) = await SeedAppointmentWithClient(withPhone: false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendReminderAsync(appointmentId, "48h");

        await _notifications.Received(1)
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendReminderAsync_ClientHasPhone_SendsSmsAndEmail()
    {
        (Guid appointmentId, _) = await SeedAppointmentWithClient(withPhone: true);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendReminderAsync(appointmentId, "48h");

        await _notifications.Received(1).SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _notifications.Received(1).SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendReminderAsync_ClientHasNoPhone_DoesNotSendSms()
    {
        (Guid appointmentId, _) = await SeedAppointmentWithClient(withPhone: false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendReminderAsync(appointmentId, "48h");

        await _notifications.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendReminderAsync_ValidAppointment_WritesEmailNotificationLog()
    {
        (Guid appointmentId, Guid studioId) = await SeedAppointmentWithClient(withPhone: false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendReminderAsync(appointmentId, "48h");

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        NotificationLog? log = await verify.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendReminderAsync_EmailFails_WritesFailedLog()
    {
        (Guid appointmentId, Guid studioId) = await SeedAppointmentWithClient(withPhone: false);

        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .ThrowsAsync(new InvalidOperationException("SMTP error"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendReminderAsync(appointmentId, "48h");

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        NotificationLog? log = await verify.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SendReminderAsync_UnknownAppointmentId_DoesNotSendOrThrow()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        Func<Task> act = () => CreateSut(db).SendReminderAsync(Guid.NewGuid(), "48h");

        await act.Should().NotThrowAsync();
        await _notifications.DidNotReceive().SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendReminderAsync_CancelledAppointment_DoesNotSendEmail()
    {
        (Guid appointmentId, _) = await SeedAppointmentWithClient(withPhone: false, status: AppointmentStatus.Cancelled);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendReminderAsync(appointmentId, "24h");

        await _notifications.DidNotReceive()
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendReminderAsync_Subject_Contains48hForType48h()
    {
        (Guid appointmentId, _) = await SeedAppointmentWithClient(withPhone: false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendReminderAsync(appointmentId, "48h");

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains("Reminder")),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendReminderAsync_ValidAppointment_PushesNotificationReceivedEvent()
    {
        (Guid appointmentId, Guid studioId) = await SeedAppointmentWithClient(withPhone: false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendReminderAsync(appointmentId, "48h");

        await _realtime.Received(1).NotifyStudioAsync(
            studioId, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendReminderAsync_ClientHasPhone_PushesNotificationReceivedTwice()
    {
        (Guid appointmentId, Guid studioId) = await SeedAppointmentWithClient(withPhone: true);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendReminderAsync(appointmentId, "48h");

        await _realtime.Received(2).NotifyStudioAsync(
            studioId, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    private async Task<(Guid AppointmentId, Guid StudioId)> SeedAppointmentWithClient(
        bool withPhone,
        AppointmentStatus status = AppointmentStatus.Pending)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);

        Guid studioId = Guid.NewGuid();

        Client client = new()
        {
            StudioId  = studioId,
            FirstName = "Ana",
            LastName  = "Silva",
            Email     = "ana@example.com",
            Phone     = withPhone ? "+351910000001" : null
        };
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

        Artist artist = new()
        {
            StudioId  = studioId,
            FirstName = "João",
            LastName  = "Artista",
            Email     = "joao@example.com"
        };
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();

        Appointment appointment = new()
        {
            StudioId        = studioId,
            ArtistId        = artist.Id,
            ClientId        = client.Id,
            Date            = DateTime.UtcNow.AddDays(2),
            EndDate         = DateTime.UtcNow.AddDays(2).AddHours(2),
            DurationMinutes = 120,
            Status          = status,
            DepositStatus   = DepositStatus.Paid,
            DepositAmount   = 50m
        };
        ctx.Appointments.Add(appointment);
        await ctx.SaveChangesAsync();

        return (appointment.Id, studioId);
    }
}
