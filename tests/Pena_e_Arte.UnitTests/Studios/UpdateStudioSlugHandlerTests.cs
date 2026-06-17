using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class UpdateStudioSlugHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private UpdateStudioSlugHandler CreateSut() => new(_db);

    private async Task<Studio> SeedStudio(string slug = "original-slug")
    {
        Studio studio = new() { Name = "Test Studio", Slug = slug, City = "Lisbon" };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        return studio;
    }

    [Fact]
    public async Task Handle_ValidSlug_UpdatesSlugOnStudio()
    {
        // Arrange
        Studio studio = await SeedStudio("old-slug");

        // Act
        Unit result = await CreateSut().Handle(new UpdateStudioSlugCommand(studio.Id, "new-slug"), default);

        // Assert
        result.Should().Be(Unit.Value);
        _db.Studios.Single(s => s.Id == studio.Id).Slug.Should().Be("new-slug");
    }

    [Fact]
    public async Task Handle_SlugAlreadyTaken_ThrowsBusinessRuleViolationException()
    {
        // Arrange
        Studio studio = await SeedStudio("first-slug");
        _db.Studios.Add(new Studio { Name = "Other", Slug = "taken-slug", City = "Porto" });
        await _db.SaveChangesAsync();

        // Act
        Func<Task> act = () => CreateSut().Handle(new UpdateStudioSlugCommand(studio.Id, "taken-slug"), default);

        // Assert
        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already taken*");
    }

    [Fact]
    public async Task Handle_SlugAlreadyChangedOnce_ThrowsBusinessRuleViolationException()
    {
        // Arrange
        Studio studio = await SeedStudio("original-slug");
        studio.SlugLockedAt = DateTime.UtcNow.AddDays(-1);
        await _db.SaveChangesAsync();

        // Act
        Func<Task> act = () => CreateSut().Handle(new UpdateStudioSlugCommand(studio.Id, "new-slug"), default);

        // Assert
        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already been changed*");
    }

    [Fact]
    public void Handle_InvalidSlugFormat_FailsFluentValidation()
    {
        // Arrange
        UpdateStudioSlugValidator validator = new();
        UpdateStudioSlugCommand command     = new(Guid.NewGuid(), "Invalid Slug With Spaces");

        // Act
        ValidationResult result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewSlug");
    }

    [Fact]
    public void Handle_SlugWithUppercase_FailsFluentValidation()
    {
        // Arrange
        UpdateStudioSlugValidator validator = new();
        UpdateStudioSlugCommand command     = new(Guid.NewGuid(), "UpperCase-Slug");

        // Act
        ValidationResult result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewSlug");
    }

    [Fact]
    public void Handle_SlugTooLong_FailsFluentValidation()
    {
        // Arrange
        UpdateStudioSlugValidator validator = new();
        string tooLong                     = new('a', 61);
        UpdateStudioSlugCommand command     = new(Guid.NewGuid(), tooLong);

        // Act
        ValidationResult result = validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewSlug");
    }

    [Fact]
    public async Task Handle_SlugUnchanged_DoesNotSetSlugLockedAt()
    {
        // Arrange
        Studio studio = await SeedStudio("same-slug");

        // Act
        await CreateSut().Handle(new UpdateStudioSlugCommand(studio.Id, "same-slug"), default);

        // Assert — slug did not change so SlugLockedAt should NOT be set
        _db.Studios.Single(s => s.Id == studio.Id).SlugLockedAt.Should().BeNull();
    }
}
