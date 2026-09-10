using FluentAssertions;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class DesignCatalogHandlerIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetDesignCatalog_TenantScopedBySlug_OnlyReturnsThatStudiosItems()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        string slugA = $"studio-a-{Guid.NewGuid():N}";
        string slugB = $"studio-b-{Guid.NewGuid():N}";

        Guid artistA = await SeedPublishedStudioWithCatalogItem(tenantA, slugA, "Flash A");
        _ = await SeedPublishedStudioWithCatalogItem(tenantB, slugB, "Flash B");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetDesignCatalogHandler handler = new(db);
        List<DesignCatalogItemResponse> result = await handler.Handle(new GetDesignCatalogQuery(slugA), default);

        result.Should().ContainSingle(r => r.Title == "Flash A" && r.ArtistId == artistA);
    }

    [Fact]
    public async Task GetDesignCatalog_NonCatalogDesign_NotReturned()
    {
        Guid tenantId = Guid.NewGuid();
        string slug = $"studio-{Guid.NewGuid():N}";
        await SeedPublishedStudioWithCatalogItem(tenantId, slug, "Flash A");

        await using AppDbContext seedCtx = fixture.CreateDbContext(tenantId);
        Artist artist = seedCtx.Artists.First();
        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        seedCtx.Clients.Add(client);
        await seedCtx.SaveChangesAsync();
        seedCtx.Designs.Add(new Design
        {
            StudioId = tenantId,
            ArtistId = artist.Id,
            ClientId = client.Id,
            Title = "Not catalog",
        });
        await seedCtx.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetDesignCatalogHandler handler = new(db);
        List<DesignCatalogItemResponse> result = await handler.Handle(new GetDesignCatalogQuery(slug), default);

        result.Should().NotContain(r => r.Title == "Not catalog");
    }

    private async Task<Guid> SeedPublishedStudioWithCatalogItem(Guid tenantId, string slug, string title)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Studio studio = new()
        {
            Id = tenantId,
            Name = $"Studio {slug}",
            Slug = slug,
            City = "Porto",
            IsActive = true,
            IsPublished = true,
        };
        ctx.Studios.Add(studio);

        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        ctx.Artists.Add(artist);
        await ctx.SaveChangesAsync();

        Design design = new()
        {
            StudioId = tenantId,
            ArtistId = artist.Id,
            ClientId = null,
            IsCatalogItem = true,
            Title = title,
            Price = 100m,
        };
        ctx.Designs.Add(design);
        await ctx.SaveChangesAsync();

        return artist.Id;
    }
}
