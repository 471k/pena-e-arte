import type { ReactNode } from "react";
import { ShieldAlert } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import type { ConductReportResponse } from "../conductReports.types";

// Shared between ConductReportsPage.tsx (owner/artist) and ConductReportInboxPage.tsx (admin)
// — was copy-pasted verbatim across both before this module existed. Non-component exports
// (STATUS_BADGE, fmt) live in ./conductReportFormat.ts instead of here, since mixing them into
// this file trips react-refresh/only-export-components.

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
   * admin inbox passes 5 to match its historically busier list. */
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
        <p className="text-sm text-destructive-text">Failed to load reports.</p>
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
