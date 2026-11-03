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

    // Preparation requirements (DEC03). Each one only turns on a checklist item for the client;
    // whether it is met is derived at read time (Domain.Services.AppointmentReadiness), never stored,
    // and it never blocks booking or confirmation. All default to false, so an existing service shows
    // its clients no checklist until the owner turns one on.
    public bool RequiresConsentForm { get; set; }
    public bool RequiresIntakeForm { get; set; }
    public bool RequiresReferenceImage { get; set; }
    public bool RequiresDepositPaid { get; set; }
}
