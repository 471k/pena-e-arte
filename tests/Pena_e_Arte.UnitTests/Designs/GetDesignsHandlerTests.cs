using FluentAssertions;
using Pena_e_Arte.Application.Designs.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class GetDesignsHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetDesignsHandler CreateSut() => new(_db);

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
        Guid artist  = Guid.NewGuid();

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
        Guid client  = Guid.NewGuid();
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

    private async Task SeedDesigns(params (Guid ClientId, Guid ArtistId, string Title)[] designs)
    {
        foreach ((Guid clientId, Guid artistId, string title) in designs)
            _db.Designs.Add(new Design { StudioId = _studioId, ClientId = clientId, ArtistId = artistId, Title = title });

        await _db.SaveChangesAsync();
    }
}
