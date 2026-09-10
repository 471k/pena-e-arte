using FluentValidation;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Validators;

public class JoinWaitlistValidator : AbstractValidator<JoinWaitlistCommand>
{
    public JoinWaitlistValidator(ICurrentUser currentUser)
    {
        RuleFor(x => x.Request.PreferredDateTo)
            .GreaterThanOrEqualTo(x => x.Request.PreferredDateFrom)
            .WithMessage("PreferredDateTo must not be before PreferredDateFrom.");

        bool isSignedInClient = currentUser.IsAuthenticated && currentUser.Role == "client";

        When(_ => !isSignedInClient, () =>
        {
            RuleFor(x => x.Request.StudioSlug).NotEmpty()
                .WithMessage("StudioSlug is required for a guest waitlist submission.");
            RuleFor(x => x.Request.GuestName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Request.GuestEmail).NotEmpty().EmailAddress().MaximumLength(320);
            RuleFor(x => x.Request.GuestPhone).NotEmpty().MaximumLength(50);
        });

        RuleFor(x => x.Request.Notes).MaximumLength(1000);
    }
}
