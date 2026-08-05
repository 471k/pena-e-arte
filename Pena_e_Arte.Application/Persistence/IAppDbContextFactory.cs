namespace Pena_e_Arte.Application.Persistence;

/// <summary>
/// Produces short-lived, independent IAppDbContext instances for callers that need to run
/// several queries concurrently — a single DbContext cannot serve overlapping operations, so
/// code doing that (see GetTrafficBreakdownQuery) gets one context per concurrent query from
/// here instead of sharing the ambient per-request scoped context.
/// </summary>
public interface IAppDbContextFactory
{
    Task<IAppDbContext> CreateDbContextAsync(CancellationToken ct = default);
}
