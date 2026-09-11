using FluentAssertions;
using Pena_e_Arte.Application.ExternalApi.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ExternalApi;

public class GetStudioApiKeyStatusHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetStudioApiKeyStatusHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoKey_ReturnsHasActiveKeyFalse()
    {
        StudioApiKeyStatusResponse result =
            await CreateSut().Handle(new GetStudioApiKeyStatusQuery(), default);

        result.HasActiveKey.Should().BeFalse();
        result.KeyPrefix.Should().BeNull();
    }

    [Fact]
    public async Task Handle_OnlyRevokedKeyExists_ReturnsHasActiveKeyFalse()
    {
        _db.StudioApiKeys.Add(new StudioApiKey
        {
            StudioId = _studioId, KeyHash = "hash", KeyPrefix = "tos_live_abcd",
            RevokedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        StudioApiKeyStatusResponse result =
            await CreateSut().Handle(new GetStudioApiKeyStatusQuery(), default);

        result.HasActiveKey.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ActiveKeyExists_ReturnsPrefixAndTimestamps()
    {
        DateTime lastUsed = DateTime.UtcNow.AddHours(-1);
        _db.StudioApiKeys.Add(new StudioApiKey
        {
            StudioId = _studioId, KeyHash = "hash", KeyPrefix = "tos_live_abcd",
            LastUsedAt = lastUsed,
        });
        await _db.SaveChangesAsync();

        StudioApiKeyStatusResponse result =
            await CreateSut().Handle(new GetStudioApiKeyStatusQuery(), default);

        result.HasActiveKey.Should().BeTrue();
        result.KeyPrefix.Should().Be("tos_live_abcd");
        result.LastUsedAt.Should().Be(lastUsed);
    }
}
