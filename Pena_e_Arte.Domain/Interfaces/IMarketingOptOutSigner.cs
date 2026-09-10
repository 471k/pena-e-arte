namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Signs the unsubscribe link embedded in every marketing campaign email so the anonymous
/// GET /api/v1/marketing/unsubscribe callback can trust it came from an email this API sent,
/// before trusting the clientId it carries. A new signer with its own key — deliberately NOT
/// ISocialOAuthStateSigner/IInstagramStateSigner reused, which are scoped to their own OAuth
/// flows — same "new signer per new anonymous-identity-proof use case" pattern this codebase
/// already established for those two.
/// </summary>
public interface IMarketingOptOutSigner
{
    string Sign(Guid clientId);

    bool TryValidate(string token, out Guid clientId);
}
