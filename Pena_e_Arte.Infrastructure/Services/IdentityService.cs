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
    IConfiguration            configuration) : IIdentityService
{
    public async Task<(bool Success, Guid UserId, string[] Errors)> CreateUserAsync(
        string email, string password, string role, Guid studioId, string? firstName = null)
    {
        IdentityUser user = new() { UserName = email, Email = email };
        IdentityResult result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
            return (false, Guid.Empty, result.Errors.Select(e => e.Description).ToArray());

        await userManager.AddToRoleAsync(user, role);
        await userManager.AddClaimAsync(user, new Claim("tenant_id", studioId.ToString()));
        if (firstName is not null)
            await userManager.AddClaimAsync(user, new Claim(JwtRegisteredClaimNames.GivenName, firstName));

        return (true, Guid.Parse(user.Id), []);
    }

    public async Task<(bool Success, string? Token, string? Error)> LoginAsync(string email, string password)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);
        if (user is null) return (false, null, "Invalid credentials.");

        bool valid = await userManager.CheckPasswordAsync(user, password);
        if (!valid) return (false, null, "Invalid credentials.");

        IList<string> roles      = await userManager.GetRolesAsync(user);
        IList<Claim>  userClaims = await userManager.GetClaimsAsync(user);

        return (true, GenerateJwt(user, roles, userClaims), null);
    }

    public async Task<(bool Success, string? Token, string? Error)> GeneratePasswordResetTokenAsync(string email)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return (true, null, null); // don't reveal user existence

        string token = await userManager.GeneratePasswordResetTokenAsync(user);
        return (true, token, null);
    }

    public async Task<(bool Success, string[] Errors)> ResetPasswordAsync(
        string email, string token, string newPassword)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return (false, ["Invalid reset request."]);

        IdentityResult result = await userManager.ResetPasswordAsync(user, token, newPassword);
        return result.Succeeded
            ? (true, [])
            : (false, result.Errors.Select(e => e.Description).ToArray());
    }

    public async Task<string> CreateRefreshTokenAsync(string email)
    {
        IdentityUser user = (await userManager.FindByEmailAsync(email))!;

        string randomPart   = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
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

        string newRandom       = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string newRefreshToken = $"{user.Id}.{newRandom}";
        await userManager.SetAuthenticationTokenAsync(user, "App", "RefreshToken", newRefreshToken);

        IList<string> roles      = await userManager.GetRolesAsync(user);
        IList<Claim>  userClaims = await userManager.GetClaimsAsync(user);
        string newAccessToken    = GenerateJwt(user, roles, userClaims);

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

    public async Task<(bool Success, string? AccessToken, string? Error)> LoginWithVerifiedEmailAsync(
        string email)
    {
        IdentityUser? user = await userManager.FindByEmailAsync(email);

        if (user is null)
            return (false, null, "No account found with this email. Please register first.");

        IList<string> roles      = await userManager.GetRolesAsync(user);
        IList<Claim>  userClaims = await userManager.GetClaimsAsync(user);

        return (true, GenerateJwt(user, roles, userClaims), null);
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
        if (firstName is not null)
            await userManager.AddClaimAsync(user, new Claim(JwtRegisteredClaimNames.GivenName, firstName));

        return (true, Guid.Parse(user.Id), []);
    }

    private string GenerateJwt(IdentityUser user, IList<string> roles, IList<Claim> userClaims)
    {
        string secretKey  = configuration["Jwt:SecretKey"]!;
        string issuer     = configuration["Jwt:Issuer"]!;
        string audience   = configuration["Jwt:Audience"]!;
        int    expiryMins = configuration.GetValue<int>("Jwt:AccessTokenExpiryMinutes");

        SymmetricSecurityKey key   = new(Encoding.UTF8.GetBytes(secretKey));
        SigningCredentials   creds = new(key, SecurityAlgorithms.HmacSha256);

        List<Claim> tokenClaims =
        [
            new(JwtRegisteredClaimNames.Sub,   user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        ];

        tokenClaims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        tokenClaims.AddRange(userClaims);

        JwtSecurityToken token = new(
            issuer:             issuer,
            audience:           audience,
            claims:             tokenClaims,
            expires:            DateTime.UtcNow.AddMinutes(expiryMins),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
