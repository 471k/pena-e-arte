using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Payments.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

// Currency-specific cases (empty/too-long/numeric) removed with the Currency field itself
// (§2.3/§9.3 of docs/claude/overnight-prompt-studio-currency-2026-09-27.md) — the request no
// longer carries a currency at all; the studio's own Currency is always used.
public class CreatePaymentIntentValidatorTests
{
    private readonly CreatePaymentIntentValidator _sut = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        _sut.ShouldBeValid(new CreatePaymentIntentCommand(
            new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), 100m)));
    }

    [Fact]
    public void Validate_EmptyAppointmentId_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.Empty, Guid.NewGuid(), 100m)),
            "Request.AppointmentId");
    }

    [Fact]
    public void Validate_EmptyClientId_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.Empty, 100m)),
            "Request.ClientId");
    }

    [Fact]
    public void Validate_ZeroAmount_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), 0m)),
            "Request.Amount");
    }

    [Fact]
    public void Validate_NegativeAmount_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), -1m)),
            "Request.Amount");
    }
}
