using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reviews;

public class GetStudioReviewsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetStudioReviewsHandler CreateSut() => new(_db);

    private async Task<Studio> SeedStudio(string slug = "test-studio")
    {
        Studio studio = new() { Name = "Test Studio", Slug = slug, City = "Porto", IsActive = true };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        return studio;
    }

    [Fact]
    public async Task Projection_includes_owner_response_fields_when_present()
    {
        Studio studio = await SeedStudio();
        Review review = Review.ForStudio(studio.Id, Guid.NewGuid(), "Ana Silva", 5, "Wonderful experience!");
        review.Respond("Thank you for the kind words!");
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();

        List<ReviewResponse> result = await CreateSut().Handle(
            new GetStudioReviewsQuery(studio.Slug), CancellationToken.None);

        result.Should().ContainSingle(r =>
            r.OwnerResponse == "Thank you for the kind words!" && r.OwnerResponseAt != null);
    }

    [Fact]
    public async Task Projection_has_null_owner_response_when_not_answered()
    {
        Studio studio = await SeedStudio();
        _db.Reviews.Add(Review.ForStudio(studio.Id, Guid.NewGuid(), "Ana Silva", 5, "Great work!"));
        await _db.SaveChangesAsync();

        List<ReviewResponse> result = await CreateSut().Handle(
            new GetStudioReviewsQuery(studio.Slug), CancellationToken.None);

        result.Should().ContainSingle(r => r.OwnerResponse == null && r.OwnerResponseAt == null);
    }
}
