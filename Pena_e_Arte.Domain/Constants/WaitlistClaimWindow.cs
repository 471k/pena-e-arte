namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// How long a notified waitlist entry has to book the slot it was offered, counted from
/// <c>Waitlist.NotifiedAt</c>. One home for the window: WaitlistNotificationExpiryJob expires entries past it,
/// and the claim page and its token read the same value, so "24 hours" means one thing everywhere.
/// The slot is never held during the window (first come, first served); the window only decides how long
/// this person stays the one being offered it before the offer moves on.
/// </summary>
public static class WaitlistClaimWindow
{
    public static readonly TimeSpan Duration = TimeSpan.FromHours(24);

    public static DateTime EndsAt(DateTime notifiedAt) => notifiedAt + Duration;
}
