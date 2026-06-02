using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
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
    public async Task<(bool Success, string[] Errors)> CreateUserAsync(
        string email, string password, string role, Guid studioId)
    {
        IdentityUser user = new() { UserName = email, Email = email };
        IdentityResult result = await userManager.CreateAsync(user, password);

        if (!result.Succeeded)
            return (false, result.Errors.Select(e => e.Description).ToArray());

        await userManager.AddToRoleAsync(user, role);
        await userManager.AddClaimAsync(user, new Claim("tenant_id", studioId.ToString()));

        return (true, []);
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
