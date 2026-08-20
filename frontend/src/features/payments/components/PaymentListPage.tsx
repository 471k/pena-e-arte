import { useMemo, useState } from "react";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useNavigate } from "react-router-dom";
import { ChevronRight, CreditCard, Loader2, Plus, Search } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Input } from "@/shared/components/ui/input";
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

const STATUS_LABELS: Record<PaymentStatus, string> = {
  [PaymentStatus.Pending]:     "Pending",
  [PaymentStatus.CashPending]: "Cash Pending",
  [PaymentStatus.Captured]:    "Captured",
  [PaymentStatus.Paid]:        "Paid",
  [PaymentStatus.Refunded]:    "Refunded",
  [PaymentStatus.Failed]:      "Failed",
};

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
      {STATUS_LABELS[status]}
    </Badge>
  );
}

function PaymentRowSkeleton() {
  return (
    <div
      className="flex items-center gap-4 py-3 border-b"
      aria-hidden="true"
    >
      <Skeleton className="h-4 w-28 flex-1" />
      <Skeleton className="h-4 w-20" />
      <Skeleton className="h-4 w-16 font-semibold" />
      <Skeleton className="h-5 w-24 rounded-full" />
      <Skeleton className="h-4 w-10" />
      <Skeleton className="h-4 w-20" />
      <Skeleton className="h-7 w-14 rounded-md" />
    </div>
  );
}

export function PaymentListPage() {
  useDocumentMeta({ title: "Payments — TattooOS", canonical: "/payments" });

  const navigate = useNavigate();
  const [cursor, setCursor]               = useState<string | undefined>(undefined);
  const [previousPages, setPreviousPages] = useState<PaymentResponse[]>([]);

  const { data, isLoading, isFetching, isError, refetch } = useGetPaymentsQuery({
    lastSeenId: cursor,
    pageSize:   PAGE_SIZE,
  });
  const errorMessage = useSuspensionAwareError(isError, "Failed to load payments. Please try again.");

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

  const [search,       setSearch]       = useState("");
  const [statusFilter, setStatusFilter] = useState<PaymentStatus | null>(null);

  const presentStatuses = useMemo<PaymentStatus[]>(() => {
    const set = new Set<PaymentStatus>();
    allPayments.forEach((p) => set.add(p.status));
    return [...set];
  }, [allPayments]);

  const filteredPayments = useMemo<PaymentResponse[]>(() => {
    let result = allPayments;
    const term = search.trim().toLowerCase();
    if (term) {
      result = result.filter((p) => p.clientName.toLowerCase().includes(term));
    }
    if (statusFilter) {
      result = result.filter((p) => p.status === statusFilter);
    }
    return result;
  }, [allPayments, search, statusFilter]);

  const tableEmptyMessage = search
    ? `No payments match "${search}".`
    : statusFilter
    ? `No ${STATUS_LABELS[statusFilter]} payments found.`
    : "No payments yet.";

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Payments</span>
        </div>
        <div className="flex items-center gap-3">
          {allPayments.length > 0 && (
            <span className="text-xs text-muted-foreground">
              {allPayments.length} payment{allPayments.length !== 1 ? "s" : ""}
            </span>
          )}
          <Button size="sm" className="gap-1.5" onClick={() => navigate("/payments/new")}>
            <Plus className="h-4 w-4" />
            New payment
          </Button>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        {/* Search */}
        <div className="relative">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
          <Input
            placeholder="Search by client name…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="pl-9"
          />
        </div>

        {/* Status filter pills — only shown when loaded data contains multiple statuses */}
        {!isLoading && !isError && presentStatuses.length > 1 && (
          <div
            className="flex flex-wrap items-center gap-2"
            aria-label="Filter by payment status"
          >
            {presentStatuses.map((s) => (
              <button
                key={s}
                type="button"
                aria-pressed={statusFilter === s}
                onClick={() => setStatusFilter(statusFilter === s ? null : s)}
                className={cn(
                  "rounded-full border px-3 py-0.5 text-xs font-medium transition-colors",
                  statusFilter === s
                    ? "border-foreground bg-foreground text-background"
                    : "border-border bg-background text-muted-foreground hover:border-foreground hover:text-foreground",
                )}
              >
                {STATUS_LABELS[s]}
              </button>
            ))}
          </div>
        )}

        {isLoading && (
          <div className="space-y-0">
            {Array.from({ length: 8 }).map((_, i) => (
              <PaymentRowSkeleton key={i} />
            ))}
          </div>
        )}

        {errorMessage && (
          <p className="text-center text-sm text-destructive py-16" role="alert">
            {errorMessage}{" "}
            <button type="button" className="underline" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        )}

        {/* Rich empty state — zero payments loaded at all */}
        {!isLoading && !isError && allPayments.length === 0 && (
          <div className="flex flex-col items-center gap-3 py-16 text-center">
            <CreditCard className="h-8 w-8 text-muted-foreground/40" />
            <p className="text-sm font-medium">No payments yet</p>
            <p className="text-xs text-muted-foreground">
              Record your first payment to start tracking studio revenue.
            </p>
            <Button
              size="sm"
              onClick={() => navigate("/payments/new")}
              className="gap-1.5 mt-1"
            >
              <Plus className="h-3.5 w-3.5" />
              Record payment
            </Button>
          </div>
        )}

        {/* Table — when payments are loaded */}
        {!isLoading && !isError && allPayments.length > 0 && (
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
                header: "Session Date",
                cell: (p) =>
                  p.appointmentDate ? (
                    <span className="text-sm text-muted-foreground">
                      {formatDate(p.appointmentDate)}
                    </span>
                  ) : (
                    "—"
                  ),
              },
              {
                header: "Amount",
                cell: (p) => (
                  <span className="font-semibold">{formatCurrency(p.amount)}</span>
                ),
              },
              {
                header: "Status",
                cell: (p) => <PaymentStatusBadge status={p.status} />,
              },
              {
                header: "Method",
                cell: (p) => p.method,
              },
              {
                header: "Date Paid",
                cell: (p) => (p.paidAt ? formatDate(p.paidAt) : "—"),
              },
              {
                header: "",
                cell: (p) => (
                  <div
                    className="flex items-center justify-end"
                    onClick={(e) => e.stopPropagation()}
                  >
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-7 text-xs gap-1 text-muted-foreground hover:text-foreground"
                      onClick={() => navigate(`/payments/${p.appointmentId}`)}
                    >
                      View
                      <ChevronRight className="h-3 w-3" />
                    </Button>
                  </div>
                ),
              },
            ]}
            data={filteredPayments}
            keyExtractor={(p) => p.id}
            onRowClick={(p) => navigate(`/payments/${p.appointmentId}`)}
            emptyMessage={tableEmptyMessage}
            mobileCard={(p) => (
              <div className="space-y-1">
                <div className="flex items-center justify-between gap-2">
                  <span className="text-sm font-medium truncate">{p.clientName || "—"}</span>
                  <PaymentStatusBadge status={p.status} />
                </div>
                <div className="flex items-center justify-between gap-2 text-sm">
                  <span className="text-muted-foreground">
                    {p.appointmentDate ? formatDate(p.appointmentDate) : "—"}
                  </span>
                  <span className="font-semibold">{formatCurrency(p.amount)}</span>
                </div>
                <p className="text-xs text-muted-foreground">
                  {p.method}{p.paidAt ? ` · Paid ${formatDate(p.paidAt)}` : ""}
                </p>
              </div>
            )}
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
