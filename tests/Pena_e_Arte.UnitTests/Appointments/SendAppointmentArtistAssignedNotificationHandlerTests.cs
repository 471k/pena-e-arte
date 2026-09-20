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

/// <summary>
/// The "let the studio choose my artist" flow: once the owner assigns an artist, the client is told
/// (existing behaviour) and now the assigned artist is too — email plus their notification-bell entry.
/// </summary>
public class SendAppointmentArtistAssignedNotificationHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly INotificationPreferenceService _prefs = new AlwaysEnabledNotificationPreferences();

    public SendAppointmentArtistAssignedNotificationHandlerTests()
    {
        _emailRenderer
            .RenderAppointmentArtistAssigned(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<string>(), Arg.Any<bool>())
            .Returns("<html>client</html>");
        _emailRenderer
            .RenderAppointmentAssignedToArtist(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<string?>())
            .Returns("<html>artist</html>");
    }

    private SendAppointmentArtistAssignedNotificationHandler CreateSut() =>
        new(_db, _emailRenderer, _notifications, _prefs, _realtime,
            NullLogger<SendAppointmentArtistAssignedNotificationHandler>.Instance);

    private async Task<(Guid appointmentId, Studio studio, Client client, Artist artist)> SeedAssignedBooking(
        string artistEmail = "ali@test.com")
    {
        Studio studio = new() { Name = "Test Studio", Slug = "test", OwnerEmail = "owner@test.com" };
        _db.Studios.Add(studio);

        Client client = new()
        {
            StudioId = studio.Id,
            FirstName = "Ana",
            LastName = "Silva",
            Email = "ana@test.com",
        };
        _db.Clients.Add(client);

        Artist artist = new()
        {
            StudioId = studio.Id,
            FirstName = "Ali",
            LastName = "Kreku",
            Email = artistEmail,
        };
        _db.Artists.Add(artist);

        Appointment appointment = new()
        {
            StudioId = studio.Id,
            ArtistId = artist.Id,
            Artist = artist,
            ClientId = client.Id,
            Client = client,
            Date = DateTime.UtcNow.AddDays(3),
            EndDate = DateTime.UtcNow.AddDays(3).AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return (appointment.Id, studio, client, artist);
    }

    [Fact]
    public async Task Handle_Assignment_StillEmailsTheClient()
    {
        (Guid appointmentId, _, Client client, _) = await SeedAssignedBooking();

        await CreateSut().Handle(new SendAppointmentArtistAssignedNotificationCommand(appointmentId), default);

        await _notifications.Received(1)
            .SendEmailAsync(client.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Assignment_EmailsTheAssignedArtist()
    {
        (Guid appointmentId, _, _, Artist artist) = await SeedAssignedBooking();

        await CreateSut().Handle(new SendAppointmentArtistAssignedNotificationCommand(appointmentId), default);

        await _notifications.Received(1)
            .SendEmailAsync(artist.Email, Arg.Is<string>(s => s.Contains("assigned")),
                "<html>artist</html>", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Assignment_WritesArtistAddressedLogForTheirBell()
    {
        (Guid appointmentId, _, _, Artist artist) = await SeedAssignedBooking();

        await CreateSut().Handle(new SendAppointmentArtistAssignedNotificationCommand(appointmentId), default);

        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.RecipientType == NotificationRecipientType.Artist);
        log.Should().NotBeNull();
        log!.RecipientId.Should().Be(artist.Id);
        log.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Assignment_PushesNotificationReceivedSoTheArtistBellRefreshes()
    {
        (Guid appointmentId, Studio studio, _, _) = await SeedAssignedBooking();

        await CreateSut().Handle(new SendAppointmentArtistAssignedNotificationCommand(appointmentId), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(studio.Id, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ArtistIsTheOwner_DoesNotEmailTheSameAddressTwice()
    {
        (Guid appointmentId, _, _, Artist artist) = await SeedAssignedBooking(artistEmail: "owner@test.com");

        await CreateSut().Handle(new SendAppointmentArtistAssignedNotificationCommand(appointmentId), default);

        // The client is emailed; the owner-artist address is not (the owner already gets studio mail),
        // but the artist-addressed bell row is still written.
        await _notifications.DidNotReceive()
            .SendEmailAsync("owner@test.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        (await _db.NotificationLogs.AnyAsync(
            n => n.RecipientType == NotificationRecipientType.Artist && n.RecipientId == artist.Id))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ArtistEmailFails_DoesNotThrowAndLogsFailure()
    {
        (Guid appointmentId, _, _, Artist artist) = await SeedAssignedBooking();
        _notifications
            .SendEmailAsync(artist.Email, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("SMTP down"));

        Func<Task> act = () => CreateSut().Handle(
            new SendAppointmentArtistAssignedNotificationCommand(appointmentId), default);

        await act.Should().NotThrowAsync();
        NotificationLog? log = await _db.NotificationLogs
            .FirstOrDefaultAsync(n => n.RecipientType == NotificationRecipientType.Artist);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }
}
