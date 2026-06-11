using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using StackExchange.Redis;

namespace Pena_e_Arte.Infrastructure.Services;

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
        catch (RedisConnectionException ex)
        {
            throw new ServiceUnavailableException("Booking service temporarily unavailable. Please try again shortly.") { Data = { ["inner"] = ex.Message } };
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
