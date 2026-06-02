using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

[Collection("Database")]
public class SoftDeleteFilterTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Client_WithDeletedAt_IsExcludedFromNormalQuery()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClient(tenantId, softDelete: true);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        bool exists = await ctx.Clients.AnyAsync(c => c.Id == clientId);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Client_WithoutDeletedAt_IsIncludedInNormalQuery()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClient(tenantId, softDelete: false);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        bool exists = await ctx.Clients.AnyAsync(c => c.Id == clientId);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Client_WithDeletedAt_IsVisibleWhenFilterIgnored()
    {
        Guid tenantId = Guid.NewGuid();
        Guid clientId = await SeedClient(tenantId, softDelete: true);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        bool exists = await ctx.Clients
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == clientId);

        exists.Should().BeTrue();
    }

    [Fact]
    public async Task Design_WithDeletedAt_IsExcludedFromNormalQuery()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext seedCtx = fixture.CreateDbContext(tenantId);

        Artist artist = new() { StudioId = tenantId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        seedCtx.Artists.Add(artist);
        seedCtx.Clients.Add(client);
        await seedCtx.SaveChangesAsync();

        Design design = new()
        {
            StudioId  = tenantId,
            ClientId  = client.Id,
            ArtistId  = artist.Id,
            Title     = "Deleted Design",
            DeletedAt = DateTime.UtcNow
        };
        seedCtx.Designs.Add(design);
        await seedCtx.SaveChangesAsync();

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        bool exists = await ctx.Designs.AnyAsync(d => d.Id == design.Id);

        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Artist_WithDeletedAt_IsExcludedFromNormalQuery()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext seedCtx = fixture.CreateDbContext(tenantId);

        Artist artist = new()
        {
            StudioId  = tenantId,
            FirstName = "Deleted",
            LastName  = "Artist",
            Email     = $"{Guid.NewGuid()}@artist.com",
            DeletedAt = DateTime.UtcNow
        };
        seedCtx.Artists.Add(artist);
        await seedCtx.SaveChangesAsync();

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        bool exists = await ctx.Artists.AnyAsync(a => a.Id == artist.Id);

        exists.Should().BeFalse();
    }

    private async Task<Guid> SeedClient(Guid tenantId, bool softDelete)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);

        Client client = new()
        {
            StudioId  = tenantId,
            FirstName = "Soft",
            LastName  = "Delete",
            Email     = $"{Guid.NewGuid()}@example.com",
            DeletedAt = softDelete ? DateTime.UtcNow : null
        };
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        return client.Id;
    }
}
