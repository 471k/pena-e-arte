using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// S01: the review and conduct-report "which visit" pickers name the day on the appointment's studio's calendar,
/// so all four responses carry that studio's zone. Run against real MySQL because the zone is read with a
/// correlated subquery inside the projection, and the in-memory double the unit tests use would accept a shape
/// the real provider cannot translate.
/// </summary>
[Collection("Database")]
public class VisitPickerStudioZoneIntegrationTests(DatabaseFixture fixture)
{
    private const string Zone = "Pacific/Kiritimati";

    [Fact]
    public async Task AllFourVisitPickers_CarryTheAppointmentsStudioZone()
    {
        (Studio studio, Artist artist, Guid clientUserId) = await SeedAsync();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        List<ReviewableAppointmentResponse> reviewStudio = await new GetReviewableStudioAppointmentsHandler(db)
            .Handle(new GetReviewableStudioAppointmentsQuery(studio.Slug, clientUserId), default);
        List<ReviewableAppointmentResponse> reviewArtist = await new GetReviewableArtistAppointmentsHandler(db)
            .Handle(new GetReviewableArtistAppointmentsQuery(artist.Slug!, clientUserId), default);
        List<ReportableAppointmentResponse> reportStudio = await new GetReportableStudioAppointmentsHandler(db)
            .Handle(new GetReportableStudioAppointmentsQuery(studio.Slug, clientUserId), default);
        List<ReportableAppointmentResponse> reportArtist = await new GetReportableArtistAppointmentsHandler(db)
            .Handle(new GetReportableArtistAppointmentsQuery(artist.Slug!, clientUserId), default);

        reviewStudio.Should().ContainSingle().Which.StudioTimezone.Should().Be(Zone);
        reviewArtist.Should().ContainSingle().Which.StudioTimezone.Should().Be(Zone);
        reportStudio.Should().ContainSingle().Which.StudioTimezone.Should().Be(Zone);
        reportArtist.Should().ContainSingle().Which.StudioTimezone.Should().Be(Zone);
    }

    private async Task<(Studio, Artist, Guid)> SeedAsync()
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Zone Studio",
            Slug = "zone-" + Guid.NewGuid().ToString("N")[..8],
            City = "Tirana",
            IsActive = true,
            IsPublished = true,
            Timezone = Zone,
        };
        seed.Studios.Add(studio);
        await seed.SaveChangesAsync();

        Artist artist = new() { StudioId = studio.Id, FirstName = "Z", LastName = "A", Email = $"{Guid.NewGuid():N}@z.test" };
        artist.SetSlug("zoned-" + Guid.NewGuid().ToString("N")[..8]);
        seed.Artists.Add(artist);

        Guid clientUserId = Guid.NewGuid();
        Client client = new()
        {
            StudioId = studio.Id,
            UserId = clientUserId,
            FirstName = "C",
            LastName = "L",
            Email = $"{Guid.NewGuid():N}@c.test",
        };
        seed.Clients.Add(client);
        await seed.SaveChangesAsync();

        DateTime start = DateTime.UtcNow.AddDays(-7);
        seed.Appointments.Add(new Appointment
        {
            StudioId = studio.Id,
            ArtistId = artist.Id,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Completed,
        });
        await seed.SaveChangesAsync();
        return (studio, artist, clientUserId);
    }
}
