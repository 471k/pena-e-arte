using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class GetDesignCatalogHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetDesignCatalogHandler CreateSut() => new(_db);

    private Studio SeedStudio(string slug = "ink-studio")
    {
        Studio studio = new() { Name = "Ink Studio", Slug = slug, IsActive = true, IsPublished = true };
        _db.Studios.Add(studio);
        _db.SaveChanges();
        return studio;
    }

    private Artist SeedArtist(Guid studioId)
    {
        Artist artist = new() { StudioId = studioId, FirstName = "Jamie", LastName = "Lee", Email = $"{Guid.NewGuid()}@a.com" };
        _db.Artists.Add(artist);
        _db.SaveChanges();
        return artist;
    }

    [Fact]
    public async Task Handle_UnknownSlug_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetDesignCatalogQuery("no-such-studio"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_OnlyReturnsCatalogItemsWithNoClient()
    {
        Studio studio = SeedStudio();
        Artist artist = SeedArtist(studio.Id);

        _db.Designs.Add(new Design { StudioId = studio.Id, ArtistId = artist.Id, ClientId = null, IsCatalogItem = true, Title = "Flash A", Price = 100m });
        _db.Designs.Add(new Design { StudioId = studio.Id, ArtistId = artist.Id, ClientId = Guid.NewGuid(), Title = "Client design" });
        _db.Designs.Add(new Design { StudioId = studio.Id, ArtistId = artist.Id, ClientId = null, IsCatalogItem = false, Title = "Draft, not catalog" });
        await _db.SaveChangesAsync();

        List<DesignCatalogItemResponse> result = await CreateSut().Handle(new GetDesignCatalogQuery(studio.Slug), default);

        result.Should().ContainSingle(r => r.Title == "Flash A");
    }

    [Fact]
    public async Task Handle_TenantScoped_DoesNotLeakOtherStudiosCatalogItems()
    {
        Studio studioA = SeedStudio("studio-a");
        Studio studioB = SeedStudio("studio-b");
        Artist artistB = SeedArtist(studioB.Id);
        _db.Designs.Add(new Design { StudioId = studioB.Id, ArtistId = artistB.Id, ClientId = null, IsCatalogItem = true, Title = "B's flash" });
        await _db.SaveChangesAsync();

        List<DesignCatalogItemResponse> result = await CreateSut().Handle(new GetDesignCatalogQuery(studioA.Slug), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_IncludesArtistNameAndLatestRevisionImage()
    {
        Studio studio = SeedStudio();
        Artist artist = SeedArtist(studio.Id);
        Design design = new() { StudioId = studio.Id, ArtistId = artist.Id, ClientId = null, IsCatalogItem = true, Title = "Flash A", Price = 80m };
        _db.Designs.Add(design);
        await _db.SaveChangesAsync();
        _db.DesignRevisions.Add(new DesignRevision { StudioId = studio.Id, DesignId = design.Id, VersionNumber = 1, FileUrl = "https://r2/v1.png" });
        _db.DesignRevisions.Add(new DesignRevision { StudioId = studio.Id, DesignId = design.Id, VersionNumber = 2, FileUrl = "https://r2/v2.png" });
        await _db.SaveChangesAsync();

        List<DesignCatalogItemResponse> result = await CreateSut().Handle(new GetDesignCatalogQuery(studio.Slug), default);

        result.Single().ArtistName.Should().Be("Jamie Lee");
        result.Single().ImageUrl.Should().Be("https://r2/v2.png");
        result.Single().Price.Should().Be(80m);
    }
}
