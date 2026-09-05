using FluentAssertions;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdateClientArtistHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private UpdateClientArtistHandler CreateSut() => new(_db);

    private Client AddClient(Guid? artistId = null)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Ana",
            LastName = "Costa",
            Email = $"{Guid.NewGuid()}@test.com",
            ArtistId = artistId
        };
        _db.Clients.Add(client);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return client;
    }

    private Artist AddArtist(bool isActive = true)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = "Art",
            LastName = "Ist",
            Email = $"{Guid.NewGuid()}@test.com",
            IsActive = isActive
        };
        _db.Artists.Add(artist);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return artist;
    }

    [Fact]
    public async Task Handle_ReassignToDifferentArtist_UpdatesClientArtistId()
    {
        Artist firstArtist = AddArtist();
        Artist secondArtist = AddArtist();
        Client client = AddClient(firstArtist.Id);

        ClientResponse result = await CreateSut().Handle(
            new UpdateClientArtistCommand(client.Id, new UpdateClientArtistRequest(secondArtist.Id)), default);

        result.ArtistId.Should().Be(secondArtist.Id);
        _db.Clients.Single().ArtistId.Should().Be(secondArtist.Id);
    }

    [Fact]
    public async Task Handle_UnassignArtist_SetsArtistIdNull()
    {
        Artist artist = AddArtist();
        Client client = AddClient(artist.Id);

        ClientResponse result = await CreateSut().Handle(
            new UpdateClientArtistCommand(client.Id, new UpdateClientArtistRequest(null)), default);

        result.ArtistId.Should().BeNull();
        result.ArtistName.Should().BeNull();
        _db.Clients.Single().ArtistId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UnknownClient_ThrowsNotFoundException()
    {
        Artist artist = AddArtist();

        Func<Task> act = () => CreateSut().Handle(
            new UpdateClientArtistCommand(Guid.NewGuid(), new UpdateClientArtistRequest(artist.Id)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_UnknownArtist_ThrowsNotFoundException()
    {
        Client client = AddClient();

        Func<Task> act = () => CreateSut().Handle(
            new UpdateClientArtistCommand(client.Id, new UpdateClientArtistRequest(Guid.NewGuid())), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_InactiveArtist_ThrowsBusinessRuleViolationException()
    {
        Artist inactiveArtist = AddArtist(isActive: false);
        Client client = AddClient();

        Func<Task> act = () => CreateSut().Handle(
            new UpdateClientArtistCommand(client.Id, new UpdateClientArtistRequest(inactiveArtist.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ValidReassignment_UpdatesUpdatedAtTimestamp()
    {
        Artist firstArtist = AddArtist();
        Artist secondArtist = AddArtist();
        Client client = AddClient(firstArtist.Id);
        DateTime before = client.UpdatedAt;

        await CreateSut().Handle(
            new UpdateClientArtistCommand(client.Id, new UpdateClientArtistRequest(secondArtist.Id)), default);

        _db.Clients.Single().UpdatedAt.Should().BeOnOrAfter(before);
    }
}
