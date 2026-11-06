using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

/// <summary>
/// 2026-11-06 (Phi's go): opening one design, or its versions, is limited to the people the design list
/// already shows it to — its client, its artist, owners and admins. Everyone else gets 404, the same answer
/// as for a design that doesn't exist.
/// </summary>
public class DesignReadScopingTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _clientUserId = Guid.NewGuid();
    private readonly Guid _artistUserId = Guid.NewGuid();
    private readonly Guid _designId;
    private readonly Guid _catalogId;

    public DesignReadScopingTests()
    {
        Artist artist = new() { StudioId = _studioId, UserId = _artistUserId, FirstName = "A", LastName = "B", Email = "a@a.test" };
        Artist otherArtist = new() { StudioId = _studioId, UserId = Guid.NewGuid(), FirstName = "O", LastName = "A", Email = "o@a.test" };
        Client client = new() { StudioId = _studioId, UserId = _clientUserId, FirstName = "C", LastName = "D", Email = "c@c.test" };
        Client otherClient = new() { StudioId = _studioId, UserId = Guid.NewGuid(), FirstName = "E", LastName = "F", Email = "e@c.test" };
        _db.Artists.AddRange(artist, otherArtist);
        _db.Clients.AddRange(client, otherClient);
        Design design = new() { StudioId = _studioId, ClientId = client.Id, ArtistId = artist.Id, Title = "Sleeve" };
        Design catalog = new() { StudioId = _studioId, ClientId = null, ArtistId = artist.Id, Title = "Flash", IsCatalogItem = true };
        _db.Designs.AddRange(design, catalog);
        _db.DesignRevisions.Add(new DesignRevision { StudioId = _studioId, DesignId = design.Id, VersionNumber = 1, FileUrl = "https://r2.example.com/v1.png" });
        _db.SaveChanges();
        _designId = design.Id;
        _catalogId = catalog.Id;
        _r2.IsR2Url(Arg.Any<string>()).Returns(false);
    }

    private Task<DesignResponse> GetDesign(FakeCurrentUser user, Guid? id = null) =>
        new GetDesignHandler(_db, user).Handle(new GetDesignQuery(id ?? _designId), default);

    private Task<List<DesignRevisionResponse>> GetRevisions(FakeCurrentUser user, Guid? id = null) =>
        new GetDesignRevisionsHandler(_db, _r2, user).Handle(new GetDesignRevisionsQuery(id ?? _designId), default);

    public static TheoryData<string> Allowed => new() { "own-client", "own-artist", "owner", "admin" };
    public static TheoryData<string> Refused => new() { "other-client", "other-artist", "client-without-record", "artist-without-profile" };

    private FakeCurrentUser User(string who) => who switch
    {
        "own-client" => new FakeCurrentUser(_clientUserId, "client"),
        "own-artist" => new FakeCurrentUser(_artistUserId, "artist"),
        "owner" => FakeCurrentUser.Owner(),
        "admin" => FakeCurrentUser.Admin(),
        "other-client" => new FakeCurrentUser(_db.Clients.Single(c => c.Email == "e@c.test").UserId!.Value, "client"),
        "other-artist" => new FakeCurrentUser(_db.Artists.Single(a => a.Email == "o@a.test").UserId!.Value, "artist"),
        "client-without-record" => FakeCurrentUser.Client(),
        "artist-without-profile" => FakeCurrentUser.Artist(),
        _ => throw new ArgumentOutOfRangeException(nameof(who)),
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public async Task TheDesignsOwnPeople_CanOpenItAndItsVersions(string who)
    {
        (await GetDesign(User(who))).Id.Should().Be(_designId);
        (await GetRevisions(User(who))).Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task AnyoneElseInTheStudio_GetsNotFound_ForTheDesignAndItsVersions(string who)
    {
        Func<Task> design = () => GetDesign(User(who));
        Func<Task> revisions = () => GetRevisions(User(who));

        await design.Should().ThrowAsync<NotFoundException>();
        await revisions.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ARefusal_LooksExactlyLikeADesignThatDoesNotExist()
    {
        Func<Task> missing = () => GetRevisions(FakeCurrentUser.Owner(), Guid.NewGuid());

        await missing.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ACatalogTemplate_OpensForItsArtistAndStaff_NotForAClient()
    {
        (await GetDesign(User("own-artist"), _catalogId)).Id.Should().Be(_catalogId);
        (await GetDesign(User("owner"), _catalogId)).Id.Should().Be(_catalogId);

        Func<Task> client = () => GetDesign(User("own-client"), _catalogId);
        await client.Should().ThrowAsync<NotFoundException>();
    }
}
