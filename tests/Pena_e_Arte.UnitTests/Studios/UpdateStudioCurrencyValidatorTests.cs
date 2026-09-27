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
        // CurrencyCatalog.IsSupported is case-insensitive.
        _sut.ShouldBeValid(new UpdateStudioCurrencyCommand(Guid.NewGuid(), "eur"));
}
