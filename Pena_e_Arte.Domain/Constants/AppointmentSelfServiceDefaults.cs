namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Fallback used for client self-service cancel/reschedule gating when an appointment
/// has no active DepositRule, or the active rule leaves CancellationWindowHours null.
/// </summary>
public static class AppointmentSelfServiceDefaults
{
    public const int CancellationWindowHours = 24;
}
