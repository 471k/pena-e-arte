using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Endpoints;

// Meta's Deauthorize and Data Deletion callbacks, end to end: the REAL routes, the REAL anonymous
// access (no JWT is ever sent), the REAL form parsing, the REAL signature parser, the REAL
// MediatR pipeline and a real MySQL database. Unit tests can't prove the wiring — this is what
// would catch a missing DI registration, an antiforgery rejection of Meta's form POST, or a
// rate-limit policy that doesn't exist.
[Collection("Database")]
public class InstagramMetaCallbackEndpointTests(DatabaseFixture fixture)
{
    private const string AppSecret = "meta-callback-integration-test-secret";
    private const string BaseUrl = "https://app.example.test";

    private sealed record World(Guid StudioId, Guid ArtistId, string InstagramUserId, Guid OtherArtistId, string OtherInstagramUserId);

    private async Task<World> SeedWorld()
    {
        Guid studioId = Guid.NewGuid();
        Guid artistId = Guid.NewGuid();
        Guid otherArtistId = Guid.NewGuid();
        string igUser = $"ig-{Guid.NewGuid():N}";
        string otherIgUser = $"ig-{Guid.NewGuid():N}";

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        foreach ((Guid id, string ig) in new[] { (artistId, igUser), (otherArtistId, otherIgUser) })
        {
            db.Artists.Add(new Artist
            {
                Id = id,
                StudioId = studioId,
                FirstName = "Meta",
                LastName = "Tester",
                Email = $"{Guid.NewGuid():N}@meta.test",
            });
            db.InstagramConnections.Add(new InstagramConnection
            {
                StudioId = studioId,
                ArtistId = id,
                InstagramUserId = ig,
                InstagramAccountId = $"acct-{ig}",
                Username = ig,
                EncryptedToken = "encrypted-token",
                TokenExpiresAt = DateTime.UtcNow.AddDays(60),
            });
            db.InstagramPosts.Add(new InstagramPost
            {
                StudioId = studioId,
                ArtistId = id,
                InstagramMediaId = $"media-{Guid.NewGuid():N}",
                MediaUrl = "https://cdn.test/a.jpg",
                PostedAt = DateTime.UtcNow,
                IsVisible = true,
            });
            db.SocialAccountLinks.Add(new SocialAccountLink
            {
                StudioId = studioId,
                SubjectType = SocialLinkSubjectType.Artist,
                SubjectId = id,
                Platform = SocialPlatform.Instagram,
                Handle = ig,
                IsVerified = true,
                ExternalUserId = ig,
                AlternateExternalUserId = $"acct-{ig}",
                EncryptedToken = "encrypted-token",
                TokenExpiresAt = DateTime.UtcNow.AddDays(60),
            });
        }
        await db.SaveChangesAsync();

        return new World(studioId, artistId, igUser, otherArtistId, otherIgUser);
    }

    // Built exactly the way Meta builds it: HMAC-SHA256 over the still-encoded payload.
    private static string SignedRequest(string instagramUserId, string secret = AppSecret)
    {
        string payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{{\"algorithm\":\"HMAC-SHA256\",\"user_id\":\"{instagramUserId}\",\"issued_at\":1790000000}}"));
        byte[] signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return $"{Base64Url.EncodeToString(signature)}.{payload}";
    }

    private static Task<HttpResponseMessage> PostForm(HttpClient client, string path, string signedRequest) =>
        client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["signed_request"] = signedRequest,
        }));

    [Fact]
    public async Task DataDeletion_SignedByMeta_ErasesThatUsersDataAndReturnsTheRequiredJson()
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await PostForm(client, "/api/v1/instagram/data-deletion", SignedRequest(w.InstagramUserId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string code = body.RootElement.GetProperty("confirmation_code").GetString()!;
        body.RootElement.GetProperty("url").GetString().Should().Be($"{BaseUrl}/data-deletion/instagram?code={code}");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        (await db.InstagramConnections.AnyAsync(c => c.ArtistId == w.ArtistId)).Should().BeFalse();
        (await db.InstagramPosts.AnyAsync(p => p.ArtistId == w.ArtistId)).Should().BeFalse();
        SocialAccountLink link = await db.SocialAccountLinks.SingleAsync(l => l.SubjectId == w.ArtistId);
        link.IsVerified.Should().BeFalse();
        link.EncryptedToken.Should().BeNull();
        link.ExternalUserId.Should().BeNull();
        link.AlternateExternalUserId.Should().BeNull();

        // Another artist's Instagram data is untouched.
        (await db.InstagramConnections.AnyAsync(c => c.ArtistId == w.OtherArtistId)).Should().BeTrue();
        (await db.InstagramPosts.AnyAsync(p => p.ArtistId == w.OtherArtistId)).Should().BeTrue();
        (await db.SocialAccountLinks.SingleAsync(l => l.SubjectId == w.OtherArtistId)).IsVerified.Should().BeTrue();

        // The audit row records that Meta triggered it, in the right studio, without the Instagram id.
        AuditLogEntry audit = await db.AuditLogEntries.SingleAsync(a => a.TargetId == w.ArtistId);
        audit.StudioId.Should().Be(w.StudioId);
        audit.ActorRole.Should().Be("meta-callback");
        audit.Metadata.Should().NotContain(w.InstagramUserId);
    }

    [Fact]
    public async Task Deauthorize_SignedByMeta_ErasesThatUsersData()
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await PostForm(client, "/api/v1/instagram/deauthorize", SignedRequest(w.InstagramUserId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        (await db.InstagramConnections.AnyAsync(c => c.ArtistId == w.ArtistId)).Should().BeFalse();
        (await db.InstagramConnections.AnyAsync(c => c.ArtistId == w.OtherArtistId)).Should().BeTrue();
    }

    // Production regression: Meta's callback may identify the user by the professional account id rather
    // than the app-scoped id the token exchange returns. Matching only the latter erased nothing.
    [Theory]
    [InlineData("/api/v1/instagram/deauthorize")]
    [InlineData("/api/v1/instagram/data-deletion")]
    public async Task Callback_IdentifyingTheUserByTheProfessionalAccountId_ErasesThatUsersData(string path)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await PostForm(client, path, SignedRequest($"acct-{w.InstagramUserId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        (await db.InstagramConnections.AnyAsync(c => c.ArtistId == w.ArtistId)).Should().BeFalse();
        (await db.InstagramPosts.AnyAsync(p => p.ArtistId == w.ArtistId)).Should().BeFalse();
        SocialAccountLink link = await db.SocialAccountLinks.SingleAsync(l => l.SubjectId == w.ArtistId);
        link.IsVerified.Should().BeFalse();
        link.EncryptedToken.Should().BeNull();
        link.AlternateExternalUserId.Should().BeNull();
        (await db.InstagramConnections.AnyAsync(c => c.ArtistId == w.OtherArtistId)).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/instagram/deauthorize")]
    [InlineData("/api/v1/instagram/data-deletion")]
    public async Task ForgedSignature_IsRejectedAndErasesNothing(string path)
    {
        World w = await SeedWorld();
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        // Perfectly formed, but signed with a secret that isn't ours — exactly what an attacker
        // trying to wipe a victim's Instagram data would send.
        HttpResponseMessage response = await PostForm(client, path, SignedRequest(w.InstagramUserId, secret: "attackers-secret"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        (await db.InstagramConnections.AnyAsync(c => c.ArtistId == w.ArtistId)).Should().BeTrue();
        (await db.InstagramPosts.AnyAsync(p => p.ArtistId == w.ArtistId)).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/instagram/deauthorize")]
    [InlineData("/api/v1/instagram/data-deletion")]
    public async Task MissingSignedRequest_OrNonFormBody_ReturnsBadRequest(string path)
    {
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage emptyForm = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>()));
        HttpResponseMessage jsonBody = await client.PostAsync(path, JsonContent.Create(new { signed_request = "x" }));

        emptyForm.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        jsonBody.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DataDeletion_ForAnInstagramUserWeNeverHeldData_StillConfirms()
    {
        using IHost host = await BuildHost();
        using HttpClient client = host.GetTestServer().CreateClient();

        HttpResponseMessage response = await PostForm(client, "/api/v1/instagram/data-deletion", SignedRequest("never-connected"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("confirmation_code");
    }

    private async Task<IHost> BuildHost()
    {
        IAppSettings appSettings = Substitute.For<IAppSettings>();
        appSettings.BaseUrl.Returns(BaseUrl);
        ISubscriptionAccessService subscriptions = Substitute.For<ISubscriptionAccessService>();
        IPlanLimitService planLimits = Substitute.For<IPlanLimitService>();

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
                    services.AddSingleton(appSettings);
                    // The existing GET /instagram/callback shares MapInstagramCallbackEndpoint, so its
                    // handler's services must resolve too or minimal APIs try to infer a body for them.
                    services.AddSingleton(Substitute.For<IInstagramStateSigner>());
                    services.AddSingleton(subscriptions);
                    services.AddSingleton(planLimits);
                    services.AddSingleton<IMetaSignedRequestParser>(
                        new MetaSignedRequestParser(Options.Create(new InstagramOptions { AppSecret = AppSecret })));
                    // The endpoints require this named policy; the real one lives in Program.cs.
                    services.AddRateLimiter(o => o.AddPolicy("public-write",
                        _ => RateLimitPartition.GetNoLimiter("test")));
                    services.AddAuthentication();
                    services.AddAuthorization();

                    services.AddDbContext<AppDbContext>(options =>
                        options.UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0))));
                    services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
                    services.AddScoped<ICurrentTenant, CurrentTenantService>();
                    services.AddScoped<ICurrentUser, CurrentUserService>();

                    Assembly applicationAssembly = typeof(ValidationBehavior<,>).Assembly;
                    services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
                    services.AddValidatorsFromAssembly(applicationAssembly);
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PlanLimitBehavior<,>));
                    services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditLogBehavior<,>));
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<ExceptionMiddleware>();
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapInstagramCallbackEndpoint());
                });
            });

        return await builder.StartAsync();
    }
}
