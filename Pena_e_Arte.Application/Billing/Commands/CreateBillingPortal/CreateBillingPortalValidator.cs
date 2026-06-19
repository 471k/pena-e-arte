using FluentValidation;

namespace Pena_e_Arte.Application.Billing.Commands.CreateBillingPortal;

public sealed class CreateBillingPortalValidator : AbstractValidator<CreateBillingPortalCommand>
{
    public CreateBillingPortalValidator()
    {
        RuleFor(x => x.ReturnUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("ReturnUrl must be a valid absolute URL.");
    }
}
