using Pena_e_Arte.Application.Persistence;

namespace Pena_e_Arte.UnitTests.Helpers;

/// <summary>
/// Hands out fresh FakeDbContext instances pointed at the same named in-memory database, so
/// handlers that take IAppDbContextFactory (to run several queries concurrently) see the same
/// seeded data as a test's own FakeDbContext.Create(databaseName) instance.
/// </summary>
public class FakeDbContextFactory(string databaseName) : IAppDbContextFactory
{
    public Task<IAppDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
        Task.FromResult<IAppDbContext>(FakeDbContext.Create(databaseName));
}
