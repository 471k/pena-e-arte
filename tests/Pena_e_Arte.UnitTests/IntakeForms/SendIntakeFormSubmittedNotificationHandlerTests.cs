using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class SendIntakeFormSubmittedNotificationHandlerTests
{
    private readonly FakeDbContext        _db            = FakeDbContext.Create();
    private readonly IEmailRenderer       _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier    _realtime      = Substitute.For<IRealtimeNotifier>();

    public SendIntakeFormSubmittedNotificationHandlerTests() =>
        _emailRenderer
            .RenderIntakeFormSubmitted(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns("<html>intake</html>");

    private SendIntakeFormSubmittedNotificationHandler CreateSut() =>
        new(_db, _emailRenderer, _notifications, _realtime,
            NullLogger<SendIntakeFormSubmittedNotificationHandler>.Instance);

    private async Task<(Guid formId, Studio studio)> SeedData(bool withAppointment = false)
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

        Guid? appointmentId = null;
        if (withAppointment)
        {
            Appointment appointment = new()
            {
                StudioId        = studio.Id,
                ArtistId        = Guid.NewGuid(),
                ClientId        = client.Id,
                Date            = DateTime.UtcNow.AddDays(3),
                EndDate         = DateTime.UtcNow.AddDays(3).AddHours(2),
                DurationMinutes = 120,
                Status          = AppointmentStatus.Pending,
                DepositStatus   = DepositStatus.Pending,
            };
            _db.Appointments.Add(appointment);
            appointmentId = appointment.Id;
        }

        IntakeForm form = new()
        {
            StudioId      = studio.Id,
            ClientId      = client.Id,
            Client        = client,
            AppointmentId = appointmentId,
            FormData      = "{}",
            SubmittedAt   = DateTime.UtcNow,
        };
        _db.IntakeForms.Add(form);
        await _db.SaveChangesAsync();
        return (form.Id, studio);
    }

    [Fact]
    public async Task Handle_ValidInput_SendsEmailToStudioOwner()
    {
        (Guid formId, Studio studio) = await SeedData();

        await CreateSut().Handle(new SendIntakeFormSubmittedNotificationCommand(formId), default);

        await _notifications.Received(1)
            .SendEmailAsync(studio.OwnerEmail, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EntityNotFound_DoesNotSendEmail()
    {
        await CreateSut().Handle(new SendIntakeFormSubmittedNotificationCommand(Guid.NewGuid()), default);

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

        Func<Task> act = () => CreateSut().Handle(new SendIntakeFormSubmittedNotificationCommand(formId), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_EmailFails_WritesFailedNotificationLog()
    {
        (Guid formId, _) = await SeedData();
        _notifications
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await CreateSut().Handle(new SendIntakeFormSubmittedNotificationCommand(formId), default);

        NotificationLog? log = await _db.NotificationLogs.FirstOrDefaultAsync();
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ValidInput_WritesSuccessNotificationLog()
    {
        (Guid formId, Studio studio) = await SeedData();

        await CreateSut().Handle(new SendIntakeFormSubmittedNotificationCommand(formId), default);

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

        await CreateSut().Handle(new SendIntakeFormSubmittedNotificationCommand(formId), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(studio.Id, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FormWithAppointment_IncludesAppointmentDateInEmail()
    {
        (Guid formId, _) = await SeedData(withAppointment: true);

        await CreateSut().Handle(new SendIntakeFormSubmittedNotificationCommand(formId), default);

        _emailRenderer.Received(1).RenderIntakeFormSubmitted(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(s => s != "(no appointment date)"),
            Arg.Any<bool>());
    }

    [Fact]
    public async Task Handle_FormWithoutAppointment_UsesFallbackDate()
    {
        (Guid formId, _) = await SeedData(withAppointment: false);

        await CreateSut().Handle(new SendIntakeFormSubmittedNotificationCommand(formId), default);

        _emailRenderer.Received(1).RenderIntakeFormSubmitted(
            Arg.Any<string>(), Arg.Any<string>(),
            "(no appointment date)",
            Arg.Any<bool>());
    }
}
