using FluentValidation;
using Pena_e_Arte.Application.Appointments.Commands;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class ConfirmAppointmentValidator : AbstractValidator<ConfirmAppointmentCommand>
{
    public ConfirmAppointmentValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
