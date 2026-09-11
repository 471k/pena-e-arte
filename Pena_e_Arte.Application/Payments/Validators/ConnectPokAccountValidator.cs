using FluentValidation;
using Pena_e_Arte.Application.Payments.Commands;

namespace Pena_e_Arte.Application.Payments.Validators;

public class ConnectPokAccountValidator : AbstractValidator<ConnectPokAccountCommand>
{
    public ConnectPokAccountValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.Request.KeyId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Request.KeySecret).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Request.MerchantId).NotEmpty().MaximumLength(64);
    }
}
