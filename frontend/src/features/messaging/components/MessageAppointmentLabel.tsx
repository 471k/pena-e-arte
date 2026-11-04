import { Link } from "react-router-dom";
import { CalendarDays } from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { useAppSelector } from "@/app/hooks";
import { appointmentHrefForViewer, formatAppointmentLabel } from "../appointmentLabel";
import type { MessageAppointmentLabel as Label } from "../messaging.types";

interface MessageAppointmentLabelProps {
  label:          Label;
  studioTimezone: string;
  /** In the thread the label links to the appointment. In an inbox row it is plain context: the
   * whole row is already a button, and a link can't sit inside a button. */
  asLink:         boolean;
  className?:     string;
  id?:            string;
}

const PILL = "inline-flex max-w-full items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground";

/** "Which appointment is this about" — subordinate context beside a message or in an inbox row. */
export function MessageAppointmentLabel({ label, studioTimezone, asLink, className, id }: MessageAppointmentLabelProps) {
  const role = useAppSelector((s) => s.auth.role);
  const text = formatAppointmentLabel(label, studioTimezone);
  const href = asLink ? appointmentHrefForViewer(role, label.appointmentId) : null;

  const content = (
    <>
      <CalendarDays className="h-3 w-3 shrink-0" aria-hidden="true" />
      <span className="sr-only">{`Appointment: ${text}`}</span>
      <span className="truncate" aria-hidden="true">{text}</span>
    </>
  );

  if (href) {
    return (
      <Link
        id={id}
        to={href}
        className={cn(
          PILL,
          "hover:text-foreground hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
          className,
        )}
      >
        {content}
      </Link>
    );
  }
  return <span id={id} className={cn(PILL, className)}>{content}</span>;
}
