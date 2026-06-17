using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class SendDepositCapturedNotificationHandlerTests
{
    private readonly FakeDbContext        _db            = FakeDbContext.Create();
    private readonly IEmailRenderer       _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier    _realtime      = Substitute.For<IRealtimeNotifier>();

    public SendDepositCapturedNotificationHandlerTests() =>
        _emailRenderer
            .RenderDepositCaptured(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns("<html>deposit</html>");

    private SendDepositCapturedNotificationHandler CreateSut() =>
        new(_db, _emailRenderer, _notifications, _realtime,
            NullLogger<SendDepositCapturedNotificationHandler>.Instance);

    private async Task<(Guid paymentId, Studio studio, Client client)> SeedData(string? phone = null)
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
            Date            = DateTime.UtcNow.AddDays(3),
            EndDate         = DateTime.UtcNow.AddDays(3).AddHours(2),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Confirmed,
            DepositStatus   = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);

        Payment payment = new()
        {
            StudioId      = studio.Id,
            ClientId      = client.Id,
            AppointmentId = appointment.Id,
            Client        = client,
            Appointment   = appointment,
            Amount        = 100m,
            Status        = PaymentStatus.Paid,
            Method        = ClientPaymentMethod.Card,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return (payment.Id, studio, client);
    }

    [Fact]
    public async Task Handle_ValidInput_SendsEmailToClient()
    {
        (Guid paymentId, _, Client client) = await SeedData();

        await CreateSut().Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        await _notifications.Received(1)
            .SendEmailAsync(client.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EntityNotFound_DoesNotSendEmail()
    {
        await CreateSut().Handle(new SendDepositCapturedNotificationCommand(Guid.NewGuid()), default);

        await _notifications.DidNotReceive()
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmailFails_DoesNotThrow()
    {
        (Guid paymentId, _, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        Func<Task> act = () => CreateSut().Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_EmailFails_WritesFailedNotificationLog()
    {
        (Guid paymentId, _, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await CreateSut().Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidInput_WritesSuccessNotificationLog()
    {
        (Guid paymentId, Studio studio, Client client) = await SeedData();

        await CreateSut().Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
        log.StudioId.Should().Be(studio.Id);
        log.RecipientId.Should().Be(client.Id);
        log.RecipientType.Should().Be(NotificationRecipientType.Client);
    }

    [Fact]
    public async Task Handle_ClientHasPhone_SendsSms()
    {
        (Guid paymentId, _, _) = await SeedData(phone: "+351912345678");

        await CreateSut().Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        await _notifications.Received(1)
            .SendSmsAsync("+351912345678", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ClientHasNoPhone_DoesNotSendSms()
    {
        (Guid paymentId, _, _) = await SeedData(phone: null);

        await CreateSut().Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        await _notifications.DidNotReceive()
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SmsFails_DoesNotThrow()
    {
        (Guid paymentId, _, _) = await SeedData(phone: "+351912345678");
        _notifications
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Twilio down"));

        Func<Task> act = () => CreateSut().Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_SmsFails_WritesFailedSmsLog()
    {
        (Guid paymentId, _, _) = await SeedData(phone: "+351912345678");
        _notifications
            .SendSmsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Twilio down"));

        await CreateSut().Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        NotificationLog? smsLog = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Sms);
        smsLog.Should().NotBeNull();
        smsLog!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidInput_PushesNotificationReceivedEvent()
    {
        (Guid paymentId, Studio studio, _) = await SeedData();

        await CreateSut().Handle(new SendDepositCapturedNotificationCommand(paymentId), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(studio.Id, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }
}
