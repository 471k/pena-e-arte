namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// Sent by the frontend after receiving a Google or Apple ID token.
/// The provider field is "google" or "apple" (lowercase).
/// </summary>
public record OAuthLoginRequest(string Provider, string IdToken);
