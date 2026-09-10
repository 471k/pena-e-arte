using FluentAssertions;
using Pena_e_Arte.API.Middleware;

namespace Pena_e_Arte.UnitTests.Middleware;

public class ImpersonationAllowListTests
{
    [Theory]
    [InlineData("/api/v1/appointments")]
    [InlineData("/api/v1/appointments/")]
    [InlineData("/api/v1/appointments/check-slot")]
    [InlineData("/api/v1/artists")]
    [InlineData("/api/v1/clients")]
    [InlineData("/api/v1/studios/me")]
    [InlineData("/api/v1/deposit-rules")]
    [InlineData("/api/v1/reminders")]
    [InlineData("/api/v1/notifications")]
    public void IsAllowed_AllowListedGetRoute_ReturnsTrue(string path)
    {
        ImpersonationAllowList.IsAllowed("GET", path).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/appointments")]
    [InlineData("/api/v1/artists")]
    [InlineData("/api/v1/clients")]
    [InlineData("/api/v1/deposit-rules")]
    public void IsAllowed_GuidSegmentRoute_ReturnsTrue(string basePath)
    {
        string path = $"{basePath}/{Guid.NewGuid()}";
        ImpersonationAllowList.IsAllowed("GET", path).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_ArtistScheduleGuidRoute_ReturnsTrue()
    {
        string path = $"/api/v1/artists/{Guid.NewGuid()}/schedule";
        ImpersonationAllowList.IsAllowed("GET", path).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_StudioClosuresGuidRoute_ReturnsTrue()
    {
        string path = $"/api/v1/studios/{Guid.NewGuid()}/closures";
        ImpersonationAllowList.IsAllowed("GET", path).Should().BeTrue();
    }

    [Theory]
    [InlineData("GET", "/api/v1/payments")]
    [InlineData("GET", "/api/v1/billing")]
    [InlineData("GET", "/api/v1/clients/me/profile")]
    [InlineData("GET", "/api/v1/artists/me")]
    [InlineData("GET", "/api/v1/studios/me/audit-log")]
    [InlineData("GET", "/api/v1/platform/stats")]
    [InlineData("GET", "/api/v1/designs")]
    [InlineData("GET", "/api/v1/forms/intake")]
    public void IsAllowed_NotOnAllowList_ReturnsFalseByDefault(string method, string path)
    {
        ImpersonationAllowList.IsAllowed(method, path).Should().BeFalse();
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void IsAllowed_NonGetMethodOnAnAllowListedPath_ReturnsFalse(string method)
    {
        ImpersonationAllowList.IsAllowed(method, "/api/v1/appointments").Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_ClientProfileSubroute_ReturnsFalse()
    {
        // "basic identity fields only" — /clients/{id} is allowed, /clients/{id}/profile
        // (medical/PII-adjacent) must not match, since the regex is anchored end-to-end.
        string path = $"/api/v1/clients/{Guid.NewGuid()}/profile";
        ImpersonationAllowList.IsAllowed("GET", path).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_ClientTattoosSubroute_ReturnsFalse()
    {
        string path = $"/api/v1/clients/{Guid.NewGuid()}/tattoos";
        ImpersonationAllowList.IsAllowed("GET", path).Should().BeFalse();
    }

    [Fact]
    public void IsAllowed_PortableProfileSubroute_ReturnsFalse()
    {
        string path = $"/api/v1/clients/{Guid.NewGuid()}/portable-profile";
        ImpersonationAllowList.IsAllowed("GET", path).Should().BeFalse();
    }
}
