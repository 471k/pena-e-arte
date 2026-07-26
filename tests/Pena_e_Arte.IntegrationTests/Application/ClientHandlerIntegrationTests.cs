using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class ClientHandlerIntegrationTests(DatabaseFixture fixture)
{
    // ── CreateClient ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateClient_NewEmail_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        CreateClientRequest req = new("Ana", "Costa", $"{Guid.NewGuid()}@example.com", null);

        ClientResponse result = await RunCreateHandler(tenantId, req);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        bool exists = await verify.Clients.AnyAsync(c => c.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task CreateClient_DuplicateEmailSameTenant_ThrowsBusinessRuleViolationException()
    {
        Guid tenantId = Guid.NewGuid();
        string email = $"{Guid.NewGuid()}@example.com";

        await RunCreateHandler(tenantId, new("First", "Client", email, null));

        Func<Task> act = () => RunCreateHandler(tenantId, new("Second", "Client", email, null));

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task CreateClient_SameEmailDifferentTenants_BothSucceed()
    {
        string sharedEmail = $"{Guid.NewGuid()}@shared.com";
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await RunCreateHandler(tenantA, new("Ana", "A", sharedEmail, null));
        Func<Task> act = () => RunCreateHandler(tenantB, new("Bea", "B", sharedEmail, null));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateClient_WithPhone_PersistsPhone()
    {
        Guid tenantId = Guid.NewGuid();
        CreateClientRequest req = new("Rui", "Neves", $"{Guid.NewGuid()}@example.com", "+351912000000");

        ClientResponse result = await RunCreateHandler(tenantId, req);

        result.Phone.Should().Be("+351912000000");
    }

    [Fact]
    public async Task CreateClient_TenantIsolation_DuplicateEmailCheckDoesNotCrossTenantsAtHandlerLevel()
    {
        // The handler uses db.Clients.AnyAsync which applies the tenant query filter.
        // A client in tenantA should NOT block the same email in tenantB at the handler level.
        string email = $"{Guid.NewGuid()}@example.com";
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await using AppDbContext seed = fixture.CreateDbContext(tenantA);
        seed.Clients.Add(new Client { StudioId = tenantA, FirstName = "A", LastName = "B", Email = email });
        await seed.SaveChangesAsync();

        // Handler for tenantB should not see tenantA's client
        Func<Task> act = () => RunCreateHandler(tenantB, new("C", "D", email, null));

        await act.Should().NotThrowAsync(
            because: "the tenant query filter ensures the AnyAsync check is scoped to the current tenant");
    }

    // ── GetClients ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetClients_ReturnsOnlyCurrentTenantClients()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await RunCreateHandler(tenantA, new("Ana", "A", $"{Guid.NewGuid()}@a.com", null));
        await RunCreateHandler(tenantA, new("Bea", "A", $"{Guid.NewGuid()}@a.com", null));
        await RunCreateHandler(tenantB, new("Carlos", "B", $"{Guid.NewGuid()}@b.com", null));

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        GetClientsHandler handler = new(db);
        List<ClientResponse> result = await handler.Handle(new GetClientsQuery(null), default);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(c => c.StudioId.Should().Be(tenantA));
    }

    [Fact]
    public async Task GetClients_SearchByEmail_ReturnsMatchWithRealSql()
    {
        Guid tenantId = Guid.NewGuid();
        string uniquePart = Guid.NewGuid().ToString("N")[..8];
        string targetEmail = $"target-{uniquePart}@example.com";

        await RunCreateHandler(tenantId, new("Target", "User", targetEmail, null));
        await RunCreateHandler(tenantId, new("Other", "User", $"other-{uniquePart}@example.com", null));

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        GetClientsHandler handler = new(db);
        List<ClientResponse> result = await handler.Handle(new GetClientsQuery(targetEmail), default);

        result.Should().ContainSingle(c => c.Email == targetEmail);
    }

    // ── AddTattooRecord ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddTattooRecord_ValidRequest_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid artistId) = await SeedClientAndArtistAsync(tenantId);
        AddTattooRecordRequest req = new(artistId, null, "Dragon sleeve", "left_arm", [], DateTime.UtcNow.AddDays(-5));

        TattooRecordResponse result = await RunAddTattooRecordHandler(tenantId, clientId, req);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        bool exists = await verify.TattooRecords.AnyAsync(t => t.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task AddTattooRecord_TenantIsolation_RecordScopedToTenant()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        (Guid clientId, Guid artistId) = await SeedClientAndArtistAsync(tenantA);
        AddTattooRecordRequest req = new(artistId, null, "Rose", "wrist", [], DateTime.UtcNow.AddDays(-1));

        TattooRecordResponse result = await RunAddTattooRecordHandler(tenantA, clientId, req);

        await using AppDbContext tenantBCtx = fixture.CreateDbContext(tenantB);
        bool visibleToOtherTenant = await tenantBCtx.TattooRecords.AnyAsync(t => t.Id == result.Id);
        visibleToOtherTenant.Should().BeFalse(because: "tenant query filter must isolate records");
    }

    [Fact]
    public async Task AddTattooRecord_UnknownClient_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();
        (_, Guid artistId) = await SeedClientAndArtistAsync(tenantId);
        AddTattooRecordRequest req = new(artistId, null, "Skull", "neck", [], DateTime.UtcNow.AddDays(-2));

        Func<Task> act = () => RunAddTattooRecordHandler(tenantId, Guid.NewGuid(), req);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── GetTattooRecords ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTattooRecords_ReturnsOnlyCurrentTenantRecords()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        (Guid clientA, Guid artistA) = await SeedClientAndArtistAsync(tenantA);
        (Guid clientB, Guid artistB) = await SeedClientAndArtistAsync(tenantB);

        await RunAddTattooRecordHandler(tenantA, clientA, new(artistA, null, "A1", "arm", [], DateTime.UtcNow.AddDays(-1)));
        await RunAddTattooRecordHandler(tenantA, clientA, new(artistA, null, "A2", "leg", [], DateTime.UtcNow.AddDays(-2)));
        await RunAddTattooRecordHandler(tenantB, clientB, new(artistB, null, "B1", "back", [], DateTime.UtcNow.AddDays(-1)));

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        GetTattooRecordsHandler handler = new(db);
        List<TattooRecordResponse> result = await handler.Handle(new GetTattooRecordsQuery(clientA), default);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.ClientId.Should().Be(clientA));
    }

    // ── GetTattooRecord (single) ─────────────────────────────────────────────────

    [Fact]
    public async Task GetTattooRecord_ExistingRecord_ReturnsRecord()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid artistId) = await SeedClientAndArtistAsync(tenantId);
        AddTattooRecordRequest addReq = new(artistId, null, "Lotus", "shoulder", [], DateTime.UtcNow.AddDays(-7));
        TattooRecordResponse added = await RunAddTattooRecordHandler(tenantId, clientId, addReq);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        GetTattooRecordHandler handler = new(db);
        TattooRecordResponse result = await handler.Handle(new GetTattooRecordQuery(clientId, added.Id), default);

        result.Id.Should().Be(added.Id);
        result.Description.Should().Be("Lotus");
    }

    // ── UpdateTattooRecord ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateTattooRecord_ValidRequest_PersistsChangesToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid artistId) = await SeedClientAndArtistAsync(tenantId);
        TattooRecordResponse added = await RunAddTattooRecordHandler(tenantId, clientId,
            new(artistId, null, "Old desc", "left_arm", [], DateTime.UtcNow.AddDays(-10)));

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        UpdateTattooRecordHandler handler = new(db);
        UpdateTattooRecordRequest updateReq = new("New desc", "right_leg", [], DateTime.UtcNow.AddDays(-3));
        TattooRecordResponse updated = await handler.Handle(
            new UpdateTattooRecordCommand(clientId, added.Id, updateReq), default);

        updated.Description.Should().Be("New desc");
        updated.BodyLocation.Should().Be("right_leg");

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        TattooRecord? persisted = await verify.TattooRecords.FirstOrDefaultAsync(t => t.Id == added.Id);
        persisted!.Description.Should().Be("New desc");
    }

    // ── DeleteTattooRecord ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteTattooRecord_ExistingRecord_SoftDeletesFromDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        (Guid clientId, Guid artistId) = await SeedClientAndArtistAsync(tenantId);
        TattooRecordResponse added = await RunAddTattooRecordHandler(tenantId, clientId,
            new(artistId, null, "Phoenix", "back", [], DateTime.UtcNow.AddDays(-15)));

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        DeleteTattooRecordHandler handler = new(db);
        await handler.Handle(new DeleteTattooRecordCommand(clientId, added.Id), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        bool stillVisible = await verify.TattooRecords.AnyAsync(t => t.Id == added.Id);
        stillVisible.Should().BeFalse(because: "soft-delete filter excludes records with DeletedAt set");

        bool existsUnfiltered = await verify.TattooRecords
            .IgnoreQueryFilters()
            .AnyAsync(t => t.Id == added.Id && t.DeletedAt != null);
        existsUnfiltered.Should().BeTrue(because: "row must still exist with DeletedAt populated");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<ClientResponse> RunCreateHandler(Guid tenantId, CreateClientRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        CreateClientHandler handler = new(db, tenant);
        return await handler.Handle(new CreateClientCommand(req), default);
    }

    private async Task<(Guid clientId, Guid artistId)> SeedClientAndArtistAsync(Guid tenantId)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId = tenantId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        Artist artist = new()
        {
            StudioId = tenantId,
            FirstName = "Test",
            LastName = "Artist",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        db.Clients.Add(client);
        db.Artists.Add(artist);
        await db.SaveChangesAsync();
        return (client.Id, artist.Id);
    }

    private async Task<TattooRecordResponse> RunAddTattooRecordHandler(
        Guid tenantId, Guid clientId, AddTattooRecordRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        AddTattooRecordHandler handler = new(db, tenant);
        return await handler.Handle(new AddTattooRecordCommand(clientId, req), default);
    }
}
