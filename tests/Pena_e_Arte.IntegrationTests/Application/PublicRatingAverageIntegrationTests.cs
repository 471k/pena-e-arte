using System.Globalization;
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
/// The public rating averages against real MySQL, under the one display rule (RatingDisplay): the mean of
/// the whole-star ratings taken exactly from the integer sum and count, to one decimal, a half rounded up.
///
/// The cases are the five one-decimal midpoints reachable with 4 or 20 reviews. Only the quarters (4.25,
/// 3.75) are exact midpoints in binary; 4.35, 4.45 and 4.15 are not (4.35 is 4.3499999999999996 as a
/// double), which is why the mean is taken in decimal. Before this change the API used Math.Round on a
/// double with the default ToEven mode and showed 4.2 / 3.8 / 4.3 / 4.5 / 4.2 for these.
///
/// Every assertion is on the raw number the API returns and on the wire, never a formatted string.
/// </summary>
[Collection("Database")]
public class PublicRatingAverageIntegrationTests(DatabaseFixture fixture)
{
    public static TheoryData<int[], decimal> Midpoints => new()
    {
        { new[] { 4, 5, 5 }, 4.7m },              // 4.666...: not a midpoint at all
        { new[] { 1, 2 }, 1.5m },                 // already one decimal
        { new[] { 4, 4, 4, 5 }, 4.3m },           // 4.25: was 4.2
        { new[] { 4, 4, 4, 3 }, 3.8m },           // 3.75: was 3.8
        { Ratings(fives: 7, fours: 13), 4.4m },   // 87/20 = 4.35: was 4.3
        { Ratings(fives: 9, fours: 11), 4.5m },   // 89/20 = 4.45: was 4.5
        { Ratings(fives: 3, fours: 17), 4.2m },   // 83/20 = 4.15: was 4.2
    };

    [Theory]
    [MemberData(nameof(Midpoints))]
    public async Task StudioAverage_IsTheSameNumberOnTheStudioPageAndAboveItsReviewList(int[] ratings, decimal expected)
    {
        (Studio studio, Artist artist) = await SeedAsync();
        await SeedReviews(studio, artist, ratings, (r, appointmentId) => Review.ForStudio(studio.Id, appointmentId, Guid.NewGuid(), "A", r, "ok"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        PublicStudioResponse? page = await new GetPublicStudioHandler(db).Handle(new GetPublicStudioQuery(studio.Slug), default);
        ReviewListResponse list = await new GetStudioReviewsHandler(db).Handle(new GetStudioReviewsQuery(studio.Slug), default);

        page!.AverageRating.Should().Be(expected);
        page.ReviewCount.Should().Be(ratings.Length);
        Wire(page).Should().Contain($"\"averageRating\":{Num(expected)}");

        list.AverageRating.Should().Be(expected);
        list.ReviewCount.Should().Be(ratings.Length);
        Wire(list).Should().Contain($"\"averageRating\":{Num(expected)}");
    }

    [Theory]
    [MemberData(nameof(Midpoints))]
    public async Task ArtistAverage_IsTheSameNumberOnTheArtistPageTheStudioPageAndAboveItsReviewList(int[] ratings, decimal expected)
    {
        (Studio studio, Artist artist) = await SeedAsync();
        await SeedReviews(studio, artist, ratings, (r, appointmentId) => Review.ForArtist(artist.Id, appointmentId, Guid.NewGuid(), "A", r, "ok"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        PublicArtistResponse? artistPage = await new GetPublicArtistHandler(db).Handle(new GetPublicArtistQuery(artist.Slug!, null), default);
        PublicStudioResponse? studioPage = await new GetPublicStudioHandler(db).Handle(new GetPublicStudioQuery(studio.Slug), default);
        ReviewListResponse list = await new GetArtistReviewsHandler(db).Handle(new GetArtistReviewsQuery(artist.Slug!), default);

        artistPage!.AverageRating.Should().Be(expected);
        artistPage.ReviewCount.Should().Be(ratings.Length);
        Wire(artistPage).Should().Contain($"\"averageRating\":{Num(expected)}");

        PublicArtistSummary summary = studioPage!.Artists.Single(a => a.ArtistId == artist.Id);
        summary.AverageRating.Should().Be(expected);
        summary.ReviewCount.Should().Be(ratings.Length);

        list.AverageRating.Should().Be(expected);
        list.ReviewCount.Should().Be(ratings.Length);

        // Artist reviews are not studio reviews: the studio's own average stays empty.
        studioPage.AverageRating.Should().BeNull();
        studioPage.ReviewCount.Should().Be(0);
    }

    [Fact]
    public async Task ReviewListSummary_CoversEveryReview_NotOnlyTheNewestFiftyItShows()
    {
        // 50 fives then one 1, newest last: the list shows the newest 50 (one 1 and 49 fives, mean 4.92),
        // but the summary is over all 51 (251/51 = 4.92...), and the count is 51, not 50.
        int[] ratings = [.. Enumerable.Repeat(5, 50), 1];
        (Studio studio, Artist artist) = await SeedAsync();
        await SeedReviews(studio, artist, ratings, (r, appointmentId) => Review.ForStudio(studio.Id, appointmentId, Guid.NewGuid(), "A", r, "ok"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        ReviewListResponse list = await new GetStudioReviewsHandler(db).Handle(new GetStudioReviewsQuery(studio.Slug), default);
        PublicStudioResponse? page = await new GetPublicStudioHandler(db).Handle(new GetPublicStudioQuery(studio.Slug), default);

        list.Reviews.Should().HaveCount(50);
        list.ReviewCount.Should().Be(51);
        list.AverageRating.Should().Be(4.9m);
        list.AverageRating.Should().Be(page!.AverageRating);
    }

    [Fact]
    public async Task ReviewListSummary_IsEmpty_WhenThereAreNoReviews()
    {
        (Studio studio, _) = await SeedAsync();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        ReviewListResponse list = await new GetStudioReviewsHandler(db).Handle(new GetStudioReviewsQuery(studio.Slug), default);

        list.Reviews.Should().BeEmpty();
        list.AverageRating.Should().BeNull();
        list.ReviewCount.Should().Be(0);
        Wire(list).Should().Contain("\"averageRating\":null");
    }

    private static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static int[] Ratings(int fives, int fours) => [.. Enumerable.Repeat(5, fives), .. Enumerable.Repeat(4, fours)];

    // The API serializes with the web defaults (camelCase): this is the number a client receives.
    private static string Wire<T>(T response) => JsonSerializer.Serialize(response, JsonSerializerOptions.Web);

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
    // Saved one at a time so CreatedAt follows the array order (newest last).
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
            await seed.SaveChangesAsync();
        }
    }
}
