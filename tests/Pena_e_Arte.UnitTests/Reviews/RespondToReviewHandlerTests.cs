using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reviews;

public class RespondToReviewHandlerTests
{
    private readonly FakeDbContext  _db     = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private RespondToReviewHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Responds_successfully_when_review_is_for_this_studio()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Studio", Slug = "ink-studio", City = "Porto", IsActive = true });
        Review review = Review.ForStudio(studioId, Guid.NewGuid(), "Ana Silva", 5, "Great studio!");
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();
        _tenant.StudioId.Returns(studioId);

        await CreateSut().Handle(new RespondToReviewCommand(review.Id, "Thanks Ana!"), CancellationToken.None);

        Review? persisted = await _db.Reviews.FindAsync(review.Id);
        persisted!.OwnerResponse.Should().Be("Thanks Ana!");
        persisted.OwnerResponseAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Responds_successfully_when_review_is_for_an_artist_in_this_studio()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Studio", Slug = "ink-studio", City = "Porto", IsActive = true });
        Artist artist = new() { StudioId = studioId, FirstName = "Maria", LastName = "Silva", Email = "maria@example.com" };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        Review review = Review.ForArtist(artist.Id, Guid.NewGuid(), "Ana Silva", 5, "Amazing tattoo!");
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();
        _tenant.StudioId.Returns(studioId);

        await CreateSut().Handle(new RespondToReviewCommand(review.Id, "Thanks Ana!"), CancellationToken.None);

        Review? persisted = await _db.Reviews.FindAsync(review.Id);
        persisted!.OwnerResponse.Should().Be("Thanks Ana!");
    }

    [Fact]
    public async Task Throws_NotFoundException_when_review_does_not_exist()
    {
        _tenant.StudioId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut()
            .Handle(new RespondToReviewCommand(Guid.NewGuid(), "Thanks!"), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Throws_ForbiddenException_when_review_belongs_to_a_different_studio()
    {
        Guid studioId       = Guid.NewGuid();
        Guid otherStudioId  = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Studio", Slug = "ink-studio", City = "Porto", IsActive = true });
        Review review = Review.ForStudio(studioId, Guid.NewGuid(), "Ana Silva", 5, "Great studio!");
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();
        _tenant.StudioId.Returns(otherStudioId);

        Func<Task> act = () => CreateSut()
            .Handle(new RespondToReviewCommand(review.Id, "Thanks!"), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Respond_is_idempotent_calling_twice_updates_owner_response_at()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Studio", Slug = "ink-studio", City = "Porto", IsActive = true });
        Review review = Review.ForStudio(studioId, Guid.NewGuid(), "Ana Silva", 5, "Great studio!");
        _db.Reviews.Add(review);
        await _db.SaveChangesAsync();
        _tenant.StudioId.Returns(studioId);

        await CreateSut().Handle(new RespondToReviewCommand(review.Id, "First reply"), CancellationToken.None);
        Review? afterFirst = await _db.Reviews.FindAsync(review.Id);
        afterFirst!.OwnerResponse.Should().Be("First reply");

        await CreateSut().Handle(new RespondToReviewCommand(review.Id, "Updated reply"), CancellationToken.None);
        Review? afterSecond = await _db.Reviews.FindAsync(review.Id);

        afterSecond!.OwnerResponse.Should().Be("Updated reply");
        afterSecond.OwnerResponseAt.Should().NotBeNull();
    }
}
