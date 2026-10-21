namespace Pena_e_Arte.Domain.Constants;

/// <summary>
/// Fallback used for client self-service cancel/reschedule gating when an appointment
/// has no active DepositRule, or the active rule leaves CancellationWindowHours null.
/// The only copy of these numbers: the frontend reads them from GET /api/v1/deposit-rules/defaults
/// (a second copy in the frontend drifted to 24 h once — G-3/G-17, October 2026).
/// </summary>
public static class AppointmentSelfServiceDefaults
{
    // 48 hours — founder-confirmed default (2026-08-01). A studio's active DepositRule can still
    // set its own CancellationWindowHours to override this.
    public const int CancellationWindowHours = 48;

    // Share of the deposit refunded when a client cancels inside the notice window and the
    // studio has no deposit rule saying otherwise: none (the deposit is kept).
    public const int RefundPercentOnLateCancel = 0;
}
