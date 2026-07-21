using FluentAssertions;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class GetStudioClosuresHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetStudioClosuresHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ReturnsFutureClosuresOrderedByStartDate()
    {
        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId  = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(10),
            EndDate   = DateTime.UtcNow.Date.AddDays(12),
            Reason    = "Later",
        });
        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId  = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(1),
            EndDate   = DateTime.UtcNow.Date.AddDays(2),
            Reason    = "Sooner",
        });
        await _db.SaveChangesAsync();

        List<StudioClosureResponse> result = await CreateSut().Handle(
            new GetStudioClosuresQuery(_studioId), default);

        result.Should().HaveCount(2);
        result[0].Reason.Should().Be("Sooner");
        result[1].Reason.Should().Be("Later");
    }

    [Fact]
    public async Task Handle_ExcludesPastClosures()
    {
        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId  = _studioId,
            StartDate = DateTime.UtcNow.Date.AddDays(-10),
            EndDate   = DateTime.UtcNow.Date.AddDays(-8),
            Reason    = "Past",
        });
        await _db.SaveChangesAsync();

        List<StudioClosureResponse> result = await CreateSut().Handle(
            new GetStudioClosuresQuery(_studioId), default);

        result.Should().BeEmpty();
    }
}
