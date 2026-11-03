import { AppointmentAttachmentCategory } from "./appointment.types";
import type { AppointmentResponse } from "./appointment.types";

/**
 * The appointment's images split by category. Attachments fall back to the deprecated flat
 * `imageUrls` (all treated as Reference, matching the server-side backfill default).
 * Shared by the staff appointment page and the client appointment page.
 */
export function splitAppointmentImages(appt: AppointmentResponse | undefined): {
  areaPhotoUrls: string[];
  referenceUrls: string[];
} {
  const attachments = appt?.attachments
    ?? appt?.imageUrls?.map((url) => ({ url, category: AppointmentAttachmentCategory.Reference }))
    ?? [];
  return {
    areaPhotoUrls: attachments
      .filter((a) => a.category === AppointmentAttachmentCategory.AreaPhoto)
      .map((a) => a.url),
    referenceUrls: attachments
      .filter((a) => a.category === AppointmentAttachmentCategory.Reference)
      .map((a) => a.url),
  };
}
