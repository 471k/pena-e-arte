namespace Pena_e_Arte.Application.Persistence;

/// <summary>
/// Produces independent IAppDbContext instances for callers that need to run several queries
/// concurrently — a single DbContext can't serve overlapping operations, so code doing that (see
/// GetTrafficBreakdownQuery) gets one context per concurrent query from here instead of sharing
/// the ambient per-request scoped context.
/// </summary>
public interface IAppDbContextFactory
{
    Task<IAppDbContextLease> CreateDbContextAsync(CancellationToken ct = default);
}

/// <summary>
/// Owns the DI scope backing a leased IAppDbContext — disposing the lease disposes that scope
/// (and, with it, the context and every scoped dependency resolved into it, e.g. ICurrentTenant).
/// </summary>
public interface IAppDbContextLease : IAsyncDisposable
{
    IAppDbContext Context { get; }
}
