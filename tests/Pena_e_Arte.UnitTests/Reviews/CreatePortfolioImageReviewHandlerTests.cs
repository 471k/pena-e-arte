using FluentAssertions;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reviews;

public class CreatePortfolioImageReviewHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private CreatePortfolioImageReviewHandler CreateSut() => new(_db);

    private async Task<PortfolioImage> SeedImage()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Studio", Slug = "ink-studio", City = "Lisbon", IsActive = true });

        Artist artist = new() { StudioId = studioId, FirstName = "Ana", LastName = "Silva", Email = "ana@ink.com" };
        artist.SetSlug("ana-silva");
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        PortfolioImage image = new() { ArtistId = artist.Id, StudioId = studioId, ImageUrl = "https://cdn.example.com/1.jpg" };
        _db.PortfolioImages.Add(image);
        await _db.SaveChangesAsync();

        return image;
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsReview()
    {
        PortfolioImage image = await SeedImage();
        Guid authorId = Guid.NewGuid();

        CreatePortfolioImageReviewCommand command = new(
            ImageId: image.Id,
            AuthorUserId: authorId,
            AuthorName: "Client A",
            Rating: 5,
            Body: "Incredible work, will definitely be back!");

        await CreateSut().Handle(command, CancellationToken.None);

        Review? review = _db.Reviews.SingleOrDefault(r => r.PortfolioImageId == image.Id);
        review.Should().NotBeNull();
        review!.Rating.Should().Be(5);
        review.AuthorUserId.Should().Be(authorId);
    }

    [Fact]
    public async Task Handle_UnknownImageId_ThrowsNotFoundException()
    {
        CreatePortfolioImageReviewCommand command = new(
            ImageId: Guid.NewGuid(),
            AuthorUserId: Guid.NewGuid(),
            AuthorName: "Client B",
            Rating: 4,
            Body: "Some review body here.");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_DuplicateReview_ThrowsConflictException()
    {
        PortfolioImage image = await SeedImage();
        Guid authorId = Guid.NewGuid();

        CreatePortfolioImageReviewCommand first = new(image.Id, authorId, "Client C", 3, "First review body here.");
        await CreateSut().Handle(first, CancellationToken.None);

        CreatePortfolioImageReviewCommand second = new(image.Id, authorId, "Client C", 5, "Second review attempt.");

        Func<Task> act = () => CreateSut().Handle(second, CancellationToken.None);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Handle_Review_HasCorrectPortfolioImageId()
    {
        PortfolioImage image = await SeedImage();

        CreatePortfolioImageReviewCommand command = new(
            ImageId: image.Id,
            AuthorUserId: Guid.NewGuid(),
            AuthorName: "Client D",
            Rating: 4,
            Body: "Really solid technique and clean lines.");

        await CreateSut().Handle(command, CancellationToken.None);

        Review review = _db.Reviews.Single(r => r.PortfolioImageId == image.Id);
        review.PortfolioImageId.Should().Be(image.Id);
        review.StudioId.Should().BeNull();
        review.ArtistId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ValidCommand_MultipleReviewersAllowed()
    {
        PortfolioImage image = await SeedImage();

        CreatePortfolioImageReviewCommand cmd1 = new(image.Id, Guid.NewGuid(), "Client E", 5, "Outstanding portfolio image.");
        CreatePortfolioImageReviewCommand cmd2 = new(image.Id, Guid.NewGuid(), "Client F", 4, "Really nice tattoo work here.");

        await CreateSut().Handle(cmd1, CancellationToken.None);
        await CreateSut().Handle(cmd2, CancellationToken.None);

        int count = _db.Reviews.Count(r => r.PortfolioImageId == image.Id);
        count.Should().Be(2);
    }
}
