using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

[Collection("Database")]
public class IdentityServiceTests(DatabaseFixture fixture)
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"]               = "test-secret-key-must-be-at-least-32-chars!",
            ["Jwt:Issuer"]                  = "test-issuer",
            ["Jwt:Audience"]                = "test-audience",
            ["Jwt:AccessTokenExpiryMinutes"] = "15"
        })
        .Build();

    private IdentityService CreateSut(UserManager<IdentityUser> userManager) =>
        new(userManager, Config);

    private async Task<UserManager<IdentityUser>> BuildUserManagerAsync()
    {
        ServiceCollection services = new();

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 4, 0))));
        services.AddScoped<Microsoft.AspNetCore.Http.IHttpContextAccessor,
                           Microsoft.AspNetCore.Http.HttpContextAccessor>();
        services.AddScoped<Pena_e_Arte.Domain.Interfaces.ICurrentTenant, CurrentTenantService>();

        services.AddIdentityCore<IdentityUser>(options =>
            options.Password.RequireNonAlphanumeric = false)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        ServiceProvider sp = services.BuildServiceProvider();

        RoleManager<IdentityRole> roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (string role in new[] { "client", "artist", "owner", "issuer" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        return sp.GetRequiredService<UserManager<IdentityUser>>();
    }

    [Fact]
    public async Task CreateUserAsync_ValidCredentials_ReturnsSuccess()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService sut          = CreateSut(um);

        (bool success, Guid _, string[] errors) = await sut.CreateUserAsync(
            UniqueEmail(), "Password1!", "owner", Guid.NewGuid());

        success.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateEmail_ReturnsFalseWithErrors()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email = UniqueEmail();

        await sut.CreateUserAsync(email, "Password1!", "owner", Guid.NewGuid());
        (bool success, Guid _, string[] errors) = await sut.CreateUserAsync(email, "Password1!", "owner", Guid.NewGuid());

        success.Should().BeFalse();
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateUserAsync_WeakPassword_ReturnsFalseWithErrors()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);

        (bool success, Guid _, string[] errors) = await sut.CreateUserAsync(UniqueEmail(), "123", "owner", Guid.NewGuid());

        success.Should().BeFalse();
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateUserAsync_ValidUser_AssignsRoleCorrectly()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email = UniqueEmail();

        await sut.CreateUserAsync(email, "Password1!", "artist", Guid.NewGuid());

        IdentityUser? user = await um.FindByEmailAsync(email);
        IList<string> roles = await um.GetRolesAsync(user!);
        roles.Should().Contain("artist");
    }

    [Fact]
    public async Task CreateUserAsync_ValidUser_AddsTenantIdClaim()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email    = UniqueEmail();
        Guid   studioId = Guid.NewGuid();

        await sut.CreateUserAsync(email, "Password1!", "owner", studioId);

        IdentityUser?  user   = await um.FindByEmailAsync(email);
        IList<Claim>   claims = await um.GetClaimsAsync(user!);
        claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == studioId.ToString());
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsToken()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email = UniqueEmail();
        await sut.CreateUserAsync(email, "Password1!", "client", Guid.NewGuid());

        (bool success, string? token, string? error) = await sut.LoginAsync(email, "Password1!");

        success.Should().BeTrue();
        token.Should().NotBeNullOrEmpty();
        error.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ReturnsFalse()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email = UniqueEmail();
        await sut.CreateUserAsync(email, "Password1!", "client", Guid.NewGuid());

        (bool success, _, string? error) = await sut.LoginAsync(email, "WrongPassword!");

        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task LoginAsync_NonExistentUser_ReturnsFalse()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);

        (bool success, _, _) = await sut.LoginAsync("nobody@example.com", "Password1!");

        success.Should().BeFalse();
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_TokenContainsSubClaim()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email = UniqueEmail();
        await sut.CreateUserAsync(email, "Password1!", "owner", Guid.NewGuid());

        (_, string? token, _) = await sut.LoginAsync(email, "Password1!");

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_TokenContainsEmailClaim()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email = UniqueEmail();
        await sut.CreateUserAsync(email, "Password1!", "owner", Guid.NewGuid());

        (_, string? token, _) = await sut.LoginAsync(email, "Password1!");

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c =>
            c.Type == JwtRegisteredClaimNames.Email && c.Value == email);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_TokenContainsRoleClaim()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email = UniqueEmail();
        await sut.CreateUserAsync(email, "Password1!", "artist", Guid.NewGuid());

        (_, string? token, _) = await sut.LoginAsync(email, "Password1!");

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "artist");
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_TokenContainsTenantIdClaim()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email    = UniqueEmail();
        Guid   studioId = Guid.NewGuid();
        await sut.CreateUserAsync(email, "Password1!", "owner", studioId);

        (_, string? token, _) = await sut.LoginAsync(email, "Password1!");

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().Contain(c =>
            c.Type == "tenant_id" && c.Value == studioId.ToString());
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_TokenExpiryIsCorrect()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email = UniqueEmail();
        await sut.CreateUserAsync(email, "Password1!", "client", Guid.NewGuid());

        (_, string? token, _) = await sut.LoginAsync(email, "Password1!");

        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(15), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task CreateUserAsync_ValidUser_SetsActiveTenantIdToken()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email    = UniqueEmail();
        Guid   studioId = Guid.NewGuid();

        await sut.CreateUserAsync(email, "Password1!", "client", studioId);

        IdentityUser user   = (await um.FindByEmailAsync(email))!;
        string? stored = await um.GetAuthenticationTokenAsync(user, "App", "ActiveTenantId");
        stored.Should().Be(studioId.ToString());
    }

    [Fact]
    public async Task IssueTokensForTenantAsync_UserWithTwoStudioClaims_TokenContainsOnlyActiveOne()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email  = UniqueEmail();
        Guid   studioA = Guid.NewGuid();
        Guid   studioB = Guid.NewGuid();

        await sut.CreateUserAsync(email, "Password1!", "client", studioA);
        IdentityUser user   = (await um.FindByEmailAsync(email))!;
        Guid         userId = Guid.Parse(user.Id);
        await sut.EnsureTenantClaimAsync(userId, studioB, default);

        (bool success, string? accessToken, string? refreshToken, string? error) =
            await sut.IssueTokensForTenantAsync(userId, studioB, default);

        success.Should().BeTrue();
        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Claims.Where(c => c.Type == "tenant_id").Should().ContainSingle()
            .Which.Value.Should().Be(studioB.ToString());
    }

    [Fact]
    public async Task RefreshTokenAsync_AfterSwitchingStudio_PreservesActiveStudio()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email  = UniqueEmail();
        Guid   studioA = Guid.NewGuid();
        Guid   studioB = Guid.NewGuid();

        await sut.CreateUserAsync(email, "Password1!", "client", studioA);
        IdentityUser user   = (await um.FindByEmailAsync(email))!;
        Guid         userId = Guid.Parse(user.Id);
        await sut.EnsureTenantClaimAsync(userId, studioB, default);
        (_, _, string? refreshToken, _) = await sut.IssueTokensForTenantAsync(userId, studioB, default);

        (bool success, string? newAccessToken, _, _) = await sut.RefreshTokenAsync(refreshToken!);

        success.Should().BeTrue();
        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(newAccessToken);
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == studioB.ToString());
    }

    [Fact]
    public async Task LoginAsync_LegacyUserWithNoActiveTenantToken_StillGetsSingleTenantClaim()
    {
        // Regression: accounts created before "ActiveTenantId" tracking existed must be
        // completely unaffected — GenerateJwt falls back to the first stored claim.
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email    = UniqueEmail();
        Guid   studioId = Guid.NewGuid();
        IdentityUser user = new() { UserName = email, Email = email };
        await um.CreateAsync(user, "Password1!");
        await um.AddToRoleAsync(user, "owner");
        await um.AddClaimAsync(user, new Claim("tenant_id", studioId.ToString()));
        // Deliberately no "ActiveTenantId" auth token — simulates a pre-migration account.

        (bool success, string? token, _) = await sut.LoginAsync(email, "Password1!");

        success.Should().BeTrue();
        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().ContainSingle(c => c.Type == "tenant_id")
            .Which.Value.Should().Be(studioId.ToString());
    }

    [Fact]
    public async Task EnsureTenantClaimAsync_CalledTwiceForSameStudio_DoesNotDuplicateClaim()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email    = UniqueEmail();
        Guid   studioId = Guid.NewGuid();
        await sut.CreateUserAsync(email, "Password1!", "client", studioId);
        IdentityUser user   = (await um.FindByEmailAsync(email))!;
        Guid         userId = Guid.Parse(user.Id);

        await sut.EnsureTenantClaimAsync(userId, studioId, default);
        await sut.EnsureTenantClaimAsync(userId, studioId, default);

        IList<Claim> claims = await um.GetClaimsAsync(user);
        claims.Count(c => c.Type == "tenant_id" && c.Value == studioId.ToString()).Should().Be(1);
    }

    [Fact]
    public async Task GetTenantIdsAsync_UserWithTwoStudios_ReturnsBoth()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);
        string email  = UniqueEmail();
        Guid   studioA = Guid.NewGuid();
        Guid   studioB = Guid.NewGuid();
        await sut.CreateUserAsync(email, "Password1!", "client", studioA);
        IdentityUser user   = (await um.FindByEmailAsync(email))!;
        Guid         userId = Guid.Parse(user.Id);
        await sut.EnsureTenantClaimAsync(userId, studioB, default);

        IReadOnlyList<Guid> tenantIds = await sut.GetTenantIdsAsync(userId, default);

        tenantIds.Should().BeEquivalentTo([studioA, studioB]);
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@test.com";
}
