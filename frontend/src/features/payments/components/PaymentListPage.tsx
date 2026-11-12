import { useState } from "react";
import { useSuspensionAwareError } from "@/shared/hooks/useSuspensionAwareError";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useNavigate } from "react-router-dom";
import { ChevronRight, CreditCard, Download, Loader2, Plus, Search } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Input } from "@/shared/components/ui/input";
import { DataTable } from "@/shared/components/DataTable";
import { Badge } from "@/shared/components/ui/badge";
import { cn } from "@/shared/utils/cn";
import { useGetPaymentsQuery } from "../paymentsApi";
import type { PaymentResponse } from "../payment.types";
import { PaymentStatus } from "../payment.types";
import { PAYMENT_STATUS_LABELS } from "../paymentStatusLabels";
import { describeDeposit, type DepositOutcomeKind } from "../depositOutcome";
import { Money } from "@/shared/components/Money";
import { Label } from "@/shared/components/ui/label";
import { useAppSelector } from "@/app/hooks";
import { useDebouncedValue } from "@/shared/hooks/useDebouncedValue";
import { downloadAuthenticatedFile } from "@/shared/utils/downloadAuthenticatedFile";
import type { GetPaymentsParams } from "../payment.types";

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;
const ALL_STATUSES = Object.values(PaymentStatus);
// The revenue export only ever holds money received.
const EXPORTABLE_STATUSES: ReadonlySet<PaymentStatus> = new Set([PaymentStatus.Paid, PaymentStatus.Refunded]);

type Filters = Pick<GetPaymentsParams, "search" | "status" | "from" | "to">;

function exportPath(filters: Filters): string {
  const params = new URLSearchParams();
  if (filters.search) params.set("search", filters.search);
  if (filters.status) params.set("status", filters.status);
  if (filters.from)   params.set("from", filters.from);
  if (filters.to)     params.set("to", filters.to);
  const qs = params.toString();
  return `reports/revenue/export.csv${qs ? `?${qs}` : ""}`;
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

// Coloured by what happened (G-13), not the raw status: "Partly kept" is money received.
const OUTCOME_LIST_STYLES: Record<DepositOutcomeKind, string> = {
  pending:        PAYMENT_STATUS_STYLES[PaymentStatus.Pending],
  cashPending:    PAYMENT_STATUS_STYLES[PaymentStatus.CashPending],
  onHold:         PAYMENT_STATUS_STYLES[PaymentStatus.Captured],
  paid:           PAYMENT_STATUS_STYLES[PaymentStatus.Paid],
  kept:           PAYMENT_STATUS_STYLES[PaymentStatus.Paid],
  partlyKept:     PAYMENT_STATUS_STYLES[PaymentStatus.Paid],
  partlyRefunded: PAYMENT_STATUS_STYLES[PaymentStatus.Refunded],
  refunded:       PAYMENT_STATUS_STYLES[PaymentStatus.Refunded],
  released:       PAYMENT_STATUS_STYLES[PaymentStatus.Refunded],
  failed:         PAYMENT_STATUS_STYLES[PaymentStatus.Failed],
};

function PaymentStatusBadge({ payment }: { payment: PaymentResponse }) {
  const outcome = describeDeposit(payment);
  return (
    <Badge variant="outline" className={cn(OUTCOME_LIST_STYLES[outcome.kind])} title={outcome.ownerDetail ?? undefined}>
      {outcome.ownerLabel}
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
  const { token, tenantId } = useAppSelector((s) => s.auth);

  // Every filter is applied by the server (E05b-1, O05): filtering the loaded page in the browser only ever
  // searched the 20 oldest payments.
  const [searchInput,  setSearchInput]  = useState("");
  const [statusFilter, setStatusFilter] = useState<PaymentStatus | null>(null);
  const [fromDate,     setFromDate]     = useState("");
  const [toDate,       setToDate]       = useState("");
  const search = useDebouncedValue(searchInput.trim(), SEARCH_DEBOUNCE_MS);
  const datesInOrder = !fromDate || !toDate || fromDate <= toDate;

  const filters: Filters = {
    search: search || undefined,
    status: statusFilter ?? undefined,
    from:   datesInOrder && fromDate ? fromDate : undefined,
    to:     datesInOrder && toDate ? toDate : undefined,
  };
  const filtersActive = Boolean(filters.search || filters.status || filters.from || filters.to);

  // "Load more" pages belong to the filters they were loaded under. Changing a filter starts again from the
  // first page: derived from the filter key on render, never reset by an effect.
  const filterKey = JSON.stringify(filters);
  const [paging, setPaging] = useState<{ key: string; cursor?: string; previous: PaymentResponse[] }>({
    key: filterKey, previous: [],
  });
  const page = paging.key === filterKey ? paging : { key: filterKey, cursor: undefined, previous: [] };

  // currentData, not data: data keeps the previous filter's rows while the new ones load.
  const { currentData, isFetching, isError, refetch } = useGetPaymentsQuery({
    ...filters,
    lastSeenId: page.cursor,
    pageSize:   PAGE_SIZE,
  });
  const errorMessage = useSuspensionAwareError(isError, "Failed to load payments. Please try again.");

  const allPayments = [...page.previous, ...(currentData ?? [])];
  const firstPageLoading = page.cursor === undefined && currentData === undefined && !isError;
  const hasMore = (currentData?.length ?? 0) === PAGE_SIZE;
  const settled = !firstPageLoading && !isError;

  function handleLoadMore() {
    const last = allPayments[allPayments.length - 1];
    if (!last) return;
    setPaging({ key: filterKey, cursor: last.id, previous: allPayments });
  }

  function clearFilters() {
    setSearchInput("");
    setStatusFilter(null);
    setFromDate("");
    setToDate("");
  }

  const canExport = !filters.status || EXPORTABLE_STATUSES.has(filters.status);
  const [exporting, setExporting] = useState(false);
  async function handleExport() {
    setExporting(true);
    try {
      await downloadAuthenticatedFile(exportPath(filters), "payments.csv", token, tenantId);
    } catch {
      toast.error("Couldn't export payments. Please try again.");
    } finally {
      setExporting(false);
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Payments</span>
        </div>
        <div className="flex items-center gap-3">
          {/* How many are loaded, not a total: more may be one "Load more" away. */}
          {allPayments.length > 0 && (
            <span className="text-xs text-muted-foreground" data-testid="payments-shown">
              {allPayments.length} shown
            </span>
          )}
          <Button size="sm" className="gap-1.5" onClick={() => navigate("/payments/new")}>
            <Plus className="h-4 w-4" />
            New payment
          </Button>
        </div>
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        {/* Filters (E05b-1): newest first; each one narrows the server's query, so a search finds any payment,
            not just the ones already loaded. */}
        <div className="space-y-3" data-testid="payment-filters">
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground pointer-events-none" />
            <Input
              type="search"
              aria-label="Search payments by client or artist name"
              placeholder="Search by client or artist name…"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              maxLength={100}
              className="pl-9"
            />
          </div>

          <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by payment status">
            {ALL_STATUSES.map((s) => (
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
                {PAYMENT_STATUS_LABELS[s]}
              </button>
            ))}
          </div>

          <div className="grid grid-cols-2 gap-3 sm:flex sm:items-end">
            <div className="space-y-1">
              <Label htmlFor="payments-from" className="text-xs">Paid from</Label>
              <Input id="payments-from" type="date" value={fromDate} max={toDate || undefined}
                     onChange={(e) => setFromDate(e.target.value)} className="h-9" />
            </div>
            <div className="space-y-1">
              <Label htmlFor="payments-to" className="text-xs">Paid to</Label>
              <Input id="payments-to" type="date" value={toDate} min={fromDate || undefined}
                     onChange={(e) => setToDate(e.target.value)} className="h-9" />
            </div>
            <div className="col-span-2 flex flex-wrap items-center gap-2 sm:ml-auto">
              {filtersActive && (
                <Button variant="ghost" size="sm" onClick={clearFilters}>Clear filters</Button>
              )}
              <Button variant="outline" size="sm" className="gap-1.5" onClick={handleExport}
                      disabled={!canExport || exporting}>
                <Download className="h-3.5 w-3.5" />
                Export CSV
              </Button>
            </div>
          </div>
          {!datesInOrder && (
            <p className="text-xs text-destructive-text" role="alert">&ldquo;Paid to&rdquo; is before &ldquo;Paid from&rdquo;, so the dates aren&apos;t applied.</p>
          )}
          <p className="text-xs text-muted-foreground">
            Dates are in your studio&apos;s timezone and match when a payment was paid, so a date range leaves out
            payments that haven&apos;t been paid yet. Export CSV saves the paid and refunded payments matching these filters.
            {!canExport && " The export holds only paid and refunded payments, so it's off for this status."}
          </p>
        </div>

        {firstPageLoading && (
          <div className="space-y-0" aria-label="Loading payments">
            {Array.from({ length: 8 }).map((_, i) => (
              <PaymentRowSkeleton key={i} />
            ))}
          </div>
        )}

        {errorMessage && (
          <p className="text-center text-sm text-destructive-text py-16" role="alert">
            {errorMessage}{" "}
            <button type="button" className="underline" onClick={() => refetch()}>
              Try again
            </button>
          </p>
        )}

        {/* Four different answers: loading (above), error (above), no payments at all, and nothing matching
            the filters. "No payments yet" under an active filter would tell the owner something false. */}
        {settled && allPayments.length === 0 && filtersActive && (
          <div className="flex flex-col items-center gap-3 py-16 text-center" data-testid="payments-no-matches">
            <Search className="h-8 w-8 text-muted-foreground/40" />
            <p className="text-sm font-medium">No payments match these filters</p>
            <Button variant="outline" size="sm" onClick={clearFilters}>Clear filters</Button>
          </div>
        )}

        {/* Rich empty state — the studio has no payments at all */}
        {settled && allPayments.length === 0 && !filtersActive && (
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
        {!firstPageLoading && allPayments.length > 0 && (
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
                  <Money amount={p.amount} currency={p.currency} frozen={p} className="font-semibold" />
                ),
              },
              {
                header: "Status",
                cell: (p) => <PaymentStatusBadge payment={p} />,
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
            data={allPayments}
            keyExtractor={(p) => p.id}
            onRowClick={(p) => navigate(`/payments/${p.appointmentId}`)}
            emptyMessage="No payments match these filters."
            mobileCard={(p) => (
              <div className="space-y-1">
                <div className="flex items-center justify-between gap-2">
                  <span className="text-sm font-medium truncate">{p.clientName || "—"}</span>
                  <PaymentStatusBadge payment={p} />
                </div>
                <div className="flex items-center justify-between gap-2 text-sm">
                  <span className="text-muted-foreground">
                    {p.appointmentDate ? formatDate(p.appointmentDate) : "—"}
                  </span>
                  <Money amount={p.amount} currency={p.currency} frozen={p} className="font-semibold" />
                </div>
                <p className="text-xs text-muted-foreground">
                  {p.method}{p.paidAt ? ` · Paid ${formatDate(p.paidAt)}` : ""}
                </p>
              </div>
            )}
          />
        )}

        {settled && hasMore && (
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
