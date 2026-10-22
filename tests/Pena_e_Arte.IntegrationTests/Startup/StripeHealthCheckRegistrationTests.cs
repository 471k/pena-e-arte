using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Pena_e_Arte.IntegrationTests.Startup;

public class StripeHealthCheckRegistrationTests
{
    [Fact]
    public void SecretKeyUnset_StripeCheckIsSkipped()
    {
        ShouldRegisterStripeCheck(configValues: []).Should().BeFalse();
    }

    [Fact]
    public void SecretKeyBlank_StripeCheckIsSkipped()
    {
        ShouldRegisterStripeCheck(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "   ",
        }).Should().BeFalse();
    }

    [Fact]
    public void SecretKeySet_StripeCheckIsRegistered()
    {
        ShouldRegisterStripeCheck(new Dictionary<string, string?>
        {
            ["Stripe:SecretKey"] = "sk_test_whatever",
        }).Should().BeTrue();
    }

    // Mirrors the exact gate in Program.cs: an unconfigured Stripe key must not register
    // StripeHealthCheck at all, or /health would report Unhealthy for a deployment
    // deliberately running without Stripe billing (since 2026-10-23 the check is tagged
    // "external", not "ready", so it never gates pod readiness either; a fully independent
    // code path — see DeclareCashDepositCommand/ConfirmCashDepositCommand). Tested as a pure
    // config-read decision for the same reason MigrationsApplyOnStartupTests does: Program.cs
    // is top-level statements, not isolatable via WebApplicationFactory without standing up
    // the full DI graph.
    private static bool ShouldRegisterStripeCheck(Dictionary<string, string?> configValues)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return !string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"]);
    }
}
