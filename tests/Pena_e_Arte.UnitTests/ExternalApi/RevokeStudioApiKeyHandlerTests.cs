using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.ExternalApi.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ExternalApi;

public class RevokeStudioApiKeyHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public RevokeStudioApiKeyHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private RevokeStudioApiKeyHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_ActiveKeyExists_MarksItRevoked()
    {
        StudioApiKey key = new() { StudioId = _studioId, KeyHash = "hash", KeyPrefix = "tos_live_abcd" };
        _db.StudioApiKeys.Add(key);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new RevokeStudioApiKeyCommand(), default);

        _db.StudioApiKeys.Single().RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_NoActiveKey_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().Handle(new RevokeStudioApiKeyCommand(), default);

        await act.Should().NotThrowAsync();
    }
}
