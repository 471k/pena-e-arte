namespace Pena_e_Arte.Domain.Interfaces;

public interface IIdentityService
{
    /// <summary>
    /// Creates an Identity user. <paramref name="studioId"/> is null for a studio-less
    /// client registration — no tenant_id claim or active-tenant token is set in that
    /// case, and the caller must not create a linked Client row either.
    /// </summary>
    Task<(bool Success, Guid UserId, string[] Errors)> CreateUserAsync(string email, string password, string role, Guid? studioId, string? firstName = null);
    Task<(bool Success, string? Token, string? Error)> LoginAsync(string email, string password);
    Task<(bool Success, string? Token, string? Error)> GeneratePasswordResetTokenAsync(string email);
    /// <summary>
    /// <paramref name="TokenInvalid"/> is true when the failure is due to the reset
    /// token itself (missing user, malformed, or expired) rather than the new
    /// password failing policy — ASP.NET Core Identity's token provider does not
    /// distinguish "expired" from "malformed", so both surface the same flag.
    /// </summary>
    Task<(bool Success, string[] Errors, bool TokenInvalid)> ResetPasswordAsync(string email, string token, string newPassword);
    Task<string> CreateRefreshTokenAsync(string email);
    Task<(bool Success, string? AccessToken, string? RefreshToken, string? Error)> RefreshTokenAsync(string refreshToken);
    Task<(bool Success, string[] Errors)> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct);
    Task<string> GenerateEmailConfirmationTokenAsync(Guid userId);
    Task<(bool Success, string[] Errors)> ConfirmEmailAsync(Guid userId, string token, CancellationToken ct);
    Task<bool> IsEmailConfirmedAsync(Guid userId, CancellationToken ct);
    Task<string?> GetUserEmailAsync(Guid userId, CancellationToken ct);
    Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken ct);

    /// <summary>
    /// Returns the user's given-name Identity claim (set at registration when provided),
    /// or null if no account exists for the email or no such claim was ever set.
    /// </summary>
    Task<string?> GetUserDisplayNameAsync(string email, CancellationToken ct);

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

    /// <summary>
    /// Removes the user's "tenant_id" claim for the given studio.
    /// Also clears the active-tenant token if it matches the removed studio.
    /// Idempotent — safe to call even if the user no longer holds that claim.
    /// </summary>
    Task RemoveTenantClaimAsync(Guid userId, Guid studioId, CancellationToken ct);

    /// <summary>
    /// Verifies <paramref name="currentPassword"/> and that <paramref name="newEmail"/> is
    /// not already in use, then issues a change-email token. The token must only ever be
    /// delivered to the NEW address — it is the proof that the account owner controls it,
    /// not just that they were logged in when the request was made.
    /// </summary>
    Task<(bool Success, string? Token, string[] Errors, bool EmailTaken)> GenerateChangeEmailTokenAsync(
        Guid userId, string currentPassword, string newEmail, CancellationToken ct);

    /// <summary>
    /// <paramref name="TokenInvalid"/> mirrors <see cref="ResetPasswordAsync"/> — true when
    /// the failure is the token itself (missing user, malformed, expired) rather than the
    /// new email having been claimed by someone else since the request was made
    /// (<paramref name="EmailTaken"/>). On success, also updates the username to match the
    /// new email — this app treats username and email as always identical (see
    /// <see cref="CreateUserAsync"/>) — so sign-in and every email lookup stays consistent.
    /// </summary>
    Task<(bool Success, string[] Errors, bool TokenInvalid, bool EmailTaken)> ConfirmChangeEmailAsync(
        Guid userId, string newEmail, string token, CancellationToken ct);
}
