using FluentValidation;
using Pena_e_Arte.Application.Messaging.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Messaging.Validators;

public class CreateConversationValidator : AbstractValidator<CreateConversationCommand>
{
    public CreateConversationValidator(ICurrentTenant currentTenant)
    {
        RuleFor(x => x)
            .Must(_ => currentTenant.IsSet)
            .WithName("Studio")
            .WithMessage("You need to belong to a studio to send messages.");

        RuleFor(x => x.Request.RecipientUserId).NotEmpty();
    }
}
