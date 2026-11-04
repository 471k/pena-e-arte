import { useState } from "react";
import { Link } from "react-router-dom";
import { toast } from "sonner";
import { Banknote, CalendarDays, CheckCircle2, ChevronRight, ChevronUp, CreditCard, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Separator } from "@/shared/components/ui/separator";
import {
  useGetMyAppointmentsQuery,
  useCancelAppointmentMutation,
  useGetCancellationPreviewQuery,
} from "../appointmentsApi";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { useGetPaymentByAppointmentQuery } from "@/features/payments/paymentsApi";
import { describeDeposit } from "@/features/payments/depositOutcome";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { Money } from "@/shared/components/Money";
import type { MoneyStudio } from "@/shared/hooks/useEurAllRate";
import { useStudioCurrency } from "@/shared/hooks/useStudioCurrency";
import { formatNoticeWindow } from "@/shared/utils/noticeWindow";
import { cn } from "@/shared/utils/cn";
import { PaymentMethodSelector } from "@/features/payments/components/PaymentMethodSelector";
import { AppointmentStatusBadge } from "./AppointmentStatusBadge";
import { RescheduleDialog } from "./RescheduleDialog";
import { CancellationOutcome, DepositStatus } from "../appointment.types";
import type { AppointmentResponse, CancellationPreviewResponse } from "../appointment.types";
import type { ArtistResponse } from "@/features/artists/artistsApi";

// The notice window, and everything that follows from it (reschedule cutoff, refund), is decided by
// the server only: GET /appointments/mine carries `selfService` per appointment, and the cancel
// confirmation asks GET /appointments/{id}/cancellation-preview. A frontend copy of this policy
// drifted from the server's once — 24–48 h before an appointment the client was promised "a full
// refund" and offered Reschedule, while the server refused the reschedule and kept the whole
// deposit (G-3/G-17, October 2026) — so there is deliberately no notice-window or refund
// arithmetic in this file.
//
// RescheduleArea, CancelArea and DepositArea are exported so the client appointment page
// (ClientAppointmentDetailPage) reuses exactly this behaviour rather than a second copy of it.

// ── Reschedule appointment ────────────────────────────────────────────────

export function RescheduleArea({ appt }: { appt: AppointmentResponse }) {
  const [open, setOpen] = useState(false);

  if (appt.status !== "Pending" && appt.status !== "Confirmed") return null;

  // Disabled (without the cutoff message) while the server's answer isn't known, so the client is
  // never offered a reschedule the server would refuse.
  const within = appt.selfService?.isWithinNoticeWindow;
  const canReschedule = within === true;
  const windowHours = appt.selfService?.noticeWindowHours ?? 0;

  return (
    <>
      <Button
        size="sm"
        variant="ghost"
        className="h-7 px-2 text-xs"
        disabled={!canReschedule}
        title={
          within === false
            ? `This appointment is less than ${formatNoticeWindow(windowHours)} away — please contact the studio directly to reschedule.`
            : undefined
        }
        onClick={() => setOpen(true)}
      >
        Reschedule
      </Button>
      {within === false && (
        <p className="text-xs text-muted-foreground">
          Less than {formatNoticeWindow(windowHours)} away — contact the studio directly to reschedule.
        </p>
      )}
      <RescheduleDialog
        appointment={appt}
        open={open}
        onOpenChange={setOpen}
        description="Pick a new date, time, and duration for your appointment."
      />
    </>
  );
}

// ── Cancel appointment ────────────────────────────────────────────────────

// What the server projects for the deposit, in the client's words. Never claims a refund has
// happened — it hasn't yet; once the cancellation is committed, DepositArea shows what actually did.
function ConsequenceText({ preview }: { preview: CancellationPreviewResponse }) {
  const { countryCode } = useStudioCurrency();
  const studio: MoneyStudio = { countryCode, currency: preview.currency };
  const money = (amount: number) => <Money amount={amount} currency={preview.currency} studio={studio} />;
  const late = preview.noticeWindowApplies && !preview.isWithinNoticeWindow
    ? <>It&apos;s less than {formatNoticeWindow(preview.noticeWindowHours)} before your appointment. </>
    : null;

  switch (preview.outcome) {
    case CancellationOutcome.FullyRefunded:
      return <>You&apos;ll receive a full refund of {money(preview.refundAmount)}.</>;
    case CancellationOutcome.PartiallyRefunded:
      return (
        <>
          {late}Under the studio&apos;s policy you&apos;ll get {money(preview.refundAmount)} back and the
          studio keeps {money(preview.keptAmount)} of your deposit.
        </>
      );
    case CancellationOutcome.DepositKept:
      return (
        <>{late}Cancelling now forfeits 100% of your deposit — the studio keeps {money(preview.keptAmount)}.</>
      );
    case CancellationOutcome.NothingCollected:
      return <>No deposit has been taken, so nothing is charged or refunded.</>;
    case CancellationOutcome.SettledWithStudio:
      return (
        <>
          Your deposit was paid at the studio, so it isn&apos;t refunded automatically — the studio will
          settle it with you.
        </>
      );
    default:
      return null;
  }
}

function StudioContact() {
  const { data: studio } = useGetMyStudioQuery();
  if (studio?.phoneNumber) {
    return (
      <>
        Please contact the studio to cancel:{" "}
        <a className="underline" href={`tel:${studio.phoneNumber}`}>{studio.phoneNumber}</a>.
      </>
    );
  }
  return <>Please contact the studio to cancel.</>;
}

export function CancelArea({ appt }: { appt: AppointmentResponse }) {
  const [confirming, setConfirming] = useState(false);
  const [cancelAppointment, { isLoading }] = useCancelAppointmentMutation();
  // Fetched only once the client opens the confirmation, and fresh every time it opens.
  const {
    data: preview,
    isFetching: previewLoading,
    isError: previewFailed,
  } = useGetCancellationPreviewQuery(appt.id, { skip: !confirming, refetchOnMountOrArgChange: true });

  if (appt.status !== "Pending" && appt.status !== "Confirmed") return null;

  // Confirming is only offered once the server has said what will happen. A failed preview, or an
  // outcome the server can't tell, points the client at the studio — never at a locally computed guess.
  const outcomeKnown = preview !== undefined && preview.outcome !== CancellationOutcome.Unknown;
  const canConfirm = !previewLoading && !previewFailed && outcomeKnown;

  async function handleCancel() {
    const result = await cancelAppointment(appt.id);
    if ("error" in result) {
      toast.error("Failed to cancel appointment.");
      return;
    }
    // Deliberately no money wording here: the deposit line now shows what the server actually did,
    // which can differ from the projection (e.g. the studio's policy changed in between).
    toast.success("Appointment cancelled.");
    setConfirming(false);
  }

  if (!confirming) {
    return (
      <button
        type="button"
        className="text-xs text-muted-foreground underline hover:text-destructive-text"
        onClick={() => setConfirming(true)}
      >
        Cancel appointment
      </button>
    );
  }

  const titleId = `cancel-${appt.id}-title`;
  const detailId = `cancel-${appt.id}-detail`;

  return (
    <div
      role="alertdialog"
      aria-modal="false"
      aria-labelledby={titleId}
      aria-describedby={detailId}
      className="rounded-md border border-destructive/30 bg-destructive/5 p-3 space-y-2"
    >
      <p id={titleId} className="text-xs">Cancel this appointment?</p>
      <div id={detailId} className="space-y-1">
        {previewLoading ? (
          <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <Loader2 className="h-3 w-3 animate-spin" />
            Checking what this means for your deposit…
          </p>
        ) : previewFailed ? (
          <p className="text-xs">
            We couldn&apos;t check what cancelling would mean for your deposit. <StudioContact />
          </p>
        ) : !outcomeKnown ? (
          <p className="text-xs">
            We can&apos;t tell what would happen to your deposit. <StudioContact />
          </p>
        ) : (
          <>
            <p className="text-xs"><ConsequenceText preview={preview} /></p>
            <p className="text-xs text-muted-foreground">
              The studio&apos;s cancellation policy decides this; it&apos;s confirmed when you cancel.
            </p>
          </>
        )}
      </div>
      <div className="flex gap-2">
        <Button
          size="sm"
          variant="destructive"
          className="h-7 flex-1 text-xs"
          disabled={isLoading || !canConfirm}
          onClick={handleCancel}
        >
          {isLoading ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, cancel"}
        </Button>
        <Button
          size="sm"
          variant="outline"
          className="h-7 flex-1 text-xs"
          disabled={isLoading}
          // The safe choice is where focus lands when the confirmation opens.
          autoFocus
          onClick={() => setConfirming(false)}
        >
          Keep appointment
        </Button>
      </div>
    </div>
  );
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    weekday: "short", day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
  });
}

// ── Deposit state per booking ─────────────────────────────────────────────

// `prominent` renders the pay button as the page's filled primary action (the client appointment
// page, when paying the deposit is the next thing to do). The bookings list keeps the outline button.
export function DepositArea({ appt, prominent = false }: { appt: AppointmentResponse; prominent?: boolean }) {
  const [paying, setPaying] = useState(false);

  const { data: payment, isLoading } = useGetPaymentByAppointmentQuery(appt.id, {
    skip: appt.depositAmount <= 0,
  });
  const { currency: studioCurrency, countryCode: studioCountry } = useStudioCurrency();
  // The payment row carries its own currency; "my studio" can still be loading right after a studio
  // switch (G-2), so never fall back to the bare number.
  const depositCurrency = payment?.currency ?? studioCurrency;
  // Albanian EUR studios: lek next to the euro deposit — frozen once a card order exists, else approximate.
  const moneyStudio: MoneyStudio = { countryCode: studioCountry, currency: depositCurrency };

  if (appt.depositAmount <= 0) return null;

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-xs text-muted-foreground py-1">
        <Loader2 className="h-3 w-3 animate-spin" />
        Checking deposit…
      </div>
    );
  }

  // Settled states — nothing for the client to do. Worded from what actually happened to the
  // money (G-13/G-14): a full refund, a partial refund, a released hold and a late-cancel partial
  // charge used to read "Deposit paid" or "Deposit refunded" interchangeably.
  if (payment && (payment.status === "Paid" || payment.status === "Refunded" || payment.status === "Captured")) {
    const outcome = describeDeposit(payment);
    const positive = outcome.kind === "paid" || outcome.kind === "onHold";
    return (
      <div className="space-y-0.5">
        <p className={cn("flex items-center gap-1.5 text-xs",
          positive ? "text-success-text dark:text-green-400" : "text-muted-foreground")}>
          {positive && <CheckCircle2 className="h-3.5 w-3.5" />}
          {outcome.clientLabel}
        </p>
        {outcome.clientDetail && <p className="text-xs text-muted-foreground">{outcome.clientDetail}</p>}
      </div>
    );
  }

  // No payment row to describe (e.g. data from before payments existed): fall back to the
  // appointment's own deposit status.
  if (appt.depositStatus === DepositStatus.Forfeited) {
    return <p className="text-xs text-muted-foreground">Deposit kept</p>;
  }
  if (appt.depositStatus === DepositStatus.Refunded) {
    return <p className="text-xs text-muted-foreground">Deposit refunded</p>;
  }
  if (appt.depositStatus === DepositStatus.Paid) {
    return (
      <p className="flex items-center gap-1.5 text-xs text-success-text dark:text-green-400">
        <CheckCircle2 className="h-3.5 w-3.5" />
        Deposit paid
      </p>
    );
  }

  if (paying) {
    return (
      <div className="space-y-3 pt-2">
        <PaymentMethodSelector
          appointmentId={appt.id}
          amount={appt.depositAmount}
          countryCode={studioCountry}
          onSuccess={(method) => {
            setPaying(false);
            toast.success(
              method === "cash"
                ? "Noted — bring the deposit in cash to the studio."
                : "Deposit paid.",
            );
          }}
          onError={(message) => toast.error(message)}
        />
        <button
          type="button"
          onClick={() => setPaying(false)}
          className="w-full flex items-center justify-center gap-1 text-xs text-muted-foreground hover:text-foreground"
        >
          <ChevronUp className="h-3 w-3" />
          Close
        </button>
      </div>
    );
  }

  if (payment?.status === "CashPending") {
    return (
      <div className="flex items-center justify-between gap-2">
        <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
          <Banknote className="h-3.5 w-3.5" />
          Paying <Money amount={appt.depositAmount} currency={payment.currency} studio={moneyStudio} /> in cash at the studio
        </p>
        <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
          onClick={() => setPaying(true)}>
          Pay by card instead
        </Button>
      </div>
    );
  }

  // No payment yet, an unauthorised card intent, or a failed attempt
  return (
    <Button size="sm" variant={prominent ? "default" : "outline"} className="h-8 gap-1.5 text-xs"
      onClick={() => setPaying(true)}>
      <CreditCard className="h-3.5 w-3.5" />
      {payment?.status === "Pending"
        ? "Finish deposit payment"
        : depositCurrency
          ? <>Pay deposit — <Money amount={appt.depositAmount} currency={depositCurrency} studio={moneyStudio} frozen={payment} /></>
          : "Pay deposit"}
    </Button>
  );
}

// ── Booking row ───────────────────────────────────────────────────────────

function artistName(artistId: string | null, artists: ArtistResponse[]): string {
  if (artistId === null) return "Studio will assign an artist";
  const a = artists.find((x) => x.id === artistId);
  return a ? `${a.firstName} ${a.lastName}` : "—";
}

// "2 things to do" from the server's readiness summary (DEC03) — only for an upcoming booking whose
// service turns requirements on. The detail lives on the appointment page.
function ReadinessHint({ appt }: { appt: AppointmentResponse }) {
  if (appt.status !== "Pending" && appt.status !== "Confirmed") return null;
  const outstanding = appt.readiness ? appt.readiness.required - appt.readiness.satisfied : 0;
  if (outstanding <= 0) return null;
  return (
    <p className="text-xs font-medium">
      {outstanding === 1 ? "1 thing to do" : `${outstanding} things to do`} before your appointment
    </p>
  );
}

function BookingRow({
  appt, artists,
}: {
  appt: AppointmentResponse;
  artists: ArtistResponse[];
}) {
  return (
    <div className="py-3 space-y-2">
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-medium">{formatDate(appt.date)}</p>
          <p className="text-xs text-muted-foreground truncate">
            {artistName(appt.artistId, artists)} · {appt.durationMinutes} min
          </p>
        </div>
        <AppointmentStatusBadge status={appt.status} />
      </div>
      {appt.notes && (
        <p className="text-xs text-muted-foreground">{appt.notes}</p>
      )}
      {appt.status === "Cancelled" && (
        <p className="text-xs text-muted-foreground">
          {appt.cancellationReason === "StudioDeclined"
            ? "The studio couldn't take this request."
            : "This appointment was cancelled."}{" "}
          <button
            type="button"
            className="underline hover:text-foreground"
            onClick={() => window.scrollTo({ top: 0, behavior: "smooth" })}
          >
            Book a new appointment
          </button>
        </p>
      )}
      <ReadinessHint appt={appt} />
      <DepositArea appt={appt} />
      <div className="flex items-center gap-3">
        <RescheduleArea appt={appt} />
        <CancelArea appt={appt} />
      </div>
      <Link
        to={`/my-appointments/${appt.id}`}
        data-tour="client-appointment-details-link"
        className="inline-flex items-center gap-0.5 text-xs text-link underline underline-offset-2"
      >
        View appointment details
        <ChevronRight className="h-3 w-3" aria-hidden="true" />
      </Link>
    </div>
  );
}

// ── Section ───────────────────────────────────────────────────────────────

export function MyBookingsSection() {
  const { data: appointments = [], isLoading, isError } = useGetMyAppointmentsQuery();
  const { data: artists = [] } = useGetArtistsQuery(undefined);

  const now = new Date();

  const upcoming = appointments.filter(
    (a) =>
      a.status !== "Cancelled" &&
      a.status !== "Completed" &&
      a.status !== "NoShow" &&
      new Date(a.endDate) >= now,
  );

  const past = appointments.filter(
    (a) =>
      a.status === "Cancelled" ||
      a.status === "Completed" ||
      a.status === "NoShow" ||
      new Date(a.endDate) < now,
  );

  return (
    <Card>
      <CardHeader className="pb-1">
        <CardTitle className="text-base flex items-center gap-2">
          <CalendarDays className="h-4 w-4" />
          My bookings
          {upcoming.length > 0 && (
            <span className="ml-auto text-xs font-normal text-muted-foreground">
              {upcoming.length} upcoming
            </span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isLoading && (
          <div className="flex items-center gap-2 py-4 text-muted-foreground text-sm">
            <Loader2 className="h-4 w-4 animate-spin" />
            Loading your bookings…
          </div>
        )}

        {isError && (
          <p className="py-4 text-sm text-destructive-text">
            Couldn&apos;t load your bookings. Please refresh and try again.
          </p>
        )}

        {!isLoading && !isError && upcoming.length === 0 && past.length === 0 && (
          <p className="py-4 text-sm text-muted-foreground">
            No upcoming bookings yet — your appointments and any deposits to pay will appear here.
          </p>
        )}

        {!isLoading && !isError && (
          <>
            {upcoming.length > 0 && (
              <div>
                {upcoming.map((appt, i) => (
                  <div key={appt.id}>
                    {i > 0 && <Separator />}
                    <BookingRow appt={appt} artists={artists} />
                  </div>
                ))}
              </div>
            )}

            {past.length > 0 && (
              <div className={upcoming.length > 0 ? "mt-4" : ""}>
                {upcoming.length > 0 && <Separator className="mb-3" />}
                <p className="text-xs text-muted-foreground font-medium uppercase tracking-wide mb-2">
                  Past
                </p>
                {past.map((appt, i) => (
                  <div key={appt.id}>
                    {i > 0 && <Separator />}
                    <BookingRow appt={appt} artists={artists} />
                  </div>
                ))}
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  );
}
