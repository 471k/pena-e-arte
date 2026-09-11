using FluentValidation;
using Pena_e_Arte.Application.Payments.Queries;

namespace Pena_e_Arte.Application.Payments.Validators;

public class GetPaymentClientTokenValidator : AbstractValidator<GetPaymentClientTokenQuery>
{
    public GetPaymentClientTokenValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
