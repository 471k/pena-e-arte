import { useRef } from "react";
import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Loader2, RotateCw } from "lucide-react";
import {
  AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";
import { Button } from "@/shared/components/ui/button";
import { formatSessionSpan } from "@/shared/utils/studioDay";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { AppointmentStatusBadge } from "@/features/appointments/components/AppointmentStatusBadge";
import { AppointmentStatus } from "@/features/appointments/appointment.types";
import { useGetTimeOffAffectedSessionsQuery } from "../artistsApi";
import { formatLocalRange } from "../timeOffDates";

// E04c (A06): which booked sessions a time-off change concerns, shown BEFORE it is committed, from the
// server's own check (GET artists/{id}/time-off/affected-sessions). The same consequence-first shape as
// E04b's no-show dialog: confirm stays disabled until the check has loaded, and if it fails the dialog
// says so instead of committing blind. Every affected session is listed (never just a count), each a
// link to its own page. Nothing here, or in the commit, moves or cancels a session, and there is no
// bulk action: what to do with each one is the artist's decision.

function errorStatus(error: unknown): number | undefined {
  if (typeof error === "object" && error !== null && "status" in error) {
    const status = (error as { status: unknown }).status;
    return typeof status === "number" ? status : undefined;
  }
  return undefined;
}

function plural(n: number): string {
  return n === 1 ? "1 booked session falls" : `${n} booked sessions fall`;
}

interface TimeOffImpactDialogProps {
  mode:         "add" | "remove";
  artistId:     string;
  startDate:    string; // YYYY-MM-DD
  endDate:      string; // YYYY-MM-DD
  reason:       string;
  open:         boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm:    () => void;
  confirming:   boolean;
}

export function TimeOffImpactDialog({
  mode, artistId, startDate, endDate, reason, open, onOpenChange, onConfirm, confirming,
}: TimeOffImpactDialogProps) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  const { data: studio } = useGetMyStudioQuery();
  const check = useGetTimeOffAffectedSessionsQuery(
    { artistId, startDate: startDate.slice(0, 10), endDate: endDate.slice(0, 10) },
    { skip: !open, refetchOnMountOrArgChange: true },
  );

  const data = check.data;
  const canConfirm = !!data && !check.isFetching && !check.isError && !confirming;
  const range = formatLocalRange(startDate, endDate);
  const adding = mode === "add";

  let body: ReactNode;
  if (check.isFetching && !data) {
    body = (
      <p className="flex items-center gap-2 text-sm text-muted-foreground" role="status">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
        Checking which booked sessions fall inside these dates…
      </p>
    );
  } else if (check.isError && errorStatus(check.error) === 403) {
    body = (
      <p role="alert" className="text-sm text-destructive-text">
        Only the artist themselves, or the studio owner, can change this artist&apos;s time off.
      </p>
    );
  } else if (check.isError || !data) {
    body = (
      <div className="space-y-2" role="alert">
        <p className="text-sm text-destructive-text">
          Couldn&apos;t check which booked sessions fall inside these dates, so this can&apos;t be confirmed
          right now. Try again in a moment.
        </p>
        <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={() => void check.refetch()}>
          <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
          Try again
        </Button>
      </div>
    );
  } else if (data.sessions.length === 0) {
    body = (
      <p className="text-sm">
        {adding
          ? "No booked sessions fall inside these dates."
          : "No booked sessions fall inside this period. Removing it makes these days bookable again."}
      </p>
    );
  } else {
    body = (
      <div className="space-y-3">
        <p className="text-sm">
          {adding
            ? `${plural(data.sessions.length)} inside these dates. Adding time off won't move or cancel ${data.sessions.length === 1 ? "it" : "them"}: they stay booked, and the clients aren't told. Open each one to reschedule or cancel it, or to message the client.`
            : `${plural(data.sessions.length)} inside this period. Removing it makes these days bookable again; the sessions themselves stay exactly as they are.`}
        </p>
        <ul className="space-y-2" aria-label="Booked sessions inside these dates">
          {data.sessions.map((s) => (
            <li key={s.appointmentId} className="rounded-md border px-3 py-2">
              <Link
                to={`/appointments/${s.appointmentId}`}
                className="text-sm font-medium text-link underline underline-offset-2"
              >
                {formatSessionSpan(s.date, s.endDate, studio?.timezone)}
              </Link>
              <div className="mt-1 flex flex-wrap items-center gap-2 text-sm">
                <span>{s.clientName}</span>
                <AppointmentStatusBadge status={s.status as AppointmentStatus} />
              </div>
            </li>
          ))}
        </ul>
      </div>
    );
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent
        className="max-h-[90vh] overflow-y-auto"
        onOpenAutoFocus={(e) => { e.preventDefault(); cancelRef.current?.focus(); }}
      >
        <AlertDialogHeader>
          <AlertDialogTitle>{adding ? "Add time off?" : "Remove time off?"}</AlertDialogTitle>
          <AlertDialogDescription>{range} · {reason}</AlertDialogDescription>
        </AlertDialogHeader>

        <div className="space-y-3" aria-live="polite">{body}</div>

        <AlertDialogFooter>
          <AlertDialogCancel ref={cancelRef}>{adding ? "Back" : "Keep the time off"}</AlertDialogCancel>
          <Button type="button" variant={adding ? "default" : "destructive"} disabled={!canConfirm} onClick={onConfirm}>
            {confirming ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            {adding ? "Add time off" : "Remove time off"}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
