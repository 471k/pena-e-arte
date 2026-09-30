namespace Pena_e_Arte.Contracts.Requests;

public record RegisterSoloArtistRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    /// <summary>ISO 3166-1 alpha-2. Optional for backward compat with old clients — the handler
    /// defaults to "AL"/"ALL" and logs when omitted.</summary>
    string? CountryCode = null,
    /// <summary>ISO 4217. Null defaults to CountryCode's currency.</summary>
    string? Currency = null);
