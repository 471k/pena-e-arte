namespace Pena_e_Arte.Domain.Interfaces;

public interface ISlotLocker
{
    Task<bool> TryAcquireLockAsync(Guid studioId, Guid artistId, DateTime date, CancellationToken ct = default);
    Task ReleaseLockAsync(Guid studioId, Guid artistId, DateTime date, CancellationToken ct = default);
}
