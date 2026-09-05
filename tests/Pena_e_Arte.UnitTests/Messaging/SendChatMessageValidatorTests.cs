using NSubstitute;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Application.Messaging.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Messaging;

public class SendChatMessageValidatorTests
{
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private SendChatMessageValidator Sut() => new(_tenant);

    public SendChatMessageValidatorTests()
    {
        _tenant.IsSet.Returns(true);
    }

    private static SendChatMessageCommand Command(string body) =>
        new(Guid.NewGuid(), new SendChatMessageRequest(body));

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        Sut().ShouldBeValid(Command("Hello there"));
    }

    [Fact]
    public void Validate_TenantNotSet_FailsOnStudio()
    {
        _tenant.IsSet.Returns(false);
        Sut().ShouldFailOn(Command("Hello there"), "Studio");
    }

    [Fact]
    public void Validate_EmptyBody_FailsOnBody()
    {
        Sut().ShouldFailOn(Command(""), "Request.Body");
    }

    [Fact]
    public void Validate_WhitespaceOnlyBody_FailsOnBody()
    {
        Sut().ShouldFailOn(Command("   "), "Request.Body");
    }

    [Fact]
    public void Validate_BodyExceedsMaxLength_FailsOnBody()
    {
        Sut().ShouldFailOn(Command(new string('x', 2001)), "Request.Body");
    }

    [Fact]
    public void Validate_BodyAtMaxLength_IsValid()
    {
        Sut().ShouldBeValid(Command(new string('x', 2000)));
    }
}
