using FluentValidation;
using Pena_e_Arte.Application.Clients.Commands;

namespace Pena_e_Arte.Application.Clients.Validators;

public class UpsertClientProfileValidator : AbstractValidator<UpsertClientProfileCommand>
{
    public UpsertClientProfileValidator()
    {
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.Request.MedicalNotes)
            .MaximumLength(4000)
            .When(x => x.Request.MedicalNotes is not null);
        RuleFor(x => x.Request.Allergies)
            .MaximumLength(1000)
            .When(x => x.Request.Allergies is not null);
        RuleFor(x => x.Request.DateOfBirth)
            .LessThan(DateOnly.FromDateTime(DateTime.UtcNow))
            .When(x => x.Request.DateOfBirth.HasValue);
    }
}
