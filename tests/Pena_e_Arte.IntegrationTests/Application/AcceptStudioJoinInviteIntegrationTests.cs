using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Application.Studios.StudioJoinInvites;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

// Full round trip through the real Identity/UserManager stack and a real MySQL database —
// proves the single most important correctness property in the solo-artist-join-invite
// feature: after acceptance, the old solo studio is truly inaccessible (role swapped away
// from owner, tenant_id claim removed, a fresh login can no longer resolve to it).
[Collection("Database")]
public class AcceptStudioJoinInviteIntegrationTests(DatabaseFixture fixture)
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "test-secret-key-must-be-at-least-32-chars!",
            ["Jwt:Admin"] = "test-admin",
            ["Jwt:Audience"] = "test-audience",
            ["Jwt:AccessTokenExpiryMinutes"] = "15",
        })
        .Build();

    private async Task<IdentityService> BuildIdentityAsync()
    {
        ServiceCollection services = new();
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))));
        services.AddScoped<Microsoft.AspNetCore.Http.IHttpContextAccessor,
                           Microsoft.AspNetCore.Http.HttpContextAccessor>();
        services.AddScoped<ICurrentTenant, CurrentTenantService>();

        services.AddIdentityCore<IdentityUser>(options => options.Password.RequireNonAlphanumeric = false)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        ServiceProvider sp = services.BuildServiceProvider();
        RoleManager<IdentityRole> roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (string role in new[] { "owner", "artist" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        UserManager<IdentityUser> um = sp.GetRequiredService<UserManager<IdentityUser>>();
        return new IdentityService(um, Config);
    }

    [Fact]
    public async Task AcceptInvite_HappyPath_OldStudioTokensAreTrulyRejectedAfterward()
    {
        Guid oldStudioId = Guid.NewGuid();
        Guid newStudioId = Guid.NewGuid();
        string email = UniqueEmail();

        IdentityService identity = await BuildIdentityAsync();

        await using AppDbContext seedDb = fixture.CreateDbContext(Guid.Empty);
        seedDb.Studios.Add(new Studio
        {
            Id = oldStudioId,
            Name = "Jane Doe",
            Slug = $"jane-{Guid.NewGuid():N}",
            City = string.Empty,
            OwnerEmail = email,
            IsSolo = true,
            IsActive = true,
        });
        seedDb.Studios.Add(new Studio
        {
            Id = newStudioId,
            Name = "Ink Collective",
            Slug = $"ink-{Guid.NewGuid():N}",
            City = "Lisbon",
        });
        Plan plan = new() { Name = $"Starter-{Guid.NewGuid():N}", MaxArtists = 2 };
        seedDb.Plans.Add(plan);
        seedDb.Subscriptions.Add(new Subscription
        {
            StudioId = newStudioId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            GracePeriodEnd = DateTime.UtcNow.AddDays(37),
        });
        StudioJoinInvite invite = new()
        {
            StudioId = newStudioId,
            InvitedEmail = email,
            FirstName = "Jane",
            LastName = "Doe",
            Status = StudioJoinInviteStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        seedDb.StudioJoinInvites.Add(invite);
        await seedDb.SaveChangesAsync();

        (bool created, Guid userId, _) =
            await identity.CreateUserAsync(email, "Password1!", "owner", oldStudioId, "Jane");
        created.Should().BeTrue();

        await using AppDbContext handlerDb = fixture.CreateDbContext(Guid.Empty);
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        StubCurrentTenant stubTenant = new(Guid.NewGuid()); // deliberately unrelated — never consulted
        PlanLimitService planLimits = new(handlerDb, stubTenant, cache, NullLogger<PlanLimitService>.Instance);
        StubCurrentUser currentUser = new(userId, "owner", email);

        AcceptStudioJoinInviteHandler handler = new(
            handlerDb, currentUser, identity, planLimits, NullLogger<AcceptStudioJoinInviteHandler>.Instance);

        AuthResponse response = await handler.Handle(new AcceptStudioJoinInviteCommand(invite.Id), default);

        // 1. The returned tokens are scoped to the NEW studio only, with the new "artist" role.
        JwtSecurityToken jwt = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        jwt.Claims.Where(c => c.Type == "tenant_id").Should().ContainSingle()
            .Which.Value.Should().Be(newStudioId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "artist");
        jwt.Claims.Should().NotContain(c => c.Type == ClaimTypes.Role && c.Value == "owner");

        // 2. The old studio is soft-closed, data retained.
        await using AppDbContext verifyDb = fixture.CreateDbContext(Guid.Empty);
        Studio oldStudio = await verifyDb.Studios.SingleAsync(s => s.Id == oldStudioId);
        oldStudio.IsActive.Should().BeFalse();
        oldStudio.ClosedAt.Should().NotBeNull();

        // 3. The new Artist row exists at the new studio for this user.
        await using AppDbContext verifyArtistDb = fixture.CreateDbContext(newStudioId);
        Artist artist = await verifyArtistDb.Artists.SingleAsync(a => a.UserId == userId);
        artist.StudioId.Should().Be(newStudioId);

        // 4. The invite is marked Accepted.
        (await verifyDb.StudioJoinInvites.SingleAsync(i => i.Id == invite.Id))
            .Status.Should().Be(StudioJoinInviteStatus.Accepted);

        // 5. The Identity layer no longer holds a tenant_id claim for the old studio at all —
        // this is the actual mechanism that makes the old studio truly inaccessible, not just
        // the one token returned above.
        IReadOnlyList<Guid> tenantIds = await identity.GetTenantIdsAsync(userId, default);
        tenantIds.Should().NotContain(oldStudioId);
        tenantIds.Should().ContainSingle().Which.Should().Be(newStudioId);

        // 6. THE critical proof: even a brand-new login session — not just the tokens returned
        // by Accept itself — can never resolve back to the old studio, and no longer carries
        // the owner role that would have unlocked owner-only endpoints there.
        (bool loginSuccess, string? freshToken, _) = await identity.LoginAsync(email, "Password1!");
        loginSuccess.Should().BeTrue();
        JwtSecurityToken freshJwt = new JwtSecurityTokenHandler().ReadJwtToken(freshToken);
        freshJwt.Claims.Where(c => c.Type == "tenant_id").Should().ContainSingle()
            .Which.Value.Should().Be(newStudioId.ToString());
        freshJwt.Claims.Should().Contain(c => c.Type == ClaimTypes.Role && c.Value == "artist");
        freshJwt.Claims.Should().NotContain(c => c.Type == ClaimTypes.Role && c.Value == "owner");
    }

    [Fact]
    public async Task AcceptInvite_CalledTwice_SecondCallThrowsNotFoundException()
    {
        Guid oldStudioId = Guid.NewGuid();
        Guid newStudioId = Guid.NewGuid();
        string email = UniqueEmail();

        IdentityService identity = await BuildIdentityAsync();

        await using AppDbContext seedDb = fixture.CreateDbContext(Guid.Empty);
        seedDb.Studios.Add(new Studio
        {
            Id = oldStudioId,
            Name = "Jane Doe",
            Slug = $"jane-{Guid.NewGuid():N}",
            City = string.Empty,
            OwnerEmail = email,
            IsSolo = true,
            IsActive = true,
        });
        seedDb.Studios.Add(new Studio
        {
            Id = newStudioId,
            Name = "Ink Collective",
            Slug = $"ink-{Guid.NewGuid():N}",
            City = "Lisbon",
        });
        Plan plan = new() { Name = $"Starter-{Guid.NewGuid():N}", MaxArtists = 2 };
        seedDb.Plans.Add(plan);
        seedDb.Subscriptions.Add(new Subscription
        {
            StudioId = newStudioId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            GracePeriodEnd = DateTime.UtcNow.AddDays(37),
        });
        StudioJoinInvite invite = new()
        {
            StudioId = newStudioId,
            InvitedEmail = email,
            FirstName = "Jane",
            LastName = "Doe",
            Status = StudioJoinInviteStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        seedDb.StudioJoinInvites.Add(invite);
        await seedDb.SaveChangesAsync();

        (_, Guid userId, _) = await identity.CreateUserAsync(email, "Password1!", "owner", oldStudioId, "Jane");

        await using AppDbContext handlerDb = fixture.CreateDbContext(Guid.Empty);
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        StubCurrentTenant stubTenant = new(Guid.NewGuid());
        PlanLimitService planLimits = new(handlerDb, stubTenant, cache, NullLogger<PlanLimitService>.Instance);
        StubCurrentUser currentUser = new(userId, "owner", email);
        AcceptStudioJoinInviteHandler handler = new(
            handlerDb, currentUser, identity, planLimits, NullLogger<AcceptStudioJoinInviteHandler>.Instance);

        await handler.Handle(new AcceptStudioJoinInviteCommand(invite.Id), default);

        Func<Task> act = () => handler.Handle(new AcceptStudioJoinInviteCommand(invite.Id), default);
        await act.Should().ThrowAsync<DomainException>();
    }

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@test.com";

    private sealed record StubCurrentUser(Guid UserId, string Role, string? Email = null) : ICurrentUser
    {
        public bool IsAuthenticated => true;
    }

    private sealed class StubCurrentTenant(Guid studioId) : ICurrentTenant
    {
        public Guid StudioId { get; private set; } = studioId;
        public bool IsSet => true;
        public void SetTenant(Guid studioId) => StudioId = studioId;
    }
}
