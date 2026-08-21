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
public class ManualReminderJobTests(DatabaseFixture fixture)
{
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();

    private ManualReminderJob CreateSut(AppDbContext db) =>
        new(_notifications, db, _realtime, NullLogger<ManualReminderJob>.Instance);

    private async Task<(Guid ReminderId, Guid StudioId)> SeedReminder(
        Guid studioId,
        Action<ManualReminder>? configure = null,
        Guid? appointmentId = null,
        Guid? clientId = null,
        bool clientOptedOut = false,
        AppointmentStatus? appointmentStatus = null)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);

        Artist artist = new() { StudioId = studioId, FirstName = "Jo", LastName = "Artist", Email = $"{Guid.NewGuid()}@a.com" };
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();

        Guid? resolvedClientId = clientId;
        Guid? resolvedAppointmentId = appointmentId;

        if (clientId is null && appointmentStatus is not null)
        {
            Client client = new()
            {
                StudioId = studioId,
                FirstName = "Ana",
                LastName = "Silva",
                Email = $"{Guid.NewGuid()}@c.com",
                Phone = "+351910000001",
                SmsOptOut = clientOptedOut
            };
            ctx.Clients.Add(client);
            await ctx.SaveChangesAsync();
            resolvedClientId = client.Id;

            Appointment appointment = new()
            {
                StudioId = studioId,
                ArtistId = artist.Id,
                ClientId = client.Id,
                Date = DateTime.UtcNow.AddDays(1),
                EndDate = DateTime.UtcNow.AddDays(1).AddHours(2),
                DurationMinutes = 120,
                Status = appointmentStatus.Value,
                DepositStatus = DepositStatus.Paid
            };
            ctx.Appointments.Add(appointment);
            await ctx.SaveChangesAsync();
            resolvedAppointmentId = appointment.Id;
        }
        else if (clientId is not null)
        {
            Client client = new()
            {
                StudioId = studioId,
                Id = clientId.Value,
                FirstName = "Ana",
                LastName = "Silva",
                Email = $"{Guid.NewGuid()}@c.com",
                Phone = "+351910000001",
                SmsOptOut = clientOptedOut
            };
            ctx.Clients.Add(client);
            await ctx.SaveChangesAsync();
        }

        ManualReminder reminder = new()
        {
            StudioId = studioId,
            ArtistId = artist.Id,
            AppointmentId = resolvedAppointmentId,
            ClientId = resolvedClientId,
            RecipientName = "Walk-in",
            RecipientPhone = "+351900000000",
            ScheduledFor = DateTime.UtcNow,
            Status = ManualReminderStatus.Scheduled
        };
        configure?.Invoke(reminder);
        ctx.ManualReminders.Add(reminder);
        await ctx.SaveChangesAsync();

        return (reminder.Id, studioId);
    }

    [Fact]
    public async Task SendAsync_ReminderNotFound_LogsWarningAndReturns()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        Func<Task> act = () => CreateSut(db).SendAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
        await _notifications.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_AlreadyCancelled_SkipsSend()
    {
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId, r => r.Status = ManualReminderStatus.Cancelled);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await _notifications.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_LinkedAppointmentCancelledSinceScheduling_SkipsSendAndMarksCancelled()
    {
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId, appointmentStatus: AppointmentStatus.Cancelled);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await _notifications.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        ManualReminder reminder = await verify.ManualReminders.SingleAsync(m => m.Id == reminderId);
        reminder.Status.Should().Be(ManualReminderStatus.Cancelled);
    }

    [Fact]
    public async Task SendAsync_ClientOptedOut_MarksFailedWithoutSending()
    {
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId, clientId: Guid.NewGuid(), clientOptedOut: true);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await _notifications.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        ManualReminder reminder = await verify.ManualReminders.SingleAsync(m => m.Id == reminderId);
        reminder.Status.Should().Be(ManualReminderStatus.Failed);
    }

    [Fact]
    public async Task SendAsync_NoClientLinked_WritesExternalContactRecipientType()
    {
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        NotificationLog log = await verify.NotificationLogs.SingleAsync();
        log.RecipientType.Should().Be(NotificationRecipientType.ExternalContact);
        log.RecipientId.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_ClientLinked_WritesClientRecipientType()
    {
        Guid studioId = Guid.NewGuid();
        Guid clientId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId, clientId: clientId);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        NotificationLog log = await verify.NotificationLogs.SingleAsync();
        log.RecipientType.Should().Be(NotificationRecipientType.Client);
        log.RecipientId.Should().Be(clientId);
    }

    [Fact]
    public async Task SendAsync_SmsSucceeds_MarksSentAndWritesSuccessLog()
    {
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        ManualReminder reminder = await verify.ManualReminders.SingleAsync(m => m.Id == reminderId);
        reminder.Status.Should().Be(ManualReminderStatus.Sent);
        reminder.SentAt.Should().NotBeNull();

        NotificationLog log = await verify.NotificationLogs.SingleAsync();
        log.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_SmsThrows_MarksFailedAndWritesFailureLog()
    {
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId);

        _notifications.SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .ThrowsAsync(new InvalidOperationException("Twilio error"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        ManualReminder reminder = await verify.ManualReminders.SingleAsync(m => m.Id == reminderId);
        reminder.Status.Should().Be(ManualReminderStatus.Failed);

        NotificationLog log = await verify.NotificationLogs.SingleAsync();
        log.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ValidReminder_PushesNotificationReceivedEvent()
    {
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await _realtime.Received(1).NotifyStudioAsync(
            studioId, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_ValidReminder_SetsSendAttemptedAt()
    {
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        ManualReminder reminder = await verify.ManualReminders.SingleAsync(m => m.Id == reminderId);
        reminder.SendAttemptedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_RetryAfterPriorSendAttempt_DoesNotResendAndMarksFailed()
    {
        // A prior attempt already claimed this reminder (set SendAttemptedAt) but never
        // reached the final status-write — mirrors a Hangfire retry after a crash between
        // the SMS send and the post-send save.
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId,
            r => r.SendAttemptedAt = DateTime.UtcNow.AddMinutes(-5));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await _notifications.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        ManualReminder reminder = await verify.ManualReminders.SingleAsync(m => m.Id == reminderId);
        reminder.Status.Should().Be(ManualReminderStatus.Failed);
    }

    [Fact]
    public async Task SendAsync_StudioOverNotificationsPerMonthCapAtSendTime_SkipsSendAndMarksFailed()
    {
        Guid studioId = Guid.NewGuid();
        (Guid reminderId, _) = await SeedReminder(studioId);

        await using (AppDbContext setup = fixture.CreateDbContext(Guid.Empty))
        {
            Plan plan = new() { Name = "Starter", MaxNotificationsPerMonth = 1 };
            setup.Plans.Add(plan);
            setup.Studios.Add(new Studio
            {
                Id = studioId,
                Name = "Test Studio",
                Slug = $"test-studio-{Guid.NewGuid()}",
                OwnerEmail = "owner@test.com",
            });
            setup.Subscriptions.Add(new Subscription
            {
                StudioId = studioId,
                PlanId = plan.Id,
                Status = SubscriptionStatus.Active,
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
                GracePeriodEnd = DateTime.UtcNow.AddDays(37),
            });
            // Already at this month's cap (MaxNotificationsPerMonth = 1) before the send is attempted.
            setup.NotificationLogs.Add(new NotificationLog
            {
                StudioId = studioId,
                RecipientType = NotificationRecipientType.ExternalContact,
                Channel = NotificationChannel.Sms,
                Body = "Existing notification counted toward this month's cap.",
                SentAt = DateTime.UtcNow,
                IsSuccess = true,
            });
            await setup.SaveChangesAsync();
        }

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).SendAsync(reminderId);

        await _notifications.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        ManualReminder reminder = await verify.ManualReminders.SingleAsync(m => m.Id == reminderId);
        reminder.Status.Should().Be(ManualReminderStatus.Failed);
    }
}
