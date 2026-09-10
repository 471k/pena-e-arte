using FluentValidation;
using Pena_e_Arte.Application.GiftCards.Commands;

namespace Pena_e_Arte.Application.GiftCards.Validators;

public class RedeemGiftCardValidator : AbstractValidator<RedeemGiftCardCommand>
{
    public RedeemGiftCardValidator()
    {
        RuleFor(x => x.Request.Code).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Request.AppointmentId).NotEmpty();
        RuleFor(x => x.Request.Amount).GreaterThan(0);
    }
}
