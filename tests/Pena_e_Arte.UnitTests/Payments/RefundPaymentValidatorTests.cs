using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Payments.Validators;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class RefundPaymentValidatorTests
{
    private readonly RefundPaymentValidator _sut = new();

    [Fact]
    public void Validate_FullRefundNoAmount_Passes()
    {
        _sut.ShouldBeValid(new RefundPaymentCommand(Guid.NewGuid(), null));
    }

    [Fact]
    public void Validate_PartialRefundPositiveAmount_Passes()
    {
        _sut.ShouldBeValid(new RefundPaymentCommand(Guid.NewGuid(), 50m));
    }

    [Fact]
    public void Validate_EmptyPaymentId_Fails()
    {
        _sut.ShouldFailOn(new RefundPaymentCommand(Guid.Empty, null), "PaymentId");
    }

    [Fact]
    public void Validate_ZeroAmount_Fails()
    {
        _sut.ShouldFailOn(new RefundPaymentCommand(Guid.NewGuid(), 0m), "Amount");
    }

    [Fact]
    public void Validate_NegativeAmount_Fails()
    {
        _sut.ShouldFailOn(new RefundPaymentCommand(Guid.NewGuid(), -1m), "Amount");
    }
}
