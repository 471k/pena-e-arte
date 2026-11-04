import { Role } from "@/shared/types/roles";
import { withStudioTimeZone } from "@/shared/utils/formatInStudioTimezone";
import type { ChatMessageResponse, MessageAppointmentLabel } from "./messaging.types";

/**
 * One short line for "which appointment is this about": the studio-local day and time, plus the
 * service when there is one — e.g. "Thu 3 Dec, 14:30 · Fine line", or "Thu 3 Dec, 14:30" alone.
 * The year is added only when it isn't the current one. Always in the studio's timezone: the
 * caller renders nothing until that timezone has loaded rather than show a browser-local time.
 */
export function formatAppointmentLabel(
  label: MessageAppointmentLabel,
  studioTimezone: string,
  now: Date = new Date(),
): string {
  const startsAt = new Date(label.startsAt);
  const yearOf = (d: Date) =>
    new Intl.DateTimeFormat("en-GB", withStudioTimeZone({ year: "numeric" }, studioTimezone)).format(d);
  const when = startsAt.toLocaleString("en-GB", withStudioTimeZone({
    weekday: "short",
    day: "numeric",
    month: "short",
    ...(yearOf(startsAt) !== yearOf(now) ? { year: "numeric" as const } : {}),
    hour: "2-digit",
    minute: "2-digit",
  }, studioTimezone));
  return label.serviceName ? `${when} · ${label.serviceName}` : when;
}

/**
 * Where the label links, decided by the role of the person READING, never the sender: a client
 * goes to their own appointment page, staff to the staff appointment page. A client must never be
 * given the staff route (they would hit its role gate), so an unknown or missing role gets no link.
 */
export function appointmentHrefForViewer(role: Role | null | undefined, appointmentId: string): string | null {
  switch (role) {
    case Role.Client:
      return `/my-appointments/${appointmentId}`;
    case Role.Artist:
    case Role.Owner:
    case Role.Admin:
      return `/appointments/${appointmentId}`;
    default:
      return null;
  }
}

/**
 * Thread rule: show a message's label only where the reference changes — on the first message
 * about an appointment, and again whenever a message is about a different one (or the same one
 * after messages about nothing) — not on every message of a run about the same appointment.
 */
export function showsAppointmentLabel(
  message: ChatMessageResponse,
  previous: ChatMessageResponse | undefined,
): boolean {
  if (!message.appointment) return false;
  return (previous?.appointmentId ?? null) !== message.appointment.appointmentId;
}
