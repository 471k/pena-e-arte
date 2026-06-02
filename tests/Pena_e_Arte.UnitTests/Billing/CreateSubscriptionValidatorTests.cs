using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Application.Billing.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class CreateSubscriptionValidatorTests
{
    private readonly CreateSubscriptionValidator _sut = new();

    [Fact]
    public void Validate_EmptyPlanId_FailsOnPlanId()
    {
        _sut.ShouldFailOn(
            new CreateSubscriptionCommand(new CreateSubscriptionRequest(Guid.Empty)),
            "Request.PlanId");
    }

    [Fact]
    public void Validate_ValidPlanId_IsValid()
    {
        _sut.ShouldBeValid(new CreateSubscriptionCommand(new CreateSubscriptionRequest(Guid.NewGuid())));
    }
}
