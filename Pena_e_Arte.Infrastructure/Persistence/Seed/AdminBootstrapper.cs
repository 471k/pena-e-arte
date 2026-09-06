using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the platform's first "admin" (cross-tenant platform-admin) account on startup,
/// exactly once. Unlike DataSeeder, this is not gated by Seeding:Enabled — it must run in
/// staging and production too, since there is otherwise no way to create the initial admin
/// account there (the public /auth/register endpoint deliberately excludes the admin role;
/// see architecture.md's privilege-escalation note).
///
/// Self-guarding instead: a no-op unless both Bootstrap:AdminEmail and Bootstrap:AdminPassword
/// are configured (sourced from Vault via a K8s Secret, never committed), AND no user currently
/// holds the admin role. Once the first admin account exists, every later boot short-circuits
/// on the role check — this never overwrites an existing admin's password or creates a second
/// admin, so the bootstrap secret can safely stay configured indefinitely.
/// </summary>
public static class AdminBootstrapper
{
    private const string AdminRole = "admin";

    public static async Task RunAsync(IServiceProvider services, IConfiguration configuration)
    {
        string? email = configuration["Bootstrap:AdminEmail"];
        string? password = configuration["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        using IServiceScope scope = services.CreateScope();
        UserManager<IdentityUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        IList<IdentityUser> existingAdmins = await userManager.GetUsersInRoleAsync(AdminRole);
        if (existingAdmins.Count > 0)
            return;

        IdentityUser user = new() { UserName = email, Email = email, EmailConfirmed = true };
        IdentityResult result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Admin bootstrap failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(user, AdminRole);

        Guid userId = Guid.Parse(user.Id);
        IAppDbContext db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        db.AuditLogEntries.Add(AuditLogEntry.Create(
            actorUserId: userId,
            actorRole: "system",
            action: AuditActions.AdminAccountBootstrapped,
            targetType: AuditTargetTypes.User,
            targetId: userId,
            studioId: null,
            metadata: "{}"));
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
