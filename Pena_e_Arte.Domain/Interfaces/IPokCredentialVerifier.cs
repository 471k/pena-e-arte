namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>Outcome of testing a studio's POK keys against POK before they are saved (C-7).</summary>
public enum PokCredentialCheckResult
{
    /// <summary>POK logged in with this Key ID + Key Secret.</summary>
    Valid,

    /// <summary>POK answered and refused the pair (wrong, revoked, or from the other POK environment).</summary>
    InvalidKeys,

    /// <summary>POK couldn't be asked (timeout, network error, 5xx, 429) — says nothing about the keys.</summary>
    PokUnreachable,
}

/// <summary>
/// Tests a POK Key ID + Key Secret by logging in once, without storing anything. Before C-7 a
/// wrong key was saved silently and only surfaced as a failed checkout for the studio's first client.
/// </summary>
public interface IPokCredentialVerifier
{
    Task<PokCredentialCheckResult> CheckKeysAsync(string keyId, string keySecret, CancellationToken ct);
}
