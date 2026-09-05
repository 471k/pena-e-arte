using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class GetReportableArtistAppointmentsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetReportableArtistAppointmentsHandler CreateSut() => new(_db);

    private async Task<Artist> SeedArtist(string slug = "maria-silva")
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Studio", Slug = "ink-studio", City = "Lisbon", IsActive = true });

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
            Date = DateTime.UtcNow.AddDays(-5),
            EndDate = DateTime.UtcNow.AddDays(-5).AddHours(1),
            DurationMinutes = 60,
            Status = status,
            DepositStatus = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    [Fact]
    public async Task Returns_non_completed_appointment_unlike_the_reviewable_equivalent()
    {
        // Copy-paste guard: GetReviewableArtistAppointmentsHandler filters to Completed —
        // this query deliberately does not.
        Artist artist = await SeedArtist();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, reporterId, AppointmentStatus.Pending);

        List<ReportableAppointmentResponse> result = await CreateSut().Handle(
            new GetReportableArtistAppointmentsQuery(artist.Slug!, reporterId), CancellationToken.None);

        result.Should().ContainSingle(a => a.Id == appt.Id && a.Status == "Pending");
    }

    [Fact]
    public async Task Includes_appointment_even_if_already_reported()
    {
        // Copy-paste guard: no dedup exclusion, unlike GetReviewableArtistAppointmentsHandler.
        Artist artist = await SeedArtist();
        Guid reporterId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, reporterId, AppointmentStatus.Completed);

        _db.ConductReports.Add(ConductReport.ForArtist(
            artist.StudioId, artist.Id, appt.Id, reporterId, "Ana Silva",
            ReportCategory.Other, "Already filed one report about this same appointment."));
        await _db.SaveChangesAsync();

        List<ReportableAppointmentResponse> result = await CreateSut().Handle(
            new GetReportableArtistAppointmentsQuery(artist.Slug!, reporterId), CancellationToken.None);

        result.Should().ContainSingle(a => a.Id == appt.Id);
    }

    [Fact]
    public async Task Excludes_appointments_with_a_different_artist_at_the_same_studio()
    {
        Artist artistA = await SeedArtist("artist-a");
        Guid reporterId = Guid.NewGuid();
        Guid otherArtistId = Guid.NewGuid();
        await SeedAppointment(artistA.StudioId, otherArtistId, reporterId, AppointmentStatus.Completed);

        List<ReportableAppointmentResponse> result = await CreateSut().Handle(
            new GetReportableArtistAppointmentsQuery(artistA.Slug!, reporterId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_empty_list_when_artist_not_found()
    {
        List<ReportableAppointmentResponse> result = await CreateSut().Handle(
            new GetReportableArtistAppointmentsQuery("nonexistent", Guid.NewGuid()), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
