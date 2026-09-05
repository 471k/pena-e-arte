using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using StackExchange.Redis;

namespace Pena_e_Arte.Infrastructure.Services;

// Fails CLOSED, unlike RedisFixedWindowRateLimiter (which fails open — see that class). That
// limiter protects API uptime/abuse at the infrastructure layer, where fail-open is the safer
// default. This quota's entire purpose is bounding real Twilio SMS cost — silently allowing
// unlimited sends during a Redis blip would defeat that purpose, so a Redis outage here
// propagates as a 500 instead of letting the send through.
public class ManualReminderQuotaService(IConnectionMultiplexer redis) : IManualReminderQuotaService
{
    private const int DailyLimit = 20;

    // Atomic Lua: INCR key; set TTL on first hit — mirrors RedisFixedWindowRateLimiter's own
    // script. A plain "INCR then, only if count==1, EXPIRE" (two separate round-trips) would
    // leave a key with no TTL forever if the process crashes between the two calls on a key's
    // very first hit of the day — this way there is no window where that can happen.
    private const string LuaScript = """
        local count = redis.call('INCR', KEYS[1])
        if count == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return count
        """;

    public async Task CheckAndIncrementAsync(Guid studioId, Guid artistId, CancellationToken ct)
    {
        IDatabase db = redis.GetDatabase();
        string key = $"manualreminders:{studioId}:{artistId}:{DateTime.UtcNow:yyyyMMdd}";

        RedisResult result = await db.ScriptEvaluateAsync(
            LuaScript, [key], [(long)TimeSpan.FromHours(25).TotalSeconds]);
        long count = (long)result;

        if (count > DailyLimit)
            throw new ManualReminderQuotaExceededException();
    }
}
