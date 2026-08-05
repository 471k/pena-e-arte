using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.Application.Persistence;

namespace Pena_e_Arte.Infrastructure.Persistence;

/// <summary>
/// Not to be confused with AppDbContextFactory, which implements
/// IDesignTimeDbContextFactory&lt;AppDbContext&gt; for the `dotnet ef` CLI only.
///
/// Opens a genuine new DI scope per call and resolves IAppDbContext from it — EF Core's own
/// IDbContextFactory&lt;AppDbContext&gt; can't be used here since it resolves against the root
/// provider and can't inject AppDbContext's scoped constructor dependencies (ICurrentTenant, the
/// cache-invalidation interceptor).
/// </summary>
public class AppDbContextRuntimeFactory(IServiceScopeFactory scopeFactory) : IAppDbContextFactory
{
    public Task<IAppDbContextLease> CreateDbContextAsync(CancellationToken ct = default)
    {
        AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IAppDbContext context = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return Task.FromResult<IAppDbContextLease>(new AppDbContextLease(scope, context));
    }

    private sealed class AppDbContextLease(AsyncServiceScope scope, IAppDbContext context) : IAppDbContextLease
    {
        public IAppDbContext Context => context;

        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
