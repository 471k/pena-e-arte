using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

// End-to-end account-erasure behaviour against a real IdentityService/UserManager (same MySQL DB as
// the fixture): erasure disables login immediately, and the retention hard-purge physically removes
// the ClientProfile health data, anonymizes the Client PII, and deletes the Identity login.
[Collection("Database")]
public class AccountErasureIntegrationTests(DatabaseFixture fixture)
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "test-secret-key-must-be-at-least-32-chars!",
            ["Jwt:Admin"] = "test-admin",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:AccessTokenExpiryMinutes"] = "15",
        })
        .Build();

    [Fact]
    public async Task Erasure_DisablesLoginImmediately()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService identity = new(um, Config);
        Guid tenantId = Guid.NewGuid();

        string email = UniqueEmail();
        (bool created, Guid userId, _) = await identity.CreateUserAsync(email, "Password1!", "client", tenantId);
        created.Should().BeTrue();

        (Guid clientId, _, _) = await SeedClientAsync(tenantId, userId, email);

        // Login works before erasure.
        (bool okBefore, _, _) = await identity.LoginAsync(email, "Password1!");
        okBefore.Should().BeTrue();

        // Erase (client self-service path).
        await using (AppDbContext db = fixture.CreateDbContext(tenantId))
        {
            RequestMyDataErasureHandler handler = new(db, new StubCurrentUser(userId, "client"), identity);
            await handler.Handle(new RequestMyDataErasureCommand(), default);
        }

        // Login is now blocked.
        (bool okAfter, _, string? err) = await identity.LoginAsync(email, "Password1!");
        okAfter.Should().BeFalse(because: "the account was erased and its login disabled");
        err.Should().Be("Invalid credentials.");

        // The Client is marked for anonymization but its PII is not yet scrubbed (grace window).
        await using AppDbContext verify = fixture.CreateDbContext(tenantId);
        Client client = await verify.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.ErasureRequestedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task HardPurge_AfterGrace_RemovesProfile_AnonymizesClient_DeletesLogin()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService identity = new(um, Config);
        Guid tenantId = Guid.NewGuid();

        string email = UniqueEmail();
        (bool created, Guid userId, _) = await identity.CreateUserAsync(email, "Password1!", "client", tenantId);
        created.Should().BeTrue();

        (Guid clientId, Guid profileClientId, _) = await SeedClientAsync(tenantId, userId, email);

        // Erase, then simulate the grace window having elapsed.
        await using (AppDbContext db = fixture.CreateDbContext(tenantId))
        {
            RequestMyDataErasureHandler handler = new(db, new StubCurrentUser(userId, "client"), identity);
            await handler.Handle(new RequestMyDataErasureCommand(), default);
        }
        await using (AppDbContext backdate = fixture.CreateDbContext(tenantId))
        {
            Client c = await backdate.Clients.IgnoreQueryFilters().FirstAsync(x => x.Id == clientId);
            c.ErasureRequestedAt = DateTime.UtcNow.AddDays(-40);
            ClientProfile p = await backdate.ClientProfiles.IgnoreQueryFilters().FirstAsync(x => x.ClientId == clientId);
            p.DeletedAt = DateTime.UtcNow.AddDays(-40);
            await backdate.SaveChangesAsync();
        }

        // Run the retention job with a REAL IdentityService so it deletes the login.
        await using (AppDbContext db = fixture.CreateDbContext(tenantId))
        {
            RetentionOptions opts = new() { ConsentForms = 730, BodyMaps = 100_000, GracePeriodBeforeHardPurge = 30 };
            RetentionPurgeJob job = new(db, Substitute.For<IR2Service>(), identity, Options.Create(opts),
                NullLogger<RetentionPurgeJob>.Instance);
            await job.RunAsync();
        }

        await using AppDbContext verify = fixture.CreateDbContext(tenantId);

        // ClientProfile (health data) physically gone.
        bool profileExists = await verify.ClientProfiles.IgnoreQueryFilters().AnyAsync(p => p.ClientId == profileClientId);
        profileExists.Should().BeFalse(because: "health data must be physically purged after the grace window");

        // Client row remains (FK-referenced) but PII is anonymized and the login link is cleared.
        Client client = await verify.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.FirstName.Should().Be("Deleted");
        client.LastName.Should().Be("User");
        client.Email.Should().StartWith("deleted-");
        client.Phone.Should().BeNull();
        client.UserId.Should().BeNull();
        client.ErasureRequestedAt.Should().BeNull();

        // Identity login is deleted.
        IdentityUser? user = await um.FindByEmailAsync(email);
        user.Should().BeNull(because: "the Identity login is deleted at hard-purge");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<(Guid ClientId, Guid ProfileClientId, string Email)> SeedClientAsync(
        Guid tenantId, Guid userId, string email)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(tenantId);
        Client client = new()
        {
            StudioId = tenantId,
            UserId = userId,
            FirstName = "Ana",
            LastName = "Costa",
            Email = email,
            Phone = "+355691234567",
        };
        ctx.Clients.Add(client);
        ctx.ClientProfiles.Add(new ClientProfile
        {
            StudioId = tenantId,
            ClientId = client.Id,
            MedicalNotes = "sensitive",
            Allergies = "latex",
        });
        await ctx.SaveChangesAsync();
        return (client.Id, client.Id, email);
    }

    private async Task<UserManager<IdentityUser>> BuildUserManagerAsync()
    {
        ServiceCollection services = new();
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0))));
        services.AddScoped<Microsoft.AspNetCore.Http.IHttpContextAccessor,
                           Microsoft.AspNetCore.Http.HttpContextAccessor>();
        services.AddScoped<ICurrentTenant, CurrentTenantService>();
        services.AddDataProtection();
        services.AddIdentityCore<IdentityUser>(options => options.Password.RequireNonAlphanumeric = false)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        ServiceProvider sp = services.BuildServiceProvider();
        RoleManager<IdentityRole> roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (string role in new[] { "client", "artist", "owner", "admin" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
        return sp.GetRequiredService<UserManager<IdentityUser>>();
    }

    private static string UniqueEmail() => $"{Guid.NewGuid():N}@test.com";

    private sealed record StubCurrentUser(Guid UserId, string Role, string? Email = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }
}
