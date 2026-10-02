using System.Net;
using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Public;

public class GetVisitorGeoHandlerTests
{
    private readonly IGeoIpService _geoIp = Substitute.For<IGeoIpService>();

    private GetVisitorGeoHandler CreateSut() => new(_geoIp);

    private static GeoIpResult Result(string? countryCode, string? timeZone = null) =>
        new(countryCode, null, null, null, null, null, null, null, null, null, null, timeZone, null, null);

    [Fact]
    public async Task Handle_ResolvableIp_ReturnsUpperCasedCountryAndTimeZone()
    {
        IPAddress ip = IPAddress.Parse("203.0.113.9");
        _geoIp.Lookup(ip).Returns(Result("al", "Europe/Tirane"));

        VisitorGeoResponse response = await CreateSut().Handle(new GetVisitorGeoQuery(ip), default);

        response.CountryCode.Should().Be("AL");
        response.TimeZone.Should().Be("Europe/Tirane");
    }

    [Fact]
    public async Task Handle_NoIp_ReturnsNullsWithoutLookingUp()
    {
        VisitorGeoResponse response = await CreateSut().Handle(new GetVisitorGeoQuery(null), default);

        response.CountryCode.Should().BeNull();
        response.TimeZone.Should().BeNull();
        _geoIp.DidNotReceive().Lookup(Arg.Any<IPAddress>());
    }

    [Fact]
    public async Task Handle_UnknownRange_ReturnsNulls()
    {
        _geoIp.Lookup(Arg.Any<IPAddress>()).Returns((GeoIpResult?)null);

        VisitorGeoResponse response = await CreateSut()
            .Handle(new GetVisitorGeoQuery(IPAddress.Parse("10.0.0.1")), default);

        response.CountryCode.Should().BeNull();
        response.TimeZone.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ALB")]
    public async Task Handle_MalformedCountryCode_ReturnsNullCountryButKeepsTimeZone(string? code)
    {
        _geoIp.Lookup(Arg.Any<IPAddress>()).Returns(Result(code, "Europe/Tirane"));

        VisitorGeoResponse response = await CreateSut()
            .Handle(new GetVisitorGeoQuery(IPAddress.Parse("203.0.113.9")), default);

        response.CountryCode.Should().BeNull();
        response.TimeZone.Should().Be("Europe/Tirane");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_BlankTimeZone_ReturnsNullTimeZone(string? timeZone)
    {
        _geoIp.Lookup(Arg.Any<IPAddress>()).Returns(Result("AL", timeZone));

        VisitorGeoResponse response = await CreateSut()
            .Handle(new GetVisitorGeoQuery(IPAddress.Parse("203.0.113.9")), default);

        response.TimeZone.Should().BeNull();
    }
}
