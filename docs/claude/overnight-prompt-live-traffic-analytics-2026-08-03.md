# Overnight Prompt — Live Site Traffic & Visitor Analytics (Issuer-Only)

> Feed this file directly to Claude Code as the task prompt, in the main
> **"Pena e Artë - Engineering"** project (the one with repo write access — this
> file was produced in the separate, read-only "Engineering Consultation"
> project and cannot touch source itself). It is self-contained: exact files,
> exact current code, exact target code, exact tests, exact docs to sync. Read
> the whole file before writing anything — later phases depend on decisions
> made in §2–§3. Mode: fully autonomous, no user present.

**Date logged:** 2026-08-03
**Requested by:** Phi
**Origin:** Direct request — issuer/platform-admin should be able to see live
traffic on the site: how many visitors are currently browsing, which
country/IP they're coming from, whether each is an unauthenticated guest or a
signed-in client/artist/owner, plus whatever other static breakdown data
(device, browser, top pages, historical trend) is standard for this kind of
feature. Requester explicitly asked to use an existing open-source
module/service where one exists, to follow current industry standard, and not
to artificially narrow scope.

**Before starting, run:**
```bash
git add -A && git commit -m "checkpoint: before live-traffic-analytics overnight prompt" --allow-empty
git checkout -b feat/live-traffic-analytics
```

---

## 1. Goal

Ship a real-time + historical site-traffic analytics feature visible only to
the `issuer` role, covering the whole app (not a separate marketing site —
this codebase's `frontend/src/features/public/*` pages, e.g. `/discover`,
`/s/{slug}`, `/artist/{slug}`, `/embed`, are the public/unauthenticated
surface, and everything else is the authenticated in-app surface; "traffic on
the site" means both):

- **Live count** of visitors currently on the app in roughly real time (5s
  cadence), broken down into guests (unauthenticated) vs. signed-in users by
  role (`client`/`artist`/`owner`/`issuer`).
- **Geography** — country (and city where resolvable) per visitor, derived
  from IP address at ingestion time, IP itself never persisted (see §3.2 —
  privacy-by-design decision).
- **A live visitor list** — role, studio (if the page is studio-scoped),
  country/city, device/browser, current page, connected-since.
- **Historical trend** — daily visit counts (guest vs. registered, by role),
  top countries, device/browser mix, top pages — at minimum 30 days, viewable
  by the issuer without needing the live tab open.
- Everything scoped so **only `issuer`** can see any of it; no owner/artist/
  client-facing surface ships in this pass (see §3.4 for why, and the backlog
  spec for the deliberately-deferred owner-facing version).

Applicable `CLAUDE.md` rules for this change: #1 (tenant isolation — this
entity is deliberately non-tenant, same shape as `AuditLogEntry`/
`HelpSearchLog`, scoping enforced in handlers, not query filters), #2 (RBAC —
every new endpoint `IssuerOnly` except the anonymous beacon, which is the only
new `AllowAnonymous` endpoint this prompt adds), #3 (never log PII — raw IP is
used transiently and never persisted or logged; see §3.2), #4 (secrets via
env vars — the GeoIP license key, if the MaxMind path is chosen, per §3.1),
#6 (industry benchmark — see §9), #7 (Help sync — see §11, woven into every
phase's own "done" list below, not an appendix).

---

## 2. Decisions already made — implement as specified, do not re-litigate

1. **Scope = whole app, not a separate marketing site.** There is no separate
   marketing site in this repo; `features/public/*` (Discover, studio/artist
   portfolio pages, embed, shared design) is the unauthenticated surface and
   everything under the four role layouts is the authenticated surface. Both
   feed the same traffic feature.

2. **Not a query-filtered/tenant entity.** The new `TrafficEvent` and
   `TrafficDailyAggregate` entities are **not** `TenantEntity` subclasses and
   get **no** `HasQueryFilter()` registered — same non-tenant shape as
   `AuditLogEntry`/`HelpSearchLog`/`FeedbackReport` (verified in
   `Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs` and
   `docs/claude/database.md`). `StudioId` is nullable on both (null = the
   visit wasn't on a studio-scoped page, e.g. `/discover`, `/login`,
   `/platform/*`). Scoping for who can read which rows is enforced entirely in
   the query handlers (`IssuerOnly` policy), matching the established pattern
   — **no new `IgnoreQueryFilters()` usage is needed for reading these two
   tables.** (One new usage *is* needed elsewhere — see decision 6 below.)

3. **Real-time transport = SignalR, already built-in, no third-party.** Matches
   the existing Decisions Log row ("Real-time | SignalR | Built-in to .NET, no
   third-party") and the existing hub pattern in
   `Pena_e_Arte.Infrastructure/Hubs/`. New hub: `TrafficHub`
   (`/hubs/traffic`), `[Authorize(Policy = "IssuerOnly")]` at the class level
   — unlike `ScheduleHub`/`DesignHub`/`NotificationHub` (per-studio groups,
   any authenticated tenant member), this hub has exactly one group,
   `platform:traffic`, because every client in it is by definition already
   issuer-scoped by the hub's own authorization policy. No per-studio
   partitioning needed, no risk of the P0 cross-tenant SignalR bug fixed
   2026-07-26 (that fix validated `tenant_id` against a requested `studioId`
   for hubs any authenticated role can join — this hub only issuers can join
   at all).

4. **Live presence = Redis, not the database.** "Currently active" is
   inherently ephemeral state — modeled as a Redis sorted set (`ZADD`/
   `ZRANGEBYSCORE`, score = last-seen Unix timestamp) plus one Redis hash per
   visitor for the detail payload, both on a rolling TTL. The database
   (`TrafficEvent`) is for **historical** analytics only, written on
   navigation events, not on every heartbeat — see §6.3 for why (write-volume
   control) and the exact key scheme.

5. **GeoIP + device/browser parsing = new NuGet packages, both flagged as
   prerequisites, not silently added.** No such package exists in
   `Pena_e_Arte.Infrastructure.csproj` or `Pena_e_Arte.API.csproj` today
   (verified — full `<PackageReference>` list checked). Per Constraints
   (§10), this is called out explicitly in §3.1 as the one action item Phi
   must complete before the implementing session can finish the GeoIP part —
   the session should still build and ship everything else if this isn't done
   yet (graceful degrade, see §6.2).

6. **One new `IgnoreQueryFilters()` usage — #41.** Resolving `StudioId` for a
   traffic beacon fired from `/artist/{slug}` requires a cross-tenant `Artist`
   slug lookup (`Artist` **is** tenant-filtered, per `database.md`), exactly
   mirroring the existing `RecordArtistView` endpoint's own lookup (approved
   usage #13, `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs` line 190-216).
   `/s/{slug}` beacons resolve `StudioId` via a plain `Studios` query with
   **no** `IgnoreQueryFilters()` needed at all, because `Studio` carries no
   query filter in the first place (`database.md`: "Issuer-level — NOT
   filtered... `IgnoreQueryFilters()` not needed"). Add usage #41 to
   `architecture.md`'s approved-usages table exactly as specified in §7.2.

7. **No separate self-hosted analytics service (Umami/Plausible/Matomo).**
   Considered and rejected for this specific feature — see the ADR in §9.2.
   Open-source *libraries* are used instead (GeoIP database + reader, UA
   parser), integrated directly into this app's own stack, so the feature can
   express "guest vs. client vs. artist vs. owner, scoped or not to a studio"
   — a distinction no general-purpose analytics tool has visibility into,
   because none of them see this app's JWT/tenant model.

---

## 3. Decisions you must make explicit note of / flag, not silently assume

### 3.1 GeoIP provider — action required from Phi before this can fully ship

Verified via current web search (2026-08), not assumed from training data:

- **MaxMind GeoLite2** is the free, industry-standard IP geolocation database
  (95–99% country-level accuracy, 55–80% city-level) and has an official,
  actively-maintained, Apache-2.0-licensed .NET client,
  [`MaxMind.GeoIP2`](https://github.com/maxmind/GeoIP2-dotnet) (current stable
  6.1.0, targets .NET 8 / netstandard2.0 — confirm current version at
  implementation time). **However**, MaxMind's own sign-up flow has gotten
  materially more friction-heavy since late 2019 and especially since
  December 2023: every download now requires an account, a signed EULA, and a
  license key that **expires every 90 days** unless manually reconfirmed by
  email, plus phone-number verification on the account. Realistically ~45
  minutes of one-time paperwork per environment, and it needs periodic
  (quarterly) re-attention or the mmdb stops refreshing. Source:
  [MaxMind GeoLite2 sign-up](https://www.maxmind.com/en/geolite2/signup),
  [dev.maxmind.com GeoLite2 overview](https://dev.maxmind.com/geoip/geoip2/geolite2/).
- **DB-IP Lite** is a lower-friction alternative: free, **no account or
  license key required**, CC BY 4.0 (attribution required on any page that
  displays the data — since this only renders inside the issuer-only
  `/platform/traffic` page, not a public page, this likely satisfies the
  license without a public credit line, but this is Phi's call, not an
  engineering one), monthly-updated `.mmdb` files, and DB-IP markets its
  format as compatible with MaxMind's own reader API. Source:
  [DB-IP Lite](https://db-ip.com/db/lite.php). **Not independently verified
  by this project at the field-schema level** — the implementing session
  must do one real smoke-test lookup against a real DB-IP Lite file before
  relying on it, exactly the way the 2026-07-26 observability entry verified
  Tempo trace-ID behavior empirically rather than assuming compatibility.

**Decision needed from Phi, before or during the overnight run:**
sign up for a free MaxMind account and generate a license key (place it in
`.env` as `GeoIp__MaxMindLicenseKey`, never in source — rule #4), **or**
explicitly say "use DB-IP Lite instead" so the implementing session downloads
that file instead and skips the MaxMind account step. **Either way**, set up
a recurring refresh — MaxMind ships an official `geoipupdate` CLI tool for
exactly this (cron/scheduled task/sidecar container, monthly); DB-IP Lite has
no equivalent tool, so a monthly download would need to be a small script or
manual task. **If neither happens before the session runs**, the code must
still ship and must not fail: see §6.2's graceful-degrade requirement (no
`GeoIp:DatabasePath` configured → country/city fields are always `null`,
nothing else breaks). This is a **build-now-configure-later** situation, not
a blocker — flagged, not silently decided, per this project's own rules on
prerequisite decisions.

### 3.2 Privacy / compliance — flagged, not decided here (Legal territory)

Raw IP addresses are personal data under GDPR (this product's `Nipt` field and
`City` studio field suggest an Albania/EU-adjacent regulatory context). This
prompt's design **never persists a raw IP address or a reversible identifier
derived from it**: the IP is used transiently, in-memory, only long enough to
resolve country/city via GeoIP, then discarded (see the entity design in
§5.1's `TrafficEvent` — there is no `IpAddress` column). The only IP-adjacent
value stored is `IpHash` — a one-way SHA-256 hash of the raw IP plus a
server-side pepper (config value, not in source), kept only for rough abuse/
dedup signal, and it cannot be reversed to the original IP. This mirrors and
slightly exceeds the existing precedent (`RecordArtistView`'s Redis view
counter keeps no IP at all).

**What this prompt does NOT decide, and Phi/Legal should:** whether the
platform's public-facing privacy policy needs a line added covering
"aggregate traffic and geographic analytics of visitors," and whether an
existing cookie/consent banner (if one exists outside this repo, e.g. on a
separate marketing site) needs updating. This is the same category of
question as the NIPT prompt's checksum-verification flag — named explicitly,
not guessed at. Recommendation if asked: this is very likely fine as
legitimate-interest processing (aggregate, non-reversible, security/product
analytics) under GDPR Art. 6(1)(f), but that is not a legal opinion this
project is positioned to finalize.

### 3.3 Anonymous visitor ID = `localStorage`, matching existing precedent

The frontend needs a stable-but-anonymous per-browser identifier
(`VisitorId`) to distinguish unique visitors and to key Redis presence
entries. This prompt specifies `localStorage` (`crypto.randomUUID()`,
persisted under key `pea_visitor_id`), matching this app's own existing
pattern — the 2026-07-26 security-remediation Decisions Log entry confirms
"the SPA's JWT lives in local/session storage." (The Claude-artifact
prohibition on `localStorage` in the platform instructions applies only to
Claude-generated chat artifacts, not to this real application's source code —
noting this explicitly so the implementing session doesn't misapply it.)

### 3.4 Owner-facing version — flagged, not built tonight

Vagaro/Fresha/Boulevard/Mindbody/GlossGenius were checked (web search,
2026-08) for whether they expose anything like "live site traffic" to their
tenant-level business owners — **no evidence found** that any of them do.
This is not a vertical-booking-SaaS-competitor pattern; it's a general
platform-admin / web-analytics pattern (Google Analytics Realtime, Plausible
Live, Cloudflare Web Analytics, PostHog Live all do this, all issuer/operator-
side, not tenant-side). Per CLAUDE.md rule #6 and this project's "if a
recommendation diverges from the benchmark set, say so explicitly" rule: this
feature is correctly scoped to `issuer` only tonight, and an owner-facing "my
studio's public page views" variant (arguably useful — Fresha/Vagaro *do*
show owners simple profile-view counts) is a legitimate fast-follow, not
silently included or silently omitted. **Full backlog spec, not built:**

- `GetMyStudioTrafficQuery` (`OwnerOnly`), reading `TrafficEvent`/
  `TrafficDailyAggregate` filtered to the caller's own `tenant.StudioId` (a
  plain `.Where()`, not `IgnoreQueryFilters()`, since there's no filter to
  bypass — same non-tenant-entity shape, narrowed manually).
  Frontend: a small card on `StudioSettingsPage` or `OwnerDashboardPage`,
  "Profile views this month" + a 7-day sparkline, deliberately much smaller
  than the issuer's full live-traffic page.
  **Open question for Phi, not decided here:** should an owner be able to see
  their *guest* visitors' country/city (arguably useful marketing signal) or
  is that a privacy overreach for a business owner to see about anonymous
  members of the public? This is a genuine product-policy call, named here
  rather than guessed at.

---

## 4. Scope boundary — do not touch

- `frontend/src/features/public/**` page components themselves (the beacon
  hook is mounted once, globally, at the router/layout level — see §6.4 — not
  wired individually into each public page component).
- `Pena_e_Arte.API/Extensions/AuthorizationExtensions.cs` — no new policy
  needed, `IssuerOnly` already exists.
- `Pena_e_Arte.API/Middleware/TenantMiddleware.cs` — `/hubs` is already an
  exempt prefix (verified); no change needed for `TrafficHub`. The new
  beacon endpoint lives under `/api/v1/public/traffic/*`, and `/api/v1/public`
  is **not** in `TenantMiddleware.ExemptPrefixes` today, but that's fine as-is
  — anonymous callers carry no `tenant_id` claim so the middleware no-ops for
  them, exactly like every other existing `/api/v1/public/*` endpoint. Do not
  add `/api/v1/public` to `ExemptPrefixes` — that would be a scope-creeping,
  unrelated change to shared middleware.
- `RecordArtistView` / `portfolio:views:{artistId}` Redis counter — unrelated,
  pre-existing feature. Do not merge or refactor it into this one.
- `Pena_e_Arte.Infrastructure/Extensions/RateLimitingExtensions.cs`'s existing
  four policies (`auth`, `public-write`, `public-read`, `billing`) — reuse
  `public-write` for the beacon endpoint (see §6.4), do not add a fifth
  policy without a clear reason (there isn't one here).
- Any Stripe/billing file — unrelated.
- `docs/user-manual.html` (repo root under `docs/`, 1,700 lines) — **do not
  edit this file.** It is a stale duplicate; the live, served copy is
  `frontend/public/user-manual/index.html` (3,114 lines, confirmed as the
  actual build output target in `docs/claude/overnight-prompt-user-manual-
  2026-07-04.md`). This discrepancy is flagged here, not fixed — reconciling
  or deleting the stale copy is a separate, unrelated cleanup decision.

---

## 5. Domain + persistence layer

### 5.1 New entities

**File:** `Pena_e_Arte.Domain/Entities/TrafficEvent.cs` (NEW)

```csharp
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
```

**File:** `Pena_e_Arte.Domain/Entities/TrafficDailyAggregate.cs` (NEW)

```csharp
namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Nightly rollup of TrafficEvent, one row per (Date, StudioId-or-null,
/// Role-or-null-for-guest, CountryCode-or-null). Written by TrafficRollupJob
/// (Hangfire, daily). Non-tenant, same shape reasoning as TrafficEvent.
/// Kept indefinitely (small, count-only, no visitor-level data) after raw
/// TrafficEvent rows older than 35 days are purged by the same job.
/// </summary>
public class TrafficDailyAggregate
{
    private TrafficDailyAggregate() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateOnly Date { get; private set; }
    public Guid? StudioId { get; private set; }
    public string? Role { get; private set; }
    public string? CountryCode { get; private set; }
    public int VisitCount { get; private set; }
    public int UniqueVisitorCount { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public static TrafficDailyAggregate Create(
        DateOnly date, Guid? studioId, string? role, string? countryCode,
        int visitCount, int uniqueVisitorCount) =>
        new()
        {
            Date = date,
            StudioId = studioId,
            Role = role,
            CountryCode = countryCode,
            VisitCount = visitCount,
            UniqueVisitorCount = uniqueVisitorCount,
        };
}
```

### 5.2 `IAppDbContext` (`Pena_e_Arte.Application/Persistence/IAppDbContext.cs`)

Add, in the existing "no tenant filter" comment groupings (mirror the
`HelpSearchLog`/`AuditLogEntry` rows already there — current file quoted in
full above in research; insert after the `AuditLogEntries` line):

```csharp
    // Traffic analytics — no tenant filter (StudioId nullable, issuer-only cross-tenant reads)
    DbSet<TrafficEvent> TrafficEvents { get; }
    DbSet<TrafficDailyAggregate> TrafficDailyAggregates { get; }
```

### 5.3 `AppDbContext.cs` — add both `DbSet`s under the existing "Issuer-level
(no tenant filter)" grouping (do not add a `HasQueryFilter()` call for
either — that's the point).

### 5.4 Entity configurations (NEW files, mirror `AppointmentConfiguration`'s
shape from `backend.md`/`database.md`)

**File:** `Pena_e_Arte.Infrastructure/Persistence/Configurations/TrafficEventConfiguration.cs`

```csharp
public class TrafficEventConfiguration : IEntityTypeConfiguration<TrafficEvent>
{
    public void Configure(EntityTypeBuilder<TrafficEvent> builder)
    {
        builder.ToTable("traffic_events");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Path).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Role).HasMaxLength(20);
        builder.Property(t => t.CountryCode).HasMaxLength(2);
        builder.Property(t => t.Country).HasMaxLength(100);
        builder.Property(t => t.Region).HasMaxLength(100);
        builder.Property(t => t.City).HasMaxLength(100);
        builder.Property(t => t.IpHash).HasMaxLength(64);
        builder.Property(t => t.DeviceType).HasMaxLength(20);
        builder.Property(t => t.Browser).HasMaxLength(50);
        builder.Property(t => t.Os).HasMaxLength(50);

        builder.HasIndex(t => t.CreatedAt).HasDatabaseName("ix_traffic_events_created_at");
        builder.HasIndex(t => new { t.StudioId, t.CreatedAt })
               .HasDatabaseName("ix_traffic_events_studio_created_at");
    }
}
```

**File:** `Pena_e_Arte.Infrastructure/Persistence/Configurations/TrafficDailyAggregateConfiguration.cs`
— same pattern, plus a unique index on `(Date, StudioId, Role, CountryCode)`
(`ix_traffic_daily_aggregates_bucket`, unique) so the rollup job can safely
upsert.

### 5.5 Migration

```bash
dotnet ef migrations add AddTrafficAnalytics \
  --project Pena_e_Arte.Infrastructure \
  --startup-project Pena_e_Arte.API
```

Both new tables, both non-nullable columns only where the entity marks them
non-nullable (`Path`, `VisitorId`, `CreatedAt`; everything else nullable).
Standard additive migration — no zero-downtime staging needed (brand-new
tables, nothing reads them yet).

---

## 6. Application + Infrastructure layer

### 6.1 `IGeoIpService` (new interface + implementation)

**File:** `Pena_e_Arte.Domain/Interfaces/IGeoIpService.cs` (NEW)

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public interface IGeoIpService
{
    GeoIpResult? Lookup(System.Net.IPAddress ip);
}

public record GeoIpResult(string? CountryCode, string? Country, string? Region, string? City);
```

**File:** `Pena_e_Arte.Infrastructure/Services/GeoIpService.cs` (NEW)

```csharp
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Wraps MaxMind.GeoIP2's DatabaseReader over a local .mmdb file (GeoLite2-City
/// or a compatible DB-IP Lite file — see docs/claude/architecture.md's
/// "Live Traffic Analytics" Decisions Log entry for which was chosen and why).
/// DatabaseReader is thread-safe and reused as a singleton (per MaxMind's own
/// docs). Degrades to always-null gracefully if GeoIp:DatabasePath is unset or
/// the file is missing/unreadable — this must never throw or block ingestion.
/// </summary>
public class GeoIpService : IGeoIpService, IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly ILogger<GeoIpService> _logger;

    public GeoIpService(IConfiguration config, ILogger<GeoIpService> logger)
    {
        _logger = logger;
        string? path = config["GeoIp:DatabasePath"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _logger.LogWarning(
                "GeoIp:DatabasePath not configured or file not found — traffic events will have no country/city data until this is set up. See docs/claude/architecture.md 'Live Traffic Analytics' entry.");
            return;
        }

        try
        {
            _reader = new DatabaseReader(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open GeoIP database at {@Path}", path);
        }
    }

    public GeoIpResult? Lookup(System.Net.IPAddress ip)
    {
        if (_reader is null) return null;
        if (System.Net.IPAddress.IsLoopback(ip) || IsPrivateRange(ip)) return null;

        try
        {
            var city = _reader.City(ip);
            return new GeoIpResult(
                city.Country.IsoCode,
                city.Country.Name,
                city.MostSpecificSubdivision.Name,
                city.City.Name);
        }
        catch (AddressNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeoIP lookup failed for a request — degrading to no location data");
            return null;
        }
    }

    private static bool IsPrivateRange(System.Net.IPAddress ip)
    {
        byte[] b = ip.GetAddressBytes();
        if (b.Length != 4) return false; // IPv6 private-range check out of scope tonight — flag, don't guess
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    public void Dispose() => _reader?.Dispose();
}
```

Register as a **singleton** in
`Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs`
(`services.AddSingleton<IGeoIpService, GeoIpService>();`) — verify the exact
existing registration style in that file before adding (read it first; do not
guess the DI extension method name).

**Config** — add to `.env.example` (placeholder only) and
`appsettings.json`/`appsettings.Development.json`:
```
GeoIp__MaxMindLicenseKey=   # only needed if using MaxMind's geoipupdate tool directly; not read by GeoIpService itself
GeoIp__DatabasePath=/data/geoip/GeoLite2-City.mmdb   # or a DB-IP Lite .mmdb — see §3.1
GeoIp__IpHashPepper=        # random 32+ byte secret, never in source, used only to salt IpHash
```

**IPv6 note, flagged not guessed:** `IsPrivateRange` above only handles IPv4.
The implementing session must check whether this deployment's ingress
actually forwards real client IPv6 addresses (the 2026-07-26
`ForwardedHeaders:TrustedProxyCidr` fix was IPv4-CIDR-shaped, verify against
the live `ForwardedHeadersOptionsBuilder` before assuming IPv6 works
end-to-end) — if IPv6 isn't realistically in play yet, this is fine as-is; if
it is, add the IPv6 private/ULA-range check (`fc00::/7`) before shipping.

### 6.2 `IUserAgentParser` (new interface + implementation)

New NuGet package: **`UAParser.Core`** (community-maintained .NET port of
`ua-parser`, the same ruleset family every major analytics tool — Umami,
Plausible, PostHog — uses under the hood for device/browser/OS detection;
verified current on NuGet, v4.0.5 at research time — confirm current version
at implementation time). This is the second and last new NuGet package this
prompt introduces (alongside `MaxMind.GeoIP2`), both flagged here per
Constraints (§10), not silently added.

**File:** `Pena_e_Arte.Domain/Interfaces/IUserAgentParser.cs` (NEW)
```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public interface IUserAgentParser
{
    (string? DeviceType, string? Browser, string? Os) Parse(string? userAgent);
}
```

**File:** `Pena_e_Arte.Infrastructure/Services/UserAgentParserService.cs` (NEW)
— wraps `UAParser.Parser.GetDefault().Parse(userAgent)`, maps `.Device.Family`
to a coarse `"desktop"|"mobile"|"tablet"|"other"` bucket (UAParser's own
`Device.IsSpider` flag should be used to tag bots/crawlers distinctly rather
than lumping them into "desktop" — real analytics tools always separate bot
traffic; add a `DeviceType = "bot"` bucket and make sure the frontend KPI
cards can filter it out of the human visitor count, since counting search-
engine crawlers as "active visitors" would be a materially misleading metric).
Register as singleton (the underlying parser is stateless/thread-safe per its
own docs — confirm this claim against the actual package docs before treating
it as fact, since it wasn't independently re-verified here at the
line-of-code level).

### 6.3 Redis presence — exact key scheme

```
traffic:presence:zset                        → sorted set, member = visitorId (string), score = last-seen unix ms
traffic:presence:detail:{visitorId}           → Redis hash: { userId?, role?, studioId?, path, countryCode?, city?,
                                                  deviceType?, browser?, connectedAt }, same TTL as the zset entry (60s)
```

- On every beacon call (navigation *or* heartbeat): `ZADD` the visitor's score
  to `now`, `HSET` the detail hash, `EXPIRE` the hash key 60s. (Redis sorted
  sets have no native per-member TTL — "expiry" for the zset is handled by
  filtering `ZRANGEBYSCORE` reads to `now - 60s .. +inf` and periodically
  `ZREMRANGEBYSCORE`-ing anything older, done by the same broadcast loop in
  §6.5 to avoid unbounded growth.)
- **Never block or throw** on Redis failure — wrap in try/catch exactly like
  `RecordArtistView`'s existing pattern (`Redis unavailable — view count not
  recorded; non-critical`), same comment style, applied to presence writes.

### 6.4 Beacon endpoint

**File:** `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs` — add to the existing
`MapPublicEndpoints` group (do not create a new file; this is public-surface
by definition, same file as `RecordArtistView`):

```csharp
group.MapPost("/traffic/beacon", RecordTrafficBeacon)
     .AllowAnonymous().RequireRateLimiting("public-write");
```

Add a row to `architecture.md`'s "AllowAnonymous Exceptions" table:
`POST /api/v1/public/traffic/beacon | Anonymous + authenticated traffic beacon (role/tenant read from JWT when present) | Rate-limited (public-write); no PII accepted in the request body — see §6.4`.

**Request contract** (`Pena_e_Arte.Contracts/Requests/RecordTrafficBeaconRequest.cs`, NEW):
```csharp
public record RecordTrafficBeaconRequest(string Path, bool IsNavigation);
```
Deliberately minimal — no client-supplied `studioId`/`role`/`userId` (those
are derived server-side from the JWT and from resolving `Path` against known
public-page slugs, never trusted from the client, to prevent a caller from
spoofing another studio's/role's traffic numbers).

**Handler** (in `PublicEndpoints.cs`, mirroring `RecordArtistView`'s exact
shape — thin, no MediatR for the Redis write, MediatR command for the
best-effort historical persist):

```csharp
private static async Task<IResult> RecordTrafficBeacon(
    RecordTrafficBeaconRequest request,
    ClaimsPrincipal user,
    HttpContext http,
    IConnectionMultiplexer redis,
    IGeoIpService geoIp,
    IUserAgentParser uaParser,
    ISender mediator,
    IConfiguration config,
    CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(request.Path) || request.Path.Length > 200)
        return Results.BadRequest();

    Guid? userId = user.Identity?.IsAuthenticated == true
        ? Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid uid) ? uid : null
        : null;
    string? role = user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.Role) : null;
    Guid? studioId = Guid.TryParse(user.FindFirstValue("tenant_id"), out Guid sid) ? sid : null;

    System.Net.IPAddress? ip = http.Connection.RemoteIpAddress;
    GeoIpResult? geo = ip is not null ? geoIp.Lookup(ip) : null;
    (string? deviceType, string? browser, string? os) = uaParser.Parse(http.Request.Headers.UserAgent);
    string? ipHash = ip is not null ? HashIp(ip, config["GeoIp:IpHashPepper"]) : null;

    // Visitor id comes from the client-generated anonymous identifier, sent as a header
    // (not the request body — keeps the DTO free of anything resembling a tracking id
    // a reviewer might mistake for a required business field).
    if (!Guid.TryParse(http.Request.Headers["X-Visitor-Id"], out Guid visitorId))
        return Results.BadRequest();

    try
    {
        IDatabase db = redis.GetDatabase();
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string detailKey = $"traffic:presence:detail:{visitorId}";
        await db.SortedSetAddAsync("traffic:presence:zset", visitorId.ToString(), nowMs);
        await db.HashSetAsync(detailKey,
        [
            new HashEntry("userId", userId?.ToString() ?? ""),
            new HashEntry("role", role ?? ""),
            new HashEntry("studioId", studioId?.ToString() ?? ""),
            new HashEntry("path", request.Path),
            new HashEntry("countryCode", geo?.CountryCode ?? ""),
            new HashEntry("city", geo?.City ?? ""),
            new HashEntry("deviceType", deviceType ?? ""),
            new HashEntry("browser", browser ?? ""),
        ]);
        await db.KeyExpireAsync(detailKey, TimeSpan.FromSeconds(60));
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
```

**`RecordTrafficEventCommand`** (`Pena_e_Arte.Application/Traffic/Commands/RecordTrafficEventCommand.cs`,
NEW) — thin handler mirroring `LogHelpSearchHandler`'s exact shape: resolves
`StudioId` from `Path` when a route-based slug is present and the JWT didn't
already carry a `tenant_id` (i.e., an anonymous `/s/{slug}` or `/artist/{slug}`
visit), using the `Studios`/`Artists` lookups per decision §2.6, then
`db.TrafficEvents.Add(TrafficEvent.Create(...)); await db.SaveChangesAsync(ct);`.
No `IPipelineBehavior` needed — this isn't an `IAuditableCommand` (it's
analytics, not a trust/compliance action) and has no `IQuotaCheckedCommand`
concerns.

**Path-to-StudioId resolution — verify against the live route table before
shipping:** check `frontend/src/app/router.tsx` for every route pattern that
embeds a slug or token directly in the path (not query string) — e.g. a
share-link route if one embeds `DesignShareToken` in the path segment rather
than as a route param read separately. If any such route exists, the raw
`Path` string must be redacted/truncated at that segment before being sent
to the backend at all (frontend-side, in the beacon hook), so a live,
still-valid share token never ends up sitiing in `TrafficEvent.Path`. This is
flagged as a "verify against live source" item, not assumed either way.

### 6.5 `TrafficHub` + broadcast loop

**File:** `Pena_e_Arte.Infrastructure/Hubs/TrafficHub.cs` (NEW)
```csharp
[Authorize(Policy = "IssuerOnly")]
public class TrafficHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "platform:traffic");
        TrafficBroadcastService.ConnectedCount.Increment();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        TrafficBroadcastService.ConnectedCount.Decrement();
        await base.OnDisconnectedAsync(exception);
    }
}
```

**File:** `Pena_e_Arte.Infrastructure/Services/TrafficBroadcastService.cs` (NEW)
— `BackgroundService` using `PeriodicTimer` (5s), only does Redis work when
`ConnectedCount > 0` (a simple thread-safe counter, e.g.
`System.Threading.Interlocked`-backed, exposed as a small static/singleton
helper class referenced by the hub above — do not use `static` mutable state
carelessly; wrap it in a proper singleton service registered in DI and
injected into both the hub and the background service, rather than a bare
`static` field, to keep it testable). Each tick: `ZREMRANGEBYSCORE` anything
older than 60s, `ZRANGEBYSCORE` the rest, `HGETALL` each detail key (Redis
pipelining/batch, not N sequential round-trips — use `IBatch` or
`Task.WhenAll` over `IDatabaseAsync` calls), build a snapshot DTO (counts by
guest/role, full visitor list), broadcast via
`IHubContext<TrafficHub>.Clients.Group("platform:traffic").SendAsync("TrafficSnapshotUpdated", snapshot, ct)`.
Register as a hosted service (`services.AddHostedService<TrafficBroadcastService>()`)
and as the DI-registered presence counter, both in
`InfrastructureServiceExtensions.cs`.

Add `"TrafficSnapshotUpdated"` to the SignalR Event Naming Convention table in
`architecture.md` (§155 area) alongside the existing event names.

### 6.6 Issuer read queries

**File:** `Pena_e_Arte.Application/Platform/Queries/GetLiveTrafficSnapshotQuery.cs`
(NEW) — reads the same Redis structures as §6.5 on-demand (for the initial
page load before the first SignalR push arrives), `IssuerOnly`.

**File:** `Pena_e_Arte.Application/Platform/Queries/GetTrafficHistoryQuery.cs`
(NEW) — reads `TrafficDailyAggregate` (no `IgnoreQueryFilters()` needed, per
decision §2.2), grouped by day, guest vs. each role, last N days (default 30,
clamp 1–90 mirroring `GetHelpSearchInsightsQuery`'s `Days` clamp pattern).

**File:** `Pena_e_Arte.Application/Platform/Queries/GetTrafficBreakdownQuery.cs`
(NEW) — top countries, device/browser mix, top pages, over the same window,
also from `TrafficDailyAggregate` where possible; top-pages needs raw
`TrafficEvent` (not aggregated by path), so query the last 35 days of raw
rows directly (this is exactly why the 35-day raw retention window in §7 was
chosen — long enough for a "top pages this month" view without keeping raw
data forever).

**Endpoints** — add to the existing `IssuerOnly`-grouped
`PlatformEndpoints.cs` (do not create a new endpoints file — this is
platform/issuer surface, same file as `GetHelpSearchInsights`/`GetAuditLog`):
```csharp
group.MapGet("traffic/live", GetLiveTrafficSnapshot);
group.MapGet("traffic/history", GetTrafficHistory);
group.MapGet("traffic/breakdown", GetTrafficBreakdown);
```

### 6.7 `Program.cs` wiring
- `app.MapHub<TrafficHub>("/hubs/traffic");` alongside the other three
  `MapHub` calls.
- `builder.Services.AddHostedService<TrafficBroadcastService>();`
- Register `IGeoIpService`/`GeoIpService` and `IUserAgentParser`/
  `UserAgentParserService` as singletons in
  `InfrastructureServiceExtensions.cs` (read that file first to match its
  existing registration style exactly — do not guess the method chain).
- Add `TrafficRollupJob` recurring job (see §7) alongside the three existing
  `recurringJobs.AddOrUpdate<...>` calls, `Cron.Daily(hour: 2, minute: 30)` —
  Hangfire's `Cron` helper signature must be checked against the actual
  installed `Hangfire.Core` version (2.0.3-era API) before assuming an
  `hour:`/`minute:` overload exists; if it doesn't, use the raw cron string
  `"30 2 * * *"` instead.

---

## 7. Rollup + retention job

**File:** `Pena_e_Arte.Infrastructure/Jobs/TrafficRollupJob.cs` (NEW), same
class shape as `IndustryReportJob`/`PaymentReconciliationJob` (Hangfire job
class, `RunAsync(CancellationToken)`, uses `IgnoreQueryFilters()`-class
system-context DB access — actually not needed here since these tables carry
no filter at all, per §2.2). Runs daily at 02:30 (staggered from the existing
02:00 payment-reconciliation and 03:00 Instagram sync jobs):

1. Group yesterday's `TrafficEvent` rows by `(Date, StudioId, Role,
   CountryCode)`, upsert into `TrafficDailyAggregate` (`VisitCount` = row
   count, `UniqueVisitorCount` = distinct `VisitorId` count per bucket).
2. Delete `TrafficEvent` rows older than 35 days (`CreatedAt < UtcNow.AddDays(-35)`).
3. Log a structured summary (`rows aggregated`, `rows purged`) — no PII, per
   rule #3 (these tables never had PII to begin with, per §3.2's design).

Add this as approved usage entry in the "Hangfire background jobs" grouping
(usage #36 in the current table already covers "Hangfire background jobs run
with no request/tenant scope at all" as a class — confirm whether this job
needs its own row or fits under that existing umbrella entry; if the existing
entry's wording is job-class-generic, a one-line addition naming
`TrafficRollupJob` alongside the others there is enough, no new numbered row
needed since these tables have no filter to ignore in the first place).

---

## 8. Frontend

### 8.1 Beacon hook

**File:** `frontend/src/shared/hooks/useTrafficBeacon.ts` (NEW) — mounted
once at the app root (find the top-level layout/router wrapper component,
e.g. wherever `RoleGuard`'s parent renders for every route, verify the exact
mount point against the live `router.tsx`/`App.tsx` before picking one —
do not duplicate it into every page). Behavior:
- On mount: read or generate `pea_visitor_id` from `localStorage`
  (`crypto.randomUUID()` if absent).
- On every `location` change (`useLocation()` from `react-router-dom`): POST
  `{ path: location.pathname, isNavigation: true }` with header
  `X-Visitor-Id`.
- `setInterval` every 20s, only while `document.visibilityState === "visible"`
  (listen to `visibilitychange`, pause/resume the interval accordingly —
  don't just check the flag once): POST the current path with
  `isNavigation: false`.
- No RTK Query needed for this (it's a fire-and-forget beacon, not a cached
  read) — a plain `fetch`/existing `baseQuery`'s underlying client is fine;
  check `shared/api/baseQuery.ts` for the existing base URL pattern and reuse
  it rather than hardcoding `/api/v1/public/traffic/beacon` — no hardcoded
  API URLs, per `conventions.md`.

### 8.2 `platformApi.ts` additions

```typescript
getLiveTrafficSnapshot: builder.query<LiveTrafficSnapshotResponse, void>({
  query: () => "platform/traffic/live",
  providesTags: ["LiveTraffic"],
}),
getTrafficHistory: builder.query<TrafficHistoryResponse, { days?: number } | void>({
  query: (args) => `platform/traffic/history${args?.days ? `?days=${args.days}` : ""}`,
  providesTags: ["TrafficHistory"],
}),
getTrafficBreakdown: builder.query<TrafficBreakdownResponse, { days?: number } | void>({
  query: (args) => `platform/traffic/breakdown${args?.days ? `?days=${args.days}` : ""}`,
  providesTags: ["TrafficBreakdown"],
}),
```
Add `"LiveTraffic"`, `"TrafficHistory"`, `"TrafficBreakdown"` to `tagTypes`.
Add matching response types to `platform.types.ts` (`LiveTrafficSnapshotResponse`
— `{ totalActive, guestCount, roleCounts: Record<Role, number>, visitors:
LiveVisitorResponse[] }`; `LiveVisitorResponse` — `{ visitorId, role, studioId,
studioName, countryCode, city, deviceType, browser, path, connectedAt }`;
`TrafficHistoryResponse`/`TrafficBreakdownResponse` per the query shapes in
§6.6). No `any` anywhere, per `frontend.md`'s TypeScript rules.

### 8.3 `useLiveTrafficHub` hook

**File:** `frontend/src/shared/hooks/useLiveTrafficHub.ts` (NEW), same shape
as the existing `useSignalR.ts` (`HubConnectionBuilder`, `.withUrl("/hubs/traffic",
...)`, `.withAutomaticReconnect()`), listens for `"TrafficSnapshotUpdated"`
and dispatches `platformApi.util.updateQueryData("getLiveTrafficSnapshot",
undefined, () => payload)` (direct cache update, not just an invalidate —
this is a high-frequency push, invalidating and refetching every 5s would be
wasteful; write the pushed payload straight into the RTK Query cache instead).
**Fix the known gap, don't repeat it:** the existing Decisions Log flags that
`useSupportHub` never rejoins its group after SignalR's automatic reconnect —
`useLiveTrafficHub` must explicitly call `connection.invoke` to (re)join
`"platform:traffic"` is not actually needed here since `TrafficHub` auto-adds
every connection to the one group in `OnConnectedAsync` itself (no explicit
per-client `JoinX` call exists for this hub, unlike `ScheduleHub`/
`SupportHub`) — so the reconnect gap that affects `useSupportHub` does not
apply to this hub by construction. Note this reasoning inline as a comment so
a future reader doesn't "fix" a bug that isn't there.

### 8.4 `LiveTrafficPage.tsx`

**File:** `frontend/src/features/platform/components/LiveTrafficPage.tsx`
(NEW), route `/platform/traffic`. Sections, in order:
1. **KPI row** — "Active Now" total, Guests, Clients, Artists, Owners (5
   `KpiCard`s, reuse the existing `KpiCard` component/pattern from
   `IssuerDashboardPage.tsx` rather than inventing a new card style).
2. **Live visitor table** — role/guest badge, studio name (or "—"),
   country flag emoji + city, device/browser, current path, "connected
   Xs ago" (live-ticking relative time). Empty state: "No one's on the site
   right now." — per this project's own proactive-recommendations mandate,
   this needs a real empty state, not a blank table (`conventions.md`/
   `architecture.md` UX expectations).
3. **Historical trend chart** — hand-rolled inline SVG line/area chart,
   **matching `MrrChart.tsx`'s existing pattern exactly** (no Recharts/
   Chart.js — neither is an existing dependency, and adding one would violate
   the "no new npm packages without flagging" constraint for something this
   project already solves with plain SVG). Toggle: Guests vs. each role,
   stacked or overlaid — implementer's call on the clearest rendering, but
   must show both guest and registered trend, not just a total.
4. **Top countries table** + **device/browser breakdown** (simple bar
   list, same visual language as the top-queries table in
   `HelpInsightsPage.tsx` — check that file for the exact list-with-count-bar
   pattern already in use before inventing a new one).
5. Optional (nice-to-have, not required for "done"): a small world map using
   **Leaflet** (`react-leaflet`) — **already an existing dependency**
   (verified in `frontend/package.json`, used by the studio-nearby map
   feature), so this genuinely costs zero new npm packages if built. Only
   include this if time remains after 1–4 and all tests/Help-sync are done;
   do not let a map delay the rest of this feature.

Loading/error/empty states on every section (skeletons matching
`KpiGridSkeleton`'s existing pattern; RTK Query's own `isLoading`/`isError`
flags, no custom fetch logic — no `useEffect` for data fetching, per
`conventions.md`).

### 8.5 Nav + route + tour

**`frontend/src/layouts/IssuerLayout.tsx`** — add one entry to `NAV_ITEMS`:
```typescript
{ label: "Live Traffic", href: "/platform/traffic", icon: <Activity className="h-4 w-4" />, tourId: "issuer-traffic-nav" },
```
(import `Activity` from `lucide-react`, already a dependency; place it
directly after "Dashboard" — traffic is a top-level, frequently-checked view,
not a buried admin tool, matching how competitor platform-admin tools surface
real-time monitoring prominently, not nested).

**`frontend/src/app/router.tsx`** — add
`{ path: "traffic", element: <LiveTrafficPage /> }` to the existing
`platform` children array (alongside `studios`/`plans`/`subscriptions`/etc.),
and add the export to `frontend/src/features/platform/index.ts`.

**`frontend/src/features/help/tours/issuerTour.ts`** — add one step, matching
the exact `TourStep` shape already used:
```typescript
{
  targetSelector: '[data-tour="issuer-traffic-nav"]',
  title: "Live traffic",
  body: "See who's on the site right now — guests and signed-in users by role, where they're browsing from, and trends over time.",
},
```
Insert it after the "Platform dashboard" step and before "All studios" (it's
a dashboard-adjacent, frequently-used view — matches how the existing tour
orders nav-adjacent steps by the same left-to-right nav order).

---

## 9. Industry-standard benchmark note

**Vertical booking/scheduling SaaS set** (Vagaro, Fresha, Boulevard, Mindbody,
Zenoti, GlossGenius, Booksy, Mangomint, Schedulicity, Square Appointments):
checked via web search, 2026-08. No evidence any of them expose a "live site
traffic" or real-time visitor view to tenant business owners. This confirms
§3.4's framing — this is deliberately **not** benchmarked against that set,
because it isn't a pattern from that category at all.

**General B2B SaaS platform-admin benchmark** (per `CLAUDE.md` rule #6's
issuer-role clause): real-time visitor/session monitoring is standard
platform-operator tooling — Google Analytics Realtime, Plausible's Live view,
Cloudflare Web Analytics, Vercel Analytics, and PostHog's Live Events all
show "who's on the site right now," geography, and device breakdown, refreshed
on a several-second cadence (Google Analytics Realtime and Plausible Live
both use a similar ~5–10s refresh cadence to what's specified in §6.5 — this
prompt's 5s choice is squarely in that band, verified, not guessed).

### 9.1 Open-source components used (per the request to reuse existing
open-source modules rather than build from nothing)

- **MaxMind GeoLite2** (or DB-IP Lite, per §3.1) — the free geolocation
  database every one of the above analytics products relies on for country/
  city resolution; not something any of them wrote themselves either.
- **`ua-parser` family** (via `UAParser.Core`) — same regex-ruleset lineage
  Umami/Plausible/PostHog use for device/browser/OS classification.
- **SignalR** — already this codebase's own established real-time transport,
  used instead of a third-party push service (Pusher/Ably), matching the
  existing Decisions Log entry.

### 9.2 ADR — why not a whole self-hosted analytics service (Umami/Plausible/Matomo)

Considered and rejected, reasoning below, verified via web search (2026-08):

| | Umami | Plausible CE | Matomo |
|---|---|---|---|
| License | MIT | AGPL | GPL |
| Stack | Next.js + Postgres | Elixir + ClickHouse | PHP + MySQL |
| Real-time view | Yes | Yes ("Live") | Yes |
| Geo/device breakdown | Yes (city-level self-hosted) | Yes | Yes, most complete |
| **Knows this app's roles/tenants/guest-vs-signed-in** | **No** | **No** | **No** |

None of the three can distinguish "guest" from "authenticated client/artist/
owner/issuer" or attribute a visit to a specific studio tenant, because none
of them have any visibility into this app's JWT/`tenant_id` model — that's
the entire ask here, and it's inherently application-specific, not a generic
web-analytics capability. Adopting one would mean either (a) embedding it via
iframe and losing the role/tenant breakdown entirely, or (b) consuming its
API and re-building the exact role-aware UI this prompt already specifies —
at which point the separate service adds a whole new deployment (its own
database, its own container, its own upgrade cadence) for zero net benefit
over using its underlying open-source *libraries* (GeoIP + UA parsing)
directly in this app's own stack, which is what §6 does instead. If a future
need arises for public-facing SEO/marketing analytics (traffic sources,
referrers, campaign UTMs) that this issuer-only feature does not cover, Umami
is the better-fit option of the three for a from-scratch adoption then (MIT,
lightest footprint, genuinely turnkey) — named here as a forward-looking
option, not adopted tonight.

---

## 10. Constraints (restated in full, as required — not a diff from prior prompts)

- **New NuGet packages:** `MaxMind.GeoIP2` and `UAParser.Core` — both flagged
  above (§2.5, §6.1, §6.2), not silently added. No other new package,
  frontend or backend (Leaflet/`react-leaflet` in §8.4 is already installed).
- **No `useEffect` for data fetching** — RTK Query only; the beacon hook is
  the one deliberate exception (`useEffect` there is firing a beacon, not
  fetching/caching data — matches the same class of exception the existing
  `useSignalR.ts` already uses `useEffect` for, connection lifecycle, not
  data fetching).
- **TypeScript strict, no `any`** — every new response/request type
  explicitly declared in `platform.types.ts`/`Contracts`.
- **Explicit C# types, no unclear `var`.**
- **No business logic in endpoints** — `RecordTrafficBeacon` in §6.4 is
  intentionally thin (Redis write + a single `mediator.Send`), matching
  `RecordArtistView`'s own established exception for non-domain analytics
  writes; all real logic (StudioId resolution, entity creation) lives in
  `RecordTrafficEventCommand`'s handler.
- **Tenant isolation via EF Core global query filters everywhere** — except
  the deliberately-non-tenant `TrafficEvent`/`TrafficDailyAggregate` shape
  (§2.2) and the one new `IgnoreQueryFilters()` usage, #41 (§2.6, §7.2 of
  `architecture.md`'s table).
- **Every endpoint has `.RequireAuthorization()` with the correct policy** —
  except the one new `AllowAnonymous` beacon endpoint, added to the
  `architecture.md` exceptions table per §6.4.
- **Never log PII** — no raw IP, no name/email, ever, in a log line or a
  persisted column (§3.2, §5.1).
- **Structured logs only** — `_logger.LogWarning("...{@Path}", path)` style,
  matching `backend.md`'s Serilog convention.
- **Tests ship with every change** — see §12.

---

## 11. Help-sync obligations (per phase, not an appendix)

This feature is entirely new user-visible surface for the issuer role, so
every layer needs an update — folded into each relevant phase's own
definition of done above, restated together here for the final checklist:

1. **`frontend/src/features/help/helpContent.ts`** — new article, inserted
   directly after the existing `"issuer-audit-log"` entry (same file
   location verified above, line ~961) and before `"owner-audit-log"`:
   ```typescript
   {
     id: "issuer-live-traffic",
     roles: [Issuer],
     title: "See who's on the site right now",
     route: "/platform/traffic",
     keywords: ["live traffic", "visitors", "analytics", "site traffic", "geography", "country", "real-time"],
     summary: "A real-time view of everyone currently browsing the platform — guests and signed-in users by role — plus historical trends by country, device, and page.",
     steps: [
       "Open Live Traffic from the platform nav.",
       "The top row shows how many people are on the site right now, split into Guests and each signed-in role.",
       "The live table lists each current visitor's role, studio (if applicable), approximate location, device, and current page.",
       "Scroll down for the historical trend chart, top countries, and device/browser breakdown over the last 30 days.",
     ],
     tips: ["Country/city is approximate, resolved from IP address — it can be off by a city or two, especially on mobile networks.", "No visitor is ever identified by name, email, or IP address here — only role, rough location, and device."],
     relatedArticleIds: ["issuer-audit-log"],
   },
   ```
2. **`frontend/public/user-manual/index.html`** (confirmed the live, served
   copy — §4) — add a corresponding section, matching that file's existing
   structure/style for issuer-only features (model it on however the
   existing Audit Log / Help Insights sections are written there — read the
   file's existing issuer section first and match its heading level, tone,
   and screenshot-placeholder convention exactly, don't invent a new
   sub-structure). **Do not touch `docs/user-manual.html`** — legacy/stale
   per §4.
3. **`frontend/src/features/help/tours/issuerTour.ts`** — new step added, §8.5
   above (not "no change needed" — this is a new top-level nav item, the
   clearest possible case for needing a tour step).

---

## 12. Test requirements

**Backend unit tests** (`tests/Pena_e_Arte.UnitTests/`):
- `GeoIpServiceTests` — returns `null` gracefully when no database configured;
  returns `null` for loopback/private IPs; returns a populated result for a
  known-good test IP against a real small test `.mmdb` fixture (MaxMind
  publishes a tiny test database for exactly this purpose — use that, not a
  production-size file, in the test project).
- `UserAgentParserServiceTests` — a handful of real UA strings (Chrome/
  desktop, Safari/iOS, a known bot UA) map to the expected
  `DeviceType`/`Browser`/`Os`/bot bucket.
- `RecordTrafficEventHandlerTests` — resolves `StudioId` correctly for an
  authenticated request (from `tenant_id`), for an anonymous `/s/{slug}`
  request (via `Studios` lookup, no `IgnoreQueryFilters`), and for an
  anonymous `/artist/{slug}` request (via the new `IgnoreQueryFilters` usage
  #41); truncates an over-long `Path`; never persists a raw IP field (assert
  the entity has no such property/column at all, a compile-time-enforced
  guarantee worth a test comment noting *why* it's structurally impossible,
  not just a runtime assertion).
- `TrafficRollupJobTests` — aggregates correctly across a multi-day fixture;
  purges rows older than 35 days and does not purge newer ones; idempotent on
  double-run (upsert, not insert-only).
- `GetLiveTrafficSnapshotHandlerTests`/`GetTrafficHistoryHandlerTests` —
  correct grouping/counting; `IssuerOnly` enforcement covered at the endpoint
  authorization level (integration test below), not re-asserted here.

**Backend integration tests** (`tests/Pena_e_Arte.IntegrationTests/`):
- `POST /api/v1/public/traffic/beacon` — anonymous call succeeds (204);
  missing `X-Visitor-Id` header returns 400; an authenticated call correctly
  attributes `UserId`/`Role`/`StudioId` from the JWT; rate-limited after 30
  requests/min per IP (reuse the existing `public-write` policy test pattern
  from the Redis-rate-limiting test suite).
- `GET /api/v1/platform/traffic/*` — all three new endpoints return 401/403
  for non-issuer roles, 200 for issuer.
- `TrafficHub` — a non-issuer JWT is rejected at connection; an issuer JWT
  connects and is added to `"platform:traffic"`.

**Frontend component tests** (`__tests__/LiveTrafficPage.test.tsx`, new,
mirroring `HelpInsightsPage.test.tsx`'s existing shape): loading state
(skeletons render), empty state ("No one's on the site right now."),
populated state (KPI counts + table rows render from mock data), error state
(RTK Query error surfaces a message, not a blank page). `useTrafficBeacon`
hook test: fires on route change, respects `visibilitychange` pausing.

---

## 13. Final self-check / verification checklist (run before declaring done)

- [ ] `dotnet build` clean, `dotnet test` green (all new + existing suites).
- [ ] `pnpm build`, `pnpm test`, `pnpm lint` clean.
- [ ] Migration applies cleanly against a fresh DB and against the seeded
      dev DB.
- [ ] No file outside this prompt's scope was touched — diff reviewed against
      §4's do-not-touch list.
- [ ] No raw IP address appears in any persisted column, log line, or API
      response — grep the diff for `RemoteIpAddress` usages and confirm each
      one is transient (used, then discarded within the same method).
- [ ] `architecture.md` updated: new Feature Module Map row, new
      `IgnoreQueryFilters()` approved-usages row (#41), new `AllowAnonymous`
      exceptions row, new SignalR event name, new Decisions Log entry
      describing the GeoIP provider actually used (MaxMind vs. DB-IP —
      whichever Phi chose per §3.1) and why, including the ADR from §9.2.
- [ ] `helpContent.ts`, `frontend/public/user-manual/index.html`, and
      `issuerTour.ts` all updated (§11) — confirmed `docs/user-manual.html`
      was **not** touched.
- [ ] GeoIP degrades gracefully with `GeoIp:DatabasePath` unset (test this
      explicitly — comment out the config locally, confirm the app still
      starts and beacons still succeed with null country/city).
- [ ] Live visitor count on a real local run actually reflects a real browser
      tab hitting the app (manual smoke test, not just unit tests — matches
      this project's own "verify empirically" precedent from the
      observability entry).
- [ ] For audits/self-review: every checklist row here has a verdict, no
      blanks.

---

## 14. Final deliverable spec

**Code files** (new): `TrafficEvent.cs`, `TrafficDailyAggregate.cs`,
`TrafficEventConfiguration.cs`, `TrafficDailyAggregateConfiguration.cs`,
migration `AddTrafficAnalytics`, `IGeoIpService.cs`, `GeoIpService.cs`,
`IUserAgentParser.cs`, `UserAgentParserService.cs`,
`RecordTrafficEventCommand.cs` (+handler), `GetLiveTrafficSnapshotQuery.cs`,
`GetTrafficHistoryQuery.cs`, `GetTrafficBreakdownQuery.cs`, `TrafficHub.cs`,
`TrafficBroadcastService.cs`, `TrafficRollupJob.cs`,
`RecordTrafficBeaconRequest.cs`, response DTOs, `useTrafficBeacon.ts`,
`useLiveTrafficHub.ts`, `LiveTrafficPage.tsx`, plus edits to
`PublicEndpoints.cs`, `PlatformEndpoints.cs`, `IAppDbContext.cs`,
`AppDbContext.cs`, `InfrastructureServiceExtensions.cs`, `Program.cs`,
`platformApi.ts`, `platform.types.ts`, `IssuerLayout.tsx`, `router.tsx`,
`features/platform/index.ts`, `issuerTour.ts`, `helpContent.ts`,
`frontend/public/user-manual/index.html`, `.env.example`,
`appsettings.json`/`appsettings.Development.json`, both `.csproj` files
(new package references).

**Docs files** (this consultation project's own follow-up, not tonight's
implementing session's job — noted here for completeness): after the
implementing session finishes, this consultation project should be asked to
review the actual diff and update `architecture.md`'s Feature Module Map/
Decisions Log entries if the implementing session's own edits to those
sections need refinement.

**Commit message:**
```
feat(platform): add live site traffic & visitor analytics for issuer

- TrafficEvent/TrafficDailyAggregate (non-tenant, mirrors AuditLogEntry shape)
- GeoIP (MaxMind GeoLite2 or DB-IP Lite) + UA parsing, IP never persisted
- Redis live presence + TrafficHub (SignalR, 5s broadcast) + TrafficRollupJob
- IssuerOnly: /platform/traffic live view, history, country/device breakdown
- IgnoreQueryFilters approved usage #41 (artist-slug StudioId resolution)
- Help Menu, user manual, issuer onboarding tour updated
```
