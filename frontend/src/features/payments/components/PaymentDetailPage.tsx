import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeft,
  CheckCircle2,
  CreditCard,
  Loader2,
  RotateCcw,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import {
  useGetPaymentByAppointmentQuery,
  useCaptureDepositMutation,
  useRefundPaymentMutation,
} from "../paymentsApi";
import { PaymentStatus } from "../payment.types";
import { SessionSplitsEditor } from "./SessionSplitsEditor";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function StatusBadge({ status }: { status: PaymentStatus }) {
  const classes: Record<PaymentStatus, string> = {
    [PaymentStatus.Pending]:  "bg-yellow-500/10 text-yellow-700",
    [PaymentStatus.Paid]:     "bg-green-500/10  text-green-700",
    [PaymentStatus.Refunded]: "bg-blue-500/10   text-blue-700",
    [PaymentStatus.Failed]:   "bg-red-500/10    text-red-700",
  };
  return (
    <span className={cn("text-xs px-2 py-0.5 rounded-full font-medium", classes[status])}>
      {status}
    </span>
  );
}

function RefundSection({ paymentId }: { paymentId: string }) {
  const [open, setOpen]           = useState(false);
  const [amountStr, setAmountStr] = useState("");
  const [refund, { isLoading }]   = useRefundPaymentMutation();

  async function handleRefund() {
    const amount = amountStr ? parseFloat(amountStr) : undefined;
    await refund({ id: paymentId, amount });
    setOpen(false);
    setAmountStr("");
  }

  if (!open) {
    return (
      <Button
        variant="outline"
        size="sm"
        onClick={() => setOpen(true)}
        className="gap-1.5 text-destructive hover:text-destructive"
      >
        <RotateCcw className="h-3.5 w-3.5" />
        Refund
      </Button>
    );
  }

  return (
    <Card>
      <CardContent className="p-4 space-y-3">
        <p className="text-sm font-medium">Issue refund</p>
        <div className="space-y-1">
          <Label htmlFor="refund-amount" className="text-xs">
            Amount (€) — leave blank for full refund
          </Label>
          <Input
            id="refund-amount"
            type="number"
            step="0.01"
            min="0.01"
            placeholder="Full amount"
            value={amountStr}
            onChange={(e) => setAmountStr(e.target.value)}
          />
        </div>
        <div className="flex gap-2">
          <Button
            variant="destructive"
            size="sm"
            onClick={handleRefund}
            disabled={isLoading}
            className="flex-1"
          >
            {isLoading ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                Refunding…
              </>
            ) : (
              "Confirm refund"
            )}
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => { setOpen(false); setAmountStr(""); }}
            disabled={isLoading}
            className="flex-1"
          >
            Cancel
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

export function PaymentDetailPage() {
  const { appointmentId } = useParams<{ appointmentId: string }>();
  const navigate          = useNavigate();
  const canOwner          = usePermission(Role.Owner);

  const { data: payment, isLoading, isError } =
    useGetPaymentByAppointmentQuery(appointmentId!);

  const [capture, { isLoading: isCapturing }] = useCaptureDepositMutation();

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center gap-2 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span className="text-sm">Loading payment…</span>
      </div>
    );
  }

  if (isError || !payment) {
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-muted-foreground">No payment found for this appointment.</p>
        <div className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => navigate("/payments")}>
            <ArrowLeft className="h-4 w-4 mr-1" />
            Payments
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() =>
              navigate(`/payments/new?appointmentId=${appointmentId}`)
            }
          >
            <CreditCard className="h-4 w-4 mr-1" />
            Create payment
          </Button>
        </div>
      </div>
    );
  }

  const isPending  = payment.status === PaymentStatus.Pending;
  const isPaid     = payment.status === PaymentStatus.Paid;

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/payments")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Payments
        </Button>

        <div className="flex items-center gap-2">
          {isPending && canOwner && (
            <Button
              size="sm"
              onClick={() => capture(payment.id)}
              disabled={isCapturing}
              className="gap-1.5"
            >
              {isCapturing ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  Capturing…
                </>
              ) : (
                <>
                  <CheckCircle2 className="h-3.5 w-3.5" />
                  Capture deposit
                </>
              )}
            </Button>
          )}

          {isPaid && canOwner && <RefundSection paymentId={payment.id} />}
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        <div className="flex items-center gap-4">
          <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-primary/10">
            <CreditCard className="h-6 w-6 text-primary" />
          </div>
          <div className="space-y-1">
            <div className="flex items-center gap-2">
              <h1 className="text-lg font-semibold">{formatCurrency(payment.amount)}</h1>
              <StatusBadge status={payment.status} />
            </div>
            <p className="text-xs text-muted-foreground font-mono">
              {payment.id}
            </p>
          </div>
        </div>

        <Card>
          <CardContent className="p-4 space-y-2.5">
            <Row label="Appointment" value={payment.appointmentId} mono />
            <Row label="Client"      value={payment.clientId}      mono />
            <Row label="Created"     value={formatDate(payment.createdAt)} />
            {payment.paidAt && (
              <Row label="Paid at" value={formatDate(payment.paidAt)} />
            )}
            {payment.stripePaymentIntentId && (
              <Row label="Stripe PI" value={payment.stripePaymentIntentId} mono />
            )}
          </CardContent>
        </Card>

        {canOwner && (
          <SessionSplitsEditor
            paymentId={payment.id}
            currentSplits={payment.sessionSplits}
          />
        )}
      </main>
    </div>
  );
}

function Row({
  label,
  value,
  mono = false,
}: {
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex items-start justify-between gap-3 text-sm">
      <span className="text-muted-foreground shrink-0">{label}</span>
      <span className={cn("text-right break-all", mono && "font-mono text-xs")}>
        {value}
      </span>
    </div>
  );
}
