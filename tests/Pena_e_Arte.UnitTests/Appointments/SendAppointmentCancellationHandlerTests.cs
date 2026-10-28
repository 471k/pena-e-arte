using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class SendAppointmentCancellationHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly INotificationPreferenceService _prefs = new AlwaysEnabledNotificationPreferences();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();

    public SendAppointmentCancellationHandlerTests() => _appSettings.BaseUrl.Returns("https://app.example.test");

    private SendAppointmentCancellationHandler CreateSut() =>
        new(_db, _notifications, _prefs, _realtime, _appSettings,
            NullLogger<SendAppointmentCancellationHandler>.Instance);

    private async Task<(Guid appointmentId, Guid studioId)> SeedData()
    {
        Guid studioId = Guid.NewGuid();

        Client client = new()
        {
            StudioId = studioId,
            FirstName = "Ana",
            LastName = "Silva",
            Email = "ana@example.com",
        };
        _db.Clients.Add(client);

        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = client.Id,
            Client = client,
            Date = DateTime.UtcNow.AddDays(3),
            EndDate = DateTime.UtcNow.AddDays(3).AddHours(2),
            DurationMinutes = 120,
            Status = AppointmentStatus.Cancelled,
            DepositStatus = DepositStatus.Paid,
            DepositAmount = 50m,
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
                Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppointmentNotFound_DoesNotSendEmail()
    {
        await CreateSut().Handle(new SendAppointmentCancellationCommand(Guid.NewGuid()), default);

        await _notifications.DidNotReceive()
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailFails_DoesNotThrow()
    {
        (Guid appointmentId, _) = await SeedData();

        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
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
        log.RecipientType.Should().Be(NotificationRecipientType.Client);
    }

    [Fact]
    public async Task Handle_EmailFails_WritesFailedNotificationLog()
    {
        (Guid appointmentId, _) = await SeedData();

        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
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
            Arg.Is<string>(s => s.StartsWith("Appointment cancelled")),
            Arg.Any<string>(),
            Arg.Any<string?>(),
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

    [Fact]
    public async Task Handle_ValidAppointment_PushesRecipientNameResolvedFromClient()
    {
        (Guid appointmentId, Guid studioId) = await SeedData();

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        await _realtime.Received(1).NotifyStudioAsync(
            studioId, "NotificationReceived",
            Arg.Is<NotificationLogResponse>(r => r.RecipientName == "Ana Silva"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ShowsTheAppointmentInTheStudiosTimezone_NotUtc()
    {
        (Guid appointmentId, Guid studioId) = await SeedData();
        _db.Studios.Add(new Studio { Id = studioId, Name = "S", Slug = "s", OwnerEmail = "o@x.test", Timezone = "Europe/Paris" });
        Appointment appointment = await _db.Appointments.FirstAsync(a => a.Id == appointmentId);
        appointment.Date = new DateTime(2026, 10, 23, 14, 0, 0, DateTimeKind.Utc); // 16:00 in Paris (CEST)
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            "Appointment cancelled — S, Fri, 23 Oct 2026 at 16:00",
            Arg.Is<string>(b => b.Contains("Friday, 23 October 2026 at 16:00") && !b.Contains("14:00")),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    // ── What happened to the deposit (C-11 production test, 28 Oct 2026) ──────────

    private async Task<Guid> SeedPaidCardDeposit(Guid appointmentId, Guid studioId, decimal amount = 100m)
    {
        _db.Payments.Add(new Payment
        {
            StudioId = studioId,
            AppointmentId = appointmentId,
            ClientId = (await _db.Appointments.FirstAsync(a => a.Id == appointmentId)).ClientId,
            Amount = amount,
            Currency = "ALL",
            Method = ClientPaymentMethod.Card,
            Status = PaymentStatus.Refunded,
        });
        await _db.SaveChangesAsync();
        return appointmentId;
    }

    private string SentBody()
    {
        string? body = null;
        _notifications.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(INotificationService.SendEmailAsync))
            .ToList()
            .ForEach(c => body = (string)c.GetArguments()[2]!);
        return body!;
    }

    [Fact]
    public async Task Handle_FullRefundNow_EmailSaysTheDepositIsBeingRefunded()
    {
        (Guid appointmentId, Guid studioId) = await SeedData();
        await SeedPaidCardDeposit(appointmentId, studioId);

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId, RefundedAmount: 100m), default);

        SentBody().Should().Contain("Your deposit of 100.00 ALL is being refunded to your card. Allow 3-5 business days for it to appear.");
    }

    [Fact]
    public async Task Handle_PartialRefundNow_EmailSaysWhatIsRefundedAndWhatTheStudioKeeps()
    {
        (Guid appointmentId, Guid studioId) = await SeedData();
        await SeedPaidCardDeposit(appointmentId, studioId);

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId, RefundedAmount: 40m), default);

        string body = SentBody();
        body.Should().Contain("40.00 ALL of your 100.00 ALL deposit is being refunded to your card.");
        body.Should().Contain("The studio keeps the remaining 60.00 ALL under its cancellation policy.");
    }

    [Fact]
    public async Task Handle_DepositForfeited_EmailSaysTheDepositIsNotRefunded()
    {
        (Guid appointmentId, Guid studioId) = await SeedData();
        await SeedPaidCardDeposit(appointmentId, studioId);

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId, DepositForfeited: true), default);

        SentBody().Should().Contain("Your deposit of 100.00 ALL is not refunded, because the appointment was cancelled with less notice");
    }

    [Fact]
    public async Task Handle_NoMoneyMovedByThisCancellation_EmailSaysNothingAboutTheDeposit()
    {
        // e.g. the owner had already refunded the deposit before the booking was cancelled
        (Guid appointmentId, Guid studioId) = await SeedData();
        await SeedPaidCardDeposit(appointmentId, studioId);

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        SentBody().Should().NotContain("deposit");
    }

    // ── Who to contact, SMS, and declined requests (28 Oct 2026) ─────────────────

    private async Task<(Guid AppointmentId, Studio Studio)> SeedWithStudio(
        CancellationReason reason, string? phone = "+355690000000", bool branding = true, string? studioPhone = "+355 69 111 2222")
    {
        (Guid appointmentId, Guid studioId) = await SeedData();
        Studio studio = new()
        {
            Id = studioId, Name = "Ink & Iron", Slug = "ink-iron", OwnerEmail = "owner@example.test",
            Timezone = "Europe/Tirane", PhoneNumber = studioPhone,
        };
        if (!branding) studio.UpdateBranding(false);
        _db.Studios.Add(studio);
        Appointment appointment = await _db.Appointments.Include(a => a.Client).FirstAsync(a => a.Id == appointmentId);
        appointment.CancellationReason = reason;
        appointment.Date = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc); // 11:00 in Tirana
        appointment.Client.Phone = phone;
        await _db.SaveChangesAsync();
        return (appointmentId, studio);
    }

    [Fact]
    public async Task Handle_EmailNamesTheStudioHowToReachItAndHowToRebook()
    {
        (Guid appointmentId, _) = await SeedWithStudio(CancellationReason.StudioCancelled);

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        string body = SentBody();
        body.Should().Contain("Your appointment at <strong>Ink &amp; Iron</strong>");
        body.Should().Contain("has been cancelled by the studio.");
        body.Should().Contain("Reply to this email to reach Ink &amp; Iron, or call +355 69 111 2222.");
        body.Should().Contain("href=\"https://app.example.test/s/ink-iron\"");
        body.Should().Contain("Ink &amp; Iron &middot; Powered by TattooOS");
        body.Should().NotContain("Your Tattoo Studio").And.NotContain("please contact us");
        // Replies go to the studio owner.
        await _notifications.Received(1).SendEmailAsync(
            "ana@example.com", Arg.Any<string>(), Arg.Any<string>(), "owner@example.test", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StudioWithoutPhoneOrBranding_OmitsThem()
    {
        (Guid appointmentId, _) = await SeedWithStudio(CancellationReason.StudioCancelled, branding: false, studioPhone: null);

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        string body = SentBody();
        body.Should().Contain("Reply to this email to reach Ink &amp; Iron.");
        body.Should().NotContain("or call").And.NotContain("Powered by TattooOS");
    }

    [Fact]
    public async Task Handle_DeclinedRequest_UsesTheDeclineWording()
    {
        (Guid appointmentId, _) = await SeedWithStudio(CancellationReason.StudioDeclined);

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(), "Booking request declined — Ink & Iron", Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        SentBody().Should().Contain("can't take your booking request for <strong>Thursday, 05 November 2026 at 11:00</strong>");
    }

    [Theory]
    [InlineData(CancellationReason.StudioCancelled,
        "Hi Ana, your appointment at Ink & Iron on Thursday, 05 November 2026 at 11:00 has been cancelled.")]
    [InlineData(CancellationReason.ClientCancelled,
        "Hi Ana, your cancellation is confirmed: your appointment at Ink & Iron on Thursday, 05 November 2026 at 11:00 has been cancelled.")]
    [InlineData(CancellationReason.StudioDeclined,
        "Hi Ana, sorry, Ink & Iron can't take your booking request for Thursday, 05 November 2026 at 11:00.")]
    public async Task Handle_ClientWithPhone_GetsACancellationSms(CancellationReason reason, string expected)
    {
        // The settings page always offered an "Appointment cancelled" SMS switch, but none was sent.
        (Guid appointmentId, _) = await SeedWithStudio(reason);

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        await _notifications.Received(1).SendSmsAsync("+355690000000", expected, Arg.Any<CancellationToken>());
        (await _db.NotificationLogs.CountAsync(n => n.Channel == NotificationChannel.Sms && n.IsSuccess)).Should().Be(1);
    }

    [Fact]
    public async Task Handle_ClientWithoutPhone_SendsNoSms()
    {
        (Guid appointmentId, _) = await SeedWithStudio(CancellationReason.StudioCancelled, phone: null);

        await CreateSut().Handle(new SendAppointmentCancellationCommand(appointmentId), default);

        await _notifications.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
