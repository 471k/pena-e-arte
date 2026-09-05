using FluentValidation;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging.Validators;

public class SendChatMessageValidator : AbstractValidator<SendChatMessageCommand>
{
    public SendChatMessageValidator(ICurrentTenant currentTenant)
    {
        RuleFor(x => x)
            .Must(_ => currentTenant.IsSet)
            .WithName("Studio")
            .WithMessage("You need to belong to a studio to send messages.");

        RuleFor(x => x.Request.Body)
            .NotEmpty().WithMessage("Message cannot be empty.")
            .MaximumLength(2000).WithMessage("Message must be at most 2000 characters.");
    }
}
