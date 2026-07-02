import { useState } from "react";
import { toast } from "sonner";
import { Banknote, CalendarDays, CheckCircle2, ChevronUp, CreditCard, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Separator } from "@/shared/components/ui/separator";
import { useGetMyAppointmentsQuery } from "../appointmentsApi";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { useGetPaymentByAppointmentQuery } from "@/features/payments/paymentsApi";
import { PaymentMethodSelector } from "@/features/payments/components/PaymentMethodSelector";
import { AppointmentStatusBadge } from "./AppointmentStatusBadge";
import { DepositStatus } from "../appointment.types";
import type { AppointmentResponse } from "../appointment.types";
import type { ArtistResponse } from "@/features/artists/artistsApi";

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

  if (appt.depositAmount <= 0) return null;

  if (appt.depositStatus === DepositStatus.Forfeited) {
    return <p className="text-xs text-muted-foreground">Deposit forfeited</p>;
  }

  if (appt.depositStatus === DepositStatus.Refunded) {
    return <p className="text-xs text-muted-foreground">Deposit refunded</p>;
  }

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-xs text-muted-foreground py-1">
        <Loader2 className="h-3 w-3 animate-spin" />
        Checking deposit…
      </div>
    );
  }

  // Settled states — nothing for the client to do
  if (appt.depositStatus === DepositStatus.Paid || payment?.status === "Paid") {
    return (
      <p className="flex items-center gap-1.5 text-xs text-green-600 dark:text-green-400">
        <CheckCircle2 className="h-3.5 w-3.5" />
        Deposit paid
      </p>
    );
  }

  if (payment?.status === "Captured") {
    return (
      <p className="flex items-center gap-1.5 text-xs text-green-600 dark:text-green-400">
        <CheckCircle2 className="h-3.5 w-3.5" />
        Deposit authorised — charged when the studio confirms
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
                : "Deposit authorised.",
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
          Paying €{appt.depositAmount.toFixed(2)} in cash at the studio
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
        : `Pay deposit — €${appt.depositAmount.toFixed(2)}`}
    </Button>
  );
}

// ── Booking row ───────────────────────────────────────────────────────────

function artistName(artistId: string, artists: ArtistResponse[]): string {
  const a = artists.find((x) => x.id === artistId);
  return a ? `${a.firstName} ${a.lastName}` : "—";
}

function BookingRow({ appt, artists }: { appt: AppointmentResponse; artists: ArtistResponse[] }) {
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
          <p className="py-4 text-sm text-destructive">
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
