using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.API.Extensions;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests.Social;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// The artist-self-service authorization matrix, end to end: the REAL authorization pipeline, the
// REAL TenantMiddleware + tenant query filter, real MySQL, and real JWTs with distinct user ids.
// Unit tests can't prove any of this — the in-memory context has no HasQueryFilter and bypasses
// the policy layer, which is exactly how a missing filter or a loosened policy would slip through.
//
// Two studios x two artists. For each of the seven artist-scoped connect/verify/disconnect
// operations: an artist succeeds on their own profile, gets 403 on a colleague in the same studio
// and 404 on an artist in the other studio; an owner succeeds on both artists of their own studio
// and gets 404 on the other studio; a client is rejected; and the studio-subject endpoints stay
// owner-only.
[Collection("Database")]
public class ArtistSocialEndpointAuthorizationTests(DatabaseFixture fixture)
{
    private const string SigningKeyValue = "artist-social-endpoint-test-key-32-bytes-min!";

    public static IEnumerable<object[]> Operations() =>
    [
        ["GET",    "instagram/connect-url",     HttpStatusCode.OK],
        ["DELETE", "instagram/disconnect",      HttpStatusCode.NoContent],
        ["GET",    "social/tiktok/connect-url", HttpStatusCode.OK],
        ["PUT",    "social/tiktok/handle",      HttpStatusCode.NoContent],
        ["POST",   "social/tiktok/request-code", HttpStatusCode.OK],
        ["POST",   "social/tiktok/verify-code", HttpStatusCode.OK],
        ["DELETE", "social/tiktok/disconnect",  HttpStatusCode.NoContent],
    ];

    private sealed record World(
        Guid StudioA, Guid StudioB,
        Artist A1, Artist A2, Artist B1);

    private async Task<World> SeedWorld()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();

        Artist Make(Guid studio, string name) => new()
        {
            StudioId = studio,
            UserId = Guid.NewGuid(),
            FirstName = name,
            LastName = "Tester",
            Email = $"{Guid.NewGuid():N}@social.test",
        };

        Artist a1 = Make(studioA, "A1");
        Artist a2 = Make(studioA, "A2");
        Artist b1 = Make(studioB, "B1");

        // Each artist has a TikTok link with a handle and a live pending code, so every one of the
        // seven operations has something to succeed against.
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        db.Artists.AddRange(a1, a2, b1);
        foreach (Artist artist in new[] { a1, a2, b1 })
        {
            db.SocialAccountLinks.Add(new SocialAccountLink
            {
                StudioId = artist.StudioId,
                SubjectType = SocialLinkSubjectType.Artist,
                SubjectId = artist.Id,
                Platform = SocialPlatform.TikTok,
                Handle = "seeded",
                PendingVerificationCode = "PENA-ABC123",
                PendingCodeExpiresAt = DateTime.UtcNow.AddHours(1),
            });
        }
        await db.SaveChangesAsync();

        return new World(studioA, studioB, a1, a2, b1);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Artist_OnOwnProfile_Succeeds(string method, string path, HttpStatusCode expected)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Send(client, method, w.A1.Id, path,
            Token("artist", w.A1.UserId!.Value, w.StudioA));

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Artist_OnColleagueInTheSameStudio_Returns403(string method, string path, HttpStatusCode _)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Send(client, method, w.A2.Id, path,
            Token("artist", w.A1.UserId!.Value, w.StudioA));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Artist_OnArtistInAnotherStudio_Returns404(string method, string path, HttpStatusCode _)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Send(client, method, w.B1.Id, path,
            Token("artist", w.A1.UserId!.Value, w.StudioA));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Owner_OnEveryArtistInTheirStudio_Succeeds(string method, string path, HttpStatusCode expected)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();
        string owner = Token("owner", Guid.NewGuid(), w.StudioA);

        (await Send(client, method, w.A1.Id, path, owner)).StatusCode.Should().Be(expected);
        (await Send(client, method, w.A2.Id, path, owner)).StatusCode.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Owner_OnAnArtistInAnotherStudio_Returns404(string method, string path, HttpStatusCode _)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Send(client, method, w.B1.Id, path,
            Token("owner", Guid.NewGuid(), w.StudioA));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task DualRoleOwnerWhoIsAlsoAnArtist_CanActOnAColleagueBecauseTheirClaimIsOwner(
        string method, string path, HttpStatusCode expected)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        // The token's user id IS artist A1's linked user, but the role claim is "owner" — the
        // dual-role account. It must keep full access to the other artist in its studio.
        HttpResponseMessage response = await Send(client, method, w.A2.Id, path,
            Token("owner", w.A1.UserId!.Value, w.StudioA));

        response.StatusCode.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Client_IsRejected(string method, string path, HttpStatusCode _)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Send(client, method, w.A1.Id, path,
            Token("client", Guid.NewGuid(), w.StudioA));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task NoToken_Returns401(string method, string path, HttpStatusCode _)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Send(client, method, w.A1.Id, path, token: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", "social")]
    [InlineData("GET", "social/tiktok/connect-url")]
    [InlineData("PUT", "social/tiktok/handle")]
    [InlineData("POST", "social/tiktok/request-code")]
    [InlineData("POST", "social/tiktok/verify-code")]
    [InlineData("DELETE", "social/tiktok/disconnect")]
    public async Task StudioSubjectEndpoints_StayOwnerOnly_ForAnArtist(string method, string path)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpRequestMessage request = BuildRequest(method, $"/api/v1/studios/{w.StudioA}/{path}",
            Token("artist", w.A1.UserId!.Value, w.StudioA));
        HttpResponseMessage response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Artist_FailedAttemptOnAColleague_ChangesNothingAndWritesNoAuditEntry()
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Send(client, "PUT", w.A2.Id, "social/tiktok/handle",
            Token("artist", w.A1.UserId!.Value, w.StudioA));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        verify.SocialAccountLinks.Single(l => l.SubjectId == w.A2.Id).Handle.Should().Be("seeded");
        verify.AuditLogEntries.Any(a => a.TargetId == w.A2.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Artist_SuccessfulHandleUpdateOnOwnProfile_WritesAnAuditEntryWithoutTheHandle()
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await Send(client, "PUT", w.A1.Id, "social/tiktok/handle",
            Token("artist", w.A1.UserId!.Value, w.StudioA));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        AuditLogEntry entry = verify.AuditLogEntries.Single(a => a.TargetId == w.A1.Id);
        entry.Action.Should().Be(AuditActions.SocialHandleUpdated);
        entry.ActorRole.Should().Be("artist");
        entry.ActorUserId.Should().Be(w.A1.UserId!.Value);
        entry.StudioId.Should().Be(w.StudioA);
        // MySQL's JSON column re-serialises with spaces, so compare structure, not text.
        using JsonDocument metadata = JsonDocument.Parse(entry.Metadata);
        metadata.RootElement.EnumerateObject().Select(p => (p.Name, p.Value.GetString()))
            .Should().Equal(("platform", "TikTok"));
        entry.Metadata.Should().NotContain("changed-handle");
    }

    private static HttpRequestMessage BuildRequest(string method, string url, string? token)
    {
        HttpRequestMessage request = new(new HttpMethod(method), url);
        if (method == "PUT") request.Content = JsonContent.Create(new UpdateSocialHandleRequest("changed-handle"));
        if (token is not null) request.Headers.Add("Authorization", $"Bearer {token}");
        return request;
    }

    private static Task<HttpResponseMessage> Send(HttpClient client, string method, Guid artistId, string path, string? token) =>
        client.SendAsync(BuildRequest(method, $"/api/v1/artists/{artistId}/{path}", token));

    private async Task<IHost> BuildHost()
    {
        ISubscriptionAccessService subscriptions = Substitute.For<ISubscriptionAccessService>();
        subscriptions.IsStudioActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        subscriptions.GetSnapshotAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new SubscriptionSnapshot(SubscriptionStatus.Active, null, DateTime.MinValue));
        IPlanLimitService planLimits = Substitute.For<IPlanLimitService>();

        // The third-party edges: configured providers and a bio checker that always finds the code,
        // so the only thing that can make a request fail is authorization/tenancy.
        IInstagramService instagram = Substitute.For<IInstagramService>();
        instagram.BuildAuthorizationUrl(Arg.Any<string>()).Returns("https://instagram.test/authorize");
        IInstagramStateSigner instagramSigner = Substitute.For<IInstagramStateSigner>();
        instagramSigner.Sign(Arg.Any<Guid>()).Returns("signed-instagram-state");

        ISocialOAuthProvider provider = Substitute.For<ISocialOAuthProvider>();
        provider.IsConfigured.Returns(true);
        provider.BuildAuthorizationUrl(Arg.Any<string>()).Returns("https://social.test/authorize");
        ISocialOAuthProviderFactory providerFactory = Substitute.For<ISocialOAuthProviderFactory>();
        providerFactory.GetProvider(Arg.Any<SocialPlatform>()).Returns(provider);
        ISocialOAuthStateSigner socialSigner = Substitute.For<ISocialOAuthStateSigner>();
        socialSigner.Sign(Arg.Any<SocialLinkSubjectType>(), Arg.Any<Guid>(), Arg.Any<SocialPlatform>())
            .Returns("signed-social-state");
        ISocialBioChecker checker = Substitute.For<ISocialBioChecker>();
        checker.IsSupported.Returns(true);
        checker.BioContainsCodeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        ISocialBioCheckerFactory checkerFactory = Substitute.For<ISocialBioCheckerFactory>();
        checkerFactory.GetChecker(Arg.Any<SocialPlatform>()).Returns(checker);

        // Same reasoning as ClientArtistEndpointAuthorizationTests: AddMediatR registers every
        // handler in the Application assembly, most needing infrastructure this narrow host
        // doesn't provide, so validate-on-build is off.
        IHostBuilder builder = new HostBuilder()
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = false;
            })
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddScoped<ICurrentTenant, CurrentTenantService>();
                    services.AddScoped<ICurrentUser, CurrentUserService>();
                    services.AddSingleton(subscriptions);
                    services.AddSingleton(planLimits);
                    services.AddSingleton(instagram);
                    services.AddSingleton(instagramSigner);
                    services.AddSingleton(providerFactory);
                    services.AddSingleton(socialSigner);
                    services.AddSingleton(checkerFactory);

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))));
                    services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

                    Assembly applicationAssembly = typeof(ValidationBehavior<,>).Assembly;
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
                    services.AddValidatorsFromAssembly(applicationAssembly);
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PlanLimitBehavior<,>));
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditLogBehavior<,>));

                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                        .AddJwtBearer(o =>
                        {
                            o.TokenValidationParameters = new TokenValidationParameters
                            {
                                ValidIssuer = "iss",
                                ValidAudience = "aud",
                                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                                ClockSkew = TimeSpan.Zero,
                            };
                        });
                    services.AddApiAuthorization();
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<ExceptionMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseMiddleware<TenantMiddleware>();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapSocialEndpoints();
                        endpoints.MapInstagramEndpoints();
                    });
                });
            });

        return await builder.StartAsync();
    }

    private static string Token(string role, Guid userId, Guid tenantId) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "iss", audience: "aud",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role),
                new Claim("tenant_id", tenantId.ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKeyValue)),
                SecurityAlgorithms.HmacSha256)));
}
