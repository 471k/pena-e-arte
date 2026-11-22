import { useState } from "react";
import { toast } from "sonner";
import { useAppSelector } from "@/app/hooks";
import { downloadAuthenticatedFile } from "@/shared/utils/downloadAuthenticatedFile";

/**
 * The one calendar-file download (`GET appointments/{id}/calendar.ics`), used by the staff appointment page and,
 * since slice 6d (AP10), the client's own. The server returns a client's own appointment only (404 otherwise,
 * PR #366), in UTC so the calendar shows it on the device's clock, with the artist and deposit and nothing about
 * the client.
 */
export function useAppointmentCalendarDownload() {
  const { token, tenantId } = useAppSelector((s) => s.auth);
  const [downloading, setDownloading] = useState(false);

  async function download(appointmentId: string) {
    setDownloading(true);
    try {
      await downloadAuthenticatedFile(
        `appointments/${appointmentId}/calendar.ics`,
        `appointment-${appointmentId}.ics`,
        token,
        tenantId,
      );
    } catch {
      toast.error("Couldn't download the calendar file. Please try again.");
    } finally {
      setDownloading(false);
    }
  }

  return { download, downloading };
}
