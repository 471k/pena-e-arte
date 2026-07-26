using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reviews;

public class GetReviewableStudioAppointmentsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetReviewableStudioAppointmentsHandler CreateSut() => new(_db);

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
    public async Task Returns_completed_unreviewed_appointment_for_the_author()
    {
        Studio studio = await SeedStudio();
        Guid authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studio.Id, authorId);

        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableStudioAppointmentsQuery(studio.Slug, authorId), CancellationToken.None);

        result.Should().ContainSingle(a => a.Id == appt.Id);
    }

    [Fact]
    public async Task Excludes_appointments_that_are_not_completed()
    {
        Studio studio = await SeedStudio();
        Guid authorId = Guid.NewGuid();
        await SeedAppointment(studio.Id, authorId, AppointmentStatus.Confirmed);

        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableStudioAppointmentsQuery(studio.Slug, authorId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Excludes_appointments_belonging_to_another_client()
    {
        Studio studio = await SeedStudio();
        await SeedAppointment(studio.Id, Guid.NewGuid());

        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableStudioAppointmentsQuery(studio.Slug, Guid.NewGuid()), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Excludes_appointments_already_reviewed()
    {
        Studio studio = await SeedStudio();
        Guid authorId = Guid.NewGuid();
        Appointment appt = await SeedAppointment(studio.Id, authorId);

        _db.Reviews.Add(Review.ForStudio(studio.Id, appt.Id, authorId, "Ana Silva", 5, "Already reviewed this one"));
        await _db.SaveChangesAsync();

        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableStudioAppointmentsQuery(studio.Slug, authorId), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Returns_multiple_eligible_appointments_ordered_most_recent_first()
    {
        Studio studio = await SeedStudio();
        Guid authorId = Guid.NewGuid();
        Appointment older = await SeedAppointment(studio.Id, authorId);
        older.Date = DateTime.UtcNow.AddDays(-30);
        Appointment newer = await SeedAppointment(studio.Id, authorId);
        newer.Date = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableStudioAppointmentsQuery(studio.Slug, authorId), CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Id.Should().Be(newer.Id);
        result[1].Id.Should().Be(older.Id);
    }

    [Fact]
    public async Task Returns_empty_list_when_studio_not_found()
    {
        List<ReviewableAppointmentResponse> result = await CreateSut().Handle(
            new GetReviewableStudioAppointmentsQuery("nonexistent", Guid.NewGuid()), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
