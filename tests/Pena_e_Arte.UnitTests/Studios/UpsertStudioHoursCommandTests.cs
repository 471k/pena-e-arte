using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class UpsertStudioHoursValidatorTests
{
    private readonly UpsertStudioHoursValidator _validator = new();

    [Fact]
    public void Validate_MoreThanSevenEntries_Fails()
    {
        var entries = Enumerable.Range(0, 8)
            .Select(i => new StudioHoursEntryDto((DayOfWeek)(i % 7), TimeSpan.Zero, TimeSpan.FromHours(1), true))
            .ToList();

        var result = _validator.Validate(new UpsertStudioHoursCommand(entries));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_DuplicateDayOfWeek_Fails()
    {
        var entries = new List<StudioHoursEntryDto>
        {
            new(DayOfWeek.Monday, TimeSpan.Zero, TimeSpan.FromHours(1), true),
            new(DayOfWeek.Monday, TimeSpan.FromHours(2), TimeSpan.FromHours(3), true),
        };

        var result = _validator.Validate(new UpsertStudioHoursCommand(entries));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_StartTimeNotBeforeEndTime_Fails()
    {
        var entries = new List<StudioHoursEntryDto>
        {
            new(DayOfWeek.Monday, TimeSpan.FromHours(18), TimeSpan.FromHours(9), true),
        };

        var result = _validator.Validate(new UpsertStudioHoursCommand(entries));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ClosedDayStillRequiresStartBeforeEnd_Fails()
    {
        // Matches UpsertArtistScheduleValidator's convention: IsOpen = false rows are not
        // special-cased, same as ArtistSchedule's IsAvailable = false rows.
        var entries = new List<StudioHoursEntryDto>
        {
            new(DayOfWeek.Sunday, TimeSpan.FromHours(18), TimeSpan.FromHours(9), false),
        };

        var result = _validator.Validate(new UpsertStudioHoursCommand(entries));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_ValidWeeklySchedule_Passes()
    {
        var entries = new List<StudioHoursEntryDto>
        {
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(18), true),
            new(DayOfWeek.Sunday, TimeSpan.Zero, TimeSpan.FromHours(1), false),
        };

        var result = _validator.Validate(new UpsertStudioHoursCommand(entries));

        result.IsValid.Should().BeTrue();
    }
}

public class UpsertStudioHoursHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public UpsertStudioHoursHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private UpsertStudioHoursHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_NoExistingRows_AddsNewRows()
    {
        var entries = new List<StudioHoursEntryDto>
        {
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(18), true),
            new(DayOfWeek.Tuesday, TimeSpan.FromHours(9), TimeSpan.FromHours(18), true),
        };

        await CreateSut().Handle(new UpsertStudioHoursCommand(entries), default);

        List<StudioHours> rows = _db.StudioHours.Where(h => h.StudioId == _studioId).ToList();
        rows.Should().HaveCount(2);
        rows.Should().Contain(h => h.DayOfWeek == DayOfWeek.Monday && h.IsOpen);
    }

    [Fact]
    public async Task Handle_ExistingRow_UpdatesInPlaceRatherThanDuplicating()
    {
        _db.StudioHours.Add(new StudioHours
        {
            StudioId = _studioId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(17),
            IsOpen = true,
        });
        await _db.SaveChangesAsync();

        var entries = new List<StudioHoursEntryDto>
        {
            new(DayOfWeek.Monday, TimeSpan.FromHours(10), TimeSpan.FromHours(20), false),
        };

        await CreateSut().Handle(new UpsertStudioHoursCommand(entries), default);

        List<StudioHours> rows = _db.StudioHours.Where(h => h.StudioId == _studioId).ToList();
        rows.Should().ContainSingle();
        rows[0].StartTime.Should().Be(TimeSpan.FromHours(10));
        rows[0].EndTime.Should().Be(TimeSpan.FromHours(20));
        rows[0].IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OnlyTargetsCallerOwnStudio()
    {
        Guid otherStudioId = Guid.NewGuid();
        _db.StudioHours.Add(new StudioHours
        {
            StudioId = otherStudioId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.Zero,
            EndTime = TimeSpan.FromHours(1),
            IsOpen = true,
        });
        await _db.SaveChangesAsync();

        var entries = new List<StudioHoursEntryDto>
        {
            new(DayOfWeek.Monday, TimeSpan.FromHours(9), TimeSpan.FromHours(18), true),
        };

        await CreateSut().Handle(new UpsertStudioHoursCommand(entries), default);

        _db.StudioHours.Count(h => h.StudioId == otherStudioId).Should().Be(1);
        _db.StudioHours.Count(h => h.StudioId == _studioId).Should().Be(1);
    }
}

public class GetStudioHoursQueryTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetStudioHoursHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoRows_ReturnsEmptyList()
    {
        List<StudioHoursEntryResponse> result =
            await CreateSut().Handle(new GetStudioHoursQuery(Guid.NewGuid()), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsRowsOrderedByDayOfWeek()
    {
        Guid studioId = Guid.NewGuid();
        _db.StudioHours.Add(new StudioHours
        {
            StudioId = studioId,
            DayOfWeek = DayOfWeek.Friday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(18),
            IsOpen = true,
        });
        _db.StudioHours.Add(new StudioHours
        {
            StudioId = studioId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(18),
            IsOpen = true,
        });
        await _db.SaveChangesAsync();

        List<StudioHoursEntryResponse> result =
            await CreateSut().Handle(new GetStudioHoursQuery(studioId), default);

        result.Should().HaveCount(2);
        result[0].DayOfWeek.Should().Be(DayOfWeek.Monday);
        result[1].DayOfWeek.Should().Be(DayOfWeek.Friday);
    }
}
