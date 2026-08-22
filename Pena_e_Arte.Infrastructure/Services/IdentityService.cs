using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

public class IdentityService(
    UserManager<IdentityUser> userManager,
    IConfiguration configuration) : IIdentityService
{
    public async Task<(bool Success, Guid UserId, string[] Errors)> CreateUserAsync(
        string email, string password, string role, Guid? studioId, string? firstName = null)
    {
        IdentityUser user = new() { UserName = email, Email = email };
        IdentityResult result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
            return (false, Guid.Empty, result.Errors.Select(e => e.Description).ToArray());

        await userManager.AddToRoleAsync(user, role);
        if (studioId is not null)
        {
            await userManager.AddClaimAsync(user, new Claim("tenant_id", studioId.Value.ToString()));
            await userManager.SetAuthenticationTokenAsync(user, "App", "ActiveTenantId", studioId.Value.ToString());
        }
        if (firstName is not null)
            await userManager.AddClaimAsync(user, new Claim(JwtRegisteredClaimNames.GivenName, firstName));

        return (true, Guid.Parse(user.Id), []);
    }

    public async Task<(bool Success, string? Token, string? Error)> LoginAsync(string email, string password)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);
        if (user is null) return (false, null, "Invalid credentials.");

        // A locked-out account (e.g. after account erasure) can never log in. CheckPasswordAsync
        // alone does NOT enforce lockout, so this check is required.
        if (await userManager.IsLockedOutAsync(user)) return (false, null, "Invalid credentials.");

        bool valid = await userManager.CheckPasswordAsync(user, password);
        if (!valid) return (false, null, "Invalid credentials.");

        IList<string> roles = await userManager.GetRolesAsync(user);
        IList<Claim> userClaims = await userManager.GetClaimsAsync(user);
        Guid? activeTenantId = await ReadActiveTenantIdAsync(user);

        return (true, GenerateJwt(user, roles, userClaims, activeTenantId), null);
    }

    public async Task DisableLoginAsync(Guid userId, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return;

        await userManager.SetLockoutEnabledAsync(user, true);
        // Far-future but within MySQL's datetime range (DateTimeOffset.MaxValue can overflow it).
        await userManager.SetLockoutEndDateAsync(user, new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero));
        // Kill any existing refresh token so an in-flight session can't refresh past the lockout.
        await userManager.RemoveAuthenticationTokenAsync(user, "App", "RefreshToken");
    }

    public async Task DeleteUserAsync(Guid userId, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return;
        await userManager.DeleteAsync(user);
    }

    public async Task<(bool Success, string? Token, string? Error)> GeneratePasswordResetTokenAsync(string email)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return (true, null, null); // don't reveal user existence

        string token = await userManager.GeneratePasswordResetTokenAsync(user);
        return (true, token, null);
    }

    public async Task<(bool Success, string[] Errors, bool TokenInvalid)> ResetPasswordAsync(
        string email, string token, string newPassword)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return (false, ["Invalid reset request."], true);

        IdentityResult result = await userManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
            return (true, [], false);

        bool tokenInvalid = result.Errors.Any(e => e.Code == "InvalidToken");
        return (false, result.Errors.Select(e => e.Description).ToArray(), tokenInvalid);
    }

    public async Task<string> CreateRefreshTokenAsync(string email)
    {
        IdentityUser user = (await userManager.FindByEmailAsync(email))!;

        string randomPart = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string refreshToken = $"{user.Id}.{randomPart}";

        await userManager.SetAuthenticationTokenAsync(user, "App", "RefreshToken", refreshToken);
        return refreshToken;
    }

    public async Task<(bool Success, string? AccessToken, string? RefreshToken, string? Error)> RefreshTokenAsync(
        string refreshToken)
    {
        int dotIndex = refreshToken.IndexOf('.');
        if (dotIndex < 0) return (false, null, null, "Invalid refresh token.");

        string userId = refreshToken[..dotIndex];
        IdentityUser? user = await userManager.FindByIdAsync(userId);
        if (user is null) return (false, null, null, "Invalid refresh token.");

        string? stored = await userManager.GetAuthenticationTokenAsync(user, "App", "RefreshToken");
        if (stored != refreshToken) return (false, null, null, "Invalid refresh token.");

        string newRandom = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string newRefreshToken = $"{user.Id}.{newRandom}";
        await userManager.SetAuthenticationTokenAsync(user, "App", "RefreshToken", newRefreshToken);

        IList<string> roles = await userManager.GetRolesAsync(user);
        IList<Claim> userClaims = await userManager.GetClaimsAsync(user);
        Guid? activeTenantId = await ReadActiveTenantIdAsync(user);
        string newAccessToken = GenerateJwt(user, roles, userClaims, activeTenantId);

        return (true, newAccessToken, newRefreshToken, null);
    }

    public async Task<(bool Success, string[] Errors)> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return (false, ["User not found."]);

        IdentityResult result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
        return result.Succeeded
            ? (true, [])
            : (false, result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(Guid userId)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} not found.");
        return await userManager.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task<(bool Success, string[] Errors)> ConfirmEmailAsync(
        Guid userId, string token, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return (false, ["Invalid confirmation request."]);

        IdentityResult result = await userManager.ConfirmEmailAsync(user, token);
        return result.Succeeded
            ? (true, [])
            : (false, result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<bool> IsEmailConfirmedAsync(Guid userId, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        return user?.EmailConfirmed ?? false;
    }

    public async Task<string?> GetUserEmailAsync(Guid userId, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        return user?.Email;
    }

    public async Task<Guid?> GetUserIdByEmailAsync(string email, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);
        if (user is null) return null;
        return Guid.TryParse(user.Id, out Guid id) ? id : null;
    }

    public async Task<IReadOnlyList<string>> GetUserRolesAsync(Guid userId, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return [];
        // GetRolesAsync returns IList<string>, which does not implicitly convert to
        // IReadOnlyList<string> despite List<T> satisfying both at runtime.
        IList<string> roles = await userManager.GetRolesAsync(user);
        return roles.ToList();
    }

    public async Task<string?> GetUserDisplayNameAsync(string email, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);
        if (user is null) return null;

        IList<Claim> claims = await userManager.GetClaimsAsync(user);
        return claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.GivenName)?.Value;
    }

    public async Task<(bool Success, string? AccessToken, string? Error)> LoginWithVerifiedEmailAsync(
        string email)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);

        if (user is null)
            return (false, null, "No account found with this email. Please register first.");

        // A locked-out account (e.g. after erasure) can't log in via OAuth either.
        if (await userManager.IsLockedOutAsync(user))
            return (false, null, "No account found with this email. Please register first.");

        IList<string> roles = await userManager.GetRolesAsync(user);
        IList<Claim> userClaims = await userManager.GetClaimsAsync(user);
        Guid? activeTenantId = await ReadActiveTenantIdAsync(user);

        return (true, GenerateJwt(user, roles, userClaims, activeTenantId), null);
    }

    public async Task<(bool Success, Guid UserId, string[] Errors)> CreateOAuthUserAsync(
        string email, string role, Guid studioId, string? firstName)
    {
        // Create the Identity user with no password — they will always sign in via OAuth.
        IdentityUser user = new() { UserName = email, Email = email, EmailConfirmed = true };
        IdentityResult result = await userManager.CreateAsync(user);

        if (!result.Succeeded)
            return (false, Guid.Empty, result.Errors.Select(e => e.Description).ToArray());

        await userManager.AddToRoleAsync(user, role);
        await userManager.AddClaimAsync(user, new Claim("tenant_id", studioId.ToString()));
        await userManager.SetAuthenticationTokenAsync(user, "App", "ActiveTenantId", studioId.ToString());
        if (firstName is not null)
            await userManager.AddClaimAsync(user, new Claim(JwtRegisteredClaimNames.GivenName, firstName));

        return (true, Guid.Parse(user.Id), []);
    }

    public async Task<IReadOnlyList<Guid>> GetTenantIdsAsync(Guid userId, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return [];

        IList<Claim> claims = await userManager.GetClaimsAsync(user);
        return claims.Where(c => c.Type == "tenant_id")
                     .Select(c => Guid.Parse(c.Value))
                     .Distinct()
                     .ToList();
    }

    public async Task<Guid?> GetActiveTenantIdAsync(Guid userId, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await ReadActiveTenantIdAsync(user);
    }

    public async Task EnsureTenantClaimAsync(Guid userId, Guid studioId, CancellationToken ct)
    {
        IdentityUser user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} not found.");

        IList<Claim> claims = await userManager.GetClaimsAsync(user);
        bool alreadyMember = claims.Any(c => c.Type == "tenant_id" && c.Value == studioId.ToString());
        if (!alreadyMember)
            await userManager.AddClaimAsync(user, new Claim("tenant_id", studioId.ToString()));
    }

    public async Task<(bool Success, string? AccessToken, string? RefreshToken, string? Error)> IssueTokensForTenantAsync(
        Guid userId, Guid activeStudioId, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return (false, null, null, "User not found.");

        await userManager.SetAuthenticationTokenAsync(user, "App", "ActiveTenantId", activeStudioId.ToString());

        string newRandom = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string newRefreshToken = $"{user.Id}.{newRandom}";
        await userManager.SetAuthenticationTokenAsync(user, "App", "RefreshToken", newRefreshToken);

        IList<string> roles = await userManager.GetRolesAsync(user);
        IList<Claim> userClaims = await userManager.GetClaimsAsync(user);
        string accessToken = GenerateJwt(user, roles, userClaims, activeStudioId);

        return (true, accessToken, newRefreshToken, null);
    }

    public async Task RemoveTenantClaimAsync(Guid userId, Guid studioId, CancellationToken ct)
    {
        IdentityUser user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} not found.");

        IList<Claim> claims = await userManager.GetClaimsAsync(user);
        Claim? tenantClaim = claims.FirstOrDefault(c => c.Type == "tenant_id" && c.Value == studioId.ToString());
        if (tenantClaim is not null)
            await userManager.RemoveClaimAsync(user, tenantClaim);

        Guid? activeTenantId = await ReadActiveTenantIdAsync(user);
        if (activeTenantId == studioId)
            await userManager.RemoveAuthenticationTokenAsync(user, "App", "ActiveTenantId");
    }

    public async Task<(bool Success, string? Token, string[] Errors, bool EmailTaken)> GenerateChangeEmailTokenAsync(
        Guid userId, string currentPassword, string newEmail, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return (false, null, ["User not found."], false);

        bool passwordValid = await userManager.CheckPasswordAsync(user, currentPassword);
        if (!passwordValid) return (false, null, ["Incorrect password."], false);

        if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
            return (false, null, ["New email must be different from your current email."], false);

        IdentityUser? existing = await userManager.FindByEmailAsync(newEmail);
        if (existing is not null) return (false, null, ["That email is already in use."], true);

        string token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        return (true, token, [], false);
    }

    public async Task<(bool Success, string[] Errors, bool TokenInvalid, bool EmailTaken)> ConfirmChangeEmailAsync(
        Guid userId, string newEmail, string token, CancellationToken ct)
    {
        IdentityUser? user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return (false, ["Invalid confirmation request."], true, false);

        // Re-checked here (not just at request time) to close the race where someone else
        // claims the address between the request and this confirmation.
        IdentityUser? existing = await userManager.FindByEmailAsync(newEmail);
        if (existing is not null && existing.Id != user.Id)
            return (false, ["That email is already in use."], false, true);

        IdentityResult result = await userManager.ChangeEmailAsync(user, newEmail, token);
        if (!result.Succeeded)
        {
            bool tokenInvalid = result.Errors.Any(e => e.Code == "InvalidToken");
            return (false, result.Errors.Select(e => e.Description).ToArray(), tokenInvalid, false);
        }

        await userManager.SetUserNameAsync(user, newEmail);

        return (true, [], false, false);
    }

    private async Task<Guid?> ReadActiveTenantIdAsync(IdentityUser user)
    {
        string? stored = await userManager.GetAuthenticationTokenAsync(user, "App", "ActiveTenantId");
        return Guid.TryParse(stored, out Guid id) ? id : null;
    }

    private string GenerateJwt(
        IdentityUser user, IList<string> roles, IList<Claim> userClaims, Guid? activeStudioId = null)
    {
        string secretKey = configuration["Jwt:SecretKey"]!;
        string issuer = configuration["Jwt:Issuer"]!;
        string audience = configuration["Jwt:Audience"]!;
        int expiryMins = configuration.GetValue<int>("Jwt:AccessTokenExpiryMinutes");

        SymmetricSecurityKey key = new(Encoding.UTF8.GetBytes(secretKey));
        SigningCredentials creds = new(key, SecurityAlgorithms.HmacSha256);

        List<Claim> tokenClaims =
        [
            new(JwtRegisteredClaimNames.Sub,   user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new("email_verified", user.EmailConfirmed ? "true" : "false"),
        ];

        tokenClaims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        tokenClaims.AddRange(userClaims.Where(c => c.Type != "tenant_id"));

        // A user may hold a "tenant_id" claim for every studio they belong to, but the
        // token must carry exactly one — the caller-selected active studio if given,
        // else whichever the user was first granted (preserves today's behavior for
        // every single-studio account: artist/owner/issuer, and clients pre-dating
        // multi-studio support).
        string? activeTenantId = activeStudioId?.ToString()
            ?? userClaims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
        if (activeTenantId is not null)
            tokenClaims.Add(new Claim("tenant_id", activeTenantId));

        JwtSecurityToken token = new(
            issuer: issuer,
            audience: audience,
            claims: tokenClaims,
            expires: DateTime.UtcNow.AddMinutes(expiryMins),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
