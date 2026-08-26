import { useState } from "react";
import { toast } from "sonner";
import { Loader2, ShieldAlert } from "lucide-react";
import { useAppSelector } from "@/app/hooks";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { cn } from "@/shared/utils/cn";
import { Role } from "@/shared/types/roles";
import {
  useGetMyStudioConductReportsQuery,
  useGetMyConductReportsAsArtistQuery,
  useUpdateConductReportStatusMutation,
} from "../conductReportsApi";
import { REPORT_STATUS, REPORT_CATEGORY_LABEL } from "../conductReports.types";
import type { ConductReportResponse, ReportStatus } from "../conductReports.types";
import { STATUS_BADGE, fmt } from "./conductReportFormat";
import { ReportsList } from "./conductReportShared";

const STATUS_BUTTONS = Object.values(REPORT_STATUS);

function OwnerReportCard({ report }: { report: ConductReportResponse }) {
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
            {report.artistName ? `About ${report.artistName}` : "About the studio"} · {fmt(report.createdAt)}
            {report.reporterName && <> · Reported by {report.reporterName}</>}
          </p>
        </button>

        {expanded && (
          <div className="pt-2 space-y-3 border-t">
            <p className="text-sm text-muted-foreground whitespace-pre-wrap">{report.reason}</p>

            {report.isHighSeverity ? (
              <p className="text-xs text-muted-foreground italic">
                Escalated to platform review — only Pena e Artë staff can close this report.
              </p>
            ) : (
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
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function ArtistReportCard({ report }: { report: ConductReportResponse }) {
  const [expanded, setExpanded] = useState(false);

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
          <p className="text-xs text-muted-foreground">{fmt(report.createdAt)} · Reported by Anonymous</p>
        </button>

        {expanded && (
          <div className="pt-2 space-y-2 border-t">
            <p className="text-sm text-muted-foreground whitespace-pre-wrap">{report.reason}</p>
            <p className="text-xs text-muted-foreground/70 italic">
              The reporting client's identity is not shared with you.
            </p>
          </div>
        )}
      </CardContent>
    </Card>
  );
}

function OwnerConductReportsView() {
  const { data: reports, isLoading, isError, refetch } = useGetMyStudioConductReportsQuery();
  return (
    <ReportsList
      reports={reports}
      isLoading={isLoading}
      isError={isError}
      onRetry={refetch}
      emptyMessage="No conduct reports for your studio."
      renderCard={(report) => <OwnerReportCard key={report.id} report={report} />}
    />
  );
}

function ArtistConductReportsView() {
  const { data: reports, isLoading, isError, refetch } = useGetMyConductReportsAsArtistQuery();
  return (
    <ReportsList
      reports={reports}
      isLoading={isLoading}
      isError={isError}
      onRetry={refetch}
      emptyMessage="No reports have been filed about you."
      renderCard={(report) => <ArtistReportCard key={report.id} report={report} />}
    />
  );
}

export function ConductReportsPage() {
  const role = useAppSelector((s) => s.auth.role);
  const isArtist = role === Role.Artist;

  useDocumentMeta({
    title: isArtist ? "Reports About Me — TattooOS" : "Conduct Reports — TattooOS",
    canonical: "/conduct-reports",
  });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <ShieldAlert className="h-5 w-5" />
        <span className="font-semibold tracking-tight">
          {isArtist ? "Reports About Me" : "Conduct Reports"}
        </span>
      </header>

      <main className="max-w-3xl mx-auto px-4 py-4 space-y-3">
        {isArtist ? <ArtistConductReportsView /> : <OwnerConductReportsView />}
      </main>
    </div>
  );
}
