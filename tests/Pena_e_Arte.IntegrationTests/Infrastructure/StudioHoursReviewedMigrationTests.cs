using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

/// <summary>
/// E05a (O02): the AddStudioHoursReviewedAt backfill against real MySQL. A studio whose hours are only
/// registration's rows stays unreviewed, including a row whose UpdatedAt is a microsecond after its
/// CreatedAt (what registration's two separate DateTime.UtcNow reads actually produce); a studio whose
/// owner saved the form, editing a day or adding one, is marked reviewed.
/// </summary>
public sealed class StudioHoursReviewedMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261105001756_AddNotificationLogAppointmentId";
    private const string TargetMigration = "20261107175451_AddStudioHoursReviewedAt";

    // Short prefix: Pomelo's migrations lock name must fit MySQL's 64-character limit.
    private readonly string _databaseName = $"hr_mig_{Guid.NewGuid():N}";

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
    public async Task OnlyStudiosWhoseOwnerSavedTheHours_AreMarkedReviewed()
    {
        DateTime registered = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
        Guid untouched = Guid.NewGuid();
        Guid edited = Guid.NewGuid();
        Guid dayAdded = Guid.NewGuid();
        Guid noHours = Guid.NewGuid();

        await using (AppDbContext seed = CreateContext())
        {
            await seed.Database.OpenConnectionAsync();
            await seed.Database.ExecuteSqlRawAsync("SET FOREIGN_KEY_CHECKS = 0;");
            await seed.Database.ExecuteSqlRawAsync("SET SESSION sql_mode = '';");

            foreach (Guid id in new[] { untouched, edited, dayAdded, noHours })
            {
                await seed.Database.ExecuteSqlRawAsync(
                    "INSERT INTO studios (Id, Name, Slug, City, OwnerEmail, TrialExpiresAt, CreatedAt) VALUES ({0}, 'S', {1}, 'Tirana', 'o@s.test', {2}, {2});",
                    id.ToString(), id.ToString("N")[..12], registered);
            }

            // Registration's rows: UpdatedAt a microsecond after CreatedAt on one of them, as measured.
            await Hours(seed, untouched, 1, registered, registered);
            await Hours(seed, untouched, 2, registered, registered.AddTicks(10));

            await Hours(seed, edited, 1, registered, registered.AddDays(3));
            await Hours(seed, edited, 2, registered, registered);

            await Hours(seed, dayAdded, 1, registered, registered);
            await Hours(seed, dayAdded, 6, registered.AddDays(5), registered.AddDays(5));
        }

        await using (AppDbContext migrate = CreateContext())
        {
            await migrate.GetService<IMigrator>().MigrateAsync(TargetMigration);
        }

        await using AppDbContext assert = CreateContext();
        (await ReviewedAt(assert, untouched)).Should().BeNull("registration's rows are not the owner reviewing anything");
        (await ReviewedAt(assert, noHours)).Should().BeNull();
        (await ReviewedAt(assert, edited)).Should().Be(registered.AddDays(3));
        (await ReviewedAt(assert, dayAdded)).Should().Be(registered.AddDays(5));
    }

    private static Task<int> Hours(AppDbContext db, Guid studioId, int day, DateTime createdAt, DateTime updatedAt) =>
        db.Database.ExecuteSqlRawAsync(
            "INSERT INTO StudioHours (Id, StudioId, DayOfWeek, StartTime, EndTime, IsOpen, CreatedAt, UpdatedAt) VALUES ({0}, {1}, {2}, '09:00:00', '18:00:00', 1, {3}, {4});",
            Guid.NewGuid().ToString(), studioId.ToString(), day, createdAt, updatedAt);

    private static async Task<DateTime?> ReviewedAt(AppDbContext db, Guid studioId) =>
        (await db.Database
            .SqlQueryRaw<DateTime?>("SELECT HoursReviewedAt AS Value FROM studios WHERE Id = {0}", studioId.ToString())
            .ToListAsync())
        .Single();
}
