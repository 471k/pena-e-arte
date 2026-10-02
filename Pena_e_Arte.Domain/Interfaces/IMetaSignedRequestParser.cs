namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Verifies the <c>signed_request</c> Meta posts to an app's Deauthorize and Data Deletion
/// callbacks. Those calls carry no JWT and no OAuth <c>state</c>; the HMAC-SHA256 signature
/// (keyed with the app secret) is the only proof the call really came from Meta.
/// </summary>
public interface IMetaSignedRequestParser
{
    /// <summary>
    /// Returns true and the Instagram user id only when the signature is valid. Malformed input,
    /// a bad signature, an unsupported algorithm, a missing <c>user_id</c> and a missing app
    /// secret all return false — the caller must never act on an unverified request.
    /// </summary>
    bool TryGetUserId(string signedRequest, out string userId);
}
