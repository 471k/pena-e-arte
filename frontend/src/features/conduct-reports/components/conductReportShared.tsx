import type { ReactNode } from "react";
import { ShieldAlert } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import type { ConductReportResponse, ReportStatus } from "../conductReports.types";

// Shared between ConductReportsPage.tsx (owner/artist) and ConductReportInboxPage.tsx (issuer)
// — was copy-pasted verbatim across both before this module existed.

export const STATUS_BADGE: Record<ReportStatus, string> = {
  Open:      "bg-blue-500/15 text-blue-600",
  Reviewing: "bg-amber-500/15 text-amber-600",
  Resolved:  "bg-green-500/15 text-green-600",
  Dismissed: "bg-muted text-muted-foreground",
};

export function fmt(date: string): string {
  return new Date(date).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

export function ReportCardSkeleton() {
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

interface ReportsListProps {
  reports:        ConductReportResponse[] | undefined;
  isLoading:      boolean;
  isError:        boolean;
  onRetry:        () => void;
  emptyMessage:   string;
  renderCard:     (report: ConductReportResponse) => ReactNode;
  /** Number of skeleton cards shown while loading. Defaults to 3 (owner/artist views); the
   * issuer inbox passes 5 to match its historically busier list. */
  skeletonCount?: number;
}

export function ReportsList({
  reports, isLoading, isError, onRetry, emptyMessage, renderCard, skeletonCount = 3,
}: ReportsListProps) {
  if (isLoading) {
    return (
      <div className="space-y-3">
        {Array.from({ length: skeletonCount }, (_, i) => <ReportCardSkeleton key={i} />)}
      </div>
    );
  }
  if (isError) {
    return (
      <div className="flex flex-col items-center justify-center py-24 gap-3">
        <p className="text-sm text-destructive">Failed to load reports.</p>
        <Button size="sm" variant="outline" onClick={onRetry}>Retry</Button>
      </div>
    );
  }
  if (!reports || reports.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-24 gap-3">
        <ShieldAlert className="h-10 w-10 text-muted-foreground/30" />
        <p className="text-sm text-muted-foreground">{emptyMessage}</p>
      </div>
    );
  }
  return <div className="space-y-3">{reports.map(renderCard)}</div>;
}
