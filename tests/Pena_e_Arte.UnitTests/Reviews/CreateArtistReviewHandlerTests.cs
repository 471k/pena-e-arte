using FluentAssertions;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reviews;

public class CreateArtistReviewHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private CreateArtistReviewHandler CreateSut() => new(_db);

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
            Date            = DateTime.UtcNow.AddDays(-10),
            EndDate         = DateTime.UtcNow.AddDays(-10).AddHours(1),
            DurationMinutes = 60,
            Status          = status,
            DepositStatus   = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    [Fact]
    public async Task Creates_review_when_appointment_is_completed_and_belongs_to_author()
    {
        Artist artist   = await SeedArtist();
        Guid   authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, authorId);

        CreateArtistReviewCommand command = new(
            artist.Slug!, appt.Id, authorId, "Ana Silva", 5, "Amazing tattoo, will be back!");

        await CreateSut().Handle(command, CancellationToken.None);

        _db.Reviews.Should().ContainSingle(r =>
            r.ArtistId      == artist.Id &&
            r.AppointmentId == appt.Id   &&
            r.Rating        == 5         &&
            r.Body          == "Amazing tattoo, will be back!");
    }

    [Fact]
    public async Task Throws_NotFoundException_when_artist_not_found()
    {
        CreateArtistReviewCommand command = new(
            "nonexistent-slug", Guid.NewGuid(), Guid.NewGuid(), "Someone", 4, "Great experience here!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_NotFoundException_when_appointment_does_not_exist()
    {
        Artist artist = await SeedArtist();

        CreateArtistReviewCommand command = new(
            artist.Slug!, Guid.NewGuid(), Guid.NewGuid(), "Someone", 4, "Great experience here!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_NotFoundException_when_appointment_belongs_to_a_different_artist()
    {
        Artist artistA = await SeedArtist("artist-a");
        Artist artistB = await SeedArtist("artist-b");
        Guid   authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artistA.StudioId, artistA.Id, authorId);

        CreateArtistReviewCommand command = new(
            artistB.Slug!, appt.Id, authorId, "Ana Silva", 5, "Wrong artist!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_NotFoundException_when_appointment_belongs_to_a_different_client()
    {
        Artist artist = await SeedArtist();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, Guid.NewGuid());

        CreateArtistReviewCommand command = new(
            artist.Slug!, appt.Id, Guid.NewGuid(), "Impersonator", 5, "Not my appointment!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_BusinessRuleViolationException_when_appointment_not_completed()
    {
        Artist artist   = await SeedArtist();
        Guid   authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, authorId, AppointmentStatus.Pending);

        CreateArtistReviewCommand command = new(
            artist.Slug!, appt.Id, authorId, "Ana Silva", 5, "Trying to review too early!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Throws_ConflictException_when_appointment_already_reviewed()
    {
        Artist artist   = await SeedArtist();
        Guid   authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, authorId);

        Review existing = Review.ForArtist(artist.Id, appt.Id, authorId, "Ana Silva", 4, "First review text here");
        _db.Reviews.Add(existing);
        await _db.SaveChangesAsync();

        CreateArtistReviewCommand command = new(
            artist.Slug!, appt.Id, authorId, "Ana Silva", 5, "Trying to review again!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*already reviewed*");
    }

    [Fact]
    public async Task Allows_both_a_studio_and_an_artist_review_from_the_same_appointment()
    {
        Artist artist   = await SeedArtist();
        Guid   authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(artist.StudioId, artist.Id, authorId);

        Review studioReview = Review.ForStudio(artist.StudioId, appt.Id, authorId, "Ana Silva", 5, "Loved the studio too!");
        _db.Reviews.Add(studioReview);
        await _db.SaveChangesAsync();

        CreateArtistReviewCommand command = new(
            artist.Slug!, appt.Id, authorId, "Ana Silva", 5, "And the artist was amazing!");

        await CreateSut().Handle(command, CancellationToken.None);

        _db.Reviews.Should().HaveCount(2);
    }

    [Fact]
    public void Validator_rejects_empty_appointment_id()
    {
        CreateArtistReviewValidator validator = new();
        CreateArtistReviewCommand command = new(
            "some-artist", Guid.Empty, Guid.NewGuid(), "Ana Silva", 4, "Some body text here that is long enough");

        validator.ShouldFailOn(command, nameof(command.AppointmentId));
    }

    [Fact]
    public void Validator_rejects_rating_below_1()
    {
        CreateArtistReviewValidator validator = new();
        CreateArtistReviewCommand command = new(
            "some-artist", Guid.NewGuid(), Guid.NewGuid(), "Ana Silva", 0, "Some body text here that is long enough");

        validator.ShouldFailOn(command, nameof(command.Rating));
    }
}
