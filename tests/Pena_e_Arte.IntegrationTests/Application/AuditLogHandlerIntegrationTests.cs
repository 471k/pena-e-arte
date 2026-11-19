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
    public async Task GetAuditLog_AdminRead_SeesEntriesAcrossTenants()
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

    [Fact]
    public async Task GetAuditLog_StudioFilter_CoversTheWholeEndDay_AndNamesTheStudio()
    {
        // D01 on real MySQL: the correlated studio-name lookup and both day bounds must translate.
        Guid studioId = Guid.NewGuid();
        await using (AppDbContext ctx = fixture.CreateDbContext(studioId))
        {
            ctx.Studios.Add(new Studio { Id = studioId, Name = "Audit Day Studio", Slug = studioId.ToString("N")[..8], CountryCode = "AL", Currency = "EUR" });
            await ctx.SaveChangesAsync();
        }
        await SeedAt(studioId, new DateTime(2026, 11, 13, 23, 59, 59, DateTimeKind.Utc));
        await SeedAt(studioId, new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc));
        await SeedAt(studioId, new DateTime(2026, 11, 14, 23, 59, 59, DateTimeKind.Utc));
        await SeedAt(studioId, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        AuditLogPageResponse result = await new GetAuditLogHandler(db).Handle(
            new GetAuditLogQuery(From: new DateTime(2026, 11, 14), To: new DateTime(2026, 11, 14), StudioId: studioId),
            default);

        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(i => i.StudioName == "Audit Day Studio");
        result.Items.Select(i => i.CreatedAt.Day).Should().OnlyContain(d => d == 14);
    }

    private async Task SeedAt(Guid studioId, DateTime createdAtUtc)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        AuditLogEntry entry = AuditLogEntry.Create(
            Guid.NewGuid(), "admin", "Studio.Suspended", "Studio", studioId, studioId, "{}");
        ctx.AuditLogEntries.Add(entry);
        ctx.Entry(entry).Property(e => e.CreatedAt).CurrentValue = createdAtUtc;
        await ctx.SaveChangesAsync();
    }

    private async Task Seed(Guid contextTenantId, string action, string targetType, Guid? studioId)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(contextTenantId);
        ctx.AuditLogEntries.Add(AuditLogEntry.Create(
            Guid.NewGuid(), "owner", action, targetType, Guid.NewGuid(), studioId, "{}"));
        await ctx.SaveChangesAsync();
    }
}
