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

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<ClientResponse> RunCreateHandler(Guid tenantId, CreateClientRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        CreateClientHandler handler = new(db, tenant);
        return await handler.Handle(new CreateClientCommand(req), default);
    }
}
