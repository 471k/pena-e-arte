namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>Outcome of testing a studio's POK keys and merchant id against POK before they are saved (C-7).</summary>
public enum PokCredentialCheckResult
{
    /// <summary>POK logged in with this Key ID + Key Secret, and the merchant id belongs to them.</summary>
    Valid,

    /// <summary>POK answered and refused the pair (wrong, revoked, or from the other POK environment).</summary>
    InvalidKeys,

    /// <summary>The keys are valid, but POK forbids them for this merchant id (it belongs to another account or isn't one).</summary>
    MerchantMismatch,

    /// <summary>POK couldn't be asked (timeout, network error, 5xx, 429) — says nothing about the credentials.</summary>
    PokUnreachable,
}

/// <summary>
/// Tests a POK Key ID + Key Secret by logging in once, then checks the merchant id with one read under
/// it, without storing anything. Before C-7 wrong values were saved silently and only surfaced as a
/// failed checkout for the studio's first client.
/// </summary>
public interface IPokCredentialVerifier
{
    Task<PokCredentialCheckResult> CheckAsync(string keyId, string keySecret, string merchantId, CancellationToken ct);
}
