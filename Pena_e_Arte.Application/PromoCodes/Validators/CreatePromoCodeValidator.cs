using FluentValidation;
using Pena_e_Arte.Application.PromoCodes.Commands;

namespace Pena_e_Arte.Application.PromoCodes.Validators;

public class CreatePromoCodeValidator : AbstractValidator<CreatePromoCodeCommand>
{
    public CreatePromoCodeValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(40);

        RuleFor(x => x.Request.AmountFixed)
            .GreaterThan(0)
            .When(x => x.Request.AmountFixed.HasValue);

        RuleFor(x => x.Request.AmountPercent)
            .InclusiveBetween(0.01m, 100m)
            .When(x => x.Request.AmountPercent.HasValue);

        RuleFor(x => x.Request)
            .Must(r => r.AmountFixed.HasValue ^ r.AmountPercent.HasValue)
            .WithName("DiscountAmount")
            .WithMessage("Exactly one of AmountFixed or AmountPercent must be specified.");

        RuleFor(x => x.Request.MaxRedemptions)
            .GreaterThan(0)
            .When(x => x.Request.MaxRedemptions.HasValue);
    }
}
