using FluentAssertions;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.ValueObjects;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdateBodyMapHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private UpdateBodyMapHandler CreateSut() => new(_db);

    private async Task<ClientProfile> AddProfileAsync(Guid clientId)
    {
        ClientProfile profile = new()
        {
            StudioId = Guid.NewGuid(),
            ClientId = clientId,
            BodyMap = new BodyMap { Locations = [] },
        };
        _db.ClientProfiles.Add(profile);
        await _db.SaveChangesAsync();
        return profile;
    }

    [Fact]
    public async Task Handle_ExistingProfile_UpdatesBodyMapLocations()
    {
        Guid clientId = Guid.NewGuid();
        await AddProfileAsync(clientId);

        UpdateBodyMapRequest req = new(["left_arm", "right_shoulder"]);
        ClientProfileResponse result = await CreateSut()
            .Handle(new UpdateBodyMapCommand(clientId, req), default);

        result.BodyMapLocations.Should().BeEquivalentTo(["left_arm", "right_shoulder"]);
    }

    [Fact]
    public async Task Handle_ExistingProfile_ClearsLocationsWhenEmptyList()
    {
        Guid clientId = Guid.NewGuid();
        ClientProfile profile = await AddProfileAsync(clientId);
        profile.BodyMap = new BodyMap { Locations = ["left_arm"] };
        await _db.SaveChangesAsync();

        ClientProfileResponse result = await CreateSut()
            .Handle(new UpdateBodyMapCommand(clientId, new UpdateBodyMapRequest([])), default);

        result.BodyMapLocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MissingProfile_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut()
            .Handle(new UpdateBodyMapCommand(Guid.NewGuid(), new UpdateBodyMapRequest(["left_arm"])), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
