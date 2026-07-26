using FluentAssertions;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class GetStudioByIdHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetStudioByIdHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingStudio_ReturnsStudioResponse()
    {
        // Arrange
        Studio studio = new()
        {
            Id = Guid.NewGuid(),
            Name = "Ink Soul",
            Slug = "ink-soul",
            City = "Porto",
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        StudioResponse result = await CreateSut().Handle(new GetStudioByIdQuery(studio.Id), default);

        // Assert
        result.Id.Should().Be(studio.Id);
        result.Name.Should().Be("Ink Soul");
        result.Slug.Should().Be("ink-soul");
    }

    [Fact]
    public async Task Handle_NonExistentStudio_ThrowsNotFoundException()
    {
        // Arrange / Act / Assert
        Func<Task> act = () => CreateSut().Handle(new GetStudioByIdQuery(Guid.NewGuid()), default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SuspendedStudio_ReturnsWithIsActiveFalse()
    {
        // Arrange
        Studio studio = new()
        {
            Id = Guid.NewGuid(),
            Name = "Closed Studio",
            Slug = "closed",
            City = "Lisbon",
            IsActive = false,
            CreatedAt = DateTime.UtcNow.AddDays(-60),
            TrialExpiresAt = DateTime.UtcNow.AddDays(-10),
        };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Act
        StudioResponse result = await CreateSut().Handle(new GetStudioByIdQuery(studio.Id), default);

        // Assert
        result.IsActive.Should().BeFalse();
    }
}
