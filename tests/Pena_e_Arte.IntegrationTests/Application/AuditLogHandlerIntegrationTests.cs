using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
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
        GetAuditLogHandler handler = new(db, BuildIdentity().Identity);
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
        AuditLogPageResponse result = await new GetAuditLogHandler(db, BuildIdentity().Identity).Handle(
            new GetAuditLogQuery(From: new DateTime(2026, 11, 14), To: new DateTime(2026, 11, 14), StudioId: studioId),
            default);

        result.TotalCount.Should().Be(2);
        result.Items.Should().OnlyContain(i => i.StudioName == "Audit Day Studio");
        result.Items.Select(i => i.CreatedAt.Day).Should().OnlyContain(d => d == 14);
    }

    [Fact]
    public async Task GetAuditLog_NamesStaffFromTheirAccount_FlagsADeletedOne_AndNeverNamesAClient()
    {
        // Phi, 19 Nov: the actor's current name, read from Identity when the page is read, on real MySQL.
        (IdentityService identity, UserManager<IdentityUser> users) = BuildIdentity();
        Guid owner = await CreateAccount(users, "Maria");
        Guid client = await CreateAccount(users, "Clienta");
        Guid deleted = Guid.NewGuid();
        Guid studioId = Guid.NewGuid();
        await SeedActor(studioId, owner, "owner");
        await SeedActor(studioId, client, "client");
        await SeedActor(studioId, deleted, "artist");

        await using AppDbContext db = fixture.CreateDbContext(studioId);
        AuditLogPageResponse result = await new GetAuditLogHandler(db, identity).Handle(
            new GetAuditLogQuery(StudioId: studioId), default);

        result.Items.Single(i => i.ActorUserId == owner).ActorName.Should().Be("Maria");
        result.Items.Single(i => i.ActorUserId == client).ActorName.Should().BeNull();
        result.Items.Single(i => i.ActorUserId == deleted).ActorAccountDeleted.Should().BeTrue();

        // Current, not stored: a later change to the account's given name shows on the next read.
        IdentityUser ownerUser = (await users.FindByIdAsync(owner.ToString()))!;
        Claim oldName = (await users.GetClaimsAsync(ownerUser)).Single(c => c.Type == JwtRegisteredClaimNames.GivenName);
        await users.ReplaceClaimAsync(ownerUser, oldName, new Claim(JwtRegisteredClaimNames.GivenName, "Maria Renamed"));
        AuditLogPageResponse again = await new GetAuditLogHandler(db, identity).Handle(new GetAuditLogQuery(StudioId: studioId), default);
        again.Items.Single(i => i.ActorUserId == owner).ActorName.Should().Be("Maria Renamed");
    }

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "test-secret-key-must-be-at-least-32-chars!",
            ["Jwt:Admin"] = "test-admin",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:AccessTokenExpiryMinutes"] = "15",
        })
        .Build();

    private (IdentityService Identity, UserManager<IdentityUser> Users) BuildIdentity()
    {
        ServiceCollection services = new();
        services.AddDbContext<AppDbContext>(opts => opts.UseMySql(fixture.ConnectionString, DatabaseServerVersion.MySql));
        services.AddScoped<Microsoft.AspNetCore.Http.IHttpContextAccessor, Microsoft.AspNetCore.Http.HttpContextAccessor>();
        services.AddScoped<ICurrentTenant, CurrentTenantService>();
        services.AddIdentityCore<IdentityUser>().AddEntityFrameworkStores<AppDbContext>();
        UserManager<IdentityUser> users = services.BuildServiceProvider().GetRequiredService<UserManager<IdentityUser>>();
        return (new IdentityService(users, Config), users);
    }

    private static async Task<Guid> CreateAccount(UserManager<IdentityUser> users, string givenName)
    {
        IdentityUser user = new() { UserName = $"{Guid.NewGuid():N}@test.com", Email = $"{Guid.NewGuid():N}@test.com" };
        (await users.CreateAsync(user)).Succeeded.Should().BeTrue();
        await users.AddClaimAsync(user, new Claim(JwtRegisteredClaimNames.GivenName, givenName));
        return Guid.Parse(user.Id);
    }

    private async Task SeedActor(Guid studioId, Guid actorUserId, string role)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.AuditLogEntries.Add(AuditLogEntry.Create(actorUserId, role, "Studio.Suspended", "Studio", studioId, studioId, "{}"));
        await ctx.SaveChangesAsync();
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
