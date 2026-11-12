namespace Pena_e_Arte.Domain.Services;

/// <summary>
/// When an unpaid card order may be cancelled by PaymentReconciliationJob. One home for the rule, so
/// the job that acts on it and the owner's unsettled-payments view that shows it can never disagree
/// about the deadline (E05b-2).
/// </summary>
public static class PaymentReleaseRule
{
    /// <summary>
    /// Legacy rows only: an unpaid card payment without <c>HoldExpiresAt</c> is abandoned once its
    /// appointment is this far in the past.
    /// </summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    /// <summary>
    /// The moment after which the job cancels the unpaid order. <c>HoldExpiresAt</c> is authoritative when
    /// set (every deposit; every payment link after B4: 24 h after the order was created). The
    /// appointment-date rule only covers legacy rows without one, so a brand-new link for a past
    /// appointment is not cancelled minutes after it was created.
    /// </summary>
    public static DateTime DeadlineFor(DateTime? holdExpiresAt, DateTime appointmentDate) =>
        holdExpiresAt ?? appointmentDate + StaleAfter;
}
