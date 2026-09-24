using FluentValidation.TestHelper;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.UnitTests.Platform;

public class CancelSubscriptionValidatorTests
{
    private readonly CancelSubscriptionValidator _validator = new();

    [Fact]
    public void Validate_NoOverride_NoErrors()
    {
        _validator.TestValidate(new CancelSubscriptionCommand(Guid.NewGuid())).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyStudioId_HasError()
    {
        _validator.TestValidate(new CancelSubscriptionCommand(Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.StudioId);
    }

    [Fact]
    public void Validate_AdminFullWithReason_NoErrors()
    {
        _validator.TestValidate(new CancelSubscriptionCommand(Guid.NewGuid(), RefundRule.AdminFull, "Goodwill"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_AdminNoneWithReason_NoErrors()
    {
        _validator.TestValidate(new CancelSubscriptionCommand(Guid.NewGuid(), RefundRule.AdminNone, "Policy violation"))
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_AdminFullWithoutReason_HasError()
    {
        _validator.TestValidate(new CancelSubscriptionCommand(Guid.NewGuid(), RefundRule.AdminFull, null))
            .ShouldHaveValidationErrorFor(x => x.OverrideReason);
    }

    [Fact]
    public void Validate_AdminNoneWithEmptyReason_HasError()
    {
        _validator.TestValidate(new CancelSubscriptionCommand(Guid.NewGuid(), RefundRule.AdminNone, ""))
            .ShouldHaveValidationErrorFor(x => x.OverrideReason);
    }

    [Fact]
    public void Validate_YearlyFormulaAsExplicitOverride_HasError()
    {
        // YearlyFormula is the implicit default (Override == null) — passing it explicitly
        // is not a valid override value.
        _validator.TestValidate(new CancelSubscriptionCommand(Guid.NewGuid(), RefundRule.YearlyFormula, "n/a"))
            .ShouldHaveValidationErrorFor(x => x.Override);
    }
}
