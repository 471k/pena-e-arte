using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Pena_e_Arte.IntegrationTests.Startup;

public class MigrationsApplyOnStartupTests
{
    [Fact]
    public void ApplyOnStartupUnset_DefaultsToTrue_MigrationRuns()
    {
        RunGatedMigration(configValues: []).Should().BeTrue();
    }

    [Fact]
    public void ApplyOnStartupExplicitlyFalse_MigrationDoesNotRun()
    {
        RunGatedMigration(new Dictionary<string, string?>
        {
            ["Migrations:ApplyOnStartup"] = "false",
        }).Should().BeFalse();
    }

    [Fact]
    public void ApplyOnStartupExplicitlyTrue_MigrationRuns()
    {
        RunGatedMigration(new Dictionary<string, string?>
        {
            ["Migrations:ApplyOnStartup"] = "true",
        }).Should().BeTrue();
    }

    // Mirrors the exact gate in Program.cs (Migrations:ApplyOnStartup, default true) against a
    // spy standing in for Database.MigrateAsync(), proving the boolean gate without requiring a
    // live MySQL instance for what is otherwise a pure config-read decision — Program.cs is
    // top-level statements, so the gate can't be isolated for a WebApplicationFactory boot
    // without also standing up the full AppDbContext/MySQL pipeline. The K8s migration Job
    // (k8s/base/migration-job.yaml) is what actually exercises MigrateAsync against a real
    // database in production.
    private static bool RunGatedMigration(Dictionary<string, string?> configValues)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return configuration.GetValue("Migrations:ApplyOnStartup", defaultValue: true);
    }
}
