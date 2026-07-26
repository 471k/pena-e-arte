using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Pena_e_Arte.API.Extensions;

namespace Pena_e_Arte.UnitTests.Auth;

public class CorsExtensionsTests
{
    [Fact]
    public void AddApiCors_EmptyOriginsInProduction_Throws()
    {
        IConfiguration configuration = BuildConfiguration(origins: null);
        IHostEnvironment environment = FakeEnvironment("Production");
        ServiceCollection services = [];

        Action act = () => services.AddApiCors(configuration, environment);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Cors:AllowedOrigins*");
    }

    [Fact]
    public void AddApiCors_EmptyOriginsInDevelopment_DoesNotThrow()
    {
        IConfiguration configuration = BuildConfiguration(origins: null);
        IHostEnvironment environment = FakeEnvironment("Development");
        ServiceCollection services = [];

        Action act = () => services.AddApiCors(configuration, environment);

        act.Should().NotThrow();
    }

    [Fact]
    public void AddApiCors_ConfiguredOriginsInProduction_DoesNotThrow()
    {
        IConfiguration configuration = BuildConfiguration(origins: ["https://example.com"]);
        IHostEnvironment environment = FakeEnvironment("Production");
        ServiceCollection services = [];

        Action act = () => services.AddApiCors(configuration, environment);

        act.Should().NotThrow();
    }

    private static IHostEnvironment FakeEnvironment(string name)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    private static IConfiguration BuildConfiguration(string[]? origins)
    {
        Dictionary<string, string?> values = [];
        if (origins is not null)
        {
            for (int i = 0; i < origins.Length; i++)
                values[$"Cors:AllowedOrigins:{i}"] = origins[i];
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
