using FluentValidation;
using Pena_e_Arte.Application.Designs.Commands;

namespace Pena_e_Arte.Application.Designs.Validators;

public class RevokeDesignShareTokenValidator : AbstractValidator<RevokeDesignShareTokenCommand>
{
    public RevokeDesignShareTokenValidator()
    {
        RuleFor(x => x.DesignShareTokenId).NotEmpty();
    }
}
