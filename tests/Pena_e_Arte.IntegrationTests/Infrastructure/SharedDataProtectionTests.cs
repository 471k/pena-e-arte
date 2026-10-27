using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.Infrastructure.Extensions;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

// Two service providers on one real MySQL database stand in for two API pods. Before the shared key
// ring, each pod had its own in-memory keys, so a password-reset link issued by one pod was rejected
// by the other ("This reset link is invalid or has expired", production 27 Oct 2026).
[Collection("Database")]
public class SharedDataProtectionTests(DatabaseFixture fixture)
{
    private ServiceProvider BuildPod()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0))));
        services.AddScoped<Microsoft.AspNetCore.Http.IHttpContextAccessor,
                           Microsoft.AspNetCore.Http.HttpContextAccessor>();
        services.AddScoped<Pena_e_Arte.Domain.Interfaces.ICurrentTenant, CurrentTenantService>();
        services.AddSharedDataProtection();
        services.AddIdentityCore<IdentityUser>(options => options.Password.RequireNonAlphanumeric = false)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task PasswordResetToken_IssuedByOnePod_IsAcceptedByAnother()
    {
        await using ServiceProvider podA = BuildPod();
        await using ServiceProvider podB = BuildPod();
        string email = $"dp-{Guid.NewGuid():N}@example.test";

        string token;
        using (IServiceScope scope = podA.CreateScope())
        {
            UserManager<IdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            IdentityUser user = new() { UserName = email, Email = email };
            (await users.CreateAsync(user, "OldPassword1")).Succeeded.Should().BeTrue();
            token = await users.GeneratePasswordResetTokenAsync(user);
        }

        using (IServiceScope scope = podB.CreateScope())
        {
            UserManager<IdentityUser> users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            IdentityUser user = (await users.FindByEmailAsync(email))!;

            IdentityResult result = await users.ResetPasswordAsync(user, token, "NewPassword1");

            result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Code)));
        }

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        (await db.DataProtectionKeys.CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public void EnsureSharedKeyRepository_WithSharedRegistration_DoesNotThrow()
    {
        using ServiceProvider pod = BuildPod();

        Action check = () => DataProtectionExtensions.EnsureSharedKeyRepository(pod);

        check.Should().NotThrow();
    }

    [Fact]
    public void EnsureSharedKeyRepository_WithDefaultPerProcessKeys_Throws()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddDataProtection();
        using ServiceProvider pod = services.BuildServiceProvider();

        Action check = () => DataProtectionExtensions.EnsureSharedKeyRepository(pod);

        check.Should().Throw<InvalidOperationException>().WithMessage("*DataProtectionKeys*");
    }
}
