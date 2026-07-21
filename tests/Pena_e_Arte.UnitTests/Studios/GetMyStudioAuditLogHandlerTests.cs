using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class GetMyStudioAuditLogHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public GetMyStudioAuditLogHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private GetMyStudioAuditLogHandler CreateSut() => new(_db, _tenant);

    private async Task Seed(string action, Guid? studioId)
    {
        AuditLogEntry entry = AuditLogEntry.Create(
            Guid.NewGuid(), "owner", action, "Studio", Guid.NewGuid(), studioId, "{}");
        _db.AuditLogEntries.Add(entry);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_OnlyReturnsOwnStudioEntries()
    {
        await Seed("Studio.Suspended", _studioId);
        await Seed("Studio.Suspended", Guid.NewGuid()); // another studio

        AuditLogPageResponse result = await CreateSut().Handle(new GetMyStudioAuditLogQuery(), default);

        result.Items.Should().ContainSingle();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ExcludesPlatformWideEntriesWithNullStudioId()
    {
        await Seed("Plan.Updated", null);
        await Seed("Studio.Suspended", _studioId);

        AuditLogPageResponse result = await CreateSut().Handle(new GetMyStudioAuditLogQuery(), default);

        result.Items.Should().ContainSingle(i => i.Action == "Studio.Suspended");
    }

    [Fact]
    public async Task Handle_NoEntriesForThisStudio_ReturnsEmpty()
    {
        await Seed("Studio.Suspended", Guid.NewGuid());

        AuditLogPageResponse result = await CreateSut().Handle(new GetMyStudioAuditLogQuery(), default);

        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_FilterByAction_ScopedToOwnStudioOnly()
    {
        await Seed("Studio.Suspended", _studioId);
        await Seed("Studio.Unsuspended", _studioId);

        AuditLogPageResponse result = await CreateSut().Handle(
            new GetMyStudioAuditLogQuery(Action: "Studio.Suspended"), default);

        result.Items.Should().ContainSingle(i => i.Action == "Studio.Suspended");
    }
}
