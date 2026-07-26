using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class DeleteStudioClosureHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public DeleteStudioClosureHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private DeleteStudioClosureHandler CreateSut() => new(_db, _tenant);

    private Guid SeedClosure()
    {
        var closure = new StudioClosure
        {
            StudioId = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(1),
            EndDate = DateTime.UtcNow.Date.AddDays(5),
            Reason = "Holiday",
        };
        _db.StudioClosures.Add(closure);
        _db.SaveChanges();
        return closure.Id;
    }

    [Fact]
    public async Task Handle_ValidCommand_SoftDeletesClosure()
    {
        Guid closureId = SeedClosure();

        await CreateSut().Handle(new DeleteStudioClosureCommand(_studioId, closureId), default);

        StudioClosure row = _db.StudioClosures.First();
        row.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_UnknownClosure_ThrowsNotFoundException()
    {
        SeedClosure();

        Func<Task> act = () => CreateSut().Handle(
            new DeleteStudioClosureCommand(_studioId, Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_MismatchedStudioId_ThrowsNotFoundException()
    {
        Guid closureId = SeedClosure();

        Func<Task> act = () => CreateSut().Handle(
            new DeleteStudioClosureCommand(Guid.NewGuid(), closureId), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
