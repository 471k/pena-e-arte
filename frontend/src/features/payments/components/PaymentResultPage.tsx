import { useEffect, type ReactNode } from "react";
import { Link, useLocation, useSearchParams } from "react-router-dom";
import { CheckCircle2, Info, XCircle } from "lucide-react";
import { useAppSelector } from "@/app/hooks";
import { loginPathFor } from "@/app/router";
import { Button } from "@/shared/components/ui/button";
import { Money } from "@/shared/components/Money";
import { Role } from "@/shared/types/roles";
import { useGetPaymentResultQuery } from "../paymentsApi";
import { PaymentStatus, type PaymentConfirmationResponse } from "../payment.types";

// Where POK's own hosted payment page sends the payer after its result screen (G-12). The backend sets
// the order's redirectUrl / failRedirectUrl to /payment-result?status=paid|failed&payment=<id> and POK
// appends &orderId=<id>. C08: the URL's status and orderId are POK's contract and are NOT read here —
// anyone can type them. Only `payment` is read, to make the server call. Every sentence about money on
// this page (the payment was received, held, refunded, not completed, not confirmed yet) is reachable
// only from a successful POST /payments/{id}/confirm answer; with no answer the page says it couldn't
// check. Settlement itself was never decided here: that is the webhook + PaymentReconciliationJob.
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const NEUTRAL_TITLE = "Payment";

type Icon = "check" | "cross" | "info";

interface View {
  /** The h1, and document.title unless `neutralTitle`. */
  heading:       string;
  body:          ReactNode;
  icon:          Icon;
  neutralTitle?: boolean;
  /** Row 2: announced to screen readers, and no way to leave mid-check. */
  checking?:     boolean;
  tryAgain?:     boolean;
  refresh?:      boolean;
  signIn?:       boolean;
}

function Amount({ result, value }: { result: PaymentConfirmationResponse; value: number }) {
  return <Money amount={value} currency={result.currency} frozen={result} />;
}

// The spec's table (docs/claude/spec-c08-payment-result-honesty-2026-11-18.md §5), rows 3-9. "Try again"
// only where paying again is correct: never on a held, charged or refunded payment, and never once the
// appointment is cancelled — that would invite a second payment on the same booking.
function viewFor(result: PaymentConfirmationResponse): View | null {
  if (result.appointmentCancelled) {
    return {
      heading: "Appointment cancelled",
      icon:    "info",
      body: (
        <p className="text-sm text-muted-foreground">
          {"This appointment was cancelled while your payment was going through. Anything taken is being returned to your card automatically — you don't need to do anything. If you don't see it within a few days, contact the studio."}
        </p>
      ),
    };
  }
  switch (result.status) {
    case PaymentStatus.Paid:
      return {
        heading: "Payment received",
        icon:    "check",
        body: (
          <>
            <p className="text-sm text-muted-foreground">
              Your card was charged <Amount result={result} value={result.amount} />.
              {" If you cancel, the studio's cancellation policy decides how much of a deposit is refunded."}
            </p>
            <p className="text-sm text-muted-foreground">It can take a minute to show in My bookings.</p>
          </>
        ),
      };
    case PaymentStatus.Captured:
      return {
        heading: "Payment authorised",
        icon:    "check",
        body: (
          <p className="text-sm text-muted-foreground">
            Your bank has authorised <Amount result={result} value={result.amount} />
            {" and it's being held — not charged yet. The studio charges it when it captures the deposit."}
          </p>
        ),
      };
    case PaymentStatus.Refunded:
      return {
        heading: "Payment refunded",
        icon:    "info",
        body: (
          <p className="text-sm text-muted-foreground">
            This payment was charged and has since been refunded
            {result.refundedAmount != null && (
              <> — <Amount result={result} value={result.refundedAmount} /> went back to your card</>
            )}
            . Refunds usually take a few days to appear.
          </p>
        ),
      };
    case PaymentStatus.Failed:
      return {
        heading:  "Payment not completed",
        icon:     "cross",
        tryAgain: true,
        body: (
          <p className="text-sm text-muted-foreground">
            {"Your bank reported that this payment didn't go through, so you shouldn't see a charge for it. You can try again, or pay the studio in cash. If a charge does appear, contact the studio."}
          </p>
        ),
      };
    case PaymentStatus.Pending:
      return {
        heading:  "Payment not confirmed yet",
        icon:     "info",
        tryAgain: true,
        refresh:  true,
        body: (
          <p className="text-sm text-muted-foreground">
            {"We haven't had confirmation from your bank yet. Refresh in a moment to check again — nothing is lost either way."}
          </p>
        ),
      };
    case PaymentStatus.CashPending:
      return {
        heading: "Cash payment",
        icon:    "info",
        body: (
          <p className="text-sm text-muted-foreground">
            This payment is recorded as cash, to settle with the studio directly.
          </p>
        ),
      };
    default:
      return null; // a status this page doesn't know: say we couldn't check rather than guess
  }
}

const SIGNED_OUT: View = {
  heading:      NEUTRAL_TITLE,
  icon:         "info",
  neutralTitle: true,
  signIn:       true,
  body: (
    <p className="text-sm text-muted-foreground">
      {"We can't confirm this payment here. Sign in to check it, or check My bookings."}
    </p>
  ),
};

const CHECKING: View = {
  heading:      "Checking your payment",
  icon:         "info",
  neutralTitle: true,
  checking:     true,
  body: (
    <p className="text-sm text-muted-foreground">
      {"We're checking with your bank. This usually takes a few seconds."}
    </p>
  ),
};

const COULD_NOT_CHECK: View = {
  heading:      NEUTRAL_TITLE,
  icon:         "info",
  neutralTitle: true,
  body: (
    <p className="text-sm text-muted-foreground">
      {"We couldn't check how your payment ended. Check My bookings, or contact the studio."}
    </p>
  ),
};

function IconFor({ icon }: { icon: Icon }) {
  if (icon === "check") return <CheckCircle2 className="h-10 w-10 mx-auto text-emerald-500" aria-hidden="true" />;
  if (icon === "cross") return <XCircle className="h-10 w-10 mx-auto text-destructive-text" aria-hidden="true" />;
  return <Info className="h-10 w-10 mx-auto text-muted-foreground" aria-hidden="true" />;
}

export function PaymentResultPage() {
  const [searchParams] = useSearchParams();
  const location = useLocation();
  const role = useAppSelector((s) => s.auth.role);

  const paymentParam = searchParams.get("payment");
  const paymentId = paymentParam && GUID.test(paymentParam) ? paymentParam : null;
  const signedIn = role !== null;

  // Signed out: no call at all (the endpoint needs a sign-in, and there is an honest thing to say
  // without one). A malformed id never reaches the API.
  const { data, isUninitialized, isFetching, isError, refetch } = useGetPaymentResultQuery(paymentId ?? "", {
    skip: !signedIn || paymentId === null,
    refetchOnMountOrArgChange: true,
  });

  // First match wins (spec §5): signed out, no usable id, checking, couldn't check, then the answer.
  const view: View =
    !signedIn ? SIGNED_OUT
    : paymentId === null ? COULD_NOT_CHECK
    // isUninitialized: a render before the request is in flight has tried nothing, so it must not say
    // "we couldn't check". RTK Query 2.x already reports an un-skipped uninitialized query as fetching,
    // so today this never decides; it keeps the page true if that ever changes. Pinned by the
    // first-paint test (Desktop's C08 review 1.2).
    : isUninitialized || isFetching ? CHECKING
    : isError || !data ? COULD_NOT_CHECK
    : viewFor(data) ?? COULD_NOT_CHECK;

  const home = role === Role.Client
    ? { to: "/book", label: "Go to My bookings" }
    : { to: "/", label: "Back to TattooOS" };

  const title = view.neutralTitle ? NEUTRAL_TITLE : view.heading;
  useEffect(() => {
    const previous = document.title;
    document.title = `${title} — TattooOS`;
    return () => { document.title = previous; };
  }, [title]);

  const primary = view.tryAgain || view.signIn;

  return (
    <div className="min-h-screen bg-background flex items-center justify-center px-4">
      <main className="max-w-md w-full space-y-4 text-center">
        <IconFor icon={view.icon} />
        {view.checking ? (
          <div role="status" className="space-y-4">
            <h1 className="text-xl font-semibold tracking-tight">{view.heading}</h1>
            {view.body}
          </div>
        ) : (
          <>
            <h1 className="text-xl font-semibold tracking-tight">{view.heading}</h1>
            {view.body}
          </>
        )}

        {!view.checking && (
          <div className="flex flex-col sm:flex-row gap-2 justify-center pt-2">
            {view.signIn && (
              <Button asChild className="bg-violet-600 hover:bg-violet-700 text-white">
                <Link to={loginPathFor(location)}>Sign in</Link>
              </Button>
            )}
            {view.tryAgain && paymentId && (
              <Button asChild className="bg-violet-600 hover:bg-violet-700 text-white">
                <Link to={`/pay/${paymentId}`}>Try again</Link>
              </Button>
            )}
            {view.refresh && (
              <Button variant="outline" onClick={() => void refetch()}>Refresh</Button>
            )}
            <Button asChild variant={primary ? "outline" : "default"}>
              <Link to={view.signIn ? "/" : home.to}>{view.signIn ? "Back to TattooOS" : home.label}</Link>
            </Button>
          </div>
        )}
      </main>
    </div>
  );
}
