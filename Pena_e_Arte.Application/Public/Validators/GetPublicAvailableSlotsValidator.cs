using FluentValidation;
using Pena_e_Arte.Application.Appointments.Validators;
using Pena_e_Arte.Application.Public.Queries;

namespace Pena_e_Arte.Application.Public.Validators;

// Mirrors GetAvailableSlotsValidator (the authenticated sibling), as CheckPublicSlotAvailabilityValidator mirrors
// CheckSlotAvailabilityValidator. The range cap is what bounds an anonymous caller's read.
public class GetPublicAvailableSlotsValidator : AbstractValidator<GetPublicAvailableSlotsQuery>
{
    public GetPublicAvailableSlotsValidator(TimeProvider clock)
    {
        RuleFor(x => x.StudioSlug).NotEmpty();
        RuleFor(x => x.DurationMinutes).InclusiveBetween(30, 480);
        RuleFor(x => x).Custom((q, context) => AvailableSlotsRangeRules.Check(q.From, q.To, clock, context.AddFailure));
    }
}
