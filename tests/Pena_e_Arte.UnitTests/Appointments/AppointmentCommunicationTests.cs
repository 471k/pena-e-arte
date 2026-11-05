using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// E04b (A08): communication state from records that exist — the reschedule audit entry plus staff
/// messages carrying the appointment, and the aftercare step's per-channel log rows. "Sent" means our
/// side handed it off; nothing here can say delivered or read.
/// </summary>
public class AppointmentCommunicationTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly INotificationPreferenceService _prefs = Substitute.For<INotificationPreferenceService>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistUserId = Guid.NewGuid();
    private Guid _artistId;

    public AppointmentCommunicationTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Ink", Slug = _studioId.ToString("N")[..8] });
        Artist artist = new() { StudioId = _studioId, UserId = _artistUserId, FirstName = "Ari", LastName = "Kola", Email = "a@a.test", IsActive = true };
        _db.Artists.Add(artist);
        _db.SaveChanges();
        _artistId = artist.Id;
    }

    private Task<AppointmentCommunicationResponse> Read(Guid id, string role = "owner", Guid? userId = null)
    {
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.Role.Returns(role);
        user.UserId.Returns(userId ?? Guid.NewGuid());
        return new GetAppointmentCommunicationHandler(_db, user, _tenant).Handle(new GetAppointmentCommunicationQuery(id), default);
    }

    // ── Reschedule ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NeverRescheduled_HasNoRescheduleTask()
    {
        Guid id = SeedAppointment(AppointmentStatus.Confirmed);

        (await Read(id)).Reschedule.Should().BeNull();
    }

    [Fact]
    public async Task Rescheduled_AndNobodyMessagedTheClient_IsNotSent()
    {
        Guid id = SeedAppointment(AppointmentStatus.Confirmed);
        DateTime moved = SeedReschedule(id, DateTime.UtcNow.AddHours(-2));
        SeedMessage(id, "artist", moved.AddHours(-1));   // before the move: doesn't count
        SeedMessage(id, "client", moved.AddMinutes(30)); // from the client: doesn't count

        RescheduleCommunicationResponse r = (await Read(id)).Reschedule!;

        r.State.Should().Be("NotSent");
        r.RescheduledAt.Should().Be(moved);
        r.MessagedAt.Should().BeNull();
    }

    [Fact]
    public async Task Rescheduled_ThenStaffMessagedAboutIt_IsSent_WithTheFirstSuchMessage()
    {
        Guid id = SeedAppointment(AppointmentStatus.Confirmed);
        DateTime moved = SeedReschedule(id, DateTime.UtcNow.AddHours(-2));
        DateTime first = moved.AddMinutes(10);
        SeedMessage(id, "owner", first);
        SeedMessage(id, "artist", moved.AddMinutes(40));

        RescheduleCommunicationResponse r = (await Read(id)).Reschedule!;

        r.State.Should().Be("Sent");
        r.MessagedAt.Should().Be(first);
    }

    [Fact]
    public async Task AMessageAboutADifferentAppointment_DoesNotCount()
    {
        Guid id = SeedAppointment(AppointmentStatus.Confirmed);
        Guid other = SeedAppointment(AppointmentStatus.Confirmed);
        DateTime moved = SeedReschedule(id, DateTime.UtcNow.AddHours(-2));
        SeedMessage(other, "artist", moved.AddMinutes(5));

        (await Read(id)).Reschedule!.State.Should().Be("NotSent");
    }

    [Fact]
    public async Task OnlyTheLatestMove_Counts()
    {
        Guid id = SeedAppointment(AppointmentStatus.Confirmed);
        DateTime firstMove = SeedReschedule(id, DateTime.UtcNow.AddDays(-2));
        SeedMessage(id, "artist", firstMove.AddMinutes(5));
        DateTime secondMove = SeedReschedule(id, DateTime.UtcNow.AddHours(-1));

        RescheduleCommunicationResponse r = (await Read(id)).Reschedule!;

        r.RescheduledAt.Should().Be(secondMove);
        r.State.Should().Be("NotSent", "the client was told about the first move, not the second");
    }

    [Fact]
    public async Task AnotherStudiosAuditEntry_IsIgnored()
    {
        Guid id = SeedAppointment(AppointmentStatus.Confirmed);
        _db.AuditLogEntries.Add(AuditLogEntry.Create(Guid.NewGuid(), "owner", AuditActions.AppointmentRescheduled,
            AuditTargetTypes.Appointment, id, Guid.NewGuid(), "{}"));
        _db.SaveChanges();

        (await Read(id)).Reschedule.Should().BeNull();
    }

    // ── Aftercare ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnUpcomingAppointment_HasNoAftercareTask()
    {
        (await Read(SeedAppointment(AppointmentStatus.Confirmed))).Aftercare.Should().BeNull();
    }

    [Fact]
    public async Task Completed_AftercareNotRunYet_IsNotSent()
    {
        AftercareCommunicationResponse a = (await Read(SeedAppointment(AppointmentStatus.Completed))).Aftercare!;

        a.State.Should().Be("NotSent");
        a.RanAt.Should().BeNull();
    }

    [Fact]
    public async Task Completed_AftercareRanWithNoChannelRecord_IsNotRecorded_NeverSent()
    {
        Guid id = SeedAppointment(AppointmentStatus.Completed, aftercareRan: DateTime.UtcNow.AddHours(-1));

        (await Read(id)).Aftercare!.State.Should().Be("NotRecorded");
    }

    [Theory]
    [InlineData(true, true, "Sent")]
    [InlineData(true, false, "Sent")]
    [InlineData(false, false, "Failed")]
    public async Task Completed_AftercareChannels_GiveSentOrFailed(bool emailOk, bool smsOk, string expected)
    {
        Guid id = SeedAppointment(AppointmentStatus.Completed, aftercareRan: DateTime.UtcNow.AddHours(-1));
        SeedLog(id, NotificationChannel.Email, emailOk);
        SeedLog(id, NotificationChannel.Sms, smsOk);

        AftercareCommunicationResponse a = (await Read(id)).Aftercare!;

        a.State.Should().Be(expected);
        a.Channels.Select(c => (c.Channel, c.Sent)).Should().BeEquivalentTo(new[] { ("Email", emailOk), ("Sms", smsOk) });
    }

    // ── The aftercare step now records each channel's outcome ─────────────────────

    [Fact]
    public async Task TheAftercareStep_RecordsEachAttemptedChannel_LinkedToTheAppointment()
    {
        _prefs.IsEnabledAsync(_studioId, NotificationType.Aftercare, Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>()).Returns(true);
        _notifications.SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("twilio down"));
        Guid id = SeedAppointment(AppointmentStatus.Completed, phone: "+355690000000");

        await RunAftercare(id);

        List<NotificationLog> logs = _db.NotificationLogs.Where(n => n.AppointmentId == id).OrderBy(n => n.Channel).ToList();
        logs.Select(n => (n.Channel, n.IsSuccess)).Should().BeEquivalentTo(new[] { (NotificationChannel.Email, true), (NotificationChannel.Sms, false) });
        logs.Should().OnlyContain(n => n.RecipientType == NotificationRecipientType.Client && n.SentAt != null);
        _db.Appointments.Single(a => a.Id == id).AftercareSentAt.Should().NotBeNull();
        (await Read(id)).Aftercare!.State.Should().Be("Sent");
    }

    [Fact]
    public async Task TheAftercareStep_WithBothChannelsOff_RecordsNothing_AndStillMarksItRan()
    {
        _prefs.IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<NotificationType>(), Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>()).Returns(false);
        Guid id = SeedAppointment(AppointmentStatus.Completed, phone: "+355690000000");

        await RunAftercare(id);

        _db.NotificationLogs.Where(n => n.AppointmentId == id).Should().BeEmpty();
        _db.Appointments.Single(a => a.Id == id).AftercareSentAt.Should().NotBeNull("unchanged: it records that the step ran");
        (await Read(id)).Aftercare!.State.Should().Be("NotRecorded");
    }

    // ── Scope ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnArtist_SeesTheirOwnSession_ButNotAColleagues()
    {
        Guid mine = SeedAppointment(AppointmentStatus.Confirmed);
        Guid colleagues = SeedAppointment(AppointmentStatus.Confirmed, artistId: Guid.NewGuid());

        (await Read(mine, "artist", _artistUserId)).AppointmentId.Should().Be(mine);
        Func<Task> act = () => Read(colleagues, "artist", _artistUserId);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void TheValidator_RejectsAnEmptyId()
    {
        new GetAppointmentCommunicationValidator().Validate(new GetAppointmentCommunicationQuery(Guid.Empty)).IsValid.Should().BeFalse();
    }

    // ── Seeding ─────────────────────────────────────────────────────────────────

    private Task RunAftercare(Guid id) =>
        new SendAftercareNotificationHandler(_db, Substitute.For<IEmailRenderer>(), _notifications, _prefs,
                NullLogger<SendAftercareNotificationHandler>.Instance)
            .Handle(new SendAftercareNotificationCommand(id), default);

    private Guid SeedAppointment(AppointmentStatus status, DateTime? aftercareRan = null, Guid? artistId = null, string? phone = null)
    {
        Client client = new() { StudioId = _studioId, FirstName = "Mira", LastName = "Hoxha", Email = $"{Guid.NewGuid():N}@c.test", Phone = phone };
        _db.Clients.Add(client);
        DateTime start = DateTime.UtcNow.AddDays(status == AppointmentStatus.Completed ? -1 : 3);
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = artistId ?? _artistId,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddHours(2),
            DurationMinutes = 120,
            Status = status,
            AftercareSentAt = aftercareRan,
        };
        _db.Appointments.Add(appointment);
        _db.SaveChanges();
        return appointment.Id;
    }

    private DateTime SeedReschedule(Guid appointmentId, DateTime at)
    {
        AuditLogEntry entry = AuditLogEntry.Create(Guid.NewGuid(), "owner", AuditActions.AppointmentRescheduled,
            AuditTargetTypes.Appointment, appointmentId, _studioId, "{}");
        typeof(AuditLogEntry).GetProperty(nameof(AuditLogEntry.CreatedAt))!.SetValue(entry, at);
        _db.AuditLogEntries.Add(entry);
        _db.SaveChanges();
        return at;
    }

    private void SeedMessage(Guid appointmentId, string senderRole, DateTime at)
    {
        ChatMessage m = ChatMessage.Create(_studioId, Guid.NewGuid(), Guid.NewGuid(), senderRole, "About your appointment", appointmentId);
        // CreatedAt is init-only; a test sets it the way EF would materialise it.
        typeof(TenantEntity).GetProperty(nameof(TenantEntity.CreatedAt))!.SetValue(m, at);
        _db.ChatMessages.Add(m);
        _db.SaveChanges();
    }

    private void SeedLog(Guid appointmentId, NotificationChannel channel, bool ok)
    {
        _db.NotificationLogs.Add(new NotificationLog
        {
            StudioId = _studioId,
            RecipientType = NotificationRecipientType.Client,
            Channel = channel,
            Body = "aftercare",
            SentAt = DateTime.UtcNow.AddMinutes(-30),
            IsSuccess = ok,
            AppointmentId = appointmentId,
        });
        _db.SaveChanges();
    }
}
