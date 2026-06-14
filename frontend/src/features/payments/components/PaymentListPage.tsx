import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { CreditCard, Loader2, Plus } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { DataTable } from "@/shared/components/DataTable";
import { Badge } from "@/shared/components/ui/badge";
import { cn } from "@/shared/utils/cn";
import { useGetPaymentsQuery } from "../paymentsApi";
import type { PaymentResponse } from "../payment.types";
import { PaymentStatus } from "../payment.types";

const PAGE_SIZE = 20;

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

const PAYMENT_STATUS_STYLES: Record<PaymentStatus, string> = {
  [PaymentStatus.Pending]:     "border-yellow-300 bg-yellow-100 text-yellow-800 hover:bg-yellow-100",
  [PaymentStatus.CashPending]: "border-orange-300 bg-orange-100 text-orange-800 hover:bg-orange-100",
  [PaymentStatus.Captured]:    "border-blue-300 bg-blue-100 text-blue-800 hover:bg-blue-100",
  [PaymentStatus.Paid]:        "border-green-300 bg-green-100 text-green-800 hover:bg-green-100",
  [PaymentStatus.Refunded]:    "border-slate-300 bg-slate-100 text-slate-800 hover:bg-slate-100",
  [PaymentStatus.Failed]:      "border-red-300 bg-red-100 text-red-800 hover:bg-red-100",
};

function PaymentStatusBadge({ status }: { status: PaymentStatus }) {
  return (
    <Badge variant="outline" className={cn(PAYMENT_STATUS_STYLES[status])}>
      {status}
    </Badge>
  );
}

function PaymentRowSkeleton() {
  return (
    <div className="flex items-center gap-4 py-3 border-b">
      <Skeleton className="h-4 w-20" />
      <Skeleton className="h-5 w-16" />
      <Skeleton className="h-4 w-24" />
      <Skeleton className="h-4 w-16" />
    </div>
  );
}

export function PaymentListPage() {
  const navigate = useNavigate();
  const [cursor, setCursor]                 = useState<string | undefined>(undefined);
  const [previousPages, setPreviousPages]   = useState<PaymentResponse[]>([]);

  const { data, isLoading, isFetching, isError } = useGetPaymentsQuery({
    lastSeenId: cursor,
    pageSize:   PAGE_SIZE,
  });

  // Pages already loaded are captured at "Load more" click time; the current
  // page comes straight from the query — no effect-based accumulation needed.
  const allPayments = cursor === undefined
    ? data ?? []
    : [...previousPages, ...(data ?? [])];

  const hasMore = (data?.length ?? 0) === PAGE_SIZE;

  function handleLoadMore() {
    const last = allPayments[allPayments.length - 1];
    if (!last) return;
    setPreviousPages(allPayments);
    setCursor(last.id);
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Payments</span>
        </div>
        <div className="flex items-center gap-3">
          {allPayments.length > 0 && (
            <span className="text-xs text-muted-foreground">{allPayments.length} loaded</span>
          )}
          <Button size="sm" className="gap-1.5" onClick={() => navigate("/payments/new")}>
            <Plus className="h-4 w-4" />
            New payment
          </Button>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        {isLoading && (
          <div className="space-y-0">
            {Array.from({ length: 8 }).map((_, i) => (
              <PaymentRowSkeleton key={i} />
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load payments. Please try again.
          </p>
        )}

        {!isLoading && !isError && (
          <DataTable<PaymentResponse>
            columns={[
              {
                header: "Client",
                cell: (p) => (
                  <span className="text-sm font-medium">
                    {p.clientName || <span className="text-muted-foreground">—</span>}
                  </span>
                ),
              },
              {
                header: "Session",
                cell: (p) =>
                  p.appointmentDate ? (
                    <span className="text-sm text-muted-foreground">{formatDate(p.appointmentDate)}</span>
                  ) : (
                    "—"
                  ),
              },
              {
                header: "Status",
                cell: (p) => <PaymentStatusBadge status={p.status} />,
              },
              {
                header: "Amount",
                cell: (p) => (
                  <span className="font-semibold">{formatCurrency(p.amount)}</span>
                ),
              },
              {
                header: "Method",
                cell: (p) => p.method,
              },
              {
                header: "Paid",
                cell: (p) => (p.paidAt ? formatDate(p.paidAt) : "—"),
              },
            ]}
            data={allPayments}
            keyExtractor={(p) => p.id}
            onRowClick={(p) => navigate(`/payments/${p.appointmentId}`)}
            emptyMessage="No payments yet."
          />
        )}

        {!isLoading && !isError && hasMore && (
          <div className="flex justify-center pt-2">
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
