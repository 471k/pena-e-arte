using FluentAssertions;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Domain.Entities;
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

    [Fact]
    public async Task Creates_review_when_studio_exists_and_no_prior_review()
    {
        Studio studio     = await SeedStudio();
        Guid   authorId   = Guid.NewGuid();
        CreateStudioReviewCommand command = new(
            studio.Slug, authorId, "Ana Silva", 5, "Absolutely incredible studio!");

        await CreateSut().Handle(command, CancellationToken.None);

        _db.Reviews.Should().ContainSingle(r =>
            r.StudioId == studio.Id &&
            r.Rating   == 5          &&
            r.Body     == "Absolutely incredible studio!");
    }

    [Fact]
    public async Task Throws_NotFoundException_when_studio_not_found()
    {
        CreateStudioReviewCommand command = new(
            "nonexistent-slug", Guid.NewGuid(), "Someone", 4, "Great experience here!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_ConflictException_when_user_already_reviewed()
    {
        Studio studio   = await SeedStudio();
        Guid   authorId = Guid.NewGuid();

        Review existing = Review.ForStudio(studio.Id, authorId, "Ana Silva", 4, "First review text here");
        _db.Reviews.Add(existing);
        await _db.SaveChangesAsync();

        CreateStudioReviewCommand command = new(
            studio.Slug, authorId, "Ana Silva", 5, "Trying to review again!");

        Func<Task> act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>()
            .WithMessage("*already reviewed*");
    }

    [Fact]
    public void Validator_rejects_rating_below_1()
    {
        CreateStudioReviewValidator validator = new();
        CreateStudioReviewCommand command = new(
            "some-studio", Guid.NewGuid(), "Ana Silva", 0, "Some body text here that is long enough");

        validator.ShouldFailOn(command, nameof(command.Rating));
    }

    [Fact]
    public void Validator_rejects_body_shorter_than_10_chars()
    {
        CreateStudioReviewValidator validator = new();
        CreateStudioReviewCommand command = new(
            "some-studio", Guid.NewGuid(), "Ana Silva", 4, "short");

        validator.ShouldFailOn(command, nameof(command.Body));
    }
}
