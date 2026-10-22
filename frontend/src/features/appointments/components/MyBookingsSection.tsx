import { useState } from "react";
import { toast } from "sonner";
import { Banknote, CalendarDays, CheckCircle2, ChevronUp, CreditCard, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Separator } from "@/shared/components/ui/separator";
import { useGetMyAppointmentsQuery, useCancelAppointmentMutation } from "../appointmentsApi";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { useGetPaymentByAppointmentQuery } from "@/features/payments/paymentsApi";
import { describeDeposit } from "@/features/payments/depositOutcome";
import { useGetDepositRuleDefaultsQuery, useGetDepositRulesQuery } from "@/features/deposit-rules/depositRulesApi";
import { formatCurrency } from "@/shared/utils/formatCurrency";
import { useStudioCurrency } from "@/shared/hooks/useStudioCurrency";
import { formatNoticeWindow } from "@/shared/utils/noticeWindow";
import { cn } from "@/shared/utils/cn";
import { PaymentMethodSelector } from "@/features/payments/components/PaymentMethodSelector";
import { AppointmentStatusBadge } from "./AppointmentStatusBadge";
import { RescheduleDialog } from "./RescheduleDialog";
import { DepositStatus } from "../appointment.types";
import type { AppointmentResponse } from "../appointment.types";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import type { DepositRuleDefaultsResponse, DepositRuleResponse } from "@/features/deposit-rules/depositRule.types";

// The studio's active rule, or — when it has none or leaves a field empty — the platform defaults
// the server applies (ClientCancellationPolicy), read from GET /deposit-rules/defaults. A frontend
// copy of the default said 24 h while the server applied 48: 24–48 h before an appointment the
// client was promised "a full refund" and offered Reschedule, while the server refused the
// reschedule and kept the whole deposit (G-3, re-tested live 2026-10-15).
interface NoticePolicy {
  activeRule?: DepositRuleResponse;
  defaults?:   DepositRuleDefaultsResponse;
}

// Same notice window backs both self-cancel (refund %) and self-reschedule (hard cutoff) —
// mirrors ClientCancellationPolicy on the backend. undefined = not known yet (defaults loading).
function noticeWindowHours({ activeRule, defaults }: NoticePolicy): number | undefined {
  return activeRule?.cancellationWindowHours ?? defaults?.cancellationWindowHours;
}

function lateCancelRefundPercent({ activeRule, defaults }: NoticePolicy): number | undefined {
  return activeRule?.refundPercentOnLateCancel ?? defaults?.refundPercentOnLateCancel;
}

function isWithinNoticeWindow(appt: AppointmentResponse, policy: NoticePolicy): boolean | undefined {
  const windowHours = noticeWindowHours(policy);
  if (windowHours === undefined) return undefined;
  const hoursUntilAppointment = (new Date(appt.date).getTime() - Date.now()) / (1000 * 60 * 60);
  return hoursUntilAppointment >= windowHours;
}

// ── Reschedule appointment ────────────────────────────────────────────────

function RescheduleArea({ appt, policy }: { appt: AppointmentResponse; policy: NoticePolicy }) {
  const [open, setOpen] = useState(false);

  if (appt.status !== "Pending" && appt.status !== "Confirmed") return null;

  // Disabled (without the cutoff message) for the moment the window isn't known yet, so the client
  // is never offered a reschedule the server would refuse.
  const within = isWithinNoticeWindow(appt, policy);
  const canReschedule = within === true;
  const windowHours = noticeWindowHours(policy) ?? 0;

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

function CancelArea({ appt, policy }: { appt: AppointmentResponse; policy: NoticePolicy }) {
  const [confirming, setConfirming] = useState(false);
  const [cancelAppointment, { isLoading }] = useCancelAppointmentMutation();
  const { data: payment } = useGetPaymentByAppointmentQuery(appt.id, { skip: appt.depositAmount <= 0 });

  if (appt.status !== "Pending" && appt.status !== "Confirmed") return null;

  const withinNoticeWindow = isWithinNoticeWindow(appt, policy);
  const refundPercentOnLateCancel = lateCancelRefundPercent(policy);

  // A deposit is only actually at risk once money has been taken — a card hold
  // ("Captured") or a completed capture/cash confirmation ("Paid"). A CashPending
  // intent or an unconfirmed/never-authorized card payment hasn't collected anything
  // yet, so cancelling it can never be a forfeiture regardless of the notice window —
  // matches CancelAppointmentHandler's own gate on the backend.
  const hasActiveDeposit =
    appt.depositAmount > 0 &&
    (payment?.status === "Captured" || payment?.status === "Paid");

  async function handleCancel() {
    const result = await cancelAppointment(appt.id);
    if ("error" in result) {
      toast.error("Failed to cancel appointment.");
      return;
    }
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

  return (
    <div className="rounded-md border border-destructive/30 bg-destructive/5 p-3 space-y-2">
      <p className="text-xs">
        Cancel this appointment?{" "}
        {/* No promise either way until the policy is known (defaults still loading). */}
        {hasActiveDeposit && withinNoticeWindow === true && "You'll receive a full refund."}
        {hasActiveDeposit && withinNoticeWindow === false && refundPercentOnLateCancel !== undefined &&
          `Cancelling now forfeits ${100 - refundPercentOnLateCancel}% of your deposit.`}
      </p>
      <div className="flex gap-2">
        <Button
          size="sm"
          variant="destructive"
          className="h-7 flex-1 text-xs"
          disabled={isLoading}
          onClick={handleCancel}
        >
          {isLoading ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes, cancel"}
        </Button>
        <Button
          size="sm"
          variant="outline"
          className="h-7 flex-1 text-xs"
          disabled={isLoading}
          onClick={() => setConfirming(false)}
        >
          Keep booking
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

function DepositArea({ appt }: { appt: AppointmentResponse }) {
  const [paying, setPaying] = useState(false);

  const { data: payment, isLoading } = useGetPaymentByAppointmentQuery(appt.id, {
    skip: appt.depositAmount <= 0,
  });
  const { currency: studioCurrency } = useStudioCurrency();
  // The payment row carries its own currency; "my studio" can still be loading right after a studio
  // switch (G-2), so never fall back to the bare number.
  const depositCurrency = payment?.currency ?? studioCurrency;

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
          positive ? "text-green-600 dark:text-green-400" : "text-muted-foreground")}>
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
      <p className="flex items-center gap-1.5 text-xs text-green-600 dark:text-green-400">
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
          Paying {formatCurrency(appt.depositAmount, payment.currency)} in cash at the studio
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
    <Button size="sm" variant="outline" className="h-8 gap-1.5 text-xs"
      onClick={() => setPaying(true)}>
      <CreditCard className="h-3.5 w-3.5" />
      {payment?.status === "Pending"
        ? "Finish deposit payment"
        : depositCurrency
          ? `Pay deposit — ${formatCurrency(appt.depositAmount, depositCurrency)}`
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

function BookingRow({
  appt, artists, policy,
}: {
  appt: AppointmentResponse;
  artists: ArtistResponse[];
  policy: NoticePolicy;
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
          This appointment was cancelled.{" "}
          <button
            type="button"
            className="underline hover:text-foreground"
            onClick={() => window.scrollTo({ top: 0, behavior: "smooth" })}
          >
            Book a new appointment
          </button>
        </p>
      )}
      <DepositArea appt={appt} />
      <div className="flex items-center gap-3">
        <RescheduleArea appt={appt} policy={policy} />
        <CancelArea appt={appt} policy={policy} />
      </div>
    </div>
  );
}

// ── Section ───────────────────────────────────────────────────────────────

export function MyBookingsSection() {
  const { data: appointments = [], isLoading, isError } = useGetMyAppointmentsQuery();
  const { data: artists = [] } = useGetArtistsQuery(undefined);
  const { data: depositRules = [] } = useGetDepositRulesQuery();
  const { data: defaults } = useGetDepositRuleDefaultsQuery();
  const policy: NoticePolicy = { activeRule: depositRules.find((r) => r.isActive), defaults };

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
                    <BookingRow appt={appt} artists={artists} policy={policy} />
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
                    <BookingRow appt={appt} artists={artists} policy={policy} />
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
