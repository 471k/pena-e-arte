using Pena_e_Arte.Application.DepositRules.Commands;
using Pena_e_Arte.Application.DepositRules.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.DepositRules;

public class CreateDepositRuleValidatorTests
{
    private readonly CreateDepositRuleValidator _validator = new();

    private static CreateDepositRuleCommand Valid(int? cancellationWindowHours = null, int refundPercent = 0) =>
        new(new CreateDepositRuleRequest("Standard Deposit", 50m, null, true, cancellationWindowHours, refundPercent));

    [Fact]
    public void Validate_ValidCommand_Passes()
    {
        _validator.ShouldBeValid(Valid());
    }

    [Fact]
    public void Validate_NullCancellationWindowHours_Passes()
    {
        _validator.ShouldBeValid(Valid(cancellationWindowHours: null));
    }

    [Fact]
    public void Validate_PositiveCancellationWindowHours_Passes()
    {
        _validator.ShouldBeValid(Valid(cancellationWindowHours: 48));
    }

    [Fact]
    public void Validate_ZeroCancellationWindowHours_FailsOnCancellationWindowHours()
    {
        _validator.ShouldFailOn(Valid(cancellationWindowHours: 0), "Request.CancellationWindowHours");
    }

    [Fact]
    public void Validate_NegativeCancellationWindowHours_FailsOnCancellationWindowHours()
    {
        _validator.ShouldFailOn(Valid(cancellationWindowHours: -5), "Request.CancellationWindowHours");
    }

    [Fact]
    public void Validate_RefundPercentZero_Passes()
    {
        _validator.ShouldBeValid(Valid(refundPercent: 0));
    }

    [Fact]
    public void Validate_RefundPercentOneHundred_Passes()
    {
        _validator.ShouldBeValid(Valid(refundPercent: 100));
    }

    [Fact]
    public void Validate_RefundPercentAboveOneHundred_FailsOnRefundPercentOnLateCancel()
    {
        _validator.ShouldFailOn(Valid(refundPercent: 101), "Request.RefundPercentOnLateCancel");
    }

    [Fact]
    public void Validate_RefundPercentNegative_FailsOnRefundPercentOnLateCancel()
    {
        _validator.ShouldFailOn(Valid(refundPercent: -1), "Request.RefundPercentOnLateCancel");
    }
}
