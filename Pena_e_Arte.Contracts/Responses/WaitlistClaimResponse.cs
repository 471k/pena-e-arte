namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// What the waitlist claim page shows for a verified claim token (C09). Deliberately no personal data: no guest
/// name, email or phone and no notes, since holding the link is all it takes to read this.
/// </summary>
/// <param name="State">Claimable, SlotTaken, Booked, WindowEnded or Cancelled (see WaitlistClaimStates).</param>
public record WaitlistClaimResponse(
    string State,
    string StudioSlug,
    string StudioName,
    string StudioTimezone,
    Guid? ArtistId,
    string? ArtistName,
    DateTime SlotStart,
    int DurationMinutes,
    DateTime WindowEndsAt);

/// <summary>The values <see cref="WaitlistClaimResponse.State"/> takes.</summary>
public static class WaitlistClaimStates
{
    /// <summary>Still offered to this person and still free. Not held: whoever books first gets it.</summary>
    public const string Claimable = "Claimable";
    /// <summary>Still offered, but someone else has booked the time since.</summary>
    public const string SlotTaken = "SlotTaken";
    /// <summary>This person already booked from this offer.</summary>
    public const string Booked = "Booked";
    /// <summary>The claim window ended; the offer moved on.</summary>
    public const string WindowEnded = "WindowEnded";
    /// <summary>The person left the waitlist.</summary>
    public const string Cancelled = "Cancelled";
}
