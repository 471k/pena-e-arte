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
/// PokPaymentProvider regardless of any studio's connection state).
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
            SupportsSplit: false, SupportsAuthCapture: false, SupportsHoldExpiry: false,
            SupportedCurrencies: [], Environment: "staging"));
        await SeedConnectedStudioAsync();

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeFalse();
        result.PokEnvironment.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ProviderSupportsAuthCapture_StudioNotConnected_ReturnsUnavailable()
    {
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsSplit: true, SupportsAuthCapture: true, SupportsHoldExpiry: true,
            SupportedCurrencies: ["ALL"], Environment: "staging"));
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = null });
        await _db.SaveChangesAsync();

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeFalse();
        result.PokEnvironment.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ProviderSupportsAuthCapture_MerchantIdSetButNoCredentialRef_ReturnsUnavailable()
    {
        // A Studio.PokMerchantId with no matching StudioCredentialRef is not a real connection —
        // both must be present (ConnectPokAccountCommand always writes both together).
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsSplit: true, SupportsAuthCapture: true, SupportsHoldExpiry: true,
            SupportedCurrencies: ["ALL"], Environment: "staging"));
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = "merchant-1" });
        await _db.SaveChangesAsync();

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_ProviderSupportsAuthCapture_StudioConnected_ReturnsAvailableWithEnvironment()
    {
        _provider.Capabilities.Returns(new PaymentProviderCapabilities(
            SupportsSplit: true, SupportsAuthCapture: true, SupportsHoldExpiry: true,
            SupportedCurrencies: ["ALL"], Environment: "staging"));
        await SeedConnectedStudioAsync();

        PaymentCapabilitiesResponse result = await CreateSut().Handle(new GetPaymentCapabilitiesQuery(), default);

        result.CardPaymentsAvailable.Should().BeTrue();
        result.PokEnvironment.Should().Be("staging");
    }

    private async Task SeedConnectedStudioAsync()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "T", Slug = "t", PokMerchantId = "merchant-1" });
        _db.StudioCredentialRefs.Add(new StudioCredentialRef
        {
            StudioId = _studioId,
            Provider = CredentialProvider.Pok,
            SecretPath = "studios/x/pok",
        });
        await _db.SaveChangesAsync();
    }
}
