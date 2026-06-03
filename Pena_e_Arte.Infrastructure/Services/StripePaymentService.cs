using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

public class StripePaymentService(PaymentIntentService intentService, RefundService refundService)
    : IStripePaymentService
{
    public async Task<(string PaymentIntentId, string ClientSecret)> CreatePaymentIntentAsync(
        string            connectedAccountId,
        long              amountInCents,
        string            currency,
        Guid              paymentId,
        CancellationToken ct)
    {
        PaymentIntentCreateOptions options = new()
        {
            Amount        = amountInCents,
            Currency      = currency.ToLowerInvariant(),
            CaptureMethod = "manual",
            Metadata      = new Dictionary<string, string> { { "payment_id", paymentId.ToString() } },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true }
        };

        RequestOptions requestOptions = new() { StripeAccount = connectedAccountId };

        PaymentIntent intent = await intentService.CreateAsync(options, requestOptions, ct);
        return (intent.Id, intent.ClientSecret);
    }

    public async Task<string> RefundPaymentIntentAsync(
        string            paymentIntentId,
        string            connectedAccountId,
        long?             amountInCents,
        CancellationToken ct)
    {
        RefundCreateOptions options = new()
        {
            PaymentIntent = paymentIntentId,
            Amount        = amountInCents
        };

        RequestOptions requestOptions = new() { StripeAccount = connectedAccountId };

        Refund refund = await refundService.CreateAsync(options, requestOptions, ct);
        return refund.Id;
    }

    public async Task CapturePaymentAsync(
        string            paymentIntentId,
        string            connectedAccountId,
        CancellationToken ct)
    {
        RequestOptions requestOptions = new() { StripeAccount = connectedAccountId };
        await intentService.CaptureAsync(paymentIntentId, null, requestOptions, ct);
    }
}
