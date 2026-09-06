using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Traffic.Commands;

public record RecordTrafficEventCommand(
    Guid VisitorId,
    Guid? UserId,
    string? Role,
    Guid? StudioId,
    string Path,
    GeoIpResult? Geo,
    string? IpHash,
    string? DeviceType,
    string? Browser,
    string? Os) : IRequest;

/// <summary>
/// Thin persist handler, mirroring LogHelpSearchHandler's shape. The only real logic is
/// resolving StudioId when the JWT didn't already carry a tenant_id (an anonymous visit to
/// a studio- or artist-scoped public page) by parsing the route slug out of Path.
/// </summary>
public class RecordTrafficEventHandler(IAppDbContext db) : IRequestHandler<RecordTrafficEventCommand>
{
    public async Task Handle(RecordTrafficEventCommand command, CancellationToken ct)
    {
        Guid? studioId = command.StudioId ?? await ResolveStudioIdFromPathAsync(db, command.Path, ct);

        TrafficEvent trafficEvent = TrafficEvent.Create(
            visitorId: command.VisitorId,
            userId: command.UserId,
            role: command.Role,
            studioId: studioId,
            path: command.Path,
            countryCode: command.Geo?.CountryCode,
            country: command.Geo?.Country,
            regionCode: command.Geo?.RegionCode,
            region: command.Geo?.Region,
            city: command.Geo?.City,
            postalCode: command.Geo?.PostalCode,
            continentCode: command.Geo?.ContinentCode,
            continent: command.Geo?.Continent,
            latitude: command.Geo?.Latitude,
            longitude: command.Geo?.Longitude,
            accuracyRadiusKm: command.Geo?.AccuracyRadiusKm,
            timeZone: command.Geo?.TimeZone,
            asnNumber: command.Geo?.AsnNumber,
            asnOrganization: command.Geo?.AsnOrganization,
            ipHash: command.IpHash,
            deviceType: command.DeviceType,
            browser: command.Browser,
            os: command.Os);

        db.TrafficEvents.Add(trafficEvent);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Guid?> ResolveStudioIdFromPathAsync(IAppDbContext db, string path, CancellationToken ct)
    {
        string[] segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return null;

        if (segments[0] == "s")
        {
            string slug = segments[1];
            // Studio carries no query filter at all (admin-level, not tenant-scoped) — no
            // IgnoreQueryFilters() needed here, matching database.md's documented shape.
            // IsActive matches GetPublicStudioQuery's own filter exactly — a beacon fired at a
            // deactivated studio's page (which itself 404s) must not attribute traffic to it.
            return await db.Studios
                .Where(s => s.Slug == slug && s.IsActive)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (segments[0] == "artist")
        {
            string slug = segments[1];
            // IgnoreQueryFilters approved: usage #41 — cross-tenant artist-slug lookup for an
            // anonymous traffic beacon, exactly mirroring RecordArtistView's own lookup
            // (approved usage #13). See architecture.md.
            Guid? studioId = await db.Artists
                .IgnoreQueryFilters()
                .Where(a => a.Slug == slug && a.DeletedAt == null)
                .Select(a => (Guid?)a.StudioId)
                .FirstOrDefaultAsync(ct);

            if (studioId is null) return null;

            // Matches GetPublicArtistQuery's own check: the artist's parent studio must be
            // IsActive, or the real page 404s and this beacon must not attribute traffic to it.
            bool studioActive = await db.Studios.AnyAsync(s => s.Id == studioId && s.IsActive, ct);
            return studioActive ? studioId : null;
        }

        return null;
    }
}
