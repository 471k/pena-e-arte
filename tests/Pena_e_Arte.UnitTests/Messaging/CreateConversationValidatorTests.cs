using NSubstitute;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Application.Messaging.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

public class CreateConversationValidatorTests
{
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private CreateConversationValidator Sut() => new(_tenant);

    public CreateConversationValidatorTests()
    {
        _tenant.IsSet.Returns(true);
    }

    private static CreateConversationCommand Command(Guid recipientUserId) =>
        new(new CreateConversationRequest(recipientUserId));

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        Sut().ShouldBeValid(Command(Guid.NewGuid()));
    }

    [Fact]
    public void Validate_TenantNotSet_FailsOnStudio()
    {
        _tenant.IsSet.Returns(false);
        Sut().ShouldFailOn(Command(Guid.NewGuid()), "Studio");
    }

    [Fact]
    public void Validate_EmptyRecipientUserId_FailsOnRecipientUserId()
    {
        Sut().ShouldFailOn(Command(Guid.Empty), "Request.RecipientUserId");
    }
}
