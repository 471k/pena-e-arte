using FluentAssertions;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reviews;

public class CreateStudioReviewHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private CreateStudioReviewHandler CreateSut() => new(_db);

    private async Task<Studio> SeedStudio(string slug = "test-studio")
    {
        Studio studio = new() { Name = "Test Studio", Slug = slug, City = "Porto", IsActive = true };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        return studio;
    }

    private async Task<Appointment> SeedAppointment(
        Guid studioId, Guid authorUserId, AppointmentStatus status = AppointmentStatus.Completed)
    {
        Client client = new()
        {
            StudioId = studioId,
            UserId = authorUserId,
            FirstName = "Ana",
            LastName = "Silva",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);

        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = client.Id,
            Date = DateTime.UtcNow.AddDays(-10),
            EndDate = DateTime.UtcNow.AddDays(-10).AddHours(1),
            DurationMinutes = 60,
            Status = status,
            DepositStatus = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    [Fact]
    public async Task Creates_review_when_appointment_is_completed_and_belongs_to_author()
    {
        Studio studio = await SeedStudio();
        Guid authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studio.Id, authorId);

        CreateStudioReviewCommand command = new(
            studio.Slug, appt.Id, authorId, "Ana Silva", 5, "Absolutely incredible studio!");

        await CreateSut().Handle(command, CancellationToken.None);

        _db.Reviews.Should().ContainSingle(r =>
            r.StudioId == studio.Id &&
            r.AppointmentId == appt.Id &&
            r.Rating == 5 &&
            r.Body == "Absolutely incredible studio!");
    }

    [Fact]
    public async Task Throws_NotFoundException_when_studio_not_found()
    {
        CreateStudioReviewCommand command = new(
            "nonexistent-slug", Guid.NewGuid(), Guid.NewGuid(), "Someone", 4, "Great experience here!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_NotFoundException_when_appointment_does_not_exist()
    {
        Studio studio = await SeedStudio();

        CreateStudioReviewCommand command = new(
            studio.Slug, Guid.NewGuid(), Guid.NewGuid(), "Someone", 4, "Great experience here!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_NotFoundException_when_appointment_belongs_to_a_different_studio()
    {
        Studio studioA = await SeedStudio("studio-a");
        Studio studioB = await SeedStudio("studio-b");
        Guid authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studioA.Id, authorId);

        CreateStudioReviewCommand command = new(
            studioB.Slug, appt.Id, authorId, "Ana Silva", 5, "Wrong studio!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_NotFoundException_when_appointment_belongs_to_a_different_client()
    {
        Studio studio = await SeedStudio();
        Appointment appt = await SeedAppointment(studio.Id, Guid.NewGuid());

        CreateStudioReviewCommand command = new(
            studio.Slug, appt.Id, Guid.NewGuid(), "Impersonator", 5, "Not my appointment!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_BusinessRuleViolationException_when_appointment_not_completed()
    {
        Studio studio = await SeedStudio();
        Guid authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studio.Id, authorId, AppointmentStatus.Confirmed);

        CreateStudioReviewCommand command = new(
            studio.Slug, appt.Id, authorId, "Ana Silva", 5, "Trying to review too early!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Throws_ConflictException_when_appointment_already_reviewed()
    {
        Studio studio = await SeedStudio();
        Guid authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studio.Id, authorId);

        Review existing = Review.ForStudio(studio.Id, appt.Id, authorId, "Ana Silva", 4, "First review text here");
        _db.Reviews.Add(existing);
        await _db.SaveChangesAsync();

        CreateStudioReviewCommand command = new(
            studio.Slug, appt.Id, authorId, "Ana Silva", 5, "Trying to review again!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*already reviewed*");
    }

    [Fact]
    public async Task Allows_a_second_review_from_a_different_completed_appointment()
    {
        Studio studio = await SeedStudio();
        Guid authorId = Guid.NewGuid();
        Appointment firstAppt = await SeedAppointment(studio.Id, authorId);

        await CreateSut().Handle(
            new CreateStudioReviewCommand(studio.Slug, firstAppt.Id, authorId, "Ana Silva", 5, "First visit was great!"),
            CancellationToken.None);

        Appointment secondAppt = await SeedAppointment(studio.Id, authorId);

        await CreateSut().Handle(
            new CreateStudioReviewCommand(studio.Slug, secondAppt.Id, authorId, "Ana Silva", 4, "Second visit, still good!"),
            CancellationToken.None);

        _db.Reviews.Should().HaveCount(2);
    }

    [Fact]
    public void Validator_rejects_rating_below_1()
    {
        CreateStudioReviewValidator validator = new();
        CreateStudioReviewCommand command = new(
            "some-studio", Guid.NewGuid(), Guid.NewGuid(), "Ana Silva", 0, "Some body text here that is long enough");

        validator.ShouldFailOn(command, nameof(command.Rating));
    }

    [Fact]
    public void Validator_rejects_body_shorter_than_10_chars()
    {
        CreateStudioReviewValidator validator = new();
        CreateStudioReviewCommand command = new(
            "some-studio", Guid.NewGuid(), Guid.NewGuid(), "Ana Silva", 4, "short");

        validator.ShouldFailOn(command, nameof(command.Body));
    }

    [Fact]
    public void Validator_rejects_empty_appointment_id()
    {
        CreateStudioReviewValidator validator = new();
        CreateStudioReviewCommand command = new(
            "some-studio", Guid.Empty, Guid.NewGuid(), "Ana Silva", 4, "Some body text here that is long enough");

        validator.ShouldFailOn(command, nameof(command.AppointmentId));
    }
}
