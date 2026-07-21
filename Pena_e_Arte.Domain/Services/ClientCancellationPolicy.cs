using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Domain.Services;

/// <summary>
/// Gates client self-service cancel/reschedule against the studio's active DepositRule
/// (falling back to AppointmentSelfServiceDefaults.CancellationWindowHours when the rule
/// leaves CancellationWindowHours null, or there is no active rule at all). Staff-initiated
/// cancel/reschedule never call into this — they are unaffected by the notice window.
/// </summary>
public static class ClientCancellationPolicy
{
    /// <summary>
    /// Percent of the deposit to refund on a client-initiated cancel: 100 when the client
    /// gave enough notice, otherwise the rule's configured late-cancel refund percent (0 by
    /// default, i.e. the deposit is forfeited).
    /// </summary>
    public static int ResolveRefundPercent(DepositRule? rule, DateTime appointmentDate, DateTime nowUtc) =>
        IsWithinNoticeWindow(rule, appointmentDate, nowUtc) ? 100 : rule?.RefundPercentOnLateCancel ?? 0;

    /// <summary>
    /// True when the client is cancelling/rescheduling with at least the required notice.
    /// Used directly (without the refund math) to gate client self-reschedule in Phase 3.
    /// </summary>
    public static bool IsWithinNoticeWindow(DepositRule? rule, DateTime appointmentDate, DateTime nowUtc)
    {
        int windowHours = rule?.CancellationWindowHours ?? AppointmentSelfServiceDefaults.CancellationWindowHours;
        return (appointmentDate - nowUtc).TotalHours >= windowHours;
    }
}
