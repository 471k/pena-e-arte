using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Services;

/// <summary>Why a reschedule is refused before any time is even looked at.</summary>
public enum RescheduleBlock
{
    None,
    /// <summary>The appointment is Cancelled, Completed or NoShow.</summary>
    AppointmentClosed,
    /// <summary>A client is inside the studio's notice window (ClientCancellationPolicy).</summary>
    InsideNoticeWindow,
}

/// <param name="NoticeWindowApplies">False for a staff caller: staff are never held to the notice window.</param>
public sealed record RescheduleEligibilityDecision(
    RescheduleBlock Block,
    int NoticeWindowHours,
    bool NoticeWindowApplies)
{
    public bool Allowed => Block == RescheduleBlock.None;
}

/// <summary>
/// The one place that decides whether an appointment may be moved at all (E04b, A02): the status gate,
/// then, for a client, the notice window. RescheduleAppointmentHandler (which then checks the slot under
/// the booking lock) and the reschedule preview both call it, so the dialog can say "you can't move
/// this" before a time is picked and the save can never disagree. The notice window itself comes from
/// <see cref="ClientCancellationPolicy"/>, never a second source. Pure: no database, no clock.
/// </summary>
public static class RescheduleEligibility
{
    public static RescheduleEligibilityDecision Decide(
        AppointmentStatus status, bool isClient, DepositRule? rule, DateTime appointmentDate, DateTime nowUtc)
    {
        int windowHours = ClientCancellationPolicy.NoticeWindowHours(rule);

        if (status is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow)
            return new RescheduleEligibilityDecision(RescheduleBlock.AppointmentClosed, windowHours, isClient);

        // Client self-reschedule is cutoff-gated by the same notice window as self-cancel; staff
        // reschedule is unaffected.
        if (isClient && !ClientCancellationPolicy.IsWithinNoticeWindow(rule, appointmentDate, nowUtc))
            return new RescheduleEligibilityDecision(RescheduleBlock.InsideNoticeWindow, windowHours, true);

        return new RescheduleEligibilityDecision(RescheduleBlock.None, windowHours, isClient);
    }

    /// <summary>
    /// The refusal text, word for word what the save path has always thrown, so the preview and a
    /// refused save say the same thing. Null when the move is allowed.
    /// </summary>
    public static string? Message(RescheduleEligibilityDecision decision, AppointmentStatus status) =>
        decision.Block switch
        {
            RescheduleBlock.AppointmentClosed => $"Cannot reschedule a {status} appointment.",
            RescheduleBlock.InsideNoticeWindow =>
                $"This appointment is less than {decision.NoticeWindowHours} hours away — please contact the studio directly to reschedule.",
            _ => null,
        };
}
