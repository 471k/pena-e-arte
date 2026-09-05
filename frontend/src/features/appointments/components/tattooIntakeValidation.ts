import { ReferralSource } from "../appointment.types";

export interface TattooIntakeValues {
  tattooDescription:   string;
  referralSource:      string;
  referralSourceOther: string;
  /** Maps to BookingIntake.SafetyNotes — "anything else I should know?" (medical issues,
   *  allergies, antibiotics, skin conditions). Distinct from Appointment.Notes, which each
   *  form renders separately as a plain, general-purpose field. */
  safetyNotes:         string;
}

export interface TattooIntakeValidationErrors {
  tattooDescriptionError:   string | null;
  referralSourceOtherError: string | null;
}

/**
 * The two manual (non-react-hook-form) validation checks TattooIntakeFields' consumers must run
 * before submit — required description, and a required "where" when ReferralSource is Other.
 * Was duplicated identically in BookAppointmentForm's and GuestBookAppointmentForm's onSubmit
 * handlers. Found via /code-review, 2026-09-01.
 *
 * Lives in this non-component file (not TattooIntakeFields.tsx) because mixing non-component
 * exports into a component file trips react-refresh/only-export-components — same pattern as
 * conductReportShared.tsx / conductReportFormat.ts.
 */
export function validateTattooIntake(value: TattooIntakeValues): TattooIntakeValidationErrors {
  return {
    tattooDescriptionError: value.tattooDescription.trim()
      ? null
      : "Tell us what you're looking to get done.",
    referralSourceOtherError: value.referralSource === ReferralSource.Other && !value.referralSourceOther.trim()
      ? "Please tell us where."
      : null,
  };
}
