import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeft,
  Banknote,
  CheckCircle2,
  CreditCard,
  Download,
  Loader2,
  RotateCcw,
} from "lucide-react";
import { toast } from "sonner";
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
  useConfirmCashDepositMutation,
  useDownloadInvoiceMutation,
} from "../paymentsApi";
import { PaymentMethod, PaymentStatus } from "../payment.types";
import { SessionSplitsEditor } from "./SessionSplitsEditor";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function StatusBadge({ status }: { status: string }) {
  const classes: Record<string, string> = {
    Pending:     "bg-yellow-500/10 text-yellow-700",
    CashPending: "bg-orange-500/10 text-orange-700",
    Captured:    "bg-blue-500/10   text-blue-700",
    Paid:        "bg-green-500/10  text-green-700",
    Refunded:    "bg-slate-500/10  text-slate-700",
    Failed:      "bg-red-500/10    text-red-700",
  };
  return (
    <span className={cn("text-xs px-2 py-0.5 rounded-full font-medium", classes[status] ?? "bg-muted")}>
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
    try {
      await refund({ id: paymentId, amount }).unwrap();
      setOpen(false);
      setAmountStr("");
    } catch (e) {
      const err = e as { data?: { message?: string } } | undefined;
      toast.error(err?.data?.message ?? "Refund failed. Please try again.");
    }
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

  const [capture,     { isLoading: isCapturing }]  = useCaptureDepositMutation();
  const [confirmCash, { isLoading: isConfirming }] = useConfirmCashDepositMutation();
  const [downloadInvoice, { isLoading: isDownloading }] = useDownloadInvoiceMutation();

  async function handleCapture(paymentId: string) {
    try {
      await capture(paymentId).unwrap();
      toast.success("Deposit captured.");
    } catch (e) {
      const err = e as { data?: { message?: string } } | undefined;
      toast.error(err?.data?.message ?? "Capture failed. Please try again.");
    }
  }

  async function handleDownloadInvoice(paymentId: string) {
    try {
      const blob = await downloadInvoice(paymentId).unwrap();
      const url  = URL.createObjectURL(blob);
      const a    = document.createElement("a");
      a.href     = url;
      a.download = `invoice-${paymentId.slice(0, 8)}.pdf`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      toast.error("Failed to download invoice. Please try again.");
    }
  }

  async function handleConfirmCash(paymentId: string) {
    try {
      await confirmCash(paymentId).unwrap();
      toast.success("Cash deposit confirmed.");
    } catch (e) {
      const err = e as { data?: { message?: string } } | undefined;
      toast.error(err?.data?.message ?? "Confirmation failed. Please try again.");
    }
  }

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
            onClick={() => navigate(`/payments/new?appointmentId=${appointmentId}`)}
          >
            <CreditCard className="h-4 w-4 mr-1" />
            Create payment
          </Button>
        </div>
      </div>
    );
  }

  const isAwaitingAction = payment.status === PaymentStatus.Pending
    || payment.status === PaymentStatus.CashPending;
  const isPaid       = payment.status === PaymentStatus.Paid;
  const isRefunded   = payment.status === PaymentStatus.Refunded;
  const isCard       = payment.method === PaymentMethod.Card;
  const isCash       = payment.method === PaymentMethod.Cash;
  const hasReceipt   = isPaid || isRefunded;

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
          {payment.status === PaymentStatus.Pending && canOwner && isCard && (
            <span className="text-xs text-muted-foreground">
              Awaiting client card authorization — share the payment link to collect the deposit.
            </span>
          )}

          {payment.status === PaymentStatus.Captured && canOwner && isCard && (
            <Button
              size="sm"
              onClick={() => handleCapture(payment.id)}
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

          {payment.status === PaymentStatus.CashPending && canOwner && isCash && (
            <Button
              size="sm"
              onClick={() => handleConfirmCash(payment.id)}
              disabled={isConfirming}
              className="gap-1.5"
            >
              {isConfirming ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  Confirming…
                </>
              ) : (
                <>
                  <Banknote className="h-3.5 w-3.5" />
                  Confirm cash received
                </>
              )}
            </Button>
          )}

          {isPaid && canOwner && isCard && <RefundSection paymentId={payment.id} />}

          {hasReceipt && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => handleDownloadInvoice(payment.id)}
              disabled={isDownloading}
              className="gap-1.5"
            >
              {isDownloading ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  Downloading…
                </>
              ) : (
                <>
                  <Download className="h-3.5 w-3.5" />
                  Download receipt
                </>
              )}
            </Button>
          )}
        </div>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        <div className="flex items-center gap-4">
          <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-primary/10">
            {isCash
              ? <Banknote   className="h-6 w-6 text-primary" />
              : <CreditCard className="h-6 w-6 text-primary" />}
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
            <Row label="Method"      value={isCash ? "Cash" : "Card"} />
            <Row label="Appointment" value={payment.appointmentId} mono />
            {payment.paidAt && (
              <Row label="Paid at" value={formatDate(payment.paidAt)} />
            )}
            {payment.cashNote && (
              <Row label="Note" value={payment.cashNote} />
            )}
            {payment.stripePaymentIntentId && (
              <Row label="Stripe PI" value={payment.stripePaymentIntentId} mono />
            )}
          </CardContent>
        </Card>

        {isAwaitingAction && isCash && (
          <div className="rounded-lg border border-orange-200 bg-orange-50/30 p-4 text-sm text-orange-800">
            Awaiting cash collection from client.
          </div>
        )}

        {canOwner && (
          <SessionSplitsEditor
            paymentId={payment.id}
            currentSplits={payment.splits ?? []}
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
