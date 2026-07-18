using FluentAssertions;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class CreatePlanValidatorTests
{
    private readonly CreatePlanValidator _sut = new();

    [Fact]
    public void Validate_ZeroPrices_IsValid()
    {
        _sut.ShouldBeValid(new CreatePlanCommand(
            new CreatePlanRequest("Free", "Monthly", 0m, 0m, 0)));
    }

    [Fact]
    public void Validate_PositivePrices_IsValid()
    {
        _sut.ShouldBeValid(new CreatePlanCommand(
            new CreatePlanRequest("Pro", "Monthly", 49m, 490m, 17)));
    }

    [Fact]
    public void Validate_NegativeMonthlyPrice_FailsOnPriceMonthly()
    {
        _sut.ShouldFailOn(
            new CreatePlanCommand(new CreatePlanRequest("Bad", "Monthly", -1m, 0m, 0)),
            "Request.PriceMonthly");
    }

    [Fact]
    public void Validate_NegativeYearlyPrice_FailsOnPriceYearly()
    {
        _sut.ShouldFailOn(
            new CreatePlanCommand(new CreatePlanRequest("Bad", "Monthly", 0m, -1m, 0)),
            "Request.PriceYearly");
    }

    [Fact]
    public void Validate_MixedZeroMonthlyPositiveYearly_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new CreatePlanCommand(new CreatePlanRequest("Mixed", "Monthly", 0m, 290m, 17)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("fully free"));
    }

    [Fact]
    public void Validate_MixedPositiveMonthlyZeroYearly_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new CreatePlanCommand(new CreatePlanRequest("Mixed", "Monthly", 29m, 0m, 17)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("fully free"));
    }
}
