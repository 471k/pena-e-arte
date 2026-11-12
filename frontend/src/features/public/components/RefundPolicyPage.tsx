import { PublicContentLayout } from "./PublicContentLayout";
import { ROUTE_META } from "@/shared/seo/siteRoutes";

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
//   AppointmentSelfServiceDefaults.cs (CancellationWindowHours = 48, RefundPercentOnLateCancel = 0),
//   MarkNoShowCommand.cs (no-show => DepositStatus.Forfeited),
//   CancellationOutcome.cs (staff cancellation = 100%; confirmed cash = settled with the studio;
//   unpaid cash intent = nothing taken), RescheduleEligibility.cs (a client can't self-reschedule
//   inside the window; rescheduling never moves deposit money), C-4 (card deposits are charged when
//   paid, 22 Oct 2026). Corrected 12 Nov 2026: this page used to say a late reschedule cost part of
//   the deposit and never said the deposit is charged up front.
// If the underlying behaviour changes, this page must be updated to match. The "48 hours" below is
// policy text (a public legal page, reviewed as written), so it stays literal instead of reading
// GET /deposit-rules/defaults like the app screens do.
export function RefundPolicyPage() {
  return (
    <PublicContentLayout
      title={ROUTE_META["/refund-policy"].title}
      description={ROUTE_META["/refund-policy"].description}
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
        <p>
          A card deposit is charged in full when you pay it, not held until your
          appointment. It is paid to the studio.
        </p>
      </Section>

      <Section heading="Cancelling">
        <p>
          If you cancel with at least the required notice, your deposit is refunded in
          full (100%). The default notice window is 48 hours before the appointment; an
          individual studio may set a different window.
        </p>
        <p>
          If you cancel inside the notice window, the studio&apos;s late-cancellation refund
          percentage applies. By default this is 0% — the deposit is forfeited — unless the
          studio has configured a partial refund.
        </p>
      </Section>

      <Section heading="Rescheduling">
        <p>
          You can move your booking to another time yourself if you have at least the
          required notice. Your deposit stays with the booking; moving it costs nothing.
        </p>
        <p>
          Inside the notice window you can&apos;t reschedule yourself. Contact the studio
          directly to ask.
        </p>
      </Section>

      <Section heading="No-shows">
        <p>
          If you do not attend a booked appointment, the deposit is forfeited.
        </p>
      </Section>

      <Section heading="Cancellations by the studio">
        <p>
          If the studio cancels your appointment, your card deposit is refunded in full,
          whatever the timing. The notice window applies only to cancellations you make.
        </p>
      </Section>

      <Section heading="Cash deposits">
        <p>
          If you chose to pay your deposit in cash at the studio and haven&apos;t paid it yet,
          cancelling costs nothing, at any time. A cash deposit you have already paid is not
          refunded through TattooOS: the studio settles it with you directly.
        </p>
      </Section>

      <Section heading="How refunds reach you">
        <p>
          A card refund goes back to the card you paid with and is sent as soon as the
          appointment is cancelled. Your bank may take a few days to show it.
        </p>
      </Section>
    </PublicContentLayout>
  );
}
