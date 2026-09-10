using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.PromoCodes.Commands;
using Pena_e_Arte.Application.PromoCodes.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class PromoCodeHandlerIntegrationTests(DatabaseFixture fixture)
{
    // ── CreatePromoCode ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePromoCode_FixedAmount_PersistsToDatabase()
    {
        Guid tenantId = Guid.NewGuid();
        CreatePromoCodeRequest req = new("SAVE20", 20m, null, true);

        PromoCodeResponse result = await RunCreateHandler(tenantId, req);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        bool exists = await verify.PromoCodes.AnyAsync(p => p.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task CreatePromoCode_TenantIsolation_ScopedToOwningTenant()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        Guid codeAId = await SeedCode(tenantA, "A20", 20m, null, true);

        await using AppDbContext verify = fixture.CreateDbContext(tenantB);
        PromoCode? found = await verify.PromoCodes.FindAsync(codeAId);
        found.Should().BeNull();
    }

    // ── UpdatePromoCode ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdatePromoCode_ValidRequest_PersistsChanges()
    {
        Guid tenantId = Guid.NewGuid();
        Guid codeId = await SeedCode(tenantId, "ORIGINAL", 20m, null, false);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        UpdatePromoCodeHandler handler = new(db);
        await handler.Handle(new UpdatePromoCodeCommand(codeId, new("UPDATED", null, 15m, true)), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        PromoCode? code = await verify.PromoCodes.FindAsync(codeId);
        code!.Code.Should().Be("UPDATED");
        code.AmountFixed.Should().BeNull();
        code.AmountPercent.Should().Be(15m);
        code.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task UpdatePromoCode_NonExistentId_ThrowsNotFoundException()
    {
        Guid tenantId = Guid.NewGuid();
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        UpdatePromoCodeHandler handler = new(db);

        Func<Task> act = () => handler.Handle(
            new UpdatePromoCodeCommand(Guid.NewGuid(), new("X", 10m, null, false)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── DeletePromoCode ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeletePromoCode_ValidId_SetsDeletedAt()
    {
        Guid tenantId = Guid.NewGuid();
        Guid codeId = await SeedCode(tenantId, "DELETEME", 20m, null, false);

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        DeletePromoCodeHandler handler = new(db);
        await handler.Handle(new DeletePromoCodeCommand(codeId), default);

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        PromoCode? code = await verify.PromoCodes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == codeId);
        code!.DeletedAt.Should().NotBeNull();
    }

    // ── GetPromoCodes ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPromoCodes_ReturnsTenantScopedCodesOnly()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await SeedCode(tenantA, "A CODE", 20m, null, false);
        await SeedCode(tenantB, "B CODE", 10m, null, true);

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        GetPromoCodesHandler handler = new(db);
        List<PromoCodeResponse> result = await handler.Handle(new GetPromoCodesQuery(), default);

        result.Should().ContainSingle(p => p.Code == "A CODE");
    }

    [Fact]
    public async Task GetPromoCodes_DeletedCodes_NotReturned()
    {
        Guid tenantId = Guid.NewGuid();
        Guid codeId = await SeedCode(tenantId, "TO DELETE", 20m, null, false);

        await using AppDbContext deleteCtx = fixture.CreateDbContext(tenantId);
        PromoCode? code = await deleteCtx.PromoCodes.FindAsync(codeId);
        code!.DeletedAt = DateTime.UtcNow;
        await deleteCtx.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        GetPromoCodesHandler handler = new(db);
        List<PromoCodeResponse> result = await handler.Handle(new GetPromoCodesQuery(), default);

        result.Should().NotContain(p => p.Id == codeId);
    }

    // ── Seed helpers ─────────────────────────────────────────────────────────────

    private async Task<Guid> SeedCode(
        Guid tenantId, string code, decimal? fixed_, decimal? percent, bool isActive)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        PromoCode promoCode = new()
        {
            StudioId = tenantId,
            Code = code,
            AmountFixed = fixed_,
            AmountPercent = percent,
            IsActive = isActive
        };
        ctx.PromoCodes.Add(promoCode);
        await ctx.SaveChangesAsync();
        return promoCode.Id;
    }

    private async Task<PromoCodeResponse> RunCreateHandler(Guid tenantId, CreatePromoCodeRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(tenantId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantId);
        CreatePromoCodeHandler handler = new(db, tenant);
        return await handler.Handle(new CreatePromoCodeCommand(req), default);
    }
}
