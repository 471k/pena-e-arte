import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { CreditCard, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { useGetPaymentsQuery } from "../paymentsApi";
import type { PaymentResponse } from "../payment.types";
import { PaymentStatus } from "../payment.types";

const PAGE_SIZE = 20;

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function PaymentStatusBadge({ status }: { status: PaymentStatus }) {
  const classes: Record<PaymentStatus, string> = {
    [PaymentStatus.Pending]:  "bg-yellow-500/10 text-yellow-700",
    [PaymentStatus.Paid]:     "bg-green-500/10  text-green-700",
    [PaymentStatus.Refunded]: "bg-blue-500/10   text-blue-700",
    [PaymentStatus.Failed]:   "bg-red-500/10    text-red-700",
  };
  return (
    <span className={`text-xs px-2 py-0.5 rounded-full font-medium ${classes[status]}`}>
      {status}
    </span>
  );
}

function PaymentRow({
  payment,
  onClick,
}: {
  payment: PaymentResponse;
  onClick: () => void;
}) {
  return (
    <Card
      className="cursor-pointer hover:bg-muted/40 transition-colors"
      onClick={onClick}
    >
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div className="space-y-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="font-mono text-xs text-muted-foreground">
              {payment.id.slice(0, 8)}…
            </span>
            <PaymentStatusBadge status={payment.status} />
          </div>
          <p className="text-xs text-muted-foreground">
            Appt {payment.appointmentId.slice(0, 8)}… · {formatDate(payment.createdAt)}
          </p>
          {payment.sessionSplits.length > 0 && (
            <p className="text-xs text-muted-foreground">
              {payment.sessionSplits.length} split{payment.sessionSplits.length !== 1 ? "s" : ""}
            </p>
          )}
        </div>
        <div className="text-right shrink-0">
          <p className="text-sm font-semibold">{formatCurrency(payment.amount)}</p>
          {payment.paidAt && (
            <p className="text-xs text-muted-foreground">Paid {formatDate(payment.paidAt)}</p>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

export function PaymentListPage() {
  const navigate = useNavigate();
  const [cursor, setCursor]       = useState<string | undefined>(undefined);
  const [allPayments, setAllPayments] = useState<PaymentResponse[]>([]);

  const { data, isLoading, isFetching, isError } = useGetPaymentsQuery({
    lastSeenId: cursor,
    pageSize:   PAGE_SIZE,
  });

  useEffect(() => {
    if (!data) return;
    if (cursor === undefined) {
      setAllPayments(data);
    } else {
      setAllPayments((prev) => {
        const lastId = prev[prev.length - 1]?.id;
        if (lastId !== cursor) return prev;
        return [...prev, ...data];
      });
    }
  }, [data, cursor]);

  const hasMore = (data?.length ?? 0) === PAGE_SIZE;

  function handleLoadMore() {
    const last = allPayments[allPayments.length - 1];
    if (last) setCursor(last.id);
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Payments</span>
        </div>
        {allPayments.length > 0 && (
          <span className="text-xs text-muted-foreground">{allPayments.length} loaded</span>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-2">
        {isLoading && (
          <div className="flex items-center justify-center py-16 text-muted-foreground gap-2">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading payments…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load payments. Please try again.
          </p>
        )}

        {!isLoading && !isError && allPayments.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">No payments yet.</p>
        )}

        {allPayments.map((payment) => (
          <PaymentRow
            key={payment.id}
            payment={payment}
            onClick={() => navigate(`/payments/${payment.appointmentId}`)}
          />
        ))}

        {!isLoading && !isError && hasMore && (
          <div className="flex justify-center pt-4">
            <Button
              variant="outline"
              size="sm"
              onClick={handleLoadMore}
              disabled={isFetching}
              className="gap-1.5"
            >
              {isFetching ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  Loading…
                </>
              ) : (
                "Load more"
              )}
            </Button>
          </div>
        )}
      </main>
    </div>
  );
}
