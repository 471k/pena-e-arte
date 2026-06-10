using FluentValidation;
using Pena_e_Arte.Application.Designs.Commands;

namespace Pena_e_Arte.Application.Designs.Validators;

public class CreateDesignShareTokenValidator : AbstractValidator<CreateDesignShareTokenCommand>
{
    public CreateDesignShareTokenValidator()
    {
        RuleFor(x => x.DesignRevisionId).NotEmpty();
    }
}
