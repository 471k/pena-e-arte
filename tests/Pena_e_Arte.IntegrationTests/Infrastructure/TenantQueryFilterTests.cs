using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

[Collection("Database")]
public class TenantQueryFilterTests(DatabaseFixture fixture)
{
    // ── Clients ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clients_ScopedToTenant_AreVisibleToSameTenant()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedClient(tenantId, "ana@example.com");

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        List<Client> result = await ctx.Clients.ToListAsync();

        result.Should().ContainSingle(c => c.Email == "ana@example.com");
    }

    [Fact]
    public async Task Clients_ScopedToTenantA_AreNotVisibleToTenantB()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await SeedClient(tenantA, $"{tenantA}@example.com");

        await using AppDbContext ctx = fixture.CreateDbContext(tenantB);
        List<Client> result = await ctx.Clients.ToListAsync();

        result.Should().NotContain(c => c.StudioId == tenantA);
    }

    [Fact]
    public async Task Clients_IgnoreQueryFilters_ReturnsAllTenants()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await SeedClient(tenantA, $"a-{tenantA}@example.com");
        await SeedClient(tenantB, $"b-{tenantB}@example.com");

        await using AppDbContext ctx = fixture.CreateDbContext(tenantA);
        List<Client> result = await ctx.Clients
            .IgnoreQueryFilters()
            .Where(c => c.StudioId == tenantA || c.StudioId == tenantB)
            .ToListAsync();

        result.Should().HaveCount(2);
    }

    // ── Appointments ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Appointments_ScopedToTenantA_AreNotVisibleToTenantB()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await SeedAppointment(tenantA);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantB);
        List<Appointment> result = await ctx.Appointments.ToListAsync();

        result.Should().NotContain(a => a.StudioId == tenantA);
    }

    // ── Designs ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Designs_ScopedToTenantA_AreNotVisibleToTenantB()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await SeedDesign(tenantA);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantB);
        List<Design> result = await ctx.Designs.ToListAsync();

        result.Should().NotContain(d => d.StudioId == tenantA);
    }

    [Fact]
    public async Task Designs_ScopedToSameTenant_AreVisible()
    {
        Guid tenantId = Guid.NewGuid();
        await SeedDesign(tenantId);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        List<Design> result = await ctx.Designs.ToListAsync();

        result.Should().ContainSingle(d => d.StudioId == tenantId);
    }

    // ── Artists ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Artists_ScopedToTenantA_AreNotVisibleToTenantB()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await SeedArtist(tenantA);

        await using AppDbContext ctx = fixture.CreateDbContext(tenantB);
        List<Artist> result = await ctx.Artists.ToListAsync();

        result.Should().NotContain(a => a.StudioId == tenantA);
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task SeedClient(Guid tenantId, string email)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        ctx.Clients.Add(new Client { StudioId = tenantId, FirstName = "Test", LastName = "User", Email = email });
        await ctx.SaveChangesAsync();
    }

    private async Task SeedArtist(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        ctx.Artists.Add(new Artist { StudioId = tenantId, FirstName = "Test", LastName = "Artist", Email = $"{Guid.NewGuid()}@artist.com" });
        await ctx.SaveChangesAsync();
    }

    private async Task SeedAppointment(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);

        Artist artist = new() { StudioId = tenantId, FirstName = "Art", LastName = "ist", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "Cli", LastName = "ent", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

        ctx.Appointments.Add(new Appointment
        {
            StudioId        = tenantId,
            ArtistId        = artist.Id,
            ClientId        = client.Id,
            Date            = DateTime.UtcNow.AddDays(3),
            EndDate         = DateTime.UtcNow.AddDays(3).AddHours(2),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending
        });
        await ctx.SaveChangesAsync();
    }

    private async Task SeedDesign(Guid tenantId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);

        Artist artist = new() { StudioId = tenantId, FirstName = "Art", LastName = "ist", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = tenantId, FirstName = "Cli", LastName = "ent", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();

        ctx.Designs.Add(new Design { StudioId = tenantId, ClientId = client.Id, ArtistId = artist.Id, Title = "Rose" });
        await ctx.SaveChangesAsync();
    }
}
