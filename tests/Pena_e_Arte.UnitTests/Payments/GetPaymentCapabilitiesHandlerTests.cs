using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

/// <summary>
/// Regression coverage for the code-review finding: card availability must reflect whether THIS
/// studio connected POK, not just the provider's static capability (which is always true for
/// PokPaymentProvider regardless of any studio's connection state). Also covers the currency gate
/// added in docs/claude/overnight-prompt-studio-currency-2026-09-27.md §2.8: check order is
/// provider disabled -> currency unsupported -> not connected -> available, and Currency is
/// always populated on the response.
/// </summary>
public class GetPaymentCapabilitiesHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetPaymentCapabilitiesHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private GetPaymentCapabilitiesHandler CreateSut() => new(_provider, _db, _tenant);

    [Fact]
    public async Task Handle_ProviderDoesNotSupportAuthCapture_ReturnsUnavailableRegardlessOfConnection()
    {
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: false, SupportsHoldExpiry: false,
            SupportedCurrencies: [], Environment: "staging"));
        await SeedConnectedStudioAsync(currency: "ALL");

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeFalse();
        result.PokEnvironment.Should().BeNull();
        result.CardUnavailableReason.Should().Be(CardUnavailableReasons.ProviderDisabled);
        result.Currency.Should().Be("ALL");
    }

    [Fact]
    public async Task Handle_StudioCurrencyNotSupportedByProvider_ReturnsUnavailableBeforeCheckingConnection()
    {
        // JPY isn't in POK's supported set even though this studio IS connected — the currency
        // check runs before the connection check, per §2.8's target order.
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true,
            SupportedCurrencies: ["ALL", "EUR"], Environment: "staging"));
        await SeedConnectedStudioAsync(currency: "JPY");

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeFalse();
        result.CardUnavailableReason.Should().Be(CardUnavailableReasons.ProviderUnsupportedCurrency);
        result.Currency.Should().Be("JPY");
    }

    [Fact]
    public async Task Handle_ProviderSupportsAuthCapture_StudioNotConnected_ReturnsUnavailable()
    {
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true,
            SupportedCurrencies: ["ALL"], Environment: "staging"));
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", CountryCode = "AL", Currency = "ALL", PokMerchantId = null });
        await _db.SaveChangesAsync();

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeFalse();
        result.PokEnvironment.Should().BeNull();
        result.CardUnavailableReason.Should().Be(CardUnavailableReasons.ProviderNotConnected);
        result.Currency.Should().Be("ALL");
    }

    [Fact]
    public async Task Handle_ProviderSupportsAuthCapture_MerchantIdSetButNoCredentialRef_ReturnsUnavailable()
    {
        // A Studio.PokMerchantId with no matching StudioCredentialRef is not a real connection —
        // both must be present (ConnectPokAccountCommand always writes both together).
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true,
            SupportedCurrencies: ["ALL"], Environment: "staging"));
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", CountryCode = "AL", Currency = "ALL", PokMerchantId = "merchant-1" });
        await _db.SaveChangesAsync();

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeFalse();
        result.CardUnavailableReason.Should().Be(CardUnavailableReasons.ProviderNotConnected);
    }

    [Fact]
    public async Task Handle_ProviderSupportsAuthCapture_StudioConnected_ReturnsAvailableWithEnvironment()
    {
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true,
            SupportedCurrencies: ["ALL"], Environment: "staging"));
        await SeedConnectedStudioAsync(currency: "ALL");

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeTrue();
        result.PokEnvironment.Should().Be("staging");
        result.CardUnavailableReason.Should().BeNull();
        result.Currency.Should().Be("ALL");
    }

    [Fact]
    public async Task Handle_EurStudioConnected_ReturnsAvailable()
    {
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsAuthCapture: true, SupportsHoldExpiry: true,
            SupportedCurrencies: ["ALL", "EUR"], Environment: "staging"));
        await SeedConnectedStudioAsync(currency: "EUR");

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeTrue();
        result.Currency.Should().Be("EUR");
    }

    private async Task SeedConnectedStudioAsync(string currency)
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", CountryCode = "AL", Currency = currency, PokMerchantId = "merchant-1" });
        _db.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = _studioId,
            Provider = CredentialProvider.Pok,
            SecretPath = "studios/x/pok",
        });
        await _db.SaveChangesAsync();
    }
}
