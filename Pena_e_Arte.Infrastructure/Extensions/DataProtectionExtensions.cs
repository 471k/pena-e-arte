using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Extensions;

public static class DataProtectionExtensions
{
    // Shared by every environment and replica: the application name scopes the key ring, so it must
    // never change, or every outstanding link stops working.
    public const string ApplicationName = "TattooOS";

    // Data Protection signs Identity's password-reset, set-password (guest booking, artist invite)
    // and email-confirmation tokens. Its keys must be shared by every API replica and survive
    // restarts. Without this, each pod kept its own in-memory key ring: a link only worked if it
    // reached the pod that issued it, and every deploy invalidated all outstanding links (seen in
    // production on 27 Oct 2026). Keys live in the DataProtectionKeys table; a pod that meets a key
    // it doesn't know reloads the ring from the table, so concurrent first starts are safe.
    public static IServiceCollection AddSharedDataProtection(this IServiceCollection services)
    {
        services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToDbContext<AppDbContext>();

        return services;
    }

    // Called once at startup: refuses to run if something replaced the shared key repository
    // (a later AddDataProtection().PersistKeysTo… call, or AddSharedDataProtection dropped from
    // AddInfrastructure), because per-pod keys fail silently: links just stop working.
    public static void EnsureSharedKeyRepository(IServiceProvider services)
    {
        IXmlRepository? repository = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;
        if (repository is not EntityFrameworkCoreXmlRepository<AppDbContext>)
            throw new InvalidOperationException(
                "Data Protection keys are not stored in the shared database (DataProtectionKeys table). " +
                "Every API replica needs the same key ring, or password-reset and email-confirmation " +
                "links only work on the pod that issued them. Register AddSharedDataProtection().");
    }
}
