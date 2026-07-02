using FluentValidation;
using Pena_e_Arte.Application.Appointments.Commands;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class CreateAppointmentValidator : AbstractValidator<CreateAppointmentCommand>
{
    // Mirrors BookAppointmentForm.tsx's VALID_DURATIONS — the session-length options
    // actually offered in the booking form.
    private static readonly int[] ValidDurations = [30, 45, 60, 90, 120, 180, 240, 300, 360, 480];

    public CreateAppointmentValidator()
    {
        RuleFor(x => x.Request.ArtistId).NotEmpty();
        RuleFor(x => x.Request.ClientId).NotEmpty();
        RuleFor(x => x.Request.Date)
            .GreaterThan(DateTime.UtcNow.AddMinutes(30))
            .WithMessage("Appointment must be at least 30 minutes in the future.");
        RuleFor(x => x.Request.DurationMinutes)
            .Must(d => ValidDurations.Contains(d))
            .WithMessage($"Duration must be one of: {string.Join(", ", ValidDurations)} minutes.");
        RuleFor(x => x.Request.Notes).MaximumLength(2000).When(x => x.Request.Notes is not null);
    }
}
