using Pena_e_Arte.Application.PromoCodes.Commands;
using Pena_e_Arte.Application.PromoCodes.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.PromoCodes;

public class UpdatePromoCodeValidatorTests
{
    private readonly UpdatePromoCodeValidator _validator = new();

    private static UpdatePromoCodeCommand Valid(decimal? fixedAmount = 20m, decimal? percent = null) =>
        new(Guid.NewGuid(), new UpdatePromoCodeRequest("SAVE20", fixedAmount, percent, true));

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.ShouldBeValid(Valid());
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
    public void Validate_EmptyCode_FailsOnCode()
    {
        _validator.ShouldFailOn(
            new UpdatePromoCodeCommand(Guid.NewGuid(), new UpdatePromoCodeRequest("", 20m, null, true)), "Request.Code");
    }
}
