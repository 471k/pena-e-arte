using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Payments.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class UpdateSessionSplitsValidatorTests
{
    private readonly UpdateSessionSplitsValidator _sut = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        _sut.ShouldBeValid(new UpdateSessionSplitsCommand(Guid.NewGuid(),
            new UpdateSessionSplitsRequest([new SessionSplitItem("Deposit", 100m)])));
    }

    [Fact]
    public void Validate_EmptyPaymentId_Fails()
    {
        _sut.ShouldFailOn(
            new UpdateSessionSplitsCommand(Guid.Empty,
                new UpdateSessionSplitsRequest([new SessionSplitItem("Deposit", 100m)])),
            "PaymentId");
    }

    [Fact]
    public void Validate_EmptySplitsList_Fails()
    {
        _sut.ShouldFailOn(
            new UpdateSessionSplitsCommand(Guid.NewGuid(),
                new UpdateSessionSplitsRequest([])),
            "Request.Splits");
    }

    [Fact]
    public void Validate_SplitWithEmptyLabel_Fails()
    {
        _sut.ShouldFailOn(
            new UpdateSessionSplitsCommand(Guid.NewGuid(),
                new UpdateSessionSplitsRequest([new SessionSplitItem("", 100m)])),
            "Request.Splits[0].Label");
    }

    [Fact]
    public void Validate_SplitWithZeroAmount_Fails()
    {
        _sut.ShouldFailOn(
            new UpdateSessionSplitsCommand(Guid.NewGuid(),
                new UpdateSessionSplitsRequest([new SessionSplitItem("Label", 0m)])),
            "Request.Splits[0].Amount");
    }

    [Fact]
    public void Validate_SplitWithLabelTooLong_Fails()
    {
        _sut.ShouldFailOn(
            new UpdateSessionSplitsCommand(Guid.NewGuid(),
                new UpdateSessionSplitsRequest([new SessionSplitItem(new string('x', 101), 100m)])),
            "Request.Splits[0].Label");
    }
}
