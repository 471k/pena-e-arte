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
            new CreatePlanRequest("Free", 0, [new PlanPriceRequest("Monthly", 0m)])));
    }

    [Fact]
    public void Validate_PositivePrices_IsValid()
    {
        _sut.ShouldBeValid(new CreatePlanCommand(
            new CreatePlanRequest("Pro", 17, [new PlanPriceRequest("Monthly", 49m)])));
    }

    [Fact]
    public void Validate_NegativePrice_FailsOnPrices()
    {
        _sut.ShouldFailOn(
            new CreatePlanCommand(new CreatePlanRequest("Bad", 0, [new PlanPriceRequest("Monthly", -1m)])),
            "Request.Prices[0].Price");
    }

    [Fact]
    public void Validate_EmptyPrices_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new CreatePlanCommand(new CreatePlanRequest("Bad", 0, [])));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_DuplicateInterval_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new CreatePlanCommand(new CreatePlanRequest("Bad", 17,
                [new PlanPriceRequest("Monthly", 29m), new PlanPriceRequest("Monthly", 39m)])));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("only appear once"));
    }

    [Fact]
    public void Validate_MixedZeroAndPositivePrices_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new CreatePlanCommand(new CreatePlanRequest("Mixed", 17,
                [new PlanPriceRequest("Monthly", 0m), new PlanPriceRequest("Yearly", 290m)])));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("fully free"));
    }

    [Fact]
    public void Validate_InvalidIntervalString_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new CreatePlanCommand(new CreatePlanRequest("Bad", 17, [new PlanPriceRequest("Weekly", 29m)])));

        result.IsValid.Should().BeFalse();
    }
}
