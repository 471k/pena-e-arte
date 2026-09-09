using FluentValidation;
using Pena_e_Arte.Application.Reports.Queries;

namespace Pena_e_Arte.Application.Reports.Validators;

public class ExportRevenueCsvValidator : AbstractValidator<ExportRevenueCsvQuery>
{
    public ExportRevenueCsvValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From is not null && x.To is not null)
            .WithMessage("To must be on or after From.");
    }
}
