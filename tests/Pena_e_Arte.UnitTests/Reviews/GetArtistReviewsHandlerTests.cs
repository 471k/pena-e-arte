using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reviews;

public class GetArtistReviewsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetArtistReviewsHandler CreateSut() => new(_db);

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

    [Fact]
    public async Task Projection_includes_owner_response_fields_when_present()
    {
        Artist artist = await SeedArtist();
        Review review = Review.ForArtist(artist.Id, Guid.NewGuid(), Guid.NewGuid(), "Ana Silva", 5, "Amazing tattoo!");
        review.Respond("Thanks for coming in!");
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        List<ReviewResponse> result = await CreateSut().Handle(
            new GetArtistReviewsQuery(artist.Slug!), CancellationToken.None);

        result.Should().ContainSingle(r =>
            r.OwnerResponse == "Thanks for coming in!" && r.OwnerResponseAt != null);
    }

    [Fact]
    public async Task Projection_has_null_owner_response_when_not_answered()
    {
        Artist artist = await SeedArtist();
        _db.Reviews.Add(Review.ForArtist(artist.Id, Guid.NewGuid(), Guid.NewGuid(), "Ana Silva", 5, "Great session!"));
        await _db.SaveChangesAsync();

        List<ReviewResponse> result = await CreateSut().Handle(
            new GetArtistReviewsQuery(artist.Slug!), CancellationToken.None);

        result.Should().ContainSingle(r => r.OwnerResponse == null && r.OwnerResponseAt == null);
    }
}
