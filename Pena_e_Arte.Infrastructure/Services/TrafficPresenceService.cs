using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;
using StackExchange.Redis;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Redis key scheme:
///   traffic:presence:zset                traffic:presence:zset — sorted set, member = visitorId, score = last-seen unix ms
///   traffic:presence:detail:{visitorId}   Redis hash of per-visitor detail, same 60s rolling TTL as the zset entry
/// "Expiry" for the zset itself is handled by filtering reads to the last 60s and periodically
/// trimming anything older (done here, on every read, to avoid unbounded growth — matches the
/// pattern the broadcast loop already ticks on every 5s).
/// </summary>
public class TrafficPresenceService(IConnectionMultiplexer redis, IAppDbContext db) : ITrafficPresenceReader
{
    private const string ZSetKey = "traffic:presence:zset";
    private const int TtlSeconds = 60;

    public async Task<TrafficPresenceSnapshot> ReadSnapshotAsync(CancellationToken ct = default)
    {
        IDatabase redisDb = redis.GetDatabase();
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long cutoffMs = nowMs - (TtlSeconds * 1000L);

        try
        {
            await redisDb.SortedSetRemoveRangeByScoreAsync(ZSetKey, double.NegativeInfinity, cutoffMs);

            RedisValue[] visitorIds = await redisDb.SortedSetRangeByScoreAsync(ZSetKey, cutoffMs, double.PositiveInfinity);
            if (visitorIds.Length == 0)
                return new TrafficPresenceSnapshot(0, 0, [], []);

            IBatch batch = redisDb.CreateBatch();
            Task<HashEntry[]>[] detailTasks = visitorIds
                .Select(id => batch.HashGetAllAsync($"traffic:presence:detail:{id}"))
                .ToArray();
            batch.Execute();
            HashEntry[][] details = await Task.WhenAll(detailTasks);

            List<(string VisitorId, Dictionary<string, string> Fields)> raw = [];
            for (int i = 0; i < visitorIds.Length; i++)
            {
                // Empty = the detail hash already expired between ZRANGEBYSCORE and HGETALL
                // (a race, not an error) — skip it, the next tick will trim it from the zset too.
                if (details[i].Length == 0) continue;

                Dictionary<string, string> fields = details[i].ToDictionary(
                    h => h.Name.ToString(), h => h.Value.ToString());
                raw.Add((visitorIds[i]!, fields));
            }

            List<Guid> studioIds = raw
                .Select(r => Guid.TryParse(r.Fields.GetValueOrDefault("studioId"), out Guid sid) ? sid : (Guid?)null)
                .Where(sid => sid.HasValue)
                .Select(sid => sid!.Value)
                .Distinct()
                .ToList();

            Dictionary<Guid, string> studioNames = studioIds.Count == 0
                ? []
                : await db.Studios
                    .Where(s => studioIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.Name })
                    .ToDictionaryAsync(s => s.Id, s => s.Name, ct);

            List<TrafficPresenceVisitor> visitors = [];
            Dictionary<string, int> roleCounts = [];
            int guestCount = 0;

            foreach ((string visitorId, Dictionary<string, string> fields) in raw)
            {
                string? role = NullIfEmpty(fields.GetValueOrDefault("role"));
                Guid? studioId = Guid.TryParse(fields.GetValueOrDefault("studioId"), out Guid sid) ? sid : null;
                DateTime connectedAt = long.TryParse(fields.GetValueOrDefault("connectedAt"), out long connMs)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(connMs).UtcDateTime
                    : DateTimeOffset.UtcNow.UtcDateTime;

                if (role is null) guestCount++;
                else roleCounts[role] = roleCounts.GetValueOrDefault(role) + 1;

                double? latitude = double.TryParse(
                    fields.GetValueOrDefault("latitude"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double lat) ? lat : null;
                double? longitude = double.TryParse(
                    fields.GetValueOrDefault("longitude"),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double lng) ? lng : null;

                visitors.Add(new TrafficPresenceVisitor(
                    VisitorId: visitorId,
                    Role: role,
                    StudioId: studioId?.ToString(),
                    StudioName: studioId.HasValue ? studioNames.GetValueOrDefault(studioId.Value) : null,
                    CountryCode: NullIfEmpty(fields.GetValueOrDefault("countryCode")),
                    City: NullIfEmpty(fields.GetValueOrDefault("city")),
                    Latitude: latitude,
                    Longitude: longitude,
                    DeviceType: NullIfEmpty(fields.GetValueOrDefault("deviceType")),
                    Browser: NullIfEmpty(fields.GetValueOrDefault("browser")),
                    Path: fields.GetValueOrDefault("path") ?? "",
                    ConnectedAt: connectedAt));
            }

            return new TrafficPresenceSnapshot(visitors.Count, guestCount, roleCounts, visitors);
        }
        catch
        {
            // Redis unavailable — live presence unreadable; degrade to an empty snapshot rather
            // than throwing, matching RecordArtistView's existing degrade-gracefully precedent.
            return new TrafficPresenceSnapshot(0, 0, [], []);
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
