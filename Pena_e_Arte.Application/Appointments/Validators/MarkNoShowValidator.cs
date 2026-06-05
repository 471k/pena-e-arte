using FluentValidation;
using Pena_e_Arte.Application.Appointments.Commands;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class MarkNoShowValidator : AbstractValidator<MarkNoShowCommand>
{
    public MarkNoShowValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
