using FluentValidation;
using Pena_e_Arte.Application.Appointments.Queries;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class ExportAppointmentsCsvValidator : AbstractValidator<ExportAppointmentsCsvQuery>
{
    public ExportAppointmentsCsvValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From is not null && x.To is not null)
            .WithMessage("To must be on or after From.");
    }
}
