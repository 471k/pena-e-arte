using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;

namespace Pena_e_Arte.Infrastructure.Persistence;

/// <summary>
/// Runtime IAppDbContextFactory, backed by EF Core's own IDbContextFactory&lt;AppDbContext&gt;
/// (registered via AddDbContextFactory). Not to be confused with AppDbContextFactory, which
/// implements IDesignTimeDbContextFactory&lt;AppDbContext&gt; for the `dotnet ef` CLI only.
/// </summary>
public class AppDbContextRuntimeFactory(IDbContextFactory<AppDbContext> factory) : IAppDbContextFactory
{
    public async Task<IAppDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
        await factory.CreateDbContextAsync(ct);
}
