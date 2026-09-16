namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// An owner-defined bookable service (e.g. "New Tattoo Session", "Touch-Up",
/// "Consultation"). Selecting one at booking time drives Appointment.DurationMinutes
/// (server-derived, never client-trusted — see CreateAppointmentCommand) and, when
/// DepositAmount is set, overrides the studio's DepositRule calculation entirely for
/// that booking (no stacking between the two). Many services can be IsActive at once —
/// unlike DepositRule, there is no single-active invariant here.
/// </summary>
public class Service : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }

    /// <summary>Informational "starting at" price shown to the client. Not wired into
    /// deposit/payment math anywhere — DepositCalculator only ever consumes
    /// DurationMinutes + ArtistHourlyRate + DepositRule, never Price.</summary>
    public decimal? Price { get; set; }

    /// <summary>When set, overrides the studio's DepositRule calculation entirely for a
    /// booking that selects this service (no stacking with DepositRule).</summary>
    public decimal? DepositAmount { get; set; }

    public bool IsActive { get; set; }
}
