namespace Pena_e_Arte.Domain.Interfaces;

public interface IIdentityService
{
    Task<(bool Success, Guid UserId, string[] Errors)> CreateUserAsync(string email, string password, string role, Guid studioId, string? firstName = null);
    Task<(bool Success, string? Token, string? Error)> LoginAsync(string email, string password);
    Task<(bool Success, string? Token, string? Error)> GeneratePasswordResetTokenAsync(string email);
    Task<(bool Success, string[] Errors)> ResetPasswordAsync(string email, string token, string newPassword);
    Task<string> CreateRefreshTokenAsync(string email);
    Task<(bool Success, string? AccessToken, string? RefreshToken, string? Error)> RefreshTokenAsync(string refreshToken);
    Task<(bool Success, string[] Errors)> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);
    Task<string> GenerateEmailConfirmationTokenAsync(Guid userId);
    Task<(bool Success, string[] Errors)> ConfirmEmailAsync(Guid userId, string token, CancellationToken ct);
    Task<bool>    IsEmailConfirmedAsync(Guid userId, CancellationToken ct);
    Task<string?> GetUserEmailAsync(Guid userId, CancellationToken ct);
    Task<Guid?>   GetUserIdByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// Issues a JWT for a user identified only by their verified email address.
    /// The caller is responsible for having already validated the OAuth ID token.
    /// Returns an error if no account exists with that email.
    /// </summary>
    Task<(bool Success, string? AccessToken, string? Error)> LoginWithVerifiedEmailAsync(string email);

    /// <summary>
    /// Creates an Identity user without a password (OAuth-only account).
    /// The caller must have already verified the user's email via an OAuth ID token.
    /// </summary>
    Task<(bool Success, Guid UserId, string[] Errors)> CreateOAuthUserAsync(
        string email, string role, Guid studioId, string? firstName);
}
