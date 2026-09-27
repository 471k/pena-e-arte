using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MySqlConnector;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

/// <summary>
/// Exercises the real AddStudioCurrency migration (docs/claude/overnight-prompt-studio-currency-
/// 2026-09-27.md §8) against a throwaway MySQL database: seed pre-migration-shaped rows, migrate
/// forward, assert the backfill — then prove the backfill is a no-op the second time.
///
/// FOREIGN_KEY_CHECKS and sql_mode are relaxed for the legacy-row inserts only, so each test
/// supplies just the columns the backfill actually reads/writes instead of a full valid tenant
/// graph (studio -> artist -> service -> appointment -> ...). This suite is about the migration's
/// column-level NOT NULL/default/backfill behaviour, not FK business rules — those already have
/// coverage elsewhere (e.g. SchemaConstraintTests, TenantQueryFilterTests).
/// </summary>
public sealed class StudioCurrencyMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260924164218_AddSubscriptionRevenueLedger";
    private const string TargetMigration = "20260927185937_AddStudioCurrency";

    // Short prefix deliberately: Pomelo's migrations lock name is "__{db}_EFMigrationsLock",
    // which MySQL user-level locks cap at 64 characters — "pena_arte_test_" (DatabaseFixture's
    // prefix) plus a 32-char GUID already overflows that once the lock-name wrapper is added.
    private readonly string _databaseName = $"sc_mig_{Guid.NewGuid():N}";

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
        IMigrator migrator = ctx.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
        await ctx.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = '';");
    }

    public async Task DisposeAsync()
    {
        await using AppDbContext ctx = CreateContext();
        await ctx.Database.EnsureDeletedAsync();
    }

    private async Task<T> ScalarAsync<T>(AppDbContext ctx, string sql, params object[] parameters)
        => (await ctx.Database.SqlQueryRaw<T>(sql, parameters).ToListAsync()).Single();

    [Fact]
    public async Task MigratingFromThePreCurrencySchema_BackfillsStudiosPaymentsAndPackagePurchases()
    {
        Guid studioId = Guid.NewGuid();
        Guid packageId = Guid.NewGuid();
        Guid purchaseId = Guid.NewGuid();
        Guid cashPaymentId = Guid.NewGuid();
        Guid pokPaymentId = Guid.NewGuid();

        await using (AppDbContext seed = CreateContext())
        {
            await seed.Database.OpenConnectionAsync();
            await seed.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            await seed.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = '';");

            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO studios (Id, Name, Slug, City, OwnerEmail, TrialExpiresAt, CreatedAt) " +
                "VALUES ({0}, 'Legacy Studio', 'legacy-studio', 'Tirana', 'owner@legacy.test', UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                studioId.ToString());

            // Cash payment — pre-migration schema defaults this to 'ALL'; must become 'EUR'.
            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO payments (Id, StudioId, AppointmentId, ClientId, Amount, Status, Method, Provider, Currency, CreatedAt, UpdatedAt) " +
                "VALUES ({0}, {1}, {2}, {3}, 50, 'Paid', 'Cash', '', 'ALL', UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                cashPaymentId.ToString(), studioId.ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

            // Real POK card payment — a genuine ALL charge; must keep 'ALL', not be relabelled.
            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO payments (Id, StudioId, AppointmentId, ClientId, Amount, Status, Method, Provider, ProviderReferenceId, Currency, CreatedAt, UpdatedAt) " +
                "VALUES ({0}, {1}, {2}, {3}, 5000, 'Paid', 'Card', 'pok', 'pok-ref-x', 'ALL', UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                pokPaymentId.ToString(), studioId.ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO packages (Id, StudioId, Name, Price, SessionCount, CreatedAt, UpdatedAt) " +
                "VALUES ({0}, {1}, 'Five Session Pack', 500, 5, UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                packageId.ToString(), studioId.ToString());

            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO package_purchases (Id, StudioId, PackageId, ClientId, SessionsRemaining, ProviderReferenceId, Provider, CreatedAt, UpdatedAt) " +
                "VALUES ({0}, {1}, {2}, {3}, 0, '', '', UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                purchaseId.ToString(), studioId.ToString(), packageId.ToString(), Guid.NewGuid().ToString());
        }

        await using (AppDbContext migrate = CreateContext())
        {
            IMigrator migrator = migrate.GetService<IMigrator>();
            await migrator.MigrateAsync(TargetMigration);
        }

        await using AppDbContext assert = CreateContext();

        (await ScalarAsync<string>(assert, "SELECT CountryCode FROM studios WHERE Id = {0}", studioId.ToString()))
            .Should().Be("AL");
        (await ScalarAsync<string>(assert, "SELECT Currency FROM studios WHERE Id = {0}", studioId.ToString()))
            .Should().Be("EUR");
        (await ScalarAsync<string>(assert, "SELECT Currency FROM payments WHERE Id = {0}", cashPaymentId.ToString()))
            .Should().Be("EUR");
        (await ScalarAsync<string>(assert, "SELECT Currency FROM payments WHERE Id = {0}", pokPaymentId.ToString()))
            .Should().Be("ALL");
        (await ScalarAsync<decimal>(assert, "SELECT Amount FROM package_purchases WHERE Id = {0}", purchaseId.ToString()))
            .Should().Be(500m);
    }

    [Fact]
    public async Task RunningTheBackfillSqlASecondTime_ChangesZeroRows()
    {
        Guid studioId = Guid.NewGuid();

        await using (AppDbContext seed = CreateContext())
        {
            await seed.Database.OpenConnectionAsync();
            await seed.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            await seed.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = '';");

            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO studios (Id, Name, Slug, City, OwnerEmail, TrialExpiresAt, CreatedAt) " +
                "VALUES ({0}, 'Legacy Studio', 'legacy-studio', 'Tirana', 'owner@legacy.test', UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                studioId.ToString());

            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO payments (Id, StudioId, AppointmentId, ClientId, Amount, Status, Method, Provider, Currency, CreatedAt, UpdatedAt) " +
                "VALUES ({0}, {1}, {2}, {3}, 50, 'Paid', 'Cash', '', 'ALL', UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                Guid.NewGuid().ToString(), studioId.ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        }

        await using (AppDbContext migrate = CreateContext())
        {
            IMigrator migrator = migrate.GetService<IMigrator>();
            await migrator.MigrateAsync(TargetMigration);
        }

        // Same statements the migration's Up() runs. Every WHERE clause is guarded on "still
        // unmigrated" (IS NULL / still labelled ALL and not a real POK charge), so re-running
        // against already-migrated data must match zero rows.
        await using AppDbContext rerun = CreateContext();

        int studiosChanged = await rerun.Database.ExecuteSqlRawAsync(
            "UPDATE studios SET CountryCode = 'AL' WHERE CountryCode IS NULL;");
        int currencyChanged = await rerun.Database.ExecuteSqlRawAsync(
            "UPDATE studios SET Currency = 'EUR' WHERE Currency IS NULL;");
        int paymentsChanged = await rerun.Database.ExecuteSqlRawAsync(
            """
            UPDATE payments
               SET Currency = 'EUR'
             WHERE Currency = 'ALL'
               AND NOT (Provider = 'pok' AND ProviderReferenceId IS NOT NULL AND Status IN ('Captured', 'Paid', 'Refunded'));
            """);

        studiosChanged.Should().Be(0);
        currencyChanged.Should().Be(0);
        paymentsChanged.Should().Be(0);
    }

    [Fact]
    public async Task AfterMigration_InsertingAPaymentWithoutCurrency_Fails()
    {
        await using (AppDbContext migrate = CreateContext())
        {
            IMigrator migrator = migrate.GetService<IMigrator>();
            await migrator.MigrateAsync(TargetMigration);
        }

        await using AppDbContext ctx = CreateContext();
        await ctx.Database.OpenConnectionAsync();
        // FK checks off only — sql_mode is deliberately left at MySQL's default (strict) here,
        // unlike the other tests: relaxing it would make MySQL silently fill the omitted
        // Currency with '' instead of rejecting the row, defeating the point of this test.
        await ctx.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");

        // Currency is deliberately omitted — the DEFAULT 'ALL' left by migration
        // 20260731194633 must be gone, so this must fail (NOT NULL, no default), not silently
        // insert lek.
        Func<Task> insertWithoutCurrency = async () => await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO payments (Id, StudioId, AppointmentId, ClientId, Amount, Status, Method, Provider, CreatedAt, UpdatedAt) " +
            "VALUES ({0}, {1}, {2}, {3}, 50, 'Paid', 'Cash', '', UTC_TIMESTAMP(), UTC_TIMESTAMP());",
            Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString());

        await insertWithoutCurrency.Should().ThrowAsync<MySqlException>();
    }

    [Fact]
    public async Task AfterMigration_PackagePurchasesAmountEqualsItsPackagesPrice()
    {
        Guid studioId = Guid.NewGuid();
        Guid packageId = Guid.NewGuid();
        Guid purchaseId = Guid.NewGuid();

        await using (AppDbContext seed = CreateContext())
        {
            await seed.Database.OpenConnectionAsync();
            await seed.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            await seed.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = '';");

            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO studios (Id, Name, Slug, City, OwnerEmail, TrialExpiresAt, CreatedAt) " +
                "VALUES ({0}, 'Legacy Studio', 'legacy-studio', 'Tirana', 'owner@legacy.test', UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                studioId.ToString());

            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO packages (Id, StudioId, Name, Price, SessionCount, CreatedAt, UpdatedAt) " +
                "VALUES ({0}, {1}, 'Ten Session Pack', 899.99, 10, UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                packageId.ToString(), studioId.ToString());

            await seed.Database.ExecuteSqlRawAsync(
                "INSERT INTO package_purchases (Id, StudioId, PackageId, ClientId, SessionsRemaining, ProviderReferenceId, Provider, CreatedAt, UpdatedAt) " +
                "VALUES ({0}, {1}, {2}, {3}, 0, '', '', UTC_TIMESTAMP(), UTC_TIMESTAMP());",
                purchaseId.ToString(), studioId.ToString(), packageId.ToString(), Guid.NewGuid().ToString());
        }

        await using (AppDbContext migrate = CreateContext())
        {
            IMigrator migrator = migrate.GetService<IMigrator>();
            await migrator.MigrateAsync(TargetMigration);
        }

        await using AppDbContext assert = CreateContext();
        (await ScalarAsync<decimal>(assert, "SELECT Amount FROM package_purchases WHERE Id = {0}", purchaseId.ToString()))
            .Should().Be(899.99m);
    }
}
