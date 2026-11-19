using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Platform;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

/// <summary>D01: records reachable (whole end day, studio and actor filters) and interpretable (studio name).</summary>
public class GetAuditLogReachabilityTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetAuditLogHandler CreateSut() => new(_db);

    private async Task<AuditLogEntry> SeedAt(DateTime createdAtUtc, Guid? studioId = null, Guid? actorUserId = null)
    {
        AuditLogEntry entry = AuditLogEntry.Create(
            actorUserId ?? Guid.NewGuid(), "admin", AuditActions.StudioSuspended, AuditTargetTypes.Studio,
            studioId ?? Guid.NewGuid(), studioId, "{}");
        _db.AuditLogEntries.Add(entry);
        _db.Entry(entry).Property(e => e.CreatedAt).CurrentValue = createdAtUtc;
        await _db.SaveChangesAsync();
        return entry;
    }

    [Fact]
    public async Task To_CoversTheWholeEndDay_InUtc()
    {
        await SeedAt(new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc));
        await SeedAt(new DateTime(2026, 11, 14, 23, 59, 59, DateTimeKind.Utc));
        AuditLogEntry nextDay = await SeedAt(new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

        AuditLogPageResponse result = await CreateSut().Handle(
            new GetAuditLogQuery(To: new DateTime(2026, 11, 14)), default);

        // Before D01 this ended at 00:00 on the 14th and returned nothing from that day.
        result.TotalCount.Should().Be(2);
        result.Items.Should().NotContain(i => i.Id == nextDay.Id);
    }

    [Fact]
    public async Task From_StartsAtTheBeginningOfItsDay_EvenWithATimePart()
    {
        await SeedAt(new DateTime(2026, 11, 13, 23, 59, 59, DateTimeKind.Utc));
        AuditLogEntry startOfDay = await SeedAt(new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc));

        AuditLogPageResponse result = await CreateSut().Handle(
            new GetAuditLogQuery(From: new DateTime(2026, 11, 14, 15, 30, 0)), default);

        result.Items.Should().ContainSingle().Which.Id.Should().Be(startOfDay.Id);
    }

    [Fact]
    public async Task StudioFilter_ReturnsOnlyThatStudio_WithItsNameResolved()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink & Iron", Slug = "ink-iron", CountryCode = "AL", Currency = "EUR" });
        await SeedAt(new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc), studioId);
        await SeedAt(new DateTime(2026, 11, 14, 11, 0, 0, DateTimeKind.Utc), Guid.NewGuid());

        AuditLogPageResponse result = await CreateSut().Handle(new GetAuditLogQuery(StudioId: studioId), default);

        AuditLogEntryResponse only = result.Items.Should().ContainSingle().Subject;
        only.StudioId.Should().Be(studioId);
        only.StudioName.Should().Be("Ink & Iron");
    }

    [Fact]
    public async Task PlatformWideEntry_HasNoStudioName()
    {
        AuditLogEntry entry = AuditLogEntry.Create(
            Guid.NewGuid(), "admin", AuditActions.PlanUpdated, AuditTargetTypes.Plan, Guid.NewGuid(), null, "{}");
        _db.AuditLogEntries.Add(entry);
        await _db.SaveChangesAsync();

        AuditLogPageResponse result = await CreateSut().Handle(new GetAuditLogQuery(), default);

        result.Items.Should().ContainSingle().Which.StudioName.Should().BeNull();
    }

    [Fact]
    public async Task ActorFilter_ReturnsOnlyThatActorsActions()
    {
        Guid actor = Guid.NewGuid();
        AuditLogEntry mine = await SeedAt(new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc), actorUserId: actor);
        await SeedAt(new DateTime(2026, 11, 14, 11, 0, 0, DateTimeKind.Utc));

        AuditLogPageResponse result = await CreateSut().Handle(new GetAuditLogQuery(ActorUserId: actor), default);

        result.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [Fact]
    public async Task Paging_ReachesOlderEntries_WithFiltersKept()
    {
        Guid studioId = Guid.NewGuid();
        for (int i = 0; i < 55; i++)
            await SeedAt(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), studioId);
        await SeedAt(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc)); // another studio

        AuditLogPageResponse page2 = await CreateSut().Handle(
            new GetAuditLogQuery(StudioId: studioId, Page: 2, PageSize: 50), default);

        page2.TotalCount.Should().Be(55);
        page2.Items.Should().HaveCount(5).And.OnlyContain(i => i.StudioId == studioId);
        page2.Items.Min(i => i.CreatedAt).Should().Be(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Vocabulary_ListsEveryConstant_Sorted()
    {
        AuditVocabulary.Actions.Should().Contain([AuditActions.StudioSuspended, AuditActions.PlanUpdated])
            .And.BeInAscendingOrder(StringComparer.Ordinal).And.OnlyHaveUniqueItems();
        AuditVocabulary.TargetTypes.Should().Contain([AuditTargetTypes.Studio, AuditTargetTypes.Platform]);
        // A const added to either class must reach the admin's filter with no second list to edit.
        typeof(AuditTargetTypes).GetFields().Count(f => f.IsLiteral).Should().Be(AuditVocabulary.TargetTypes.Count);
    }

    [Theory]
    [InlineData("Studio.Suspended", null, true)]
    [InlineData("Studio.suspendedx", null, false)]
    [InlineData(null, "Studio", true)]
    [InlineData(null, "Nope", false)]
    public void Validator_AcceptsOnlyNamedChoices(string? action, string? targetType, bool valid)
    {
        ValidationResult result = new GetAuditLogValidator().Validate(new GetAuditLogQuery(action, targetType));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validator_RejectsAnEndDayBeforeTheStartDay_ButAllowsTheSameDay()
    {
        GetAuditLogValidator validator = new();

        validator.Validate(new GetAuditLogQuery(From: new DateTime(2026, 11, 14), To: new DateTime(2026, 11, 13)))
            .IsValid.Should().BeFalse();
        validator.Validate(new GetAuditLogQuery(From: new DateTime(2026, 11, 14, 18, 0, 0), To: new DateTime(2026, 11, 14)))
            .IsValid.Should().BeTrue();
    }
}
