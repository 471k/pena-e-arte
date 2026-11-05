import { CheckCircle2, CircleAlert, CircleDashed, RotateCw } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { withStudioTimeZone } from "@/shared/utils/formatInStudioTimezone";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { MessageClientAboutAppointment } from "@/features/messaging/components/MessageClientAboutAppointment";
import { useGetAppointmentCommunicationQuery } from "../appointmentsApi";
import { AppointmentStatus } from "../appointment.types";
import type { AppointmentResponse } from "../appointment.types";
import {
  NO_ACCOUNT, aftercareSummary, channelLine, rescheduleNotSent, rescheduleSent,
} from "./communicationCopy";

// A08: whether the client has been told what changed, as a visible task on the staff appointment page.
// Reschedule: the client isn't notified automatically, so the task stays open until staff send a
// message about this appointment (reusing E04a's message box, which attaches the appointment).
// Aftercare: sent / failed per channel from the aftercare step's own records. Nothing here claims a
// message reached the client or was looked at; the data can't prove either.

function errorStatus(error: unknown): number | undefined {
  if (typeof error === "object" && error !== null && "status" in error) {
    const status = (error as { status: unknown }).status;
    return typeof status === "number" ? status : undefined;
  }
  return undefined;
}

export function ClientCommunicationCard({ appt }: { appt: AppointmentResponse }) {
  const { data: studio } = useGetMyStudioQuery();
  const { data, isLoading, isError, error, refetch, isFetching } = useGetAppointmentCommunicationQuery(appt.id);
  const when = (iso: string) => new Date(iso).toLocaleString(
    "en-GB", withStudioTimeZone({ day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", hourCycle: "h23" }, studio?.timezone));

  // An artist on a colleague's session gets 404 by design: there is no task of theirs to show.
  if (isError && errorStatus(error) === 404) return null;
  if (isLoading) return null;
  if (isError || !data) {
    return (
      <Card>
        <CardContent className="py-4 flex flex-wrap items-center gap-3" role="alert">
          <p className="text-sm text-destructive-text">Couldn&apos;t load the client communication status.</p>
          <Button type="button" variant="outline" size="sm" className="gap-1.5" disabled={isFetching} onClick={() => void refetch()}>
            <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
            Try again
          </Button>
        </CardContent>
      </Card>
    );
  }
  if (!data.reschedule && !data.aftercare) return null;

  const upcoming = appt.status === AppointmentStatus.Pending || appt.status === AppointmentStatus.Confirmed;

  return (
    <Card aria-labelledby="client-communication-heading">
      <CardHeader className="pb-2">
        <CardTitle id="client-communication-heading" className="text-sm font-medium">Client communication</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4" data-testid="client-communication">
        {data.reschedule && (
          <section aria-label="Reschedule" className="space-y-2">
            {data.reschedule.state === "Sent" && data.reschedule.messagedAt ? (
              <p className="flex items-start gap-2 text-sm">
                <CheckCircle2 className="h-4 w-4 mt-0.5 shrink-0 text-success-text" aria-hidden="true" />
                {rescheduleSent(data.reschedule.rescheduledAt, data.reschedule.messagedAt, when)}
              </p>
            ) : (
              <>
                <p className="flex items-start gap-2 text-sm">
                  <CircleDashed className="h-4 w-4 mt-0.5 shrink-0 text-warning-text" aria-hidden="true" />
                  <span className="text-warning-text">{rescheduleNotSent(data.reschedule.rescheduledAt, when)}</span>
                </p>
                {upcoming && (appt.clientUserId
                  ? <MessageClientAboutAppointment appt={appt} onSent={() => void refetch()} />
                  : <p className="text-xs text-muted-foreground">{NO_ACCOUNT}</p>)}
              </>
            )}
          </section>
        )}

        {data.aftercare && (
          <section aria-label="Aftercare" className="space-y-1">
            <p className="flex items-start gap-2 text-sm">
              {data.aftercare.state === "Sent"
                ? <CheckCircle2 className="h-4 w-4 mt-0.5 shrink-0 text-success-text" aria-hidden="true" />
                : data.aftercare.state === "Failed"
                  ? <CircleAlert className="h-4 w-4 mt-0.5 shrink-0 text-destructive-text" aria-hidden="true" />
                  : <CircleDashed className="h-4 w-4 mt-0.5 shrink-0 text-muted-foreground" aria-hidden="true" />}
              <span className={data.aftercare.state === "Failed" ? "text-destructive-text" : undefined}>
                {aftercareSummary(data.aftercare, when)}
              </span>
            </p>
            {data.aftercare.channels.length > 0 && (
              <ul className="pl-6 text-xs text-muted-foreground space-y-0.5" aria-label="Aftercare channels">
                {data.aftercare.channels.map((c, i) => <li key={`${c.channel}-${i}`}>{channelLine(c, when)}</li>)}
              </ul>
            )}
          </section>
        )}
      </CardContent>
    </Card>
  );
}
