using Pena_e_Arte.Application.Services.Commands;
using Pena_e_Arte.Application.Services.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Services;

public class CreateServiceValidatorTests
{
    private readonly CreateServiceValidator _validator = new();

    private static CreateServiceCommand Valid(
        string name = "New Tattoo Session", int durationMinutes = 90,
        decimal? price = null, decimal? depositAmount = null) =>
        new(new CreateServiceRequest(name, null, durationMinutes, price, depositAmount, true));

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.ShouldBeValid(Valid());
    }

    [Fact]
    public void Validate_EmptyName_FailsOnName()
    {
        _validator.ShouldFailOn(Valid(name: ""), "Request.Name");
    }

    [Fact]
    public void Validate_NameOver100Chars_FailsOnName()
    {
        _validator.ShouldFailOn(Valid(name: new string('a', 101)), "Request.Name");
    }

    [Fact]
    public void Validate_DurationBelowFiveMinutes_FailsOnDurationMinutes()
    {
        _validator.ShouldFailOn(Valid(durationMinutes: 4), "Request.DurationMinutes");
    }

    [Fact]
    public void Validate_DurationAbove600Minutes_FailsOnDurationMinutes()
    {
        _validator.ShouldFailOn(Valid(durationMinutes: 601), "Request.DurationMinutes");
    }

    [Fact]
    public void Validate_DurationAtLowerBound_Passes()
    {
        _validator.ShouldBeValid(Valid(durationMinutes: 5));
    }

    [Fact]
    public void Validate_DurationAtUpperBound_Passes()
    {
        _validator.ShouldBeValid(Valid(durationMinutes: 600));
    }

    [Fact]
    public void Validate_NegativePrice_FailsOnPrice()
    {
        _validator.ShouldFailOn(Valid(price: -1m), "Request.Price");
    }

    [Fact]
    public void Validate_ZeroPrice_Passes()
    {
        _validator.ShouldBeValid(Valid(price: 0m));
    }

    [Fact]
    public void Validate_NegativeDepositAmount_FailsOnDepositAmount()
    {
        _validator.ShouldFailOn(Valid(depositAmount: -1m), "Request.DepositAmount");
    }

    [Fact]
    public void Validate_NullPriceAndDeposit_Passes()
    {
        _validator.ShouldBeValid(Valid(price: null, depositAmount: null));
    }
}
