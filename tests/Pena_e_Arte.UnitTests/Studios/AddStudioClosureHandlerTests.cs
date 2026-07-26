using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class AddStudioClosureHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public AddStudioClosureHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private AddStudioClosureHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_ValidCommand_ReturnsNewId()
    {
        DateTime start = DateTime.UtcNow.Date.AddDays(5);
        DateTime end = DateTime.UtcNow.Date.AddDays(6);

        Guid id = await CreateSut().Handle(
            new AddStudioClosureCommand(_studioId, start, end, "Christmas"), default);

        id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsClosure()
    {
        DateTime start = DateTime.UtcNow.Date.AddDays(1);
        DateTime end = DateTime.UtcNow.Date.AddDays(3);

        await CreateSut().Handle(
            new AddStudioClosureCommand(_studioId, start, end, "Renovation"), default);

        _db.StudioClosures.Should().ContainSingle(c => c.StudioId == _studioId && c.Reason == "Renovation");
    }

    [Fact]
    public async Task Handle_MismatchedStudioId_ThrowsNotFoundException()
    {
        DateTime start = DateTime.UtcNow.Date;
        DateTime end = start.AddDays(1);

        Func<Task> act = () => CreateSut().Handle(
            new AddStudioClosureCommand(Guid.NewGuid(), start, end, "Holiday"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_OverlappingExistingClosure_ThrowsBusinessRuleViolationException()
    {
        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(5),
            EndDate = DateTime.UtcNow.Date.AddDays(10),
            Reason = "Existing",
        });
        await _db.SaveChangesAsync();

        DateTime overlapStart = DateTime.UtcNow.Date.AddDays(8);
        DateTime overlapEnd = DateTime.UtcNow.Date.AddDays(12);

        Func<Task> act = () => CreateSut().Handle(
            new AddStudioClosureCommand(_studioId, overlapStart, overlapEnd, "New request"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NonOverlappingClosure_Succeeds()
    {
        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(5),
            EndDate = DateTime.UtcNow.Date.AddDays(10),
            Reason = "Existing",
        });
        await _db.SaveChangesAsync();

        DateTime start = DateTime.UtcNow.Date.AddDays(20);
        DateTime end = DateTime.UtcNow.Date.AddDays(22);

        Func<Task> act = () => CreateSut().Handle(
            new AddStudioClosureCommand(_studioId, start, end, "New request"), default);

        await act.Should().NotThrowAsync();
    }
}
