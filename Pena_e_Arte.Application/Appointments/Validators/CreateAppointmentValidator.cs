using FluentValidation;
using Pena_e_Arte.Application.Appointments.Commands;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class CreateAppointmentValidator : AbstractValidator<CreateAppointmentCommand>
{
    public CreateAppointmentValidator()
    {
        RuleFor(x => x.Request.ArtistId).NotEmpty();
        RuleFor(x => x.Request.ClientId).NotEmpty();
        RuleFor(x => x.Request.Date)
            .GreaterThan(DateTime.UtcNow)
            .WithMessage("Appointment date must be in the future.");
        RuleFor(x => x.Request.DurationMinutes).InclusiveBetween(30, 480);
        RuleFor(x => x.Request.DepositAmount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Request.Notes).MaximumLength(2000).When(x => x.Request.Notes is not null);
    }
}
