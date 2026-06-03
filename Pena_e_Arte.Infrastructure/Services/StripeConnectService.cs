using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

public class StripeConnectService(AccountService accountService, AccountLinkService accountLinkService)
    : IStripeConnectService
{
    public async Task<string> CreateConnectedAccountAsync(string email, string country, CancellationToken ct)
    {
        AccountCreateOptions options = new()
        {
            Type    = "express",
            Email   = email,
            Country = country.ToUpperInvariant(),
        };

        Account account = await accountService.CreateAsync(options, null, ct);
        return account.Id;
    }

    public async Task<string> CreateAccountLinkAsync(
        string accountId, string returnUrl, string refreshUrl, CancellationToken ct)
    {
        AccountLinkCreateOptions options = new()
        {
            Account    = accountId,
            ReturnUrl  = returnUrl,
            RefreshUrl = refreshUrl,
            Type       = "account_onboarding",
        };

        AccountLink link = await accountLinkService.CreateAsync(options, null, ct);
        return link.Url;
    }
}
