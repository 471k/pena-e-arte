using FluentValidation;
using Pena_e_Arte.Application.Clients.Commands;

namespace Pena_e_Arte.Application.Clients.Validators;

public class UpdatePortableProfileOptInValidator : AbstractValidator<UpdatePortableProfileOptInCommand>
{
    public UpdatePortableProfileOptInValidator()
    {
        RuleFor(x => x.Request).NotNull();
    }
}
