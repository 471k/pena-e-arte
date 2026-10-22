using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

/// <summary>
/// EF Core only discovers a migration through its [Migration] and [DbContext] attributes (normally in
/// the generated .Designer.cs). Without them the migration silently never runs and
/// GetPendingMigrations() reports nothing pending — exactly what happened to
/// 20260629000001_DropArtistUniqueConstraints, hand-written without a Designer file, until 2026-10-22.
/// </summary>
public class MigrationDiscoveryTests
{
    private static readonly Type[] MigrationTypes = typeof(AppDbContext).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(Migration)) && !t.IsAbstract)
        .ToArray();

    [Fact]
    public void EveryMigration_HasMigrationAndDbContextAttributes()
    {
        MigrationTypes.Should().NotBeEmpty();

        string[] missing = MigrationTypes
            .Where(t => t.GetCustomAttribute<MigrationAttribute>() is null
                     || t.GetCustomAttribute<DbContextAttribute>()?.ContextType != typeof(AppDbContext))
            .Select(t => t.Name)
            .ToArray();

        missing.Should().BeEmpty(
            "EF Core never applies a migration without [Migration] and [DbContext(typeof(AppDbContext))] — " +
            "generate migrations with `dotnet ef migrations add`, or add both attributes to a hand-written one");
    }

    [Fact]
    public void EveryMigrationInTheAssembly_IsOneEfCoreSees()
    {
        // The design-time factory builds the context without opening a connection; listing
        // migrations reads the assembly only.
        using AppDbContext db = new AppDbContextFactory().CreateDbContext([]);

        IEnumerable<string> discovered = db.Database.GetMigrations();

        discovered.Should().HaveCount(MigrationTypes.Length,
            "every Migration class in the assembly must be one EF Core discovers and would apply");
        discovered.Should().Contain("20260629000001_DropArtistUniqueConstraints");
    }
}
