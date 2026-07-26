using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.DepositRules.Commands;
using Pena_e_Arte.Application.DepositRules.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class DepositRuleHandlerIntegrationTests(DatabaseFixture fixture)
{
    // ── CreateDepositRule ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateDepositRule_FixedAmount_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        CreateDepositRuleRequest req = new("Standard Deposit", 50m, null, false);

        DepositRuleResponse result = await RunCreateHandler(tenantId, req);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        bool exists = await verify.DepositRules.AnyAsync(r => r.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task CreateDepositRule_PercentAmount_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        CreateDepositRuleRequest req = new("Percentage Deposit", null, 20m, false);

        DepositRuleResponse result = await RunCreateHandler(tenantId, req);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        DepositRule? rule = await verify.DepositRules.FindAsync(result.Id);
        rule!.AmountPercent.Should().Be(20m);
    }

    [Fact]
    public async Task CreateDepositRule_WithIsActive_DeactivatesExistingActiveRule()
    {
        Guid tenantId = Guid.NewGuid();
        Guid existingId = await SeedRule(tenantId, "Old Rule", 30m, null, true);

        await RunCreateHandler(tenantId, new("New Rule", 50m, null, true));

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        DepositRule? old = await verify.DepositRules.FindAsync(existingId);
        old!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task CreateDepositRule_TenantIsolation_OtherTenantRuleNotAffected()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid tenantBRuleId = await SeedRule(tenantB, "B Active Rule", 40m, null, true);

        await RunCreateHandler(tenantA, new("A New Active Rule", 50m, null, true));

        await using AppDbContext verify = fixture.CreateDbContext(tenantB);
        DepositRule? bRule = await verify.DepositRules.FindAsync(tenantBRuleId);
        bRule!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateDepositRule_WithCancellationPolicy_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        CreateDepositRuleRequest req = new("Standard Deposit", 50m, null, false, 48, 50);

        DepositRuleResponse result = await RunCreateHandler(tenantId, req);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        DepositRule? rule = await verify.DepositRules.FindAsync(result.Id);
        rule!.CancellationWindowHours.Should().Be(48);
        rule.RefundPercentOnLateCancel.Should().Be(50);
    }

    // ── UpdateDepositRule ────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateDepositRule_ValidRequest_PersistsChanges()
    {
        Guid tenantId = Guid.NewGuid();
        Guid ruleId = await SeedRule(tenantId, "Original", 30m, null, false);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        UpdateDepositRuleHandler handler = new(db);
        await handler.Handle(new UpdateDepositRuleCommand(ruleId, new("Updated", null, 25m, true)), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        DepositRule? rule = await verify.DepositRules.FindAsync(ruleId);
        rule!.Name.Should().Be("Updated");
        rule.AmountFixed.Should().BeNull();
        rule.AmountPercent.Should().Be(25m);
        rule.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateDepositRule_NonExistentId_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        UpdateDepositRuleHandler handler = new(db);

        Func<Task> act = () => handler.Handle(
            new UpdateDepositRuleCommand(Guid.NewGuid(), new("X", 10m, null, false)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── DeleteDepositRule ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteDepositRule_ValidId_SetsDeletedAt()
    {
        Guid tenantId = Guid.NewGuid();
        Guid ruleId = await SeedRule(tenantId, "Rule to delete", 50m, null, false);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        DeleteDepositRuleHandler handler = new(db);
        await handler.Handle(new DeleteDepositRuleCommand(ruleId), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        DepositRule? rule = await verify.DepositRules.IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == ruleId);
        rule!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteDepositRule_NonExistentId_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        DeleteDepositRuleHandler handler = new(db);

        Func<Task> act = () => handler.Handle(new DeleteDepositRuleCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── GetDepositRules ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDepositRules_DeletedRules_NotReturned()
    {
        Guid tenantId = Guid.NewGuid();
        Guid ruleId = await SeedRule(tenantId, "To Delete", 30m, null, false);

        await using AppDbContext deleteCtx = fixture.CreateDbContext(tenantId);
        DepositRule? rule = await deleteCtx.DepositRules.FindAsync(ruleId);
        rule!.DeletedAt = DateTime.UtcNow;
        await deleteCtx.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        GetDepositRulesHandler handler = new(db);
        List<DepositRuleResponse> result = await handler.Handle(new GetDepositRulesQuery(), default);

        result.Should().NotContain(r => r.Id == ruleId);
    }

    [Fact]
    public async Task GetDepositRules_ReturnsTenantScopedRulesOnly()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await SeedRule(tenantA, "A Rule", 50m, null, false);
        await SeedRule(tenantB, "B Rule 1", 30m, null, false);
        await SeedRule(tenantB, "B Rule 2", null, 10m, true);

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        GetDepositRulesHandler handler = new(db);
        List<DepositRuleResponse> result = await handler.Handle(new GetDepositRulesQuery(), default);

        result.Should().ContainSingle(r => r.Name == "A Rule");
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task<Guid> SeedRule(
        Guid tenantId, string name, decimal? fixed_, decimal? percent, bool isActive)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        DepositRule rule = new()
        {
            StudioId = tenantId,
            Name = name,
            AmountFixed = fixed_,
            AmountPercent = percent,
            IsActive = isActive
        };
        ctx.DepositRules.Add(rule);
        await ctx.SaveChangesAsync();
        return rule.Id;
    }

    private async Task<DepositRuleResponse> RunCreateHandler(Guid tenantId, CreateDepositRuleRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        CreateDepositRuleHandler handler = new(db, tenant);
        return await handler.Handle(new CreateDepositRuleCommand(req), default);
    }
}
