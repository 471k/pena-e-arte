using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Infrastructure.Services;
using Resend;

namespace Pena_e_Arte.UnitTests.Infrastructure.Services;

public class SmsRecipientPolicyTests
{
    private const string Allowed = "+355692441454";

    private static IConfiguration Config(string? restrict, string? allowed)
    {
        Dictionary<string, string?> values = new();
        if (restrict is not null) values[SmsRecipientPolicy.RestrictKey] = restrict;
        if (allowed is not null) values[SmsRecipientPolicy.AllowedKey] = allowed;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void NotConfigured_AllowsEveryone_SoProductionIsUnchanged()
    {
        SmsRecipientPolicy.IsAllowed(Config(null, null), "+355690000000").Should().BeTrue();
    }

    [Fact]
    public void RestrictionOff_AllowsEveryone_EvenWithAListPresent()
    {
        SmsRecipientPolicy.IsAllowed(Config("false", Allowed), "+355690000000").Should().BeTrue();
    }

    [Fact]
    public void Restricted_AllowsAListedNumber()
    {
        SmsRecipientPolicy.IsAllowed(Config("true", Allowed), Allowed).Should().BeTrue();
    }

    [Fact]
    public void Restricted_BlocksAnUnlistedNumber()
    {
        SmsRecipientPolicy.IsAllowed(Config("true", Allowed), "+355692345678").Should().BeFalse();
    }

    [Fact]
    public void Restricted_WithAnEmptyList_BlocksEverything_FailClosed()
    {
        SmsRecipientPolicy.IsAllowed(Config("true", ""), Allowed).Should().BeFalse();
    }

    [Fact]
    public void Restricted_WithAMissingList_BlocksEverything_FailClosed()
    {
        SmsRecipientPolicy.IsAllowed(Config("true", null), Allowed).Should().BeFalse();
    }

    [Theory]
    [InlineData("+355 69 244 1454")]
    [InlineData("+355-692-441-454")]
    [InlineData(" +355692441454 ")]
    public void Restricted_IgnoresSpacingAndPunctuationInTheRecipient(string recipient)
    {
        SmsRecipientPolicy.IsAllowed(Config("true", Allowed), recipient).Should().BeTrue();
    }

    [Theory]
    [InlineData("+355692441454,+355691111111")]
    [InlineData("+355691111111; +355692441454")]
    [InlineData("+355 691 111 111 , +355 69 244 1454")]
    public void Restricted_AcceptsSeveralEntriesSeparatedByCommaOrSemicolon(string list)
    {
        SmsRecipientPolicy.IsAllowed(Config("true", list), Allowed).Should().BeTrue();
    }

    [Fact]
    public void Restricted_DoesNotMatchAPrefixOrSuffixOfAListedNumber()
    {
        SmsRecipientPolicy.IsAllowed(Config("true", Allowed), "+35569244145").Should().BeFalse();
        SmsRecipientPolicy.IsAllowed(Config("true", Allowed), "+3556924414540").Should().BeFalse();
    }

    [Fact]
    public void Restricted_BlocksAnEmptyRecipient()
    {
        SmsRecipientPolicy.IsAllowed(Config("true", Allowed), "").Should().BeFalse();
    }

    [Fact]
    public async Task NotificationService_RefusesABlockedRecipient_BeforeAnyProviderCall()
    {
        NotificationService sut = new(
            Config("true", Allowed), Substitute.For<IResend>(), NullLogger<NotificationService>.Instance);

        Func<Task> act = () => sut.SendSmsAsync("+355692345678", "hello");

        await act.Should().ThrowAsync<SmsRecipientNotAllowedException>();
    }

    [Fact]
    public void BlockedException_DoesNotCarryThePhoneNumber()
    {
        new SmsRecipientNotAllowedException().Message.Should().NotContain("+355");
    }
}
