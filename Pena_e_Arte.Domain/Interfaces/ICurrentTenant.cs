namespace Pena_e_Arte.Domain.Interfaces;

public interface ICurrentTenant
{
    Guid StudioId { get; }
    bool IsSet { get; }
    void SetTenant(Guid studioId);

    /// <summary>
    /// Back to no tenant, the state a Hangfire job starts in. A job that sets a tenant per studio clears it when
    /// its per-studio work ends, so nothing after the loop (a save, a send) runs as whichever studio came last.
    /// </summary>
    void ClearTenant() => SetTenant(Guid.Empty);
}
