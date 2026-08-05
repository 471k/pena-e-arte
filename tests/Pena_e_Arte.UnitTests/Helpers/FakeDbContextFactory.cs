using Pena_e_Arte.Application.Persistence;

namespace Pena_e_Arte.UnitTests.Helpers;

/// <summary>
/// Hands out fresh FakeDbContext instances pointed at the same named in-memory database, so
/// handlers that take IAppDbContextFactory (to run several queries concurrently) see the same
/// seeded data as a test's own FakeDbContext.Create(databaseName) instance.
/// </summary>
public class FakeDbContextFactory(string databaseName) : IAppDbContextFactory
{
    public Task<IAppDbContextLease> CreateDbContextAsync(CancellationToken ct = default)
    {
        FakeDbContext db = FakeDbContext.Create(databaseName);
        return Task.FromResult<IAppDbContextLease>(new FakeAppDbContextLease(db));
    }

    private sealed class FakeAppDbContextLease(FakeDbContext db) : IAppDbContextLease
    {
        public IAppDbContext Context => db;

        public ValueTask DisposeAsync() => db.DisposeAsync();
    }
}
