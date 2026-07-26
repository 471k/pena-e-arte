using FluentAssertions;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Models;
using Pena_e_Arte.Domain.ValueObjects;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class PortableProfileIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetPortableProfile_OptedInClient_ReturnsProfile()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        (Client client, _) = await SeedOptedInClientAsync(db, tenantId, userId);

        PortableProfileService service = new(db);
        GetPortableProfileHandler handler = new(service);

        PortableClientProfile? result = await handler.Handle(
            new GetPortableProfileQuery(userId), default);

        result.Should().NotBeNull();
        result!.DisplayName.Should().Contain(client.FirstName);
        result.BodyMapLocations.Should().BeEmpty();
        result.TattooHistory.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPortableProfile_OptedOutClient_ReturnsNull()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        await SeedOptedOutClientAsync(db, tenantId, userId);

        PortableProfileService service = new(db);
        GetPortableProfileHandler handler = new(service);

        PortableClientProfile? result = await handler.Handle(
            new GetPortableProfileQuery(userId), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPortableProfile_OptedInClientWithTattoos_ReturnsTattooHistory()
    {
        Guid tenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        (Client client, _) = await SeedOptedInClientAsync(db, tenantId, userId);

        Artist artist = new()
        {
            StudioId = tenantId,
            FirstName = "Luis",
            LastName = "Silva",
            Email = $"{Guid.NewGuid()}@test.com"
        };
        db.Artists.Add(artist);

        TattooRecord record = new()
        {
            StudioId = tenantId,
            ClientId = client.Id,
            ArtistId = artist.Id,
            Description = "Dragon sleeve",
            BodyLocation = "left_arm",
            PhotoUrls = ["https://r2.example.com/photo.jpg"],
            CompletedAt = DateTime.UtcNow.AddDays(-10),
        };
        db.TattooRecords.Add(record);
        await db.SaveChangesAsync();

        PortableProfileService service = new(db);
        GetPortableProfileHandler handler = new(service);

        PortableClientProfile? result = await handler.Handle(
            new GetPortableProfileQuery(userId), default);

        result.Should().NotBeNull();
        result!.TattooHistory.Should().ContainSingle();
        result.TattooHistory[0].BodyLocation.Should().Be("left_arm");
        result.TattooHistory[0].ArtistFirstName.Should().Be("Luis");
        result.TattooHistory[0].Description.Should().Be("Dragon sleeve");
    }

    [Fact]
    public async Task GetPortableProfile_CrossTenant_ArtistFromDifferentTenantCanView()
    {
        Guid clientTenantId = Guid.NewGuid();
        Guid artistTenantId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        await using AppDbContext clientDb = fixture.CreateDbContext(clientTenantId);
        await SeedOptedInClientAsync(clientDb, clientTenantId, userId);

        // Artist queries from a different tenant's context — IgnoreQueryFilters bypasses tenant filter
        await using AppDbContext artistDb = fixture.CreateDbContext(artistTenantId);
        PortableProfileService service = new(artistDb);
        GetPortableProfileHandler handler = new(service);

        PortableClientProfile? result = await handler.Handle(
            new GetPortableProfileQuery(userId), default);

        result.Should().NotBeNull(
            because: "IgnoreQueryFilters allows cross-tenant read for opted-in profiles");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task<(Client client, ClientProfile profile)> SeedOptedInClientAsync(
        AppDbContext db, Guid tenantId, Guid userId)
    {
        Client client = new()
        {
            StudioId = tenantId,
            UserId = userId,
            FirstName = "Ana",
            LastName = "Costa",
            Email = $"{Guid.NewGuid()}@test.com"
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        ClientProfile profile = new()
        {
            StudioId = tenantId,
            ClientId = client.Id,
            BodyMap = new BodyMap { Locations = [] }
        };
        profile.OptInToCrossTenant();
        db.ClientProfiles.Add(profile);
        await db.SaveChangesAsync();

        return (client, profile);
    }

    private static async Task SeedOptedOutClientAsync(AppDbContext db, Guid tenantId, Guid userId)
    {
        Client client = new()
        {
            StudioId = tenantId,
            UserId = userId,
            FirstName = "Rui",
            LastName = "Neves",
            Email = $"{Guid.NewGuid()}@test.com"
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        ClientProfile profile = new()
        {
            StudioId = tenantId,
            ClientId = client.Id,
            BodyMap = new BodyMap { Locations = [] }
        };
        db.ClientProfiles.Add(profile);
        await db.SaveChangesAsync();
    }
}
