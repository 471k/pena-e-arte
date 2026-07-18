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
            new UpdatePlanRequest("Free", 0m, 0m, 0, AllowBrandingRemoval: false)));
    }

    [Fact]
    public void Validate_PositivePrices_IsValid()
    {
        _sut.ShouldBeValid(new UpdatePlanCommand(Guid.NewGuid(),
            new UpdatePlanRequest("Pro", 49m, 490m, 17, AllowBrandingRemoval: false)));
    }

    [Fact]
    public void Validate_NegativeMonthlyPrice_FailsOnPriceMonthly()
    {
        _sut.ShouldFailOn(
            new UpdatePlanCommand(Guid.NewGuid(),
                new UpdatePlanRequest("Bad", -1m, 0m, 0, AllowBrandingRemoval: false)),
            "Request.PriceMonthly");
    }

    [Fact]
    public void Validate_MixedZeroMonthlyPositiveYearly_IsInvalid()
    {
        FluentValidation.Results.ValidationResult result = _sut.Validate(
            new UpdatePlanCommand(Guid.NewGuid(),
                new UpdatePlanRequest("Mixed", 0m, 290m, 17, AllowBrandingRemoval: false)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("fully free"));
    }
}
