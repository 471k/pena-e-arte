using FluentValidation;
using Pena_e_Arte.Application.GiftCards.Commands;

namespace Pena_e_Arte.Application.GiftCards.Validators;

public class PurchaseGiftCardValidator : AbstractValidator<PurchaseGiftCardCommand>
{
    public PurchaseGiftCardValidator()
    {
        RuleFor(x => x.Request.StudioSlug).NotEmpty();
        RuleFor(x => x.Request.Amount).GreaterThan(0).LessThanOrEqualTo(5000);
        RuleFor(x => x.Request.PurchaserEmail).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Request.RecipientEmail).EmailAddress().MaximumLength(320)
            .When(x => !string.IsNullOrEmpty(x.Request.RecipientEmail));
    }
}
