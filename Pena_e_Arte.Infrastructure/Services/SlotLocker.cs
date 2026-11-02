using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using StackExchange.Redis;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// A cheap first-line guard against an identical double submit: a 30-second Redis key on the exact
/// start minute. It is not the double-booking guard — it can't see overlapping bookings that start at
/// different minutes, and it fails open when Redis is down. The authoritative guard is the database
/// claim every booking path takes before its overlap check (IAppDbContext.ClaimArtistForBookingAsync:
/// a transaction holding a row lock on the artist), which holds with or without Redis.
/// </summary>
public class SlotLocker(IConnectionMultiplexer redis) : ISlotLocker
{
    public async Task<bool> TryAcquireLockAsync(Guid studioId, Guid artistId, DateTime date, CancellationToken ct)
    {
        try
        {
            IDatabase db = redis.GetDatabase();
            string key = $"slot:{studioId}:{artistId}:{date:yyyyMMddHHmm}";
            return await db.StringSetAsync(key, "1", TimeSpan.FromSeconds(30), When.NotExists);
        }
        catch (RedisConnectionException)
        {
            // Fail-open: Redis is unavailable, allow booking to proceed.
            // The 30-second lock window is a UX guard, not a hard business rule. There is no unique
            // index on appointments (an earlier version of this comment said there was; there never
            // was one). What prevents a double booking is the per-artist database lock each booking
            // path takes before its overlap check (IAppDbContext.ClaimArtistForBookingAsync), so
            // failing open here loses only the early rejection, not the guarantee.
            return true;
        }
    }

    public async Task ReleaseLockAsync(Guid studioId, Guid artistId, DateTime date, CancellationToken ct)
    {
        try
        {
            IDatabase db = redis.GetDatabase();
            string key = $"slot:{studioId}:{artistId}:{date:yyyyMMddHHmm}";
            await db.KeyDeleteAsync(key);
        }
        catch (RedisConnectionException)
        {
            // Lock release failure is non-fatal — the key expires in 30 s.
        }
    }
}
