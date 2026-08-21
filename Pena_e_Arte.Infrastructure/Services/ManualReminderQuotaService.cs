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

    public async Task CheckAndIncrementAsync(Guid studioId, Guid artistId, CancellationToken ct)
    {
        IDatabase db = redis.GetDatabase();
        string key = $"manualreminders:{studioId}:{artistId}:{DateTime.UtcNow:yyyyMMdd}";

        long count = await db.StringIncrementAsync(key);
        if (count == 1)
            await db.KeyExpireAsync(key, TimeSpan.FromHours(25));

        if (count > DailyLimit)
            throw new ManualReminderQuotaExceededException();
    }
}
