namespace Pena_e_Arte.Domain.Interfaces;

public interface IManualReminderQuotaService
{
    /// <summary>Throws ManualReminderQuotaExceededException if the artist has already hit
    /// today's cap; otherwise increments the counter and returns normally.</summary>
    Task CheckAndIncrementAsync(Guid studioId, Guid artistId, CancellationToken ct);
}
