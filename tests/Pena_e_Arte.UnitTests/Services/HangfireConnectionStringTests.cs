using FluentAssertions;
using MySqlConnector;
using Pena_e_Arte.Infrastructure.Extensions;
using Xunit;

namespace Pena_e_Arte.UnitTests.Services;

public class HangfireConnectionStringTests
{
    private const string AppConnectionString = "Server=db.example;Port=25060;Database=app;User=app;Password=pw;SslMode=Required";

    [Fact]
    public void BuildHangfireConnectionString_TurnsXaOff_SoADroppedClientCannotLeaveLocksBehind()
    {
        MySqlConnectionStringBuilder built = new(InfrastructureServiceExtensions.BuildHangfireConnectionString(AppConnectionString));

        built.UseXaTransactions.Should().BeFalse();
    }

    [Fact]
    public void BuildHangfireConnectionString_AllowsUserVariables_ForTheDashboard()
    {
        MySqlConnectionStringBuilder built = new(InfrastructureServiceExtensions.BuildHangfireConnectionString(AppConnectionString));

        built.AllowUserVariables.Should().BeTrue();
    }

    [Fact]
    public void BuildHangfireConnectionString_KeepsTheAppsServerAndCredentials()
    {
        MySqlConnectionStringBuilder built = new(InfrastructureServiceExtensions.BuildHangfireConnectionString(AppConnectionString));

        built.Server.Should().Be("db.example");
        built.Port.Should().Be(25060u);
        built.Database.Should().Be("app");
        built.UserID.Should().Be("app");
        built.Password.Should().Be("pw");
        built.SslMode.Should().Be(MySqlSslMode.Required);
    }
}
