namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Fallback used for client self-service cancel/reschedule gating when an appointment
/// has no active DepositRule, or the active rule leaves CancellationWindowHours null.
/// </summary>
public static class AppointmentSelfServiceDefaults
{
    // 48 hours — founder-confirmed default (2026-08-01). A studio's active DepositRule can still
    // set its own CancellationWindowHours to override this.
    public const int CancellationWindowHours = 48;
}
