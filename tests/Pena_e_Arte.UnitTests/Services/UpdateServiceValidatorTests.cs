using Pena_e_Arte.Application.Services.Commands;
using Pena_e_Arte.Application.Services.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Services;

public class UpdateServiceValidatorTests
{
    private readonly UpdateServiceValidator _validator = new();

    private static UpdateServiceCommand Valid(
        string name = "Renamed", int durationMinutes = 90,
        decimal? price = null, decimal? depositAmount = null) =>
        new(Guid.NewGuid(), new UpdateServiceRequest(name, null, durationMinutes, price, depositAmount, true));

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
    public void Validate_DurationOutOfBounds_FailsOnDurationMinutes()
    {
        _validator.ShouldFailOn(Valid(durationMinutes: 0), "Request.DurationMinutes");
    }

    [Fact]
    public void Validate_NegativePrice_FailsOnPrice()
    {
        _validator.ShouldFailOn(Valid(price: -10m), "Request.Price");
    }

    [Fact]
    public void Validate_NegativeDepositAmount_FailsOnDepositAmount()
    {
        _validator.ShouldFailOn(Valid(depositAmount: -10m), "Request.DepositAmount");
    }
}
