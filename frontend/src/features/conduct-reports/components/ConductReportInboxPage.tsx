import { useState } from "react";
import { toast } from "sonner";
import { Loader2, ShieldAlert } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import {
  useGetPlatformConductReportsQuery,
  useUpdateConductReportStatusMutation,
} from "../conductReportsApi";
import { REPORT_STATUS, REPORT_CATEGORY, REPORT_CATEGORY_LABEL } from "../conductReports.types";
import type { ConductReportResponse, ReportStatus } from "../conductReports.types";

const STATUS_FILTERS = ["all", ...Object.values(REPORT_STATUS)] as const;
const CATEGORY_FILTERS = ["all", ...Object.values(REPORT_CATEGORY)] as const;

const STATUS_BADGE: Record<ReportStatus, string> = {
  Open:      "bg-blue-500/15 text-blue-600",
  Reviewing: "bg-amber-500/15 text-amber-600",
  Resolved:  "bg-green-500/15 text-green-600",
  Dismissed: "bg-muted text-muted-foreground",
};

const STATUS_BUTTONS = Object.values(REPORT_STATUS);

function fmt(date: string): string {
  return new Date(date).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

function ReportCardSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-center gap-2">
          <Skeleton className="h-5 w-20 rounded-full" />
          <Skeleton className="h-4 w-48" />
        </div>
        <Skeleton className="h-3 w-64" />
      </CardContent>
    </Card>
  );
}

function ReportCard({ report }: { report: ConductReportResponse }) {
  const [expanded, setExpanded] = useState(false);
  const [pendingStatus, setPendingStatus] = useState<ReportStatus | null>(null);
  const [updateStatus, { isLoading }] = useUpdateConductReportStatusMutation();

  async function handleStatusClick(status: ReportStatus) {
    setPendingStatus(status);
    try {
      await updateStatus({ id: report.id, status }).unwrap();
      toast.success("Updated.");
    } catch {
      toast.error("Failed to update.");
    } finally {
      setPendingStatus(null);
    }
  }

  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <button
          type="button"
          onClick={() => setExpanded((e) => !e)}
          className="w-full text-left space-y-1.5"
          aria-expanded={expanded}
        >
          <div className="flex items-center gap-2 flex-wrap">
            {report.isHighSeverity && (
              <span className="text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 bg-red-500/15 text-red-600 flex items-center gap-1">
                <ShieldAlert className="h-3 w-3" aria-hidden="true" /> High severity
              </span>
            )}
            <span className={cn("text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0", STATUS_BADGE[report.status])}>
              {report.status}
            </span>
            <p className="text-sm font-medium truncate">{REPORT_CATEGORY_LABEL[report.category]}</p>
          </div>
          <p className="text-xs text-muted-foreground">
            {report.studioName}{report.artistName ? ` · About ${report.artistName}` : " · About the studio"} · {fmt(report.createdAt)}
            {report.reporterName && <> · Reported by {report.reporterName}</>}
          </p>
        </button>

        {expanded && (
          <div className="pt-2 space-y-3 border-t">
            <p className="text-sm text-muted-foreground whitespace-pre-wrap">{report.reason}</p>

            {/* Status controls are always enabled for the issuer, regardless of severity. */}
            <div className="flex gap-1.5 flex-wrap">
              {STATUS_BUTTONS.map((status) => (
                <Button
                  key={status}
                  size="sm"
                  variant={report.status === status ? "default" : "outline"}
                  className="h-7 px-2.5 text-xs"
                  disabled={isLoading}
                  onClick={() => handleStatusClick(status)}
                >
                  {isLoading && pendingStatus === status
                    ? <Loader2 className="h-3 w-3 animate-spin" />
                    : status}
                </Button>
              ))}
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

export function ConductReportInboxPage() {
  useDocumentMeta({ title: "Conduct Reports — TattooOS", canonical: "/platform/conduct-reports" });

  const [statusFilter, setStatusFilter]     = useState<(typeof STATUS_FILTERS)[number]>("all");
  const [categoryFilter, setCategoryFilter] = useState<(typeof CATEGORY_FILTERS)[number]>("all");

  const { data: reports, isLoading, isError, refetch } = useGetPlatformConductReportsQuery({
    status:   statusFilter === "all" ? undefined : statusFilter,
    category: categoryFilter === "all" ? undefined : categoryFilter,
  });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <ShieldAlert className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Conduct Reports</span>
        {reports && (
          <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground font-medium">
            {reports.length}
          </span>
        )}
      </header>

      <main className="max-w-3xl mx-auto px-4 py-4 space-y-3">
        <div className="flex flex-col gap-2">
          <div className="flex gap-1 flex-wrap">
            {STATUS_FILTERS.map((s) => (
              <button
                key={s}
                onClick={() => setStatusFilter(s)}
                className={cn(
                  "text-xs px-2.5 py-1 rounded-full border transition-colors",
                  statusFilter === s
                    ? "bg-primary text-primary-foreground border-primary"
                    : "hover:bg-muted border-border"
                )}
              >
                {s === "all" ? "All Statuses" : s}
              </button>
            ))}
          </div>
          <div className="flex gap-1 flex-wrap">
            {CATEGORY_FILTERS.map((c) => (
              <button
                key={c}
                onClick={() => setCategoryFilter(c)}
                className={cn(
                  "text-xs px-2.5 py-1 rounded-full border transition-colors",
                  categoryFilter === c
                    ? "bg-primary text-primary-foreground border-primary"
                    : "hover:bg-muted border-border"
                )}
              >
                {c === "all" ? "All Categories" : REPORT_CATEGORY_LABEL[c]}
              </button>
            ))}
          </div>
        </div>

        {isLoading && (
          <div className="space-y-3">
            {[1, 2, 3, 4, 5].map((i) => <ReportCardSkeleton key={i} />)}
          </div>
        )}

        {isError && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <p className="text-sm text-destructive">Failed to load reports.</p>
            <Button size="sm" variant="outline" onClick={() => refetch()}>Retry</Button>
          </div>
        )}

        {!isLoading && !isError && reports && reports.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <ShieldAlert className="h-10 w-10 text-muted-foreground/30" />
            <p className="text-sm text-muted-foreground">No conduct reports.</p>
          </div>
        )}

        {!isLoading && !isError && reports?.map((report) => (
          <ReportCard key={report.id} report={report} />
        ))}
      </main>
    </div>
  );
}
