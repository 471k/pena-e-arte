using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.API.Extensions;

namespace Pena_e_Arte.UnitTests.Auth;

public class AuthenticationExtensionsTests
{
    [Fact]
    public void AddApiAuthentication_MissingSecretKey_Throws()
    {
        IConfiguration configuration = BuildConfiguration(secretKey: null);
        ServiceCollection services = [];

        Action act = () => services.AddApiAuthentication(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Jwt:SecretKey*");
    }

    [Fact]
    public void AddApiAuthentication_EmptySecretKey_Throws()
    {
        IConfiguration configuration = BuildConfiguration(secretKey: "");
        ServiceCollection services = [];

        Action act = () => services.AddApiAuthentication(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddApiAuthentication_SecretKeyUnder32Bytes_Throws()
    {
        IConfiguration configuration = BuildConfiguration(secretKey: "too-short-31-chars-exactly!!!!!");
        ServiceCollection services = [];

        Action act = () => services.AddApiAuthentication(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddApiAuthentication_SecretKeyAtLeast32Bytes_DoesNotThrow()
    {
        IConfiguration configuration = BuildConfiguration(secretKey: "valid-secret-key-of-32-bytes-okk");
        ServiceCollection services = [];

        Action act = () => services.AddApiAuthentication(configuration);

        act.Should().NotThrow();
    }

    private static IConfiguration BuildConfiguration(string? secretKey)
    {
        Dictionary<string, string?> values = new()
        {
            ["Jwt:Issuer"] = "test-issuer",
            ["Jwt:Audience"] = "test-audience",
        };
        if (secretKey is not null) values["Jwt:SecretKey"] = secretKey;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
