using FluentAssertions;
using Pena_e_Arte.Application.Designs.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class GetDesignsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetDesignsHandler CreateSut() => new(_db, FakeCurrentUser.Owner());

    [Fact]
    public async Task Handle_NoFilter_ReturnsAllDesignsNewestFirst()
    {
        Guid clientId = Guid.NewGuid();
        Guid artistId = Guid.NewGuid();

        Design old = new() { StudioId = _studioId, ClientId = clientId, ArtistId = artistId, Title = "Old" };
        Design recent = new() { StudioId = _studioId, ClientId = clientId, ArtistId = artistId, Title = "Recent" };

        _db.Designs.Add(old);
        await _db.SaveChangesAsync();
        await Task.Delay(5);
        _db.Designs.Add(recent);
        await _db.SaveChangesAsync();

        List<DesignResponse> result = await CreateSut().Handle(new GetDesignsQuery(null, null), default);

        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Recent");
        result[1].Title.Should().Be("Old");
    }

    [Fact]
    public async Task Handle_ClientIdFilter_ReturnsOnlyMatchingClient()
    {
        Guid clientA = Guid.NewGuid();
        Guid clientB = Guid.NewGuid();
        Guid artist = Guid.NewGuid();

        await SeedDesigns(
            (clientA, artist, "Design A1"),
            (clientB, artist, "Design B1"),
            (clientA, artist, "Design A2"));

        List<DesignResponse> result = await CreateSut()
            .Handle(new GetDesignsQuery(ClientId: clientA, ArtistId: null), default);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(d => d.ClientId.Should().Be(clientA));
    }

    [Fact]
    public async Task Handle_ArtistIdFilter_ReturnsOnlyMatchingArtist()
    {
        Guid client = Guid.NewGuid();
        Guid artistA = Guid.NewGuid();
        Guid artistB = Guid.NewGuid();

        await SeedDesigns(
            (client, artistA, "Design 1"),
            (client, artistB, "Design 2"));

        List<DesignResponse> result = await CreateSut()
            .Handle(new GetDesignsQuery(ClientId: null, ArtistId: artistA), default);

        result.Should().ContainSingle(d => d.ArtistId == artistA);
    }

    [Fact]
    public async Task Handle_BothFilters_AppliesBothConditions()
    {
        Guid clientA = Guid.NewGuid();
        Guid clientB = Guid.NewGuid();
        Guid artistA = Guid.NewGuid();
        Guid artistB = Guid.NewGuid();

        await SeedDesigns(
            (clientA, artistA, "Match"),
            (clientA, artistB, "Wrong artist"),
            (clientB, artistA, "Wrong client"));

        List<DesignResponse> result = await CreateSut()
            .Handle(new GetDesignsQuery(ClientId: clientA, ArtistId: artistA), default);

        result.Should().ContainSingle(d => d.Title == "Match");
    }

    [Fact]
    public async Task Handle_EmptyDb_ReturnsEmpty()
    {
        List<DesignResponse> result = await CreateSut().Handle(new GetDesignsQuery(null, null), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ArtistCaller_ReturnsOnlyOwnDesigns()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        Guid client = Guid.NewGuid();
        Guid myArtistId = await SeedArtistForUser(artistUser.UserId);
        Guid otherArtistId = Guid.NewGuid();
        await SeedDesigns((client, myArtistId, "Mine"), (client, otherArtistId, "Not mine"));

        GetDesignsHandler sut = new(_db, artistUser);
        List<DesignResponse> result = await sut.Handle(new GetDesignsQuery(null, null), default);

        result.Should().ContainSingle(d => d.Title == "Mine");
    }

    [Fact]
    public async Task Handle_ArtistCaller_IgnoresRequestedArtistIdFilter()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        Guid client = Guid.NewGuid();
        Guid myArtistId = await SeedArtistForUser(artistUser.UserId);
        Guid otherArtistId = Guid.NewGuid();
        await SeedDesigns((client, myArtistId, "Mine"), (client, otherArtistId, "Not mine"));

        GetDesignsHandler sut = new(_db, artistUser);
        List<DesignResponse> result = await sut.Handle(new GetDesignsQuery(null, otherArtistId), default);

        result.Should().ContainSingle(d => d.Title == "Mine");
    }

    [Fact]
    public async Task Handle_ArtistCallerWithNoArtistRecord_ReturnsEmpty()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        await SeedDesigns((Guid.NewGuid(), Guid.NewGuid(), "Some design"));

        GetDesignsHandler sut = new(_db, artistUser);
        List<DesignResponse> result = await sut.Handle(new GetDesignsQuery(null, null), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoRevisions_StatusIsDraft()
    {
        _db.Designs.Add(new Design { StudioId = _studioId, ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "No revisions" });
        await _db.SaveChangesAsync();

        List<DesignResponse> result = await CreateSut().Handle(new GetDesignsQuery(null, null), default);

        result.Single().Status.Should().Be("Draft");
    }

    [Fact]
    public async Task Handle_RevisionWithNoApproval_StatusIsInReview()
    {
        Design design = new() { StudioId = _studioId, ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "In review" };
        _db.Designs.Add(design);
        _db.DesignRevisions.Add(new DesignRevision { StudioId = _studioId, DesignId = design.Id, VersionNumber = 1, FileUrl = "https://r2.example.com/v1.png" });
        await _db.SaveChangesAsync();

        List<DesignResponse> result = await CreateSut().Handle(new GetDesignsQuery(null, null), default);

        result.Single().Status.Should().Be("InReview");
    }

    [Fact]
    public async Task Handle_LatestRevisionApproved_StatusIsApproved()
    {
        Design design = new() { StudioId = _studioId, ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "Approved design" };
        _db.Designs.Add(design);
        DesignRevision revision = new() { StudioId = _studioId, DesignId = design.Id, VersionNumber = 1, FileUrl = "https://r2.example.com/v1.png" };
        _db.DesignRevisions.Add(revision);
        _db.DesignApprovals.Add(new DesignApproval { StudioId = _studioId, DesignRevisionId = revision.Id, Status = DesignApprovalStatus.Approved });
        await _db.SaveChangesAsync();

        List<DesignResponse> result = await CreateSut().Handle(new GetDesignsQuery(null, null), default);

        result.Single().Status.Should().Be("Approved");
    }

    [Fact]
    public async Task Handle_LatestRevisionChangesRequested_StatusIsChangesRequested()
    {
        Design design = new() { StudioId = _studioId, ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "Needs changes" };
        _db.Designs.Add(design);
        DesignRevision revision = new() { StudioId = _studioId, DesignId = design.Id, VersionNumber = 1, FileUrl = "https://r2.example.com/v1.png" };
        _db.DesignRevisions.Add(revision);
        _db.DesignApprovals.Add(new DesignApproval { StudioId = _studioId, DesignRevisionId = revision.Id, Status = DesignApprovalStatus.ChangesRequested });
        await _db.SaveChangesAsync();

        List<DesignResponse> result = await CreateSut().Handle(new GetDesignsQuery(null, null), default);

        result.Single().Status.Should().Be("ChangesRequested");
    }

    [Fact]
    public async Task Handle_StatusReflectsLatestRevisionNotEarlierOnes()
    {
        Design design = new() { StudioId = _studioId, ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "Multi-revision" };
        _db.Designs.Add(design);
        DesignRevision v1 = new() { StudioId = _studioId, DesignId = design.Id, VersionNumber = 1, FileUrl = "https://r2.example.com/v1.png" };
        DesignRevision v2 = new() { StudioId = _studioId, DesignId = design.Id, VersionNumber = 2, FileUrl = "https://r2.example.com/v2.png" };
        _db.DesignRevisions.Add(v1);
        _db.DesignRevisions.Add(v2);
        _db.DesignApprovals.Add(new DesignApproval { StudioId = _studioId, DesignRevisionId = v1.Id, Status = DesignApprovalStatus.ChangesRequested });
        await _db.SaveChangesAsync();

        List<DesignResponse> result = await CreateSut().Handle(new GetDesignsQuery(null, null), default);

        result.Single().Status.Should().Be("InReview");
    }

    private async Task<Guid> SeedArtistForUser(Guid userId)
    {
        var artist = new Artist
        {
            StudioId = _studioId,
            UserId = userId,
            FirstName = "Art",
            LastName = "Ist",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task SeedDesigns(params (Guid ClientId, Guid ArtistId, string Title)[] designs)
    {
        foreach ((Guid clientId, Guid artistId, string title) in designs)
            _db.Designs.Add(new Design { StudioId = _studioId, ClientId = clientId, ArtistId = artistId, Title = title });

        await _db.SaveChangesAsync();
    }
}
