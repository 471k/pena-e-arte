using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Infrastructure.Services;

// Scope note: these cover the degrade-gracefully paths only (no configured/missing database).
// A "known-good lookup against a real small test .mmdb" case (as suggested in the source
// prompt's §12, using MaxMind's own published tiny test database) was NOT added — this session
// had no way to fetch and vet that fixture from MaxMind's test-data repo. Flagged rather than
// faked: IsPrivateRange's IPv4/IPv6 branching and the actual City() lookup path are therefore
// still unverified by an automated test.
public class GeoIpServiceTests
{
    private static IConfiguration ConfigWithPath(string? path)
    {
        var dict = new Dictionary<string, string?>();
        if (path is not null) dict["GeoIp:DatabasePath"] = path;
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void Lookup_NoDatabasePathConfigured_ReturnsNullGracefully()
    {
        GeoIpService sut = new(ConfigWithPath(null), Substitute.For<ILogger<GeoIpService>>());

        GeoIpResult? result = sut.Lookup(IPAddress.Parse("8.8.8.8"));

        result.Should().BeNull();
    }

    [Fact]
    public void Lookup_DatabasePathDoesNotExistOnDisk_ReturnsNullGracefully()
    {
        GeoIpService sut = new(
            ConfigWithPath(@"C:\this\path\does\not\exist.mmdb"),
            Substitute.For<ILogger<GeoIpService>>());

        GeoIpResult? result = sut.Lookup(IPAddress.Parse("8.8.8.8"));

        result.Should().BeNull();
    }

    [Fact]
    public void Lookup_NeverThrows_EvenWithNoDatabaseConfigured()
    {
        GeoIpService sut = new(ConfigWithPath(null), Substitute.For<ILogger<GeoIpService>>());

        Action act = () => sut.Lookup(IPAddress.Parse("203.0.113.5"));

        act.Should().NotThrow();
    }
}
