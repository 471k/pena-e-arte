using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetHelpSearchInsightsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetHelpSearchInsightsHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoData_ReturnsEmptyInsights()
    {
        HelpSearchInsightsResponse result = await CreateSut().Handle(new GetHelpSearchInsightsQuery(), default);

        result.TotalSearches.Should().Be(0);
        result.TopQueries.Should().BeEmpty();
        result.ZeroResultQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_GroupsAndOrdersTopQueriesByCountDescending()
    {
        AddLog("book appointment", "client", 3, studioId: Guid.NewGuid());
        AddLog("book appointment", "artist", 2, studioId: Guid.NewGuid());
        AddLog("deposit rules", "owner", 1, studioId: Guid.NewGuid());
        await _db.SaveChangesAsync();

        HelpSearchInsightsResponse result = await CreateSut().Handle(new GetHelpSearchInsightsQuery(), default);

        result.TotalSearches.Should().Be(3);
        result.TopQueries.Should().HaveCount(2);
        result.TopQueries[0].Query.Should().Be("book appointment");
        result.TopQueries[0].Count.Should().Be(2);
        result.TopQueries[0].RolesAsked.Should().BeEquivalentTo(new[] { "artist", "client" });
        result.TopQueries[1].Query.Should().Be("deposit rules");
        result.TopQueries[1].Count.Should().Be(1);
    }

    [Fact]
    public async Task Handle_FiltersZeroResultQueries_ExcludingNonZeroOnes()
    {
        AddLog("obscure feature", "owner", resultCount: 0, studioId: Guid.NewGuid());
        AddLog("book appointment", "client", resultCount: 5, studioId: Guid.NewGuid());
        await _db.SaveChangesAsync();

        HelpSearchInsightsResponse result = await CreateSut().Handle(new GetHelpSearchInsightsQuery(), default);

        result.ZeroResultQueries.Should().ContainSingle();
        result.ZeroResultQueries[0].Query.Should().Be("obscure feature");
    }

    [Fact]
    public async Task Handle_TopQueries_LimitedToTwenty()
    {
        for (int i = 0; i < 25; i++)
        {
            AddLog($"query {i}", "client", resultCount: 1, studioId: Guid.NewGuid());
        }
        await _db.SaveChangesAsync();

        HelpSearchInsightsResponse result = await CreateSut().Handle(new GetHelpSearchInsightsQuery(), default);

        result.TopQueries.Should().HaveCount(20);
    }

    [Fact]
    public async Task Handle_AggregatesAcrossMultipleStudios_CrossTenant()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        AddLog("book appointment", "client", 2, studioId: studioA);
        AddLog("book appointment", "client", 2, studioId: studioB);
        await _db.SaveChangesAsync();

        HelpSearchInsightsResponse result = await CreateSut().Handle(new GetHelpSearchInsightsQuery(), default);

        result.TotalSearches.Should().Be(2);
        result.TopQueries.Single().Count.Should().Be(2);
    }

    [Fact]
    public async Task Handle_LogsOlderThanRequestedWindow_AreExcluded()
    {
        HelpSearchLog oldLog = HelpSearchLog.Create(Guid.NewGuid(), Guid.NewGuid(), "client", "old query", 1);
        typeof(HelpSearchLog).GetProperty("CreatedAt")!
            .SetValue(oldLog, DateTime.UtcNow.AddDays(-40));
        _db.HelpSearchLogs.Add(oldLog);
        AddLog("recent query", "client", 1, studioId: Guid.NewGuid());
        await _db.SaveChangesAsync();

        HelpSearchInsightsResponse result = await CreateSut().Handle(new GetHelpSearchInsightsQuery(Days: 30), default);

        result.TotalSearches.Should().Be(1);
        result.TopQueries.Single().Query.Should().Be("recent query");
    }

    private void AddLog(string query, string role, int resultCount, Guid studioId) =>
        _db.HelpSearchLogs.Add(HelpSearchLog.Create(studioId, Guid.NewGuid(), role, query, resultCount));
}
