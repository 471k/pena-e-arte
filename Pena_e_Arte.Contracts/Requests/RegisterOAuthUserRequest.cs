namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// Sent by the frontend during studio registration when the owner chose OAuth
/// instead of email/password. The role is always "owner" for studio registration.
/// </summary>
public record RegisterOAuthUserRequest(
    string Provider,
    string IdToken,
    string Role,
    Guid StudioId);
