using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class CreateClientHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Owner();
    private readonly Guid _studioId = Guid.NewGuid();

    public CreateClientHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CreateClientHandler CreateSut() => new(_db, _tenant, _currentUser);

    private Artist AddArtist()
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = "Art",
            LastName = "Ist",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);
        _db.SaveChangesAsync().GetAwaiter().GetResult();
        return artist;
    }

    [Fact]
    public async Task Handle_NewEmail_ReturnsClientResponse()
    {
        Artist artist = AddArtist();
        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", "+351911000000", artist.Id);

        ClientResponse result = await CreateSut().Handle(new CreateClientCommand(req), default);

        result.FirstName.Should().Be("Ana");
        result.LastName.Should().Be("Costa");
        result.Email.Should().Be("ana@example.com");
        result.Phone.Should().Be("+351911000000");
        result.StudioId.Should().Be(_studioId);
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_NewEmail_PersistsClientToDb()
    {
        Artist artist = AddArtist();
        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", null, artist.Id);

        await CreateSut().Handle(new CreateClientCommand(req), default);

        _db.Clients.Should().ContainSingle(c => c.Email == "ana@example.com");
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ThrowsBusinessRuleViolationException()
    {
        Artist artist = AddArtist();
        const string email = "duplicate@example.com";
        _db.Clients.Add(new Client
        {
            StudioId = _studioId,
            FirstName = "Existing",
            LastName = "Client",
            Email = email
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut()
            .Handle(new CreateClientCommand(new("New", "Client", email, null, artist.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage($"*{email}*");
    }

    [Fact]
    public async Task Handle_DuplicateEmail_DoesNotPersistSecondClient()
    {
        Artist artist = AddArtist();
        const string email = "duplicate@example.com";
        _db.Clients.Add(new Client { StudioId = _studioId, FirstName = "A", LastName = "B", Email = email });
        await _db.SaveChangesAsync();

        try
        {
            await CreateSut().Handle(new CreateClientCommand(new("C", "D", email, null, artist.Id)), default);
        }
        catch { }

        _db.Clients.Should().ContainSingle(c => c.Email == email);
    }

    [Fact]
    public async Task Handle_OwnerCaller_UsesRequestedArtistId()
    {
        Artist artist = AddArtist();
        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", null, artist.Id);

        ClientResponse result = await CreateSut().Handle(new CreateClientCommand(req), default);

        result.ArtistId.Should().Be(artist.Id);
    }

    [Fact]
    public async Task Handle_ArtistCaller_OverridesRequestedArtistIdWithOwnArtistId()
    {
        Artist otherArtist = AddArtist();
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        Artist callerArtist = new()
        {
            StudioId = _studioId,
            UserId = artistUser.UserId,
            FirstName = "Caller",
            LastName = "Artist",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(callerArtist);
        await _db.SaveChangesAsync();

        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", null, otherArtist.Id);
        CreateClientHandler sut = new(_db, _tenant, artistUser);

        ClientResponse result = await sut.Handle(new CreateClientCommand(req), default);

        result.ArtistId.Should().Be(callerArtist.Id);
        result.ArtistId.Should().NotBe(otherArtist.Id);
        _db.Clients.Single().ArtistId.Should().Be(callerArtist.Id);
    }

    [Fact]
    public async Task Handle_ArtistCallerWithNoArtistRecord_ThrowsForbidden()
    {
        Artist artist = AddArtist();
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", null, artist.Id);
        CreateClientHandler sut = new(_db, _tenant, artistUser);

        Func<Task> act = () => sut.Handle(new CreateClientCommand(req), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Handle_NonExistentArtistId_ThrowsNotFoundException()
    {
        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", null, Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(new CreateClientCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_InactiveArtist_ThrowsBusinessRuleViolationException()
    {
        Artist artist = AddArtist();
        artist.IsActive = false;
        await _db.SaveChangesAsync();

        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", null, artist.Id);

        Func<Task> act = () => CreateSut().Handle(new CreateClientCommand(req), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ValidRequest_ResponseIncludesArtistName()
    {
        Artist artist = AddArtist();
        CreateClientRequest req = new("Ana", "Costa", "ana@example.com", null, artist.Id);

        ClientResponse result = await CreateSut().Handle(new CreateClientCommand(req), default);

        result.ArtistName.Should().Be($"{artist.FirstName} {artist.LastName}");
    }
}
