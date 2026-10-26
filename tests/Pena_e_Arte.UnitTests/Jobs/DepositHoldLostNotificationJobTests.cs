using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class DepositHoldLostNotificationJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly INotificationPreferenceService _prefs = Substitute.For<INotificationPreferenceService>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();

    public DepositHoldLostNotificationJobTests()
    {
        _emailRenderer
            .RenderDepositHoldLost(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns("<html>hold lost</html>");
        _prefs.IsEnabledAsync(Arg.Any<Guid>(), Arg.Any<NotificationType>(), Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>())
              .Returns(true);
        _appSettings.BaseUrl.Returns("https://app.example.test");
    }

    private DepositHoldLostNotificationJob CreateSut() =>
        new(_db, _emailRenderer, _notifications, _prefs, _realtime, _appSettings,
            NullLogger<DepositHoldLostNotificationJob>.Instance);

    private async Task<(Payment payment, Studio studio)> SeedData(
        PaymentStatus status = PaymentStatus.Failed, ClientPaymentMethod method = ClientPaymentMethod.Card)
    {
        Studio studio = new()
        {
            Name = "Test Studio",
            Slug = "test",
            OwnerEmail = "owner@test.com",
            Timezone = "Europe/Paris",
        };
        _db.Studios.Add(studio);

        Client client = new() { StudioId = studio.Id, FirstName = "Ana", LastName = "Silva", Email = "ana@test.com" };
        _db.Clients.Add(client);

        Appointment appointment = new()
        {
            StudioId = studio.Id,
            ArtistId = Guid.NewGuid(),
            ClientId = client.Id,
            Date = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc),
            DurationMinutes = 120,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);

        Payment payment = new()
        {
            StudioId = studio.Id,
            ClientId = client.Id,
            AppointmentId = appointment.Id,
            Client = client,
            Appointment = appointment,
            Amount = 1234m,
            Currency = "ALL",
            Status = status,
            Method = method,
            ProviderReferenceId = "pok_order_1",
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return (payment, studio);
    }

    [Fact]
    public async Task ExecuteAsync_FailedCardPayment_EmailsTheOwner()
    {
        (Payment payment, Studio studio) = await SeedData();

        await CreateSut().ExecuteAsync(payment.Id);

        await _notifications.Received(1).SendEmailAsync(
            studio.OwnerEmail, DepositHoldLostNotificationJob.Subject, "<html>hold lost</html>", Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_FrozenLekReference_ShowsItToTheOwnerToo()
    {
        // Spec Q3, decided 2026-10-26: staff see the lek next to euro amounts as well.
        (Payment payment, _) = await SeedData();
        payment.Amount = 100m;
        payment.Currency = "EUR";
        payment.ReferenceCurrency = "ALL";
        payment.ReferenceRate = 90.74m;
        payment.ReferenceRateDate = new DateOnly(2026, 10, 23);
        payment.ReferenceRateSource = "BoA-Fix";
        payment.ReferenceAmount = 9074m;
        await _db.SaveChangesAsync();

        await CreateSut().ExecuteAsync(payment.Id);

        _emailRenderer.Received(1).RenderDepositHoldLost(
            "Ana Silva", "100.00 EUR (9,074 ALL)", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task ExecuteAsync_RendersStudioLocalTimeAmountAndPaymentLink()
    {
        (Payment payment, _) = await SeedData();

        await CreateSut().ExecuteAsync(payment.Id);

        // 10:00 UTC on 5 Nov 2026 is 11:00 in Paris (CET, after the October change).
        _emailRenderer.Received(1).RenderDepositHoldLost(
            "Ana Silva",
            Arg.Is<string>(s => s.Contains("1,234") || s.Contains("1234")),
            "Thursday, 05 November 2026 at 11:00",
            $"https://app.example.test/payments/{payment.AppointmentId}",
            Arg.Any<bool>());
    }

    [Fact]
    public async Task ExecuteAsync_WritesABellEntryForTheStudioAndPushesIt()
    {
        (Payment payment, Studio studio) = await SeedData();

        await CreateSut().ExecuteAsync(payment.Id);

        NotificationLog log = _db.NotificationLogs.Single();
        log.StudioId.Should().Be(studio.Id);
        log.RecipientId.Should().Be(studio.Id);
        log.RecipientType.Should().Be(NotificationRecipientType.Studio);
        log.Subject.Should().Be(DepositHoldLostNotificationJob.Subject);
        log.IsSuccess.Should().BeTrue();
        await _realtime.Received(1).NotifyStudioAsync(
            studio.Id, "NotificationReceived", Arg.Any<NotificationLogResponse>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_OwnerTurnedItOff_SendsNothing()
    {
        (Payment payment, Studio studio) = await SeedData();
        _prefs.IsEnabledAsync(studio.Id, NotificationType.DepositHoldLost, NotificationChannel.Email, Arg.Any<CancellationToken>())
              .Returns(false);

        await CreateSut().ExecuteAsync(payment.Id);

        await _notifications.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.NotificationLogs.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]  // the owner already asked for a new payment (the row is reused)
    [InlineData(PaymentStatus.CashPending)]
    [InlineData(PaymentStatus.Paid)]
    public async Task ExecuteAsync_PaymentNoLongerFailed_SendsNothing(PaymentStatus now)
    {
        (Payment payment, _) = await SeedData(now);

        await CreateSut().ExecuteAsync(payment.Id);

        await _notifications.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_UnknownPayment_SendsNothing()
    {
        await CreateSut().ExecuteAsync(Guid.NewGuid());

        await _notifications.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_EmailFails_LogsAFailedEntryAndDoesNotThrow()
    {
        (Payment payment, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mail down"));

        Func<Task> act = () => CreateSut().ExecuteAsync(payment.Id);

        await act.Should().NotThrowAsync();
        _db.NotificationLogs.Single().IsSuccess.Should().BeFalse();
    }
}

public class DepositHoldLostEmailTemplateTests
{
    [Fact]
    public void RenderDepositHoldLost_FillsEveryValueAndEncodesTheClientName()
    {
        string html = new Pena_e_Arte.Infrastructure.Services.MailKit.EmailRenderer().RenderDepositHoldLost(
            "Ana <b>Silva</b>", "1,234 ALL", "Thursday, 05 November 2026 at 11:00",
            "https://app.example.test/payments/abc", showBranding: false);

        html.Should().Contain("Ana &lt;b&gt;Silva&lt;/b&gt;")
            .And.Contain("1,234 ALL")
            .And.Contain("Thursday, 05 November 2026 at 11:00")
            .And.Contain("href=\"https://app.example.test/payments/abc\"")
            .And.Contain("New payment")
            .And.NotContain("{{")
            .And.NotContain("TattooOS Studio Platform");
    }
}
