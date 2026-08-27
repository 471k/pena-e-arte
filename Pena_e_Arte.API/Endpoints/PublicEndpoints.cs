using System.Security.Claims;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.ConductReports.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Application.Reviews.Commands;
using Pena_e_Arte.Application.Traffic.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using StackExchange.Redis;

namespace Pena_e_Arte.API.Endpoints;

public static class PublicEndpoints
{
    private const string SiteBaseUrl = "https://tattooos.co";

    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        // Root-level, not under /api/v1 — search engines expect /sitemap.xml at the site root.
        app.MapGet("/sitemap.xml", GetSitemap).AllowAnonymous().RequireRateLimiting("public-read");

        RouteGroupBuilder group = app.MapGroup("/api/v1/public");

        group.MapGet("/studios/{slug}", GetPublicStudio).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/artists/{slug}", GetPublicArtist).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/studios/nearby", GetNearbyStudios).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/studios/{slug}/reviews", GetStudioReviews).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/artists/{slug}/reviews", GetArtistReviews).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapPost("/studios/{slug}/reviews", CreateStudioReview)
             .RequireAuthorization("ClientAndAbove").RequireRateLimiting("public-write");
        group.MapPost("/artists/{slug}/reviews", CreateArtistReview)
             .RequireAuthorization("ClientAndAbove").RequireRateLimiting("public-write");
        group.MapGet("/studios/{slug}/reviews/eligible-appointments", GetReviewableStudioAppointments)
             .RequireAuthorization("ClientAndAbove").RequireRateLimiting("public-read");
        group.MapGet("/artists/{slug}/reviews/eligible-appointments", GetReviewableArtistAppointments)
             .RequireAuthorization("ClientAndAbove").RequireRateLimiting("public-read");
        group.MapPost("/artists/{slug}/reports", FileArtistConductReport)
             .RequireAuthorization("ClientOnly").RequireRateLimiting("public-write");
        group.MapPost("/studios/{slug}/reports", FileStudioConductReport)
             .RequireAuthorization("ClientOnly").RequireRateLimiting("public-write");
        group.MapGet("/artists/{slug}/reports/reportable-appointments", GetReportableArtistAppointments)
             .RequireAuthorization("ClientOnly").RequireRateLimiting("public-read");
        group.MapGet("/studios/{slug}/reports/reportable-appointments", GetReportableStudioAppointments)
             .RequireAuthorization("ClientOnly").RequireRateLimiting("public-read");
        group.MapPost("/artists/{slug}/view", RecordArtistView)
             .AllowAnonymous().RequireRateLimiting("public-write");
        group.MapGet("/portfolio/feed", GetPortfolioFeed).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapGet("/portfolio/{imageId:guid}/reviews", GetPortfolioImageReviews).AllowAnonymous().RequireRateLimiting("public-read");
        group.MapPost("/portfolio/{imageId:guid}/reviews", CreatePortfolioImageReview)
             .RequireAuthorization("ClientAndAbove").RequireRateLimiting("public-write");
        group.MapGet("/artists/{slug}/instagram-posts", GetArtistInstagramPosts)
             .AllowAnonymous().RequireRateLimiting("public-read");
        group.MapPost("/traffic/beacon", RecordTrafficBeacon)
             .AllowAnonymous().RequireRateLimiting("public-write");
    }

    private static async Task<IResult> GetSitemap(
        ISender mediator,
        CancellationToken ct)
    {
        List<SitemapUrlEntry> urls = await mediator.Send(new GetSitemapUrlsQuery(), ct);

        var sb = new System.Text.StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (SitemapUrlEntry url in urls)
        {
            sb.Append("<url>");
            sb.Append($"<loc>{SiteBaseUrl}{url.Path}</loc>");
            sb.Append($"<lastmod>{url.LastModified:yyyy-MM-dd}</lastmod>");
            sb.Append("</url>");
        }
        sb.Append("</urlset>");

        return Results.Text(sb.ToString(), "application/xml");
    }

    private static async Task<IResult> GetPublicStudio(
        string slug,
        ISender mediator,
        CancellationToken ct)
    {
        PublicStudioResponse? result = await mediator.Send(new GetPublicStudioQuery(slug), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> GetPublicArtist(
        string slug,
        ISender mediator,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        Guid? currentUserId = user.Identity?.IsAuthenticated == true
            ? Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid id) ? id : null
            : null;

        PublicArtistResponse? result =
            await mediator.Send(new GetPublicArtistQuery(slug, currentUserId), ct);
        return result is null ? Results.NotFound() : Results.Ok(result);
    }

    private static async Task<IResult> GetNearbyStudios(
        double lat,
        double lng,
        double radiusKm,
        ISender mediator,
        CancellationToken ct)
    {
        if (lat is < -90 or > 90 || lng is < -180 or > 180 || radiusKm is <= 0 or > 500)
            return Results.BadRequest("Invalid lat/lng/radiusKm.");

        List<NearbyStudioResponse> result =
            await mediator.Send(new GetNearbyStudiosQuery(lat, lng, radiusKm), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetStudioReviews(
        string slug,
        ISender mediator,
        CancellationToken ct)
    {
        List<ReviewResponse> result =
            await mediator.Send(new GetStudioReviewsQuery(slug), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetArtistReviews(
        string slug,
        ISender mediator,
        CancellationToken ct)
    {
        List<ReviewResponse> result =
            await mediator.Send(new GetArtistReviewsQuery(slug), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateStudioReview(
        string slug,
        CreateReviewRequest body,
        ClaimsPrincipal user,
        ISender mediator,
        CancellationToken ct)
    {
        Guid authorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        string authorName = user.FindFirstValue(ClaimTypes.Name)
                         ?? user.FindFirstValue(ClaimTypes.GivenName)
                         ?? "Anonymous";

        await mediator.Send(
            new CreateStudioReviewCommand(
                slug, body.AppointmentId ?? Guid.Empty, authorId, authorName, body.Rating, body.Body),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateArtistReview(
        string slug,
        CreateReviewRequest body,
        ClaimsPrincipal user,
        ISender mediator,
        CancellationToken ct)
    {
        Guid authorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        string authorName = user.FindFirstValue(ClaimTypes.Name)
                         ?? user.FindFirstValue(ClaimTypes.GivenName)
                         ?? "Anonymous";

        await mediator.Send(
            new CreateArtistReviewCommand(
                slug, body.AppointmentId ?? Guid.Empty, authorId, authorName, body.Rating, body.Body),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetReviewableStudioAppointments(
        string slug,
        ClaimsPrincipal user,
        ISender mediator,
        CancellationToken ct)
    {
        Guid authorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        List<ReviewableAppointmentResponse> result =
            await mediator.Send(new GetReviewableStudioAppointmentsQuery(slug, authorId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetReviewableArtistAppointments(
        string slug,
        ClaimsPrincipal user,
        ISender mediator,
        CancellationToken ct)
    {
        Guid authorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        List<ReviewableAppointmentResponse> result =
            await mediator.Send(new GetReviewableArtistAppointmentsQuery(slug, authorId), ct);
        return Results.Ok(result);
    }

    private static bool TryParseReportCategory(string raw, out ReportCategory category) =>
        Enum.TryParse(raw, ignoreCase: true, out category) && Enum.IsDefined(category);

    private static async Task<IResult> FileArtistConductReport(
        string slug,
        FileArtistConductReportRequest body,
        ClaimsPrincipal user,
        ISender mediator,
        CancellationToken ct)
    {
        if (!TryParseReportCategory(body.Category, out ReportCategory category))
            return Results.BadRequest("Unrecognized category.");

        Guid reporterId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        string reporterName = user.FindFirstValue(ClaimTypes.Name)
                           ?? user.FindFirstValue(ClaimTypes.GivenName)
                           ?? "Anonymous";

        await mediator.Send(
            new FileArtistConductReportCommand(
                slug, body.AppointmentId, reporterId, reporterName, category, body.Reason,
                body.AttachmentUrls),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> FileStudioConductReport(
        string slug,
        FileStudioConductReportRequest body,
        ClaimsPrincipal user,
        ISender mediator,
        CancellationToken ct)
    {
        if (!TryParseReportCategory(body.Category, out ReportCategory category))
            return Results.BadRequest("Unrecognized category.");

        Guid reporterId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        string reporterName = user.FindFirstValue(ClaimTypes.Name)
                           ?? user.FindFirstValue(ClaimTypes.GivenName)
                           ?? "Anonymous";

        await mediator.Send(
            new FileStudioConductReportCommand(
                slug, body.AppointmentId, reporterId, reporterName, category, body.Reason,
                body.AttachmentUrls),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetReportableArtistAppointments(
        string slug,
        ClaimsPrincipal user,
        ISender mediator,
        CancellationToken ct)
    {
        Guid reporterId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        List<ReportableAppointmentResponse> result =
            await mediator.Send(new GetReportableArtistAppointmentsQuery(slug, reporterId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetReportableStudioAppointments(
        string slug,
        ClaimsPrincipal user,
        ISender mediator,
        CancellationToken ct)
    {
        Guid reporterId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        List<ReportableAppointmentResponse> result =
            await mediator.Send(new GetReportableStudioAppointmentsQuery(slug, reporterId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> RecordArtistView(
        string slug,
        IAppDbContext db,
        IConnectionMultiplexer redis,
        CancellationToken ct)
    {
        // Fire-and-forget view counter. No MediatR needed — no domain invariants.
        // Approved: public, anonymous, write-only to Redis — not business data.
        Guid? artistId = await db.Artists
            .IgnoreQueryFilters()
            .Where(a => a.Slug == slug && a.DeletedAt == null)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        if (artistId is null) return Results.NotFound();

        try
        {
            IDatabase redisDb = redis.GetDatabase();
            await redisDb.StringIncrementAsync($"portfolio:views:{artistId}");
        }
        catch
        {
            // Redis unavailable — view count not recorded; non-critical.
        }
        return Results.NoContent();
    }

    private static async Task<IResult> GetPortfolioFeed(
        double? lat,
        double? lng,
        ISender mediator,
        CancellationToken ct,
        double radiusKm = 50,
        int page = 1,
        int pageSize = 24,
        string? style = null,
        string? category = null,
        string? search = null)
    {
        if (pageSize is < 1 or > 100) pageSize = 24;
        if (!string.IsNullOrWhiteSpace(search) && search.Length > 100) search = search[..100];

        List<PortfolioImageResponse> result = await mediator.Send(
            new GetPortfolioFeedQuery(lat, lng, radiusKm, page, pageSize, style, category, search), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPortfolioImageReviews(
        Guid imageId,
        ISender mediator,
        CancellationToken ct)
    {
        List<ReviewResponse> result =
            await mediator.Send(new GetPortfolioImageReviewsQuery(imageId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreatePortfolioImageReview(
        Guid imageId,
        CreateReviewRequest body,
        ClaimsPrincipal user,
        ISender mediator,
        CancellationToken ct)
    {
        Guid authorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        string authorName = user.FindFirstValue(ClaimTypes.Name)
                         ?? user.FindFirstValue(ClaimTypes.GivenName)
                         ?? "Anonymous";

        await mediator.Send(
            new CreatePortfolioImageReviewCommand(imageId, authorId, authorName, body.Rating, body.Body),
            ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetArtistInstagramPosts(
        string slug,
        ISender mediator,
        CancellationToken ct)
    {
        List<InstagramPostResponse> result =
            await mediator.Send(new GetPublicArtistInstagramPostsQuery(slug), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> RecordTrafficBeacon(
        RecordTrafficBeaconRequest request,
        IValidator<RecordTrafficBeaconRequest> validator,
        ClaimsPrincipal user,
        HttpContext http,
        IConnectionMultiplexer redis,
        IGeoIpService geoIp,
        IUserAgentParser uaParser,
        ISender mediator,
        IConfiguration config,
        CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) throw new ValidationException(validation.Errors);

        Guid? userId = user.Identity?.IsAuthenticated == true
            ? Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid uid) ? uid : null
            : null;
        string? role = user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.Role) : null;
        Guid? studioId = Guid.TryParse(user.FindFirstValue("tenant_id"), out Guid sid) ? sid : null;

        // Visitor id comes from the client-generated anonymous identifier, sent as a header
        // (not the request body — keeps the DTO free of anything resembling a tracking id
        // a reviewer might mistake for a required business field).
        if (!Guid.TryParse(http.Request.Headers["X-Visitor-Id"], out Guid visitorId))
            return Results.BadRequest();

        System.Net.IPAddress? ip = http.Connection.RemoteIpAddress;

        // GeoIP lookup and UA parsing only run on navigation events. A visitor's device/browser/
        // OS/location cannot change between beacons within the same presence window, so the 20s
        // heartbeats (IsNavigation: false) skip this work entirely and just refresh the
        // zset score / TTL — this is one of the highest-traffic endpoints in the app.
        GeoIpResult? geo = null;
        string? deviceType = null, browser = null, os = null;
        string? ipHash = null;
        if (request.IsNavigation)
        {
            geo = ip is not null ? geoIp.Lookup(ip) : null;
            (deviceType, browser, os) = uaParser.Parse(http.Request.Headers.UserAgent);
            ipHash = ip is not null ? HashIp(ip, config["GeoIp:IpHashPepper"]) : null;
        }

        try
        {
            IDatabase db = redis.GetDatabase();
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string detailKey = $"traffic:presence:detail:{visitorId}";

            // Pipelined into one round-trip via IBatch (matches TrafficPresenceService's own
            // batching pattern on the read side) rather than four sequential awaited calls.
            IBatch batch = db.CreateBatch();
            List<Task> batchTasks =
            [
                batch.SortedSetAddAsync("traffic:presence:zset", visitorId.ToString(), nowMs),
                batch.KeyExpireAsync(detailKey, TimeSpan.FromSeconds(60)),
            ];

            if (request.IsNavigation)
            {
                batchTasks.Add(batch.HashSetAsync(detailKey,
                [
                    new HashEntry("userId", userId?.ToString() ?? ""),
                    new HashEntry("role", role ?? ""),
                    new HashEntry("studioId", studioId?.ToString() ?? ""),
                    new HashEntry("path", request.Path),
                    new HashEntry("countryCode", geo?.CountryCode ?? ""),
                    new HashEntry("city", geo?.City ?? ""),
                    // Only lat/long are added here for the live map — postal/timezone/ASN etc.
                    // are deliberately not carried into the hot-path Redis payload, matching this
                    // hash's existing minimalism (it doesn't even carry country/region today).
                    new HashEntry("latitude", geo?.Latitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                    new HashEntry("longitude", geo?.Longitude?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ""),
                    new HashEntry("deviceType", deviceType ?? ""),
                    new HashEntry("browser", browser ?? ""),
                ]));
                // HSETNX semantics (When.NotExists): set once on the visitor's first beacon,
                // left untouched by every heartbeat/navigation after that, so "connected since"
                // reflects when they arrived, not when they were last seen.
                batchTasks.Add(batch.HashSetAsync(detailKey, "connectedAt", nowMs, When.NotExists));
            }

            batch.Execute();
            await Task.WhenAll(batchTasks);
        }
        catch
        {
            // Redis unavailable — live presence not recorded; non-critical, matches
            // RecordArtistView's existing degrade-gracefully precedent.
        }

        if (request.IsNavigation)
        {
            try
            {
                await mediator.Send(new RecordTrafficEventCommand(
                    visitorId, userId, role, studioId, request.Path,
                    geo, ipHash, deviceType, browser, os), ct);
            }
            catch
            {
                // Historical persist failed — never break the visitor's page load for this.
            }
        }

        return Results.NoContent();
    }

    private static string HashIp(System.Net.IPAddress ip, string? pepper)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(ip.ToString() + (pepper ?? ""));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}

