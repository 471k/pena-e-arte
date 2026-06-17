using FluentValidation;
using Pena_e_Arte.Application.Payments.Queries;

namespace Pena_e_Arte.Application.Payments.Validators;

public class GetPaymentInvoiceValidator : AbstractValidator<GetPaymentInvoiceQuery>
{
    public GetPaymentInvoiceValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
