using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPaymentCapabilitiesQuery : IRequest<PaymentCapabilitiesResponse>;

public class GetPaymentCapabilitiesHandler(IPaymentProvider paymentProvider)
    : IRequestHandler<GetPaymentCapabilitiesQuery, PaymentCapabilitiesResponse>
{
    public Task<PaymentCapabilitiesResponse> Handle(GetPaymentCapabilitiesQuery query, CancellationToken ct) =>
        Task.FromResult(new PaymentCapabilitiesResponse(
            CardPaymentsAvailable: paymentProvider.Capabilities.SupportsAuthCapture));
}
