using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"pena_arte_test_{Guid.NewGuid():N}";

    public string ConnectionString =>
        $"Server=127.0.0.1;Port=3306;Database={_databaseName};User=root;Password=root;AllowPublicKeyRetrieval=true;SslMode=None;";

    public async Task InitializeAsync()
    {
        await using AppDbContext ctx = CreateDbContext(Guid.Empty);
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using AppDbContext ctx = CreateDbContext(Guid.Empty);
        await ctx.Database.EnsureDeletedAsync();
    }

    public AppDbContext CreateDbContext(Guid tenantId)
    {
        CurrentTenantService tenant = new();
        if (tenantId != Guid.Empty) tenant.SetTenant(tenantId);

        return CreateDbContext(tenant);
    }

    // For tests where a handler changes the tenant mid-request (as the anonymous guest-booking
    // path does) and the caller needs that same scoped instance, like DI gives one request.
    public AppDbContext CreateDbContext(CurrentTenantService tenant)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;

        return new AppDbContext(options, tenant);
    }
}
