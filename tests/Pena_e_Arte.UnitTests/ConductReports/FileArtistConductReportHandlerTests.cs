using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.ConductReports.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class FileArtistConductReportHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private FileArtistConductReportHandler CreateSut() =>
        new(_db, _notifications, NullLogger<FileArtistConductReportHandler>.Instance);

    private async Task<Artist> SeedArtist(string slug = "maria-silva")
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = "Ink Studio",
            Slug = "ink-studio",
            City = "Lisbon",
            IsActive = true,
            OwnerEmail = "owner@ink-studio.test",
        });

        Artist artist = new() { StudioId = studioId, FirstName = "Maria", LastName = "Silva", Email = "maria@example.com" };
        artist.SetSlug(slug);
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist;
    }

    private async Task<Appointment> SeedAppointment(
        Guid studioId, Guid artistId, Guid reporterUserId, AppointmentStatus status)
    {
        Client client = new()
        {
            StudioId = studioId,
            UserId = reporterUserId,
            FirstName = "Ana",
            LastName = "Silva",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);

        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = artistId,
            ClientId = client.Id,
            Date = DateTime.UtcNow.AddDays(-2),
            EndDate = DateTime.UtcNow.AddDays(-2).AddHours(1),
            DurationMinutes = 60,
            Status = status,
            DepositStatus = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    [Fact]
    public async Task Handle_ValidAppointment_CreatesReport()
    {
        Artist artist = await SeedArtist();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, reporterId, AppointmentStatus.Completed);

        FileArtistConductReportCommand command = new(
            artist.Slug!, appt.Id, reporterId, "Ana Silva", ReportCategory.Harassment,
            "The artist yelled at me and made me uncomfortable throughout the session.", null);

        await CreateSut().Handle(command, CancellationToken.None);

        _db.ConductReports.Should().ContainSingle(r =>
            r.ArtistId == artist.Id &&
            r.StudioId == artist.StudioId &&
            r.AppointmentId == appt.Id &&
            r.ReporterUserId == reporterId &&
            r.Category == ReportCategory.Harassment);
    }

    [Fact]
    public async Task Handle_NonCompletedAppointment_StillSucceeds()
    {
        // Copy-paste guard: Review's eligibility gates on AppointmentStatus.Completed —
        // conduct reports deliberately do NOT, so a studio can't dodge a report by never
        // marking the appointment complete.
        Artist artist = await SeedArtist();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, reporterId, AppointmentStatus.Pending);

        FileArtistConductReportCommand command = new(
            artist.Slug!, appt.Id, reporterId, "Ana Silva", ReportCategory.Scam,
            "This artist scammed me out of a deposit for work never performed.", null);

        await CreateSut().Handle(command, CancellationToken.None);

        _db.ConductReports.Should().ContainSingle(r => r.AppointmentId == appt.Id);
    }

    [Fact]
    public async Task Handle_SecondReportAgainstSameAppointment_StillSucceeds()
    {
        // Copy-paste guard: reviews dedup on (AppointmentId, ArtistId) — conduct reports
        // deliberately do NOT, since a client may need to file more than one report (a second
        // incident, or more detail) against the same visit.
        Artist artist = await SeedArtist();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, reporterId, AppointmentStatus.Completed);

        FileArtistConductReportCommand first = new(
            artist.Slug!, appt.Id, reporterId, "Ana Silva", ReportCategory.PoorServiceQuality,
            "The line work was sloppy and not what we agreed on.", null);
        await CreateSut().Handle(first, CancellationToken.None);

        FileArtistConductReportCommand second = new(
            artist.Slug!, appt.Id, reporterId, "Ana Silva", ReportCategory.Harassment,
            "On reflection, the artist was also verbally abusive during the same visit.", null);
        await CreateSut().Handle(second, CancellationToken.None);

        _db.ConductReports.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_AppointmentBelongsToDifferentClient_ThrowsNotFoundException()
    {
        Artist artist = await SeedArtist();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, Guid.NewGuid(), AppointmentStatus.Completed);

        FileArtistConductReportCommand command = new(
            artist.Slug!, appt.Id, Guid.NewGuid(), "Impersonator", ReportCategory.Other,
            "Trying to report an appointment that isn't mine.", null);

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_AppointmentBelongsToDifferentArtist_ThrowsNotFoundException()
    {
        Artist artistA = await SeedArtist("artist-a");
        Artist artistB = await SeedArtist("artist-b");
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artistA.StudioId, artistA.Id, reporterId, AppointmentStatus.Completed);

        FileArtistConductReportCommand command = new(
            artistB.Slug!, appt.Id, reporterId, "Ana Silva", ReportCategory.Other,
            "Wrong artist slug used for this appointment.", null);

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ArtistNotFound_ThrowsNotFoundException()
    {
        FileArtistConductReportCommand command = new(
            "nonexistent-slug", Guid.NewGuid(), Guid.NewGuid(), "Someone", ReportCategory.Other,
            "Reporting an artist that does not exist.", null);

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_HighSeverityCategory_SendsAlertEmails()
    {
        Artist artist = await SeedArtist();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, reporterId, AppointmentStatus.Completed);

        FileArtistConductReportCommand command = new(
            artist.Slug!, appt.Id, reporterId, "Ana Silva", ReportCategory.SexualMisconduct,
            "This is a serious safety concern I need reviewed immediately.", null);

        await CreateSut().Handle(command, CancellationToken.None);

        await _notifications.Received(2).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlertEmailSendThrows_StillSucceedsAndReportIsSaved()
    {
        // The ConductReport row is already committed by the time the alert email is sent —
        // an email provider outage must never turn an already-saved report into a 500.
        _notifications.SendEmailAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("SMTP unavailable"));

        Artist artist = await SeedArtist();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, reporterId, AppointmentStatus.Completed);

        FileArtistConductReportCommand command = new(
            artist.Slug!, appt.Id, reporterId, "Ana Silva", ReportCategory.SexualMisconduct,
            "This is a serious safety concern I need reviewed immediately.", null);

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().NotThrowAsync();
        _db.ConductReports.Should().ContainSingle(r => r.AppointmentId == appt.Id);
    }

    [Fact]
    public async Task Handle_StandardSeverityCategory_SendsNoEmail()
    {
        Artist artist = await SeedArtist();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, reporterId, AppointmentStatus.Completed);

        FileArtistConductReportCommand command = new(
            artist.Slug!, appt.Id, reporterId, "Ana Silva", ReportCategory.PoorServiceQuality,
            "The service quality was below what I expected for the price.", null);

        await CreateSut().Handle(command, CancellationToken.None);

        await _notifications.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
