using FluentValidation;
using Pena_e_Arte.Application.Clients.Commands;

namespace Pena_e_Arte.Application.Clients.Validators;

public class UpdateBodyMapValidator : AbstractValidator<UpdateBodyMapCommand>
{
    public UpdateBodyMapValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Request.Locations).NotNull();
        RuleForEach(x => x.Request.Locations).NotEmpty().MaximumLength(200);
    }
}
