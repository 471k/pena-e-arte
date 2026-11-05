import { CheckCircle2, CircleDashed, Loader2, RotateCw } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { describeDeposit } from "@/features/payments/depositOutcome";
import { MessageClientAboutAppointment } from "@/features/messaging/components/MessageClientAboutAppointment";
import { useGetAppointmentPreparationQuery } from "@/features/appointments/appointmentsApi";
import { AppointmentStatus } from "@/features/appointments/appointment.types";
import type { AppointmentPreparationItem, AppointmentResponse } from "@/features/appointments/appointment.types";

// The itemised preparation for one session (A03), loaded only when the artist opens it. The facts come
// from the same server derivation as the client's checklist; the wording is about the client ("Not
// signed yet"), never the client's own instruction. Preparation informs and never blocks the session.

const UPCOMING = new Set<AppointmentStatus>([AppointmentStatus.Pending, AppointmentStatus.Confirmed]);

function PreparationItem({ item }: { item: AppointmentPreparationItem }) {
  return (
    <li className="flex items-start gap-2">
      {item.satisfied
        ? <CheckCircle2 aria-hidden="true" className="h-4 w-4 mt-0.5 shrink-0 text-success-text" />
        : <CircleDashed aria-hidden="true" className="h-4 w-4 mt-0.5 shrink-0 text-muted-foreground" />}
      <div className="min-w-0">
        <p className="text-sm">
          <span className="font-medium">{item.label}</span>
          {": "}
          <span className={item.satisfied ? "text-success-text" : "text-warning-text"}>{item.state}</span>
        </p>
        {item.followUp && <p className="text-xs text-muted-foreground">{item.followUp}</p>}
      </div>
    </li>
  );
}

export function SessionPreparation({ appt }: { appt: AppointmentResponse }) {
  const { data, isLoading, isError, refetch, isFetching } = useGetAppointmentPreparationQuery(appt.id);

  // Never shown as complete while it is still loading or couldn't be read (A03 acceptance).
  if (isLoading) {
    return (
      <p className="flex items-center gap-2 text-sm text-muted-foreground" role="status">
        <Loader2 aria-hidden="true" className="h-3.5 w-3.5 animate-spin" />
        Checking preparation…
      </p>
    );
  }
  if (isError || !data) {
    return (
      <div className="space-y-2" role="alert">
        <p className="text-sm text-destructive-text">Couldn&apos;t load the preparation details.</p>
        <Button type="button" variant="outline" size="sm" className="gap-1.5" disabled={isFetching} onClick={() => void refetch()}>
          <RotateCw aria-hidden="true" className="h-3.5 w-3.5" />
          Try again
        </Button>
      </div>
    );
  }

  const outstanding = data.items.some((i) => !i.satisfied);
  const deposit = data.deposit ? describeDeposit(data.deposit) : null;

  return (
    <div className="space-y-3">
      {data.items.length > 0 && (
        <ul aria-label="Preparation" className="space-y-2">
          {data.items.map((i) => <PreparationItem key={i.key} item={i} />)}
        </ul>
      )}
      {deposit && (
        <p className="text-sm">
          <span className="font-medium">Deposit payment: </span>
          {deposit.ownerLabel}
          {deposit.ownerDetail && <span className="text-muted-foreground"> · {deposit.ownerDetail}</span>}
        </p>
      )}
      {outstanding && UPCOMING.has(appt.status) && <MessageClientAboutAppointment appt={appt} />}
    </div>
  );
}
