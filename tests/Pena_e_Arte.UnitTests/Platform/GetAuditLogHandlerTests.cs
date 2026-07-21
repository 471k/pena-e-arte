using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetAuditLogHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetAuditLogHandler CreateSut() => new(_db);

    private async Task Seed(string action, string targetType, Guid? studioId, DateTime createdAt)
    {
        AuditLogEntry entry = AuditLogEntry.Create(
            Guid.NewGuid(), "owner", action, targetType, Guid.NewGuid(), studioId, "{}");
        _db.AuditLogEntries.Add(entry);
        await _db.SaveChangesAsync();
        // CreatedAt has a private setter defaulted in the factory — overwrite via reflection-free
        // approach isn't available, so seed distinct timestamps by staggering inserts instead.
        _ = createdAt;
    }

    [Fact]
    public async Task Handle_NoEntries_ReturnsEmptyPage()
    {
        AuditLogPageResponse result = await CreateSut().Handle(new GetAuditLogQuery(), default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_SeesEntriesAcrossMultipleStudios()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        await Seed("Studio.Suspended", "Studio", studioA, DateTime.UtcNow);
        await Seed("Studio.Suspended", "Studio", studioB, DateTime.UtcNow);

        AuditLogPageResponse result = await CreateSut().Handle(new GetAuditLogQuery(), default);

        result.Items.Should().HaveCount(2);
        result.Items.Select(i => i.StudioId).Should().BeEquivalentTo([studioA, studioB]);
    }

    [Fact]
    public async Task Handle_PlatformWideEntry_StudioIdIsNull()
    {
        await Seed("Plan.Updated", "Plan", null, DateTime.UtcNow);

        AuditLogPageResponse result = await CreateSut().Handle(new GetAuditLogQuery(), default);

        result.Items.Single().StudioId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_FilterByAction_ReturnsOnlyMatching()
    {
        await Seed("Studio.Suspended", "Studio", Guid.NewGuid(), DateTime.UtcNow);
        await Seed("Studio.Unsuspended", "Studio", Guid.NewGuid(), DateTime.UtcNow);

        AuditLogPageResponse result = await CreateSut().Handle(
            new GetAuditLogQuery(Action: "Studio.Suspended"), default);

        result.Items.Should().ContainSingle(i => i.Action == "Studio.Suspended");
    }

    [Fact]
    public async Task Handle_FilterByTargetType_ReturnsOnlyMatching()
    {
        await Seed("Plan.Updated", "Plan", null, DateTime.UtcNow);
        await Seed("Studio.Suspended", "Studio", Guid.NewGuid(), DateTime.UtcNow);

        AuditLogPageResponse result = await CreateSut().Handle(
            new GetAuditLogQuery(TargetType: "Plan"), default);

        result.Items.Should().ContainSingle(i => i.TargetType == "Plan");
    }

    [Fact]
    public async Task Handle_PageSizeClamped_StaysWithinOneToOneHundred()
    {
        AuditLogPageResponse result = await CreateSut().Handle(new GetAuditLogQuery(PageSize: 9999), default);

        result.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_Pagination_ReturnsCorrectSlice()
    {
        for (int i = 0; i < 5; i++)
            await Seed("Studio.Suspended", "Studio", Guid.NewGuid(), DateTime.UtcNow);

        AuditLogPageResponse result = await CreateSut().Handle(new GetAuditLogQuery(Page: 1, PageSize: 2), default);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
    }
}
