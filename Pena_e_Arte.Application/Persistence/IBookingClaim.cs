namespace Pena_e_Arte.Application.Persistence;

/// <summary>
/// An open per-artist booking claim (see <see cref="IAppDbContext.ClaimArtistForBookingAsync"/>): a
/// database transaction holding the artist's booking lock. Commit it right after the booking is saved;
/// disposing it without committing rolls the booking back and releases the lock.
/// </summary>
public interface IBookingClaim : IAsyncDisposable
{
    /// <summary>Commits the transaction (keeping what was saved inside it) and releases the lock.</summary>
    Task CommitAsync(CancellationToken ct);
}
