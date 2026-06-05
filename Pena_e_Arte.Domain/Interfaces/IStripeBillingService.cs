namespace Pena_e_Arte.Domain.Interfaces;

public interface IStripeBillingService
{
    Task<string> CreateCustomerAsync(string email, CancellationToken ct);

    Task<(string SubscriptionId, DateTime CurrentPeriodEnd)> CreateSubscriptionAsync(
        string customerId, string priceId, CancellationToken ct);
}
