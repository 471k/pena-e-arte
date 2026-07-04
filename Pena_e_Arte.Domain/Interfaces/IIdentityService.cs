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

    /// <summary>
    /// Returns every studio a client has a "tenant_id" claim for (i.e. every studio
    /// they've joined). Non-client roles will only ever have one.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetTenantIdsAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Returns the studio currently marked as active for this user (the one that will
    /// be embedded as the single "tenant_id" claim on their next issued token).
    /// </summary>
    Task<Guid?> GetActiveTenantIdAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Ensures the user holds a "tenant_id" claim for the given studio. Idempotent —
    /// safe to call even if the user already belongs to that studio.
    /// </summary>
    Task EnsureTenantClaimAsync(Guid userId, Guid studioId, CancellationToken ct);

    /// <summary>
    /// Marks the given studio as active and reissues access + refresh tokens scoped to
    /// it. The caller must have already ensured the user holds a "tenant_id" claim for
    /// this studio (see <see cref="EnsureTenantClaimAsync"/>).
    /// </summary>
    Task<(bool Success, string? AccessToken, string? RefreshToken, string? Error)> IssueTokensForTenantAsync(
        Guid userId, Guid activeStudioId, CancellationToken ct);
}
