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

        (bool success, string[] errors) = await sut.CreateUserAsync(
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
        (bool success, string[] errors) = await sut.CreateUserAsync(email, "Password1!", "owner", Guid.NewGuid());

        success.Should().BeFalse();
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateUserAsync_WeakPassword_ReturnsFalseWithErrors()
    {
        UserManager<IdentityUser> um = await BuildUserManagerAsync();
        IdentityService           sut = CreateSut(um);

        (bool success, string[] errors) = await sut.CreateUserAsync(UniqueEmail(), "123", "owner", Guid.NewGuid());

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

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@test.com";
}
