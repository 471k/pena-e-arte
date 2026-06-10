using FluentAssertions;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.UnitTests.Clients;

public class ClientProfileOptInTests
{
    [Fact]
    public void OptInToCrossTenant_SetsAllowCrossTenantReadTrueAndRecordsDate()
    {
        ClientProfile profile = new() { StudioId = Guid.NewGuid(), ClientId = Guid.NewGuid() };
        DateTime before = DateTime.UtcNow;

        profile.OptInToCrossTenant();

        profile.AllowCrossTenantRead.Should().BeTrue();
        profile.CrossTenantOptInAt.Should().NotBeNull();
        profile.CrossTenantOptInAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void OptOutOfCrossTenant_ClearsAllowCrossTenantReadAndDate()
    {
        ClientProfile profile = new() { StudioId = Guid.NewGuid(), ClientId = Guid.NewGuid() };
        profile.OptInToCrossTenant();

        profile.OptOutOfCrossTenant();

        profile.AllowCrossTenantRead.Should().BeFalse();
        profile.CrossTenantOptInAt.Should().BeNull();
    }

    [Fact]
    public void NewClientProfile_AllowCrossTenantRead_DefaultsFalse()
    {
        ClientProfile profile = new() { StudioId = Guid.NewGuid(), ClientId = Guid.NewGuid() };

        profile.AllowCrossTenantRead.Should().BeFalse();
        profile.CrossTenantOptInAt.Should().BeNull();
    }
}
