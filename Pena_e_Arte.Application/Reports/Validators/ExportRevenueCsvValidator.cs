using FluentValidation;
using Pena_e_Arte.Application.Payments;
using Pena_e_Arte.Application.Reports.Queries;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Reports.Validators;

public class ExportRevenueCsvValidator : AbstractValidator<ExportRevenueCsvQuery>
{
    public ExportRevenueCsvValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From is not null && x.To is not null)
            .WithMessage("To must be on or after From.");
        RuleFor(x => x.Search).ValidSearch();
        // The export only ever holds money received, so a status it can't contain is refused rather than
        // producing an empty file that looks like "no payments".
        RuleFor(x => x.Status)
            .Must(s => s is null
                       || (PaymentFilters.TryParseStatus(s, out PaymentStatus st)
                           && st is PaymentStatus.Paid or PaymentStatus.Refunded))
            .WithMessage("The revenue export holds only Paid and Refunded payments, so Status must be Paid or Refunded.");
    }
}
