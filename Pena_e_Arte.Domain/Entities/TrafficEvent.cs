namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One row per recorded page-navigation event (not every heartbeat — see
/// RecordTrafficEventCommand). Deliberately NOT a TenantEntity: StudioId is
/// nullable (null = a non-studio-scoped page, e.g. /discover or /platform/*),
/// and there is no EF Core global query filter — same non-tenant shape as
/// AuditLogEntry/HelpSearchLog/FeedbackReport. Authorization for who may read
/// which rows is enforced in the query handlers (IssuerOnly), not a filter.
/// Never stores a raw IP address — CountryCode/City/Region are resolved via
/// GeoIP at ingestion and IpHash is a one-way, unsalted-to-source SHA-256 of
/// the raw IP plus a server pepper, kept only for coarse abuse/dedup signal.
/// </summary>
public class TrafficEvent
{
    private TrafficEvent() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid VisitorId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Role { get; private set; }
    public Guid? StudioId { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public string? CountryCode { get; private set; }
    public string? Country { get; private set; }
    public string? Region { get; private set; }
    public string? City { get; private set; }
    public string? IpHash { get; private set; }
    public string? DeviceType { get; private set; }
    public string? Browser { get; private set; }
    public string? Os { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public static TrafficEvent Create(
        Guid visitorId, Guid? userId, string? role, Guid? studioId, string path,
        string? countryCode, string? country, string? region, string? city,
        string? ipHash, string? deviceType, string? browser, string? os) =>
        new()
        {
            VisitorId = visitorId,
            UserId = userId,
            Role = role,
            StudioId = studioId,
            Path = path.Length > 200 ? path[..200] : path,
            CountryCode = countryCode,
            Country = country,
            Region = region,
            City = city,
            IpHash = ipHash,
            DeviceType = deviceType,
            Browser = browser,
            Os = os,
        };
}
