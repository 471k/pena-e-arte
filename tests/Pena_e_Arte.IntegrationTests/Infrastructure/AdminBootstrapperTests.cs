using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Persistence.Seed;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

[Collection("Database")]
public class AdminBootstrapperTests(DatabaseFixture fixture)
{
    private async Task<ServiceProvider> BuildProviderAsync()
    {
        ServiceCollection services = new();

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0))));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<Microsoft.AspNetCore.Http.IHttpContextAccessor,
                           Microsoft.AspNetCore.Http.HttpContextAccessor>();
        services.AddScoped<Pena_e_Arte.Domain.Interfaces.ICurrentTenant, CurrentTenantService>();
        services.AddDataProtection();

        services.AddIdentityCore<IdentityUser>(options =>
            options.Password.RequireNonAlphanumeric = false)
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

        return sp;
    }

    private static IConfiguration ConfigWith(string? email, string? password) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Bootstrap:AdminEmail"] = email,
                ["Bootstrap:AdminPassword"] = password,
            })
            .Build();

    // A single deterministic sequence, not three independent [Fact]s: the "Database" collection
    // shares one MySQL database across every test file in it (see DatabaseFixture), so
    // GetUsersInRoleAsync("admin") is global state — both across this class's own tests and,
    // in principle, any other test file sharing the same collection. Clearing admins first and
    // running the three scenarios in one controlled sequence avoids depending on ambient state
    // or execution order for correctness.
    [Fact]
    public async Task RunAsync_NoOpsWithoutConfig_ThenBootstraps_ThenNeverBootstrapsAgain()
    {
        ServiceProvider sp = await BuildProviderAsync();
        UserManager<IdentityUser> userManager = sp.GetRequiredService<UserManager<IdentityUser>>();

        foreach (IdentityUser existingAdmin in await userManager.GetUsersInRoleAsync("admin"))
            await userManager.DeleteAsync(existingAdmin);

        // 1. Missing config: no-op.
        await AdminBootstrapper.RunAsync(sp, ConfigWith(email: null, password: null));
        (await userManager.GetUsersInRoleAsync("admin")).Should().BeEmpty();

        // 2. Configured, no existing admin: creates the account + an audit log entry.
        string firstEmail = $"bootstrap-{Guid.NewGuid():N}@test.local";
        await AdminBootstrapper.RunAsync(sp, ConfigWith(firstEmail, "Bootstrap!Password1"));

        IList<IdentityUser> admins = await userManager.GetUsersInRoleAsync("admin");
        admins.Should().ContainSingle(u => u.Email == firstEmail);

        AppDbContext db = sp.GetRequiredService<AppDbContext>();
        (await db.AuditLogEntries.AnyAsync(a =>
            a.Action == AuditActions.AdminAccountBootstrapped &&
            a.TargetId == Guid.Parse(admins[0].Id))).Should().BeTrue();

        // 3. Configured again with a different email: an admin already exists, so this is a
        // no-op — never overwrites, never creates a second admin.
        string secondEmail = $"bootstrap-{Guid.NewGuid():N}@test.local";
        await AdminBootstrapper.RunAsync(sp, ConfigWith(secondEmail, "Bootstrap!Password2"));

        (await userManager.GetUsersInRoleAsync("admin")).Should().ContainSingle(u => u.Email == firstEmail);
    }
}
