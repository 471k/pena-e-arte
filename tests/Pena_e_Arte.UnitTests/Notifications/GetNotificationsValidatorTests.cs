using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Notifications;

public class GetNotificationsValidatorTests
{
    private readonly GetNotificationsValidator _sut = new();

    [Fact]
    public void Validate_NoFilters_IsValid()
    {
        _sut.ShouldBeValid(new GetNotificationsQuery(null, null, null, null));
    }

    [Theory]
    [InlineData("Email")]
    [InlineData("Sms")]
    public void Validate_AllowedChannel_IsValid(string channel)
    {
        _sut.ShouldBeValid(new GetNotificationsQuery(null, channel, null, null));
    }

    [Fact]
    public void Validate_UnknownChannel_FailsOnChannel()
    {
        _sut.ShouldFailOn(new GetNotificationsQuery(null, "Carrier Pigeon", null, null), "Channel");
    }

    [Fact]
    public void Validate_FromBeforeTo_IsValid()
    {
        DateTime now = DateTime.UtcNow;
        _sut.ShouldBeValid(new GetNotificationsQuery(null, null, now.AddDays(-1), now));
    }

    [Fact]
    public void Validate_FromEqualsTo_IsValid()
    {
        DateTime now = DateTime.UtcNow;
        _sut.ShouldBeValid(new GetNotificationsQuery(null, null, now, now));
    }

    [Fact]
    public void Validate_FromAfterTo_FailsOnFrom()
    {
        DateTime now = DateTime.UtcNow;
        _sut.ShouldFailOn(new GetNotificationsQuery(null, null, now, now.AddDays(-1)), "From");
    }

    [Fact]
    public void Validate_RecipientIdWithoutOtherFilters_IsValid()
    {
        _sut.ShouldBeValid(new GetNotificationsQuery(Guid.NewGuid(), null, null, null));
    }
}
