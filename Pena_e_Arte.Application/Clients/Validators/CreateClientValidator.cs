using FluentValidation;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Common;

namespace Pena_e_Arte.Application.Clients.Validators;

public class CreateClientValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientValidator()
    {
        RuleFor(x => x.Request.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Request.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Request.Phone)
            .MaximumLength(20)
            .Matches(PhoneValidationRules.E164Format)
            .WithMessage(PhoneValidationRules.E164ErrorMessage)
            .When(x => x.Request.Phone is not null);
        RuleFor(x => x.Request.ArtistId).NotEmpty();
    }
}
