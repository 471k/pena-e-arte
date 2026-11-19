using FluentValidation.TestHelper;
using Pena_e_Arte.Application.Platform.Commands;

namespace Pena_e_Arte.UnitTests.Platform;

public class CancelSubscriptionValidatorTests
{
    private readonly CancelSubscriptionValidator _validator = new();
    private static readonly AdminCancellationConfirmation Reviewed = new("Active", null, 0m);

    private static CancelSubscriptionCommand Command(Guid studioId, string? overrideRule = null, string? reason = null) =>
        new(studioId, overrideRule, reason, Reviewed);

    [Fact]
    public void Validate_NoOverride_NoErrors()
    {
        _validator.TestValidate(Command(Guid.NewGuid())).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyStudioId_HasError()
    {
        _validator.TestValidate(Command(Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.StudioId);
    }

    [Theory]
    [InlineData("AdminFull")]
    [InlineData("adminfull")]   // the endpoint parsed case-insensitively before; the validator keeps that
    [InlineData("AdminNone")]
    public void Validate_AdminRuleWithReason_NoErrors(string overrideRule)
    {
        _validator.TestValidate(Command(Guid.NewGuid(), overrideRule, "Goodwill"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_AdminFullWithoutReason_HasError()
    {
        _validator.TestValidate(Command(Guid.NewGuid(), "AdminFull", null))
            .ShouldHaveValidationErrorFor(x => x.OverrideReason);
    }

    [Fact]
    public void Validate_AdminNoneWithEmptyReason_HasError()
    {
        _validator.TestValidate(Command(Guid.NewGuid(), "AdminNone", ""))
            .ShouldHaveValidationErrorFor(x => x.OverrideReason);
    }

    [Theory]
    // YearlyFormula is the implicit default (Override == null): passing it explicitly is not an override.
    [InlineData("YearlyFormula")]
    // Evidence §0.2: the endpoint ran Enum.Parse on these before the validator, so "Bogus" threw in the endpoint
    // (a 500) and "1" quietly became AdminFull. Both are now validation errors (422).
    [InlineData("Bogus")]
    [InlineData("1")]
    [InlineData("")]
    public void Validate_OverrideThatIsNotAnAdminRuleName_HasError(string overrideRule)
    {
        _validator.TestValidate(Command(Guid.NewGuid(), overrideRule, "n/a"))
            .ShouldHaveValidationErrorFor(x => x.Override);
    }

    [Fact]
    public void Validate_NoReviewedConsequence_HasError()
    {
        _validator.TestValidate(new CancelSubscriptionCommand(Guid.NewGuid()))
            .ShouldHaveValidationErrorFor(x => x.Confirmed);
    }

    [Fact]
    public void Validate_NegativeConfirmedRefund_HasError()
    {
        _validator.TestValidate(new CancelSubscriptionCommand(Guid.NewGuid(), null, null, Reviewed with { RefundAmount = -1m }))
            .ShouldHaveValidationErrorFor(x => x.Confirmed!.RefundAmount);
    }
}
