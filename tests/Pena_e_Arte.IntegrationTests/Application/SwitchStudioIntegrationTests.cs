using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

// Full round trip through the real Identity/UserManager stack and a real MySQL
// database — proves the multi-studio claim/token redesign works end to end, not
// just against the in-memory fakes used by SwitchStudioHandlerTests.
[Collection("Database")]
public class SwitchStudioIntegrationTests(DatabaseFixture fixture)
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"]                = "test-secret-key-must-be-at-least-32-chars!",
            ["Jwt:Issuer"]                   = "test-issuer",
            ["Jwt:Audience"]                 = "test-audience",
            ["Jwt:AccessTokenExpiryMinutes"] = "15",
        })
        .Build();

    private async Task<(UserManager<IdentityUser> UserManager, IdentityService Identity)> BuildIdentityAsync()
    {
        ServiceCollection services = new();
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))));
        services.AddScoped<Microsoft.AspNetCore.Http.IHttpContextAccessor,
                           Microsoft.AspNetCore.Http.HttpContextAccessor>();
        services.AddScoped<ICurrentTenant, CurrentTenantService>();

        services.AddIdentityCore<IdentityUser>(options => options.Password.RequireNonAlphanumeric = false)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        ServiceProvider sp = services.BuildServiceProvider();
        RoleManager<IdentityRole> roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("client"))
            await roleManager.CreateAsync(new IdentityRole("client"));

        UserManager<IdentityUser> um = sp.GetRequiredService<UserManager<IdentityUser>>();
        return (um, new IdentityService(um, Config));
    }

    [Fact]
    public async Task SwitchStudio_ToNewStudio_CreatesClientRowAndReissuesTokenForTargetStudio()
    {
        Guid   homeStudioId   = Guid.NewGuid();
        Guid   targetStudioId = Guid.NewGuid();
        string email          = UniqueEmail();

        (UserManager<IdentityUser> _, IdentityService identity) = await BuildIdentityAsync();

        await using AppDbContext seedDb = fixture.CreateDbContext(Guid.Empty);
        seedDb.Studios.Add(new Studio { Id = homeStudioId,   Name = "Home",   Slug = $"home-{Guid.NewGuid():N}" });
        seedDb.Studios.Add(new Studio { Id = targetStudioId, Name = "Target", Slug = $"target-{Guid.NewGuid():N}" });
        await seedDb.SaveChangesAsync();

        (bool created, Guid userId, _) =
            await identity.CreateUserAsync(email, "Password1!", "client", homeStudioId, "Ana");
        created.Should().BeTrue();

        await using AppDbContext handlerDb = fixture.CreateDbContext(homeStudioId);
        handlerDb.Clients.Add(new Client
        {
            StudioId = homeStudioId, UserId = userId, FirstName = "Ana", LastName = "Rossi", Email = email,
        });
        await handlerDb.SaveChangesAsync();

        StubCurrentUser currentUser = new(userId, "client", email);
        SwitchStudioHandler handler = new(
            handlerDb, identity, currentUser, NullLogger<SwitchStudioHandler>.Instance);

        SwitchStudioResponse response = await handler.Handle(
            new SwitchStudioCommand(new SwitchStudioRequest(targetStudioId)), default);

        response.IsNewMembership.Should().BeTrue();

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        jwt.Claims.Where(c => c.Type == "tenant_id").Should().ContainSingle()
            .Which.Value.Should().Be(targetStudioId.ToString());

        await using AppDbContext verifyDb = fixture.CreateDbContext(targetStudioId);
        (await verifyDb.Clients.CountAsync(c => c.UserId == userId && c.StudioId == targetStudioId))
            .Should().Be(1);
    }

    [Fact]
    public async Task SwitchStudio_BackToOriginalStudio_IsIdempotentAndRefreshPreservesIt()
    {
        Guid   studioA = Guid.NewGuid();
        Guid   studioB = Guid.NewGuid();
        string email   = UniqueEmail();

        (UserManager<IdentityUser> _, IdentityService identity) = await BuildIdentityAsync();

        await using AppDbContext seedDb = fixture.CreateDbContext(Guid.Empty);
        seedDb.Studios.Add(new Studio { Id = studioA, Name = "A", Slug = $"a-{Guid.NewGuid():N}" });
        seedDb.Studios.Add(new Studio { Id = studioB, Name = "B", Slug = $"b-{Guid.NewGuid():N}" });
        await seedDb.SaveChangesAsync();

        (_, Guid userId, _) = await identity.CreateUserAsync(email, "Password1!", "client", studioA, "Ana");

        await using AppDbContext handlerDb = fixture.CreateDbContext(studioA);
        handlerDb.Clients.Add(new Client
        {
            StudioId = studioA, UserId = userId, FirstName = "Ana", LastName = "Rossi", Email = email,
        });
        await handlerDb.SaveChangesAsync();

        StubCurrentUser currentUser = new(userId, "client", email);
        SwitchStudioHandler handler = new(
            handlerDb, identity, currentUser, NullLogger<SwitchStudioHandler>.Instance);

        // Switch to B (new membership), then back to A (already a member — no duplicate row).
        await handler.Handle(new SwitchStudioCommand(new SwitchStudioRequest(studioB)), default);
        SwitchStudioResponse backToA = await handler.Handle(
            new SwitchStudioCommand(new SwitchStudioRequest(studioA)), default);

        backToA.IsNewMembership.Should().BeFalse();

        await using AppDbContext verifyDb = fixture.CreateDbContext(studioA);
        (await verifyDb.Clients.CountAsync(c => c.UserId == userId && c.StudioId == studioA)).Should().Be(1);

        // Refreshing after the switch back must still resolve to A, not B or the
        // account's original claim order.
        (bool refreshSuccess, string? newAccessToken, _, _) =
            await identity.RefreshTokenAsync(backToA.RefreshToken);

        refreshSuccess.Should().BeTrue();
        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(newAccessToken);
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == studioA.ToString());
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@test.com";

    private sealed record StubCurrentUser(Guid UserId, string Role, string? Email = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
