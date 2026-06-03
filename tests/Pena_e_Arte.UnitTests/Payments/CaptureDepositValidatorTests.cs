using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Payments.Validators;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class CaptureDepositValidatorTests
{
    private readonly CaptureDepositValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(new CaptureDepositCommand(Guid.NewGuid()));
    }

    [Fact]
    public void Validate_EmptyPaymentId_FailsOnPaymentId()
    {
        _sut.ShouldFailOn(new CaptureDepositCommand(Guid.Empty), "PaymentId");
    }
}
