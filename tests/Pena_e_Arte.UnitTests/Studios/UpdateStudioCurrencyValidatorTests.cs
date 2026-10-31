using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class UpdateStudioCurrencyValidatorTests
{
    private readonly UpdateStudioCurrencyValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid() =>
        _sut.ShouldBeValid(new UpdateStudioCurrencyCommand(Guid.NewGuid(), "EUR"));

    [Fact]
    public void Validate_EmptyStudioId_Fails() =>
        _sut.ShouldFailOn(new UpdateStudioCurrencyCommand(Guid.Empty, "EUR"), "StudioId");

    [Fact]
    public void Validate_EmptyCurrency_Fails() =>
        _sut.ShouldFailOn(new UpdateStudioCurrencyCommand(Guid.NewGuid(), ""), "Currency");

    [Fact]
    public void Validate_UnknownCurrency_Fails() =>
        _sut.ShouldFailOn(new UpdateStudioCurrencyCommand(Guid.NewGuid(), "XXX"), "Currency");

    [Fact]
    public void Validate_TooLongCurrency_Fails() =>
        _sut.ShouldFailOn(new UpdateStudioCurrencyCommand(Guid.NewGuid(), "EURO"), "Currency");

    [Fact]
    public void Validate_LowercaseCurrency_IsValid() =>
        // StudioCurrencies.IsOffered is case-insensitive.
        _sut.ShouldBeValid(new UpdateStudioCurrencyCommand(Guid.NewGuid(), "eur"));

    [Fact]
    public void Validate_PrimaryCurrency_IsValid() =>
        _sut.ShouldBeValid(new UpdateStudioCurrencyCommand(Guid.NewGuid(), "ALL"));

    [Theory]
    [InlineData("USD")]
    [InlineData("GBP")]
    [InlineData("ALK")]
    public void Validate_RealCurrencyThatIsNotOffered_Fails(string currency) =>
        // Studios may only use ALL or EUR (2026-10-26).
        _sut.ShouldFailOn(new UpdateStudioCurrencyCommand(Guid.NewGuid(), currency), "Currency");
}
