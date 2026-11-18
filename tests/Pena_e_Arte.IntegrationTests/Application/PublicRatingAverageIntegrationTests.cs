using System.Text.Json;
using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// The public rating averages against real MySQL. Before this, no test asserted an average on real data
/// under either server dialect (the unit tests use the in-memory fake and whole-number means only), so the
/// SQL AVG over a double cast and the API's rounding were unpinned. Seeds use means that are not exactly
/// representable in binary (4,5,5 -> 4.666...) and one exact midpoint (4,4,4,5 -> 4.25), and assert the raw
/// number the API returns, plus the number on the wire, never a formatted string.
///
/// Today's contract, as found: Math.Round(avg, 1) with .NET's default MidpointRounding.ToEven, so 4.25
/// becomes 4.2 (not 4.3). Nothing in the contracts states the precision or the midpoint rule; this pins the
/// current behaviour so a change to either is a deliberate one.
/// </summary>
[Collection("Database")]
public class PublicRatingAverageIntegrationTests(DatabaseFixture fixture)
{
    [Theory]
    [InlineData(new[] { 4, 5, 5 }, 4.7)]
    [InlineData(new[] { 4, 4, 4, 5 }, 4.2)] // exact midpoint 4.25: ToEven rounds down
    [InlineData(new[] { 1, 2 }, 1.5)]
    public async Task StudioAverage_IsTheRoundedMeanOfItsReviews(int[] ratings, double expected)
    {
        (Studio studio, Artist artist) = await SeedAsync();
        await SeedReviews(studio, artist, ratings, (r, appointmentId) => Review.ForStudio(studio.Id, appointmentId, Guid.NewGuid(), "A", r, "ok"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        PublicStudioResponse? result = await new GetPublicStudioHandler(db).Handle(new GetPublicStudioQuery(studio.Slug), default);

        result!.AverageRating.Should().Be(expected);
        result.ReviewCount.Should().Be(ratings.Length);
        Wire(result).Should().Contain($"\"averageRating\":{Json(expected)}");
    }

    [Theory]
    [InlineData(new[] { 4, 5, 5 }, 4.7)]
    [InlineData(new[] { 4, 4, 4, 5 }, 4.2)]
    public async Task ArtistAverage_IsTheSameNumberOnTheArtistPageAndOnTheStudioPage(int[] ratings, double expected)
    {
        (Studio studio, Artist artist) = await SeedAsync();
        await SeedReviews(studio, artist, ratings, (r, appointmentId) => Review.ForArtist(artist.Id, appointmentId, Guid.NewGuid(), "A", r, "ok"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        PublicArtistResponse? artistPage = await new GetPublicArtistHandler(db).Handle(new GetPublicArtistQuery(artist.Slug!, null), default);
        PublicStudioResponse? studioPage = await new GetPublicStudioHandler(db).Handle(new GetPublicStudioQuery(studio.Slug), default);

        artistPage!.AverageRating.Should().Be(expected);
        artistPage.ReviewCount.Should().Be(ratings.Length);
        Wire(artistPage).Should().Contain($"\"averageRating\":{Json(expected)}");

        PublicArtistSummary summary = studioPage!.Artists.Single(a => a.ArtistId == artist.Id);
        summary.AverageRating.Should().Be(expected);
        summary.ReviewCount.Should().Be(ratings.Length);

        // Artist reviews are not studio reviews: the studio's own average stays empty.
        studioPage.AverageRating.Should().BeNull();
        studioPage.ReviewCount.Should().Be(0);
    }

    // The API serializes with the web defaults (camelCase): this is the number a client receives.
    private static string Wire<T>(T response) => JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

    private static string Json(double value) => JsonSerializer.Serialize(value);

    private async Task<(Studio, Artist)> SeedAsync()
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Ratings Studio",
            Slug = "ratings-" + Guid.NewGuid().ToString("N")[..8],
            City = "Tirana",
            IsActive = true,
            IsPublished = true,
        };
        seed.Studios.Add(studio);
        await seed.SaveChangesAsync();

        Artist artist = new() { StudioId = studio.Id, FirstName = "R", LastName = "A", Email = $"{Guid.NewGuid():N}@r.test" };
        artist.SetSlug("rated-" + Guid.NewGuid().ToString("N")[..8]);
        seed.Artists.Add(artist);
        await seed.SaveChangesAsync();
        return (studio, artist);
    }

    // Reviews reference a real completed appointment (FK, and one review per appointment and target).
    private async Task SeedReviews(Studio studio, Artist artist, int[] ratings, Func<int, Guid, Review> make)
    {
        await using AppDbContext seed = fixture.CreateDbContext(studio.Id);
        Client client = new() { StudioId = studio.Id, FirstName = "C", LastName = "L", Email = $"{Guid.NewGuid():N}@c.test" };
        seed.Clients.Add(client);
        await seed.SaveChangesAsync();
        foreach (int rating in ratings)
        {
            DateTime start = DateTime.UtcNow.AddDays(-7);
            Appointment appointment = new()
            {
                StudioId = studio.Id,
                ArtistId = artist.Id,
                ClientId = client.Id,
                Date = start,
                EndDate = start.AddHours(1),
                DurationMinutes = 60,
                Status = AppointmentStatus.Completed,
            };
            seed.Appointments.Add(appointment);
            await seed.SaveChangesAsync();
            seed.Reviews.Add(make(rating, appointment.Id));
        }
        await seed.SaveChangesAsync();
    }
}
