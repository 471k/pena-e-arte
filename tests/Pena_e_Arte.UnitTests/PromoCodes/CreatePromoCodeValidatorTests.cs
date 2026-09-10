using Pena_e_Arte.Application.PromoCodes.Commands;
using Pena_e_Arte.Application.PromoCodes.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.PromoCodes;

public class CreatePromoCodeValidatorTests
{
    private readonly CreatePromoCodeValidator _validator = new();

    private static CreatePromoCodeCommand Valid(decimal? fixedAmount = 20m, decimal? percent = null, int? maxRedemptions = null) =>
        new(new CreatePromoCodeRequest("SAVE20", fixedAmount, percent, true, null, maxRedemptions));

    [Fact]
    public void Validate_FixedAmount_Passes()
    {
        _validator.ShouldBeValid(Valid());
    }

    [Fact]
    public void Validate_PercentAmount_Passes()
    {
        _validator.ShouldBeValid(Valid(fixedAmount: null, percent: 15m));
    }

    [Fact]
    public void Validate_EmptyCode_FailsOnCode()
    {
        _validator.ShouldFailOn(new CreatePromoCodeCommand(new CreatePromoCodeRequest("", 20m, null, true)), "Request.Code");
    }

    [Fact]
    public void Validate_BothFixedAndPercent_FailsOnDiscountAmount()
    {
        // .WithName("DiscountAmount") only overrides the message's display name — the
        // FluentValidation error's PropertyName still derives from the RuleFor(x => x.Request)
        // lambda, i.e. "Request".
        _validator.ShouldFailOn(Valid(fixedAmount: 20m, percent: 10m), "Request");
    }

    [Fact]
    public void Validate_NeitherFixedNorPercent_FailsOnDiscountAmount()
    {
        _validator.ShouldFailOn(Valid(fixedAmount: null, percent: null), "Request");
    }

    [Fact]
    public void Validate_ZeroMaxRedemptions_FailsOnMaxRedemptions()
    {
        _validator.ShouldFailOn(Valid(maxRedemptions: 0), "Request.MaxRedemptions");
    }

    [Fact]
    public void Validate_PositiveMaxRedemptions_Passes()
    {
        _validator.ShouldBeValid(Valid(maxRedemptions: 10));
    }

    [Fact]
    public void Validate_PercentOverOneHundred_FailsOnAmountPercent()
    {
        _validator.ShouldFailOn(Valid(fixedAmount: null, percent: 150m), "Request.AmountPercent");
    }

    [Fact]
    public void Validate_NegativeFixedAmount_FailsOnAmountFixed()
    {
        _validator.ShouldFailOn(Valid(fixedAmount: -5m, percent: null), "Request.AmountFixed");
    }
}
