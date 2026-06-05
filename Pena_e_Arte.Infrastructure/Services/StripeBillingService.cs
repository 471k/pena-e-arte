using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

public class StripeBillingService(CustomerService customerService, SubscriptionService subscriptionService)
    : IStripeBillingService
{
    public async Task<string> CreateCustomerAsync(string email, CancellationToken ct)
    {
        CustomerCreateOptions options = new() { Email = email };
        Customer customer = await customerService.CreateAsync(options, null, ct);
        return customer.Id;
    }

    public async Task<(string SubscriptionId, DateTime CurrentPeriodEnd)> CreateSubscriptionAsync(
        string customerId, string priceId, CancellationToken ct)
    {
        SubscriptionCreateOptions options = new()
        {
            Customer = customerId,
            Items    = new List<SubscriptionItemOptions>
            {
                new() { Price = priceId }
            },
        };

        Stripe.Subscription sub = await subscriptionService.CreateAsync(options, null, ct);
        DateTime periodEnd = sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd
                             ?? DateTime.UtcNow.AddMonths(1);
        return (sub.Id, periodEnd);
    }
}
