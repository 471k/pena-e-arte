using FluentValidation;
using Pena_e_Arte.Application.Appointments.Commands;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class CompleteAppointmentValidator : AbstractValidator<CompleteAppointmentCommand>
{
    public CompleteAppointmentValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
