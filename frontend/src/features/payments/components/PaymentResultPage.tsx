import { useEffect } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { CheckCircle2, Info, XCircle } from "lucide-react";
import { useAppSelector } from "@/app/hooks";
import { Button } from "@/shared/components/ui/button";
import { Role } from "@/shared/types/roles";

// Where POK's own hosted payment page sends the payer after its result screen (G-12). The backend
// sets the order's redirectUrl / failRedirectUrl to /payment-result?status=paid|failed&payment=<id>
// and POK appends &orderId=<id>. Nothing is looked up here: the payment's real state reaches
// TattooOS through the webhook and reconciliation and shows in My bookings / Payments.
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function PaymentResultPage() {
  const [searchParams] = useSearchParams();
  const role = useAppSelector((s) => s.auth.role);

  const status = searchParams.get("status");
  const paymentId = searchParams.get("payment");
  const retryPath = role && paymentId && GUID.test(paymentId) ? `/pay/${paymentId}` : null;
  const home = role === Role.Client
    ? { to: "/book", label: "Go to My bookings" }
    : { to: "/", label: "Back to TattooOS" };

  const title = status === "paid" ? "Payment received" : status === "failed" ? "Payment not completed" : "Payment";

  useEffect(() => {
    const previous = document.title;
    document.title = `${title} — TattooOS`;
    return () => { document.title = previous; };
  }, [title]);

  return (
    <div className="min-h-screen bg-background flex items-center justify-center px-4">
      <main className="max-w-md w-full space-y-4 text-center">
        {status === "paid" && (
          <>
            <CheckCircle2 className="h-10 w-10 mx-auto text-emerald-500" aria-hidden="true" />
            <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
            <p className="text-sm text-muted-foreground">
              Your card payment went through. For a deposit, your card is only authorised for now — the
              studio charges it later, usually at your appointment.
            </p>
            <p className="text-sm text-muted-foreground">It can take a minute to show in My bookings.</p>
          </>
        )}
        {status === "failed" && (
          <>
            <XCircle className="h-10 w-10 mx-auto text-destructive-text" aria-hidden="true" />
            <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
            <p className="text-sm text-muted-foreground">
              Nothing was charged to your card. You can try again, or pay the studio in cash.
            </p>
          </>
        )}
        {status !== "paid" && status !== "failed" && (
          <>
            <Info className="h-10 w-10 mx-auto text-muted-foreground" aria-hidden="true" />
            <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
            <p className="text-sm text-muted-foreground">
              We couldn&apos;t tell how your payment ended. Check My bookings, or contact the studio.
            </p>
          </>
        )}

        <div className="flex flex-col sm:flex-row gap-2 justify-center pt-2">
          {status === "failed" && retryPath && (
            <Button asChild className="bg-violet-600 hover:bg-violet-700 text-white">
              <Link to={retryPath}>Try again</Link>
            </Button>
          )}
          <Button asChild variant={status === "failed" && retryPath ? "outline" : "default"}>
            <Link to={home.to}>{home.label}</Link>
          </Button>
        </div>
      </main>
    </div>
  );
}
