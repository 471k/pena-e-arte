using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// Booking-content intake captured when an appointment is requested — what the client wants
/// done, where, and how they found the studio. One-to-one with Appointment. Deliberately
/// separate from IntakeForm (the studio-sent intake/consent form a client fills out) and from
/// ClientProfile (MedicalNotes/Allergies/BodyMap, the client's permanent record across all
/// bookings) — conflating "what I want done at this booking" with either would corrupt their
/// semantics. See architecture.md's Addendum note (2026-08-31) for why this isn't named
/// IntakeForm.
/// </summary>
public class BookingIntake : TenantEntity
{
    public Guid AppointmentId { get; set; }

    /// <summary>"What are you looking to get done?" — required on every new booking.</summary>
    public string TattooDescription { get; set; } = string.Empty;

    /// <summary>"Anything else I should know?" — medical issues, allergies, antibiotics, skin
    /// conditions. Optional. Free text; NOT synced into ClientProfile.MedicalNotes/Allergies.</summary>
    public string? SafetyNotes { get; set; }

    /// <summary>Reuses ClientProfile's exact value object/JSON-column pattern — zone ids from
    /// the same BodyMap.tsx picker, but scoped to "where do you want THIS tattoo," not the
    /// client's tattoo history.</summary>
    public BodyMap DesiredPlacement { get; set; } = new();

    public ReferralSource? ReferralSource { get; set; }

    /// <summary>Required (validator-enforced) when ReferralSource == Other; ignored otherwise.</summary>
    public string? ReferralSourceOther { get; set; }

    public Appointment Appointment { get; set; } = null!;
}
