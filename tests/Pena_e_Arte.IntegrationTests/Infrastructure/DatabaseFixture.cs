using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Testcontainers.MySql;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MySqlContainer _container = new MySqlBuilder()
        .WithImage("mysql:8.4")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using AppDbContext ctx = CreateDbContext(Guid.Empty);
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public AppDbContext CreateDbContext(Guid tenantId)
    {
        CurrentTenantService tenant = new();
        if (tenantId != Guid.Empty) tenant.SetTenant(tenantId);

        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, new MySqlServerVersion(new Version(8, 4, 0)))
            .Options;

        return new AppDbContext(options, tenant);
    }
}
