using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reviews;

public class GetReviewableArtistAppointmentsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetReviewableArtistAppointmentsHandler CreateSut() => new(_db);

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
        Guid studioId, Guid artistId, Guid authorUserId, AppointmentStatus status = AppointmentStatus.Completed)
    {
        Client client = new()
        {
            StudioId  = studioId,
            UserId    = authorUserId,
            FirstName = "Ana",
            LastName  = "Silva",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);

        Appointment appointment = new()
        {
            StudioId        = studioId,
            ArtistId        = artistId,
            ClientId        = client.Id,
            Date            = DateTime.UtcNow.AddDays(-5),
            EndDate         = DateTime.UtcNow.AddDays(-5).AddHours(1),
            DurationMinutes = 60,
            Status          = status,
            DepositStatus   = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    [Fact]
    public async Task Returns_completed_unreviewed_appointment_for_the_author()
    {
        Artist artist   = await SeedArtist();
        Guid   authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, authorId);

        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableArtistAppointmentsQuery(artist.Slug!, authorId), CancellationToken.None);

        result.Should().ContainSingle(a => a.Id == appt.Id);
    }

    [Fact]
    public async Task Excludes_appointments_with_a_different_artist_at_the_same_studio()
    {
        Artist artistA = await SeedArtist("artist-a");
        Guid   authorId = Guid.NewGuid();
        Guid   otherArtistId = Guid.NewGuid();
        await SeedAppointment(artistA.StudioId, otherArtistId, authorId);

        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableArtistAppointmentsQuery(artistA.Slug!, authorId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Excludes_appointments_already_reviewed()
    {
        Artist artist   = await SeedArtist();
        Guid   authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, authorId);

        _db.Reviews.Add(Review.ForArtist(artist.Id, appt.Id, authorId, "Ana Silva", 5, "Already reviewed this one"));
        await _db.SaveChangesAsync();

        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableArtistAppointmentsQuery(artist.Slug!, authorId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_empty_list_when_artist_not_found()
    {
        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableArtistAppointmentsQuery("nonexistent", Guid.NewGuid()), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
