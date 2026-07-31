import { PublicContentLayout } from "./PublicContentLayout";

function Section({ heading, children }: { heading: string; children: React.ReactNode }) {
  return (
    <section className="mt-6">
      <h2 className="text-lg font-semibold tracking-tight">{heading}</h2>
      <div className="mt-2 space-y-2 text-sm text-muted-foreground">{children}</div>
    </section>
  );
}

// REAL copy, derived from the live implementation — NOT aspirational text:
//   DepositRule.cs, DepositCalculator.cs, ClientCancellationPolicy.cs,
//   AppointmentSelfServiceDefaults.cs (CancellationWindowHours = 24),
//   MarkNoShowCommand.cs (no-show => DepositStatus.Forfeited).
// If the underlying behaviour changes, this page must be updated to match.
export function RefundPolicyPage() {
  return (
    <PublicContentLayout
      title="Refund Policy — TattooOS"
      description="How deposits, cancellations, and no-shows are handled on TattooOS."
      canonicalPath="/refund-policy"
    >
      <h1 className="text-2xl font-semibold tracking-tight">Refund Policy</h1>
      <p className="mt-2 text-sm text-muted-foreground">
        Deposit and cancellation terms are set by each studio. This page explains how the
        platform applies them. Your booking confirmation shows the exact figures for your
        appointment.
      </p>

      <Section heading="Deposits">
        <p>
          A studio may require a deposit to secure a booking. Depending on the
          studio&apos;s active deposit rule, the deposit is either a fixed amount or a
          percentage of the estimated session price (the artist&apos;s hourly rate applied
          to the booked duration). If a percentage rule applies but the artist has no
          hourly rate set, no deposit is taken.
        </p>
      </Section>

      <Section heading="Cancelling or rescheduling">
        <p>
          If you cancel or reschedule with at least the required notice, your deposit is
          refunded in full (100%). The default notice window is 24 hours before the
          appointment; an individual studio may set a different window.
        </p>
        <p>
          If you cancel or reschedule inside the notice window, the studio&apos;s
          late-cancellation refund percentage applies. By default this is 0% — the deposit
          is forfeited — unless the studio has configured a partial refund.
        </p>
      </Section>

      <Section heading="No-shows">
        <p>
          If you do not attend a booked appointment, the deposit is forfeited.
        </p>
      </Section>

      <Section heading="Cancellations by the studio">
        <p>
          The notice window applies only to client-initiated cancellations. If the studio
          cancels your appointment, that notice window does not apply to you.
        </p>
      </Section>
    </PublicContentLayout>
  );
}
