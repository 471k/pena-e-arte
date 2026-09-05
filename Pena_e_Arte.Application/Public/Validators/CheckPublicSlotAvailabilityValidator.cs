using FluentValidation;
using Pena_e_Arte.Application.Public.Queries;

namespace Pena_e_Arte.Application.Public.Validators;

// Mirrors CheckSlotAvailabilityValidator (the authenticated sibling) — this anonymous endpoint
// was missing a validator entirely, letting an unauthenticated caller pass an unvalidated
// past/absurd Date or DurationMinutes. Found via /code-review, 2026-09-01.
public class CheckPublicSlotAvailabilityValidator : AbstractValidator<CheckPublicSlotAvailabilityQuery>
{
    public CheckPublicSlotAvailabilityValidator()
    {
        RuleFor(x => x.StudioSlug).NotEmpty();
        RuleFor(x => x.Date).GreaterThan(DateTime.UtcNow)
            .WithMessage("Date must be in the future.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(30, 480);
    }
}
