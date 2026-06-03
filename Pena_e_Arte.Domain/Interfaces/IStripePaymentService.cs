namespace Pena_e_Arte.Domain.Interfaces;

public interface IStripePaymentService
{
    Task<(string PaymentIntentId, string ClientSecret)> CreatePaymentIntentAsync(
        string            connectedAccountId,
        long              amountInCents,
        string            currency,
        Guid              paymentId,
        CancellationToken ct);

    Task<string> RefundPaymentIntentAsync(
        string            paymentIntentId,
        string            connectedAccountId,
        long?             amountInCents,
        CancellationToken ct);

    Task CapturePaymentAsync(
        string            paymentIntentId,
        string            connectedAccountId,
        CancellationToken ct);
}
