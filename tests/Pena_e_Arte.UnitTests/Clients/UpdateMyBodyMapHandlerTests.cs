using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.ValueObjects;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdateMyBodyMapHandlerTests
{
    private readonly FakeDbContext _db          = FakeDbContext.Create();
    private readonly ICurrentUser  _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid          _userId      = Guid.NewGuid();
    private readonly Guid          _studioId    = Guid.NewGuid();

    public UpdateMyBodyMapHandlerTests() =>
        _currentUser.UserId.Returns(_userId);

    private UpdateMyBodyMapHandler CreateSut() => new(_db, _currentUser);

    [Fact]
    public async Task Handle_UpdatesBodyMapLocations()
    {
        (_, Guid profileId) = await Seed();
        List<string> newLocations = ["chest", "left_forearm"];

        await CreateSut().Handle(
            new UpdateMyBodyMapCommand(new UpdateBodyMapRequest(newLocations)),
            default);

        _db.ClientProfiles.Single(p => p.Id == profileId)
            .BodyMap.Locations.Should().BeEquivalentTo(newLocations);
    }

    [Fact]
    public async Task Handle_ReturnsUpdatedProfile()
    {
        await Seed();
        List<string> newLocations = ["chest"];

        ClientProfileResponse result = await CreateSut().Handle(
            new UpdateMyBodyMapCommand(new UpdateBodyMapRequest(newLocations)),
            default);

        result.BodyMapLocations.Should().BeEquivalentTo(newLocations);
    }

    [Fact]
    public async Task Handle_ClearsLocationsWhenEmptyListPassed()
    {
        (_, Guid profileId) = await Seed(initialLocations: ["chest", "abdomen"]);

        await CreateSut().Handle(
            new UpdateMyBodyMapCommand(new UpdateBodyMapRequest([])),
            default);

        _db.ClientProfiles.Single(p => p.Id == profileId)
            .BodyMap.Locations.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoClientRecord_ThrowsNotFoundException()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(
            new UpdateMyBodyMapCommand(new UpdateBodyMapRequest(["chest"])),
            default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NoProfile_CreatesOneInsteadOfThrowing()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = "s" });
        Client client = new()
        {
            StudioId  = _studioId,
            UserId    = _userId,
            FirstName = "A",
            LastName  = "B",
            Email     = $"{_userId}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        ClientProfileResponse result = await CreateSut().Handle(
            new UpdateMyBodyMapCommand(new UpdateBodyMapRequest(["chest"])),
            default);

        result.ClientId.Should().Be(client.Id);
        result.BodyMapLocations.Should().ContainSingle("chest");
        _db.ClientProfiles.Should().ContainSingle(p => p.ClientId == client.Id);
    }

    private async Task<(Guid clientId, Guid profileId)> Seed(
        List<string>? initialLocations = null)
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = "s" });
        await _db.SaveChangesAsync();

        Client client = new()
        {
            StudioId  = _studioId,
            UserId    = _userId,
            FirstName = "A",
            LastName  = "B",
            Email     = $"{_userId}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        ClientProfile profile = new()
        {
            ClientId  = client.Id,
            StudioId  = _studioId,
            BodyMap   = new BodyMap { Locations = initialLocations ?? [] },
        };
        _db.ClientProfiles.Add(profile);
        await _db.SaveChangesAsync();

        return (client.Id, profile.Id);
    }
}
