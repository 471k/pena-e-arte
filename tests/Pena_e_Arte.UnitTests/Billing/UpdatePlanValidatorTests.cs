using FluentAssertions;
using Pena_e_Arte.Application.Plans.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class UpdatePlanValidatorTests
{
    private readonly UpdatePlanValidator _sut = new();

    [Fact]
    public void Validate_ZeroPrices_IsValid()
    {
        _sut.ShouldBeValid(new UpdatePlanCommand(Guid.NewGuid(),
            new UpdatePlanRequest("Free", 0, [new PlanPriceRequest("Monthly", 0m)], AllowBrandingRemoval: false)));
    }

    [Fact]
    public void Validate_PositivePrices_IsValid()
    {
        _sut.ShouldBeValid(new UpdatePlanCommand(Guid.NewGuid(),
            new UpdatePlanRequest("Pro", 17, [new PlanPriceRequest("Monthly", 49m)], AllowBrandingRemoval: false)));
    }

    [Fact]
    public void Validate_NegativePrice_FailsOnPrices()
    {
        _sut.ShouldFailOn(
            new UpdatePlanCommand(Guid.NewGuid(),
                new UpdatePlanRequest("Bad", 0, [new PlanPriceRequest("Monthly", -1m)], AllowBrandingRemoval: false)),
            "Request.Prices[0].Price");
    }

    [Fact]
    public void Validate_MixedZeroAndPositivePrices_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new UpdatePlanCommand(Guid.NewGuid(),
                new UpdatePlanRequest("Mixed", 17,
                    [new PlanPriceRequest("Monthly", 0m), new PlanPriceRequest("Yearly", 290m)],
                    AllowBrandingRemoval: false)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("fully free"));
    }

    [Fact]
    public void Validate_EmptyPrices_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new UpdatePlanCommand(Guid.NewGuid(), new UpdatePlanRequest("Bad", 0, [], AllowBrandingRemoval: false)));

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_DuplicateInterval_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new UpdatePlanCommand(Guid.NewGuid(), new UpdatePlanRequest("Bad", 17,
                [new PlanPriceRequest("Yearly", 290m), new PlanPriceRequest("Yearly", 390m)],
                AllowBrandingRemoval: false)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("only appear once"));
    }
}
