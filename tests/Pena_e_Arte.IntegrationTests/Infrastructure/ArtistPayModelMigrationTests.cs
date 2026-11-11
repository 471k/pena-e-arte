using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

/// <summary>
/// A07a: AddArtistPayModel against real MySQL. Additive: an existing studio becomes Commission with no
/// rate and no review, an existing artist's CommissionRate is untouched (and still a percent), and the
/// migration reverses cleanly.
/// </summary>
public sealed class ArtistPayModelMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261107175451_AddStudioHoursReviewedAt";
    private const string TargetMigration = "20261111001614_AddArtistPayModel";

    // Short prefix: Pomelo's migrations lock name must fit MySQL's 64-character limit.
    private readonly string _databaseName = $"pm_mig_{Guid.NewGuid():N}";

    private string ConnectionString =>
        $"Server=127.0.0.1;Port=3306;Database={_databaseName};User=root;Password=root;AllowPublicKeyRetrieval=true;SslMode=None;";

    private AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .Options;
        return new AppDbContext(options, new CurrentTenantService());
    }

    public async Task InitializeAsync()
    {
        await using AppDbContext ctx = CreateContext();
        await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
    }

    public async Task DisposeAsync()
    {
        await using AppDbContext ctx = CreateContext();
        await ctx.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task ExistingStudiosAndArtists_GetUnreviewedCommissionWithNoRate_AndTheMigrationReverses()
    {
        DateTime registered = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        Guid studioId = Guid.NewGuid();
        Guid artistId = Guid.NewGuid();

        await using (AppDbContext seed = CreateContext())
        {
            await seed.Database.OpenConnectionAsync();
            await seed.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            await seed.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = '';");
            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO studios (Id, Name, Slug, City, OwnerEmail, TrialExpiresAt, CreatedAt, HoursReviewedAt) VALUES ({0}, 'S', {1}, 'Tirana', 'o@s.test', {2}, {2}, {2});",
                studioId.ToString(), studioId.ToString("N")[..12], registered);
            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO artists (Id, StudioId, FirstName, LastName, Email, Specializations, CommissionRate, CreatedAt) VALUES ({0}, {1}, 'A', 'B', 'a@s.test', '[]', 60.00, {2});",
                artistId.ToString(), studioId.ToString(), registered);
        }

        await using (AppDbContext migrate = CreateContext())
        {
            await migrate.GetService<IMigrator>().MigrateAsync(TargetMigration);
        }

        await using (AppDbContext assert = CreateContext())
        {
            (await Scalar<string>(assert, "SELECT DefaultPayArrangement AS Value FROM studios WHERE Id = {0}", studioId)).Should().Be("Commission");
            (await Scalar<decimal?>(assert, "SELECT DefaultCommissionRate AS Value FROM studios WHERE Id = {0}", studioId)).Should().BeNull("no rate is seeded");
            (await Scalar<DateTime?>(assert, "SELECT PayModelReviewedAt AS Value FROM studios WHERE Id = {0}", studioId))
                .Should().BeNull("nobody has confirmed a pay model, even in a studio whose artist has a rate");
            (await Scalar<string?>(assert, "SELECT PayArrangement AS Value FROM artists WHERE Id = {0}", artistId)).Should().BeNull();
            (await Scalar<decimal?>(assert, "SELECT CommissionRate AS Value FROM artists WHERE Id = {0}", artistId)).Should().Be(60.00m);
        }

        await using (AppDbContext down = CreateContext())
        {
            await down.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        }

        await using AppDbContext after = CreateContext();
        (await Scalar<decimal?>(after, "SELECT CommissionRate AS Value FROM artists WHERE Id = {0}", artistId)).Should().Be(60.00m);
        (await Scalar<long>(after,
            "SELECT COUNT(*) AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND COLUMN_NAME IN ('DefaultPayArrangement', 'DefaultCommissionRate', 'DefaultFlatFeePerSession', 'PayModelReviewedAt', 'PayArrangement', 'FlatFeePerSession')"))
            .Should().Be(0);
    }

    private static async Task<T> Scalar<T>(AppDbContext db, string sql, Guid id) =>
        (await db.Database.SqlQueryRaw<T>(sql, id.ToString()).ToListAsync()).Single();

    private static async Task<T> Scalar<T>(AppDbContext db, string sql) =>
        (await db.Database.SqlQueryRaw<T>(sql).ToListAsync()).Single();
}
