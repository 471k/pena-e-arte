namespace Pena_e_Arte.Domain.Enums;

/// <summary>
/// How an artist is paid for a session (DEC10, A07a). Four arrangements and only four: they cover every
/// structure the benchmark sources describe, and anything else (guest spots, flash days) is a flat fee
/// with a per-artist override. Stored as its name.
/// </summary>
public enum ArtistPayArrangement
{
    /// <summary>The artist receives a percentage of what the client paid.</summary>
    Commission,

    /// <summary>The artist pays rent (BoothRentSchedule) and keeps what the client paid.</summary>
    BoothRent,

    /// <summary>The studio takes a fixed amount per session; the artist keeps the rest.</summary>
    FlatFeePerSession,

    /// <summary>Nothing is split per session; the artist is paid some other way.</summary>
    Salaried,
}
