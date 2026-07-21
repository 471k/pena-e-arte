using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class AuditLogHandlerIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task GetAuditLog_IssuerRead_SeesEntriesAcrossTenants()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await Seed(tenantA, "Studio.Suspended", "Studio", tenantA);
        await Seed(tenantB, "Studio.Suspended", "Studio", tenantB);

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        GetAuditLogHandler handler = new(db);
        AuditLogPageResponse result = await handler.Handle(new GetAuditLogQuery(), default);

        result.Items.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Items.Select(i => i.StudioId).Should().Contain([tenantA, tenantB]);
    }

    [Fact]
    public async Task GetMyStudioAuditLog_OwnerRead_OnlySeesOwnStudio()
    {
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();
        await Seed(tenantA, "Studio.Suspended", "Studio", tenantA);
        await Seed(tenantB, "Studio.Suspended", "Studio", tenantB);

        await using AppDbContext db = fixture.CreateDbContext(tenantA);
        CurrentTenantService tenant = new();
        tenant.SetTenant(tenantA);
        GetMyStudioAuditLogHandler handler = new(db, tenant);
        AuditLogPageResponse result = await handler.Handle(new GetMyStudioAuditLogQuery(), default);

        result.Items.Should().OnlyContain(i => i.StudioId == tenantA);
    }

    private async Task Seed(Guid contextTenantId, string action, string targetType, Guid? studioId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(contextTenantId);
        ctx.AuditLogEntries.Add(AuditLogEntry.Create(
            Guid.NewGuid(), "owner", action, targetType, Guid.NewGuid(), studioId, "{}"));
        await ctx.SaveChangesAsync();
    }
}
