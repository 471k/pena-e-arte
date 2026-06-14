using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Aggregator model: all charges collected into the platform's own Stripe account.
/// No StripeAccount (connected account) header is sent.
/// </summary>
public class StripePaymentService(PaymentIntentService intentService, RefundService refundService)
    : IStripePaymentService
{
    public async Task<(string PaymentIntentId, string ClientSecret)> CreatePaymentIntentAsync(
        long amountInCents, string currency, Guid paymentId, CancellationToken ct)
    {
        PaymentIntentCreateOptions options = new()
        {
            Amount        = amountInCents,
            Currency      = currency.ToLowerInvariant(),
            CaptureMethod = "manual",
            Metadata      = new Dictionary<string, string> { { "payment_id", paymentId.ToString() } },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
        };

        // No RequestOptions with StripeAccount — platform account only (aggregator model)
        PaymentIntent intent = await intentService.CreateAsync(options, null, ct);
        return (intent.Id, intent.ClientSecret!);
    }

    public async Task CapturePaymentAsync(string paymentIntentId, CancellationToken ct)
    {
        await intentService.CaptureAsync(paymentIntentId, null, null, ct);
    }

    public async Task CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct)
    {
        await intentService.CancelAsync(paymentIntentId, null, null, ct);
    }

    public async Task<string?> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken ct)
    {
        try
        {
            PaymentIntent intent = await intentService.GetAsync(paymentIntentId, null, null, ct);
            return intent.Status;
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string> RefundPaymentIntentAsync(
        string paymentIntentId, long? amountInCents, CancellationToken ct)
    {
        RefundCreateOptions options = new()
        {
            PaymentIntent = paymentIntentId,
            Amount        = amountInCents,
        };

        Refund refund = await refundService.CreateAsync(options, null, ct);
        return refund.Id;
    }
}
