using FluentValidation;
using Pena_e_Arte.Application.Appointments.Queries;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class CheckSlotAvailabilityValidator
    : AbstractValidator<CheckSlotAvailabilityQuery>
{
    public CheckSlotAvailabilityValidator()
    {
        RuleFor(x => x.Date).GreaterThan(DateTime.UtcNow)
            .WithMessage("Date must be in the future.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(30, 480);
    }
}
