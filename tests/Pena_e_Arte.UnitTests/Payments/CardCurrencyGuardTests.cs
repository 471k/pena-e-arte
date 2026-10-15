using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Payments;

/// <summary>
/// The server-side gate every new card payment goes through: the C-13 kill switch first, then the
/// studio's currency.
/// </summary>
public class CardCurrencyGuardTests
{
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();

    private void GivenCapabilities(bool cardPaymentsEnabled) =>
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true,
            SupportedCurrencies: ["ALL", "EUR"], Environment: "staging", CardPaymentsEnabled: cardPaymentsEnabled));

    [Fact]
    public void EnsureSupported_CardPaymentsSwitchedOff_RefusesEvenASupportedCurrency()
    {
        GivenCapabilities(cardPaymentsEnabled: false);

        Action act = () => CardCurrencyGuard.EnsureSupported(_provider, "ALL");

        act.Should().Throw<BusinessRuleViolationException>()
            .WithMessage(CardCurrencyGuard.CardPaymentsSwitchedOffMessage);
    }

    [Fact]
    public void EnsureCardPaymentsEnabled_ChecksOnlyTheSwitch()
    {
        GivenCapabilities(cardPaymentsEnabled: false);
        ((Action)(() => CardCurrencyGuard.EnsureCardPaymentsEnabled(_provider)))
            .Should().Throw<BusinessRuleViolationException>().WithMessage(CardCurrencyGuard.CardPaymentsSwitchedOffMessage);

        GivenCapabilities(cardPaymentsEnabled: true);
        ((Action)(() => CardCurrencyGuard.EnsureCardPaymentsEnabled(_provider))).Should().NotThrow();
    }

    [Fact]
    public void EnsureSupported_SwitchedOnAndSupportedCurrency_Passes()
    {
        GivenCapabilities(cardPaymentsEnabled: true);

        Action act = () => CardCurrencyGuard.EnsureSupported(_provider, "eur");

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureSupported_SwitchedOnButUnsupportedCurrency_RefusesWithTheCurrencyMessage()
    {
        GivenCapabilities(cardPaymentsEnabled: true);

        Action act = () => CardCurrencyGuard.EnsureSupported(_provider, "JPY");

        act.Should().Throw<BusinessRuleViolationException>().WithMessage("*JPY*");
    }
}
