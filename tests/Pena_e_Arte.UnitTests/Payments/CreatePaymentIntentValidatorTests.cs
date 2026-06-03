using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Application.Payments.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class CreatePaymentIntentValidatorTests
{
    private readonly CreatePaymentIntentValidator _sut = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        _sut.ShouldBeValid(new CreatePaymentIntentCommand(
            new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), 100m, "eur")));
    }

    [Fact]
    public void Validate_EmptyAppointmentId_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.Empty, Guid.NewGuid(), 100m, "eur")),
            "Request.AppointmentId");
    }

    [Fact]
    public void Validate_EmptyClientId_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.Empty, 100m, "eur")),
            "Request.ClientId");
    }

    [Fact]
    public void Validate_ZeroAmount_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), 0m, "eur")),
            "Request.Amount");
    }

    [Fact]
    public void Validate_NegativeAmount_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), -1m, "eur")),
            "Request.Amount");
    }

    [Fact]
    public void Validate_EmptyCurrency_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), 100m, "")),
            "Request.Currency");
    }

    [Fact]
    public void Validate_TooLongCurrency_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), 100m, "euro")),
            "Request.Currency");
    }

    [Fact]
    public void Validate_NumericCurrency_Fails()
    {
        _sut.ShouldFailOn(
            new CreatePaymentIntentCommand(new CreatePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), 100m, "123")),
            "Request.Currency");
    }
}
