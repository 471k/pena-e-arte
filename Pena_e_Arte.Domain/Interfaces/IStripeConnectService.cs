namespace Pena_e_Arte.Domain.Interfaces;

public interface IStripeConnectService
{
    Task<string> CreateConnectedAccountAsync(string email, string country, CancellationToken ct);
    Task<string> CreateAccountLinkAsync(string accountId, string returnUrl, string refreshUrl, CancellationToken ct);
}
