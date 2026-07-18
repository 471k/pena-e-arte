import { useEffect, useState } from "react";
import { toast } from "sonner";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { BarChart3, Check, Download, ExternalLink, Loader2, PlayCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  useGetIndustryReportsQuery,
  useTriggerIndustryReportMutation,
  useGetPlanUsageReportQuery,
} from "@/features/platform/platformApi";
import type { IndustryReportSummary, StudioPlanUsageRow } from "@/features/platform/platform.types";
import { cn } from "@/shared/utils/cn";

function formatPeriod(period: string): string {
  const parts = period.split("-");
  if (parts.length === 2) {
    const [year, month] = parts;
    const date = new Date(parseInt(year, 10), parseInt(month, 10) - 1);
    return date.toLocaleDateString("en-GB", { month: "long", year: "numeric" });
  }
  return period;
}

function formatDate(date: string | Date): string {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

/** Returns "1 July 2026" (or whatever the next 1st is) */
function nextReportDate(): string {
  const now  = new Date();
  const next = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1));
  return next.toLocaleDateString("en-GB", { day: "numeric", month: "long", year: "numeric" });
}

function ReportRowSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div className="space-y-1.5 flex-1">
          <Skeleton className="h-4 w-28" />
          <Skeleton className="h-3 w-40" />
        </div>
        <div className="flex items-center gap-2">
          <Skeleton className="h-7 w-24" />
          <Skeleton className="h-7 w-20" />
        </div>
      </CardContent>
    </Card>
  );
}

interface ReportRowProps {
  report: IndustryReportSummary;
}

function ReportRow({ report }: ReportRowProps) {
  const label = formatPeriod(report.period);

  return (
    <Card>
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div className="space-y-0.5 min-w-0">
          <span className="font-medium text-sm">{label}</span>
          <p className="text-xs text-muted-foreground">
            Generated {formatDate(report.generatedAt)}
            {" · "}
            <span className="font-mono text-[10px] uppercase tracking-wide text-muted-foreground/70">
              JSON
            </span>
          </p>
        </div>
        <div className="flex items-center gap-2 shrink-0">
          <Button
            size="sm"
            variant="outline"
            className="h-7 text-xs gap-1.5"
            asChild
          >
            <a
              href={report.downloadUrl}
              download={`industry-report-${report.period}.json`}
              aria-label={`Download ${label} industry report`}
            >
              <Download className="h-3.5 w-3.5" />
              Download
            </a>
          </Button>
          <Button
            size="sm"
            variant="ghost"
            className="h-7 text-xs gap-1.5 text-primary hover:text-primary"
            asChild
          >
            <a
              href={report.downloadUrl}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={`Open ${label} industry report in new tab`}
            >
              Open
              <ExternalLink className="h-3.5 w-3.5" />
            </a>
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}

function GenerateTriggerButton() {
  const [queued,     setQueued]     = useState(false);
  const [cooldown,   setCooldown]   = useState(0);
  const [error,      setError]      = useState(false);
  const [trigger, { isLoading }] = useTriggerIndustryReportMutation();

  // Count down cooldown seconds — approved useEffect: timer-based browser side-effect.
  useEffect(() => {
    if (cooldown <= 0) return;
    const id = setInterval(() => setCooldown((c) => Math.max(0, c - 1)), 1000);
    return () => clearInterval(id);
  }, [cooldown]);

  async function handleTrigger() {
    setError(false);
    try {
      await trigger().unwrap();
      setQueued(true);
      setCooldown(60);
      toast.success("Report generation queued");
      setTimeout(() => setQueued(false), 4000);
    } catch {
      setError(true);
      toast.error("Failed to queue report generation");
      setTimeout(() => setError(false), 4000);
    }
  }

  if (queued) {
    return (
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Check className="h-3.5 w-3.5 text-green-500" />
        Queued — report will appear shortly
      </div>
    );
  }

  if (error) {
    return (
      <div className="flex items-center gap-1.5 text-xs text-destructive">
        Failed to queue — try again
      </div>
    );
  }

  const onCooldown = cooldown > 0;

  return (
    <Button
      size="sm"
      variant="outline"
      className="h-7 text-xs gap-1.5"
      disabled={isLoading || onCooldown}
      onClick={handleTrigger}
      aria-label="Trigger industry report generation now"
    >
      {isLoading
        ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
        : <PlayCircle className="h-3.5 w-3.5" />}
      {onCooldown ? `Wait ${cooldown}s` : "Generate Report"}
    </Button>
  );
}

function DimensionCell({ current, max }: { current: number; max: number | null }) {
  if (max === null) {
    return <span className="text-muted-foreground/60">Unlimited</span>;
  }
  const pct = max > 0 ? current / max : 0;
  return (
    <span className={cn("tabular-nums", pct >= 0.8 && "text-amber-600 dark:text-amber-400 font-medium")}>
      {current} / {max}
    </span>
  );
}

function PlanUsageReportSection() {
  const { data, isLoading, isError } = useGetPlanUsageReportQuery();

  return (
    <div className="space-y-3">
      <div>
        <p className="text-sm font-medium">Plan usage report</p>
        <p className="text-xs text-muted-foreground">
          Real per-studio usage against each plan's caps — sorted with studios closest to
          any of their limits first. Validates the seeded numbers; does not change them.
        </p>
      </div>

      {isLoading && (
        <div className="space-y-2">
          {[1, 2, 3].map((i) => <Skeleton key={i} className="h-8 w-full" />)}
        </div>
      )}

      {isError && (
        <p className="text-center text-sm text-destructive py-8">Failed to load plan usage report.</p>
      )}

      {!isLoading && !isError && data && data.studios.length === 0 && (
        <p className="text-center text-xs text-muted-foreground py-8">
          No studios with an active plan yet.
        </p>
      )}

      {!isLoading && !isError && data && data.studios.length > 0 && (
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-xs">
            <thead>
              <tr className="border-b bg-muted/40 text-left text-muted-foreground">
                <th className="px-3 py-2 font-medium">Studio</th>
                <th className="px-3 py-2 font-medium">Plan</th>
                <th className="px-3 py-2 font-medium">Artists</th>
                <th className="px-3 py-2 font-medium">Appts / mo</th>
                <th className="px-3 py-2 font-medium">Notifs / mo</th>
                <th className="px-3 py-2 font-medium">Storage</th>
              </tr>
            </thead>
            <tbody>
              {data.studios.map((row: StudioPlanUsageRow) => (
                <tr key={row.studioId} className="border-b last:border-b-0">
                  <td className="px-3 py-2 font-medium truncate max-w-[160px]">{row.studioName}</td>
                  <td className="px-3 py-2 text-muted-foreground">{row.planName}</td>
                  <td className="px-3 py-2"><DimensionCell current={row.artistCount} max={row.maxArtists} /></td>
                  <td className="px-3 py-2"><DimensionCell current={row.appointmentsThisMonth} max={row.maxAppointmentsPerMonth} /></td>
                  <td className="px-3 py-2"><DimensionCell current={row.notificationsThisMonth} max={row.maxNotificationsPerMonth} /></td>
                  <td className="px-3 py-2"><DimensionCell current={row.storageGbUsed} max={row.maxStorageGb} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

export function IndustryReportsPage() {
  useDocumentMeta({ title: "Industry Reports — Platform Admin", canonical: "/platform/reports" });

  const { data: reports, isLoading, isError } = useGetIndustryReportsQuery();

  return (
    <div className="min-h-screen bg-background">

      {/* ── Sticky header ───────────────────────────────────────── */}
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-[var(--issuer-nav-height)] z-10">
        <BarChart3 className="h-5 w-5" aria-hidden="true" />
        <span className="font-semibold tracking-tight">Industry Reports</span>
        {reports && (
          <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full
                           bg-muted text-muted-foreground font-medium">
            {reports.length}
          </span>
        )}
        <div className="ml-auto">
          <GenerateTriggerButton />
        </div>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-4 space-y-3">

        {/* ── Helper text ─────────────────────────────────────────── */}
        <p className="text-xs text-muted-foreground">
          Anonymized platform-wide analytics — booking trends, trial conversion,
          and MRR growth. No PII, no studio-level identifiers. Reports generate
          automatically on the 1st of each month.
        </p>

        {/* ── Loading ─────────────────────────────────────────────── */}
        {isLoading && (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => <ReportRowSkeleton key={i} />)}
          </div>
        )}

        {/* ── Error ───────────────────────────────────────────────── */}
        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load reports.
          </p>
        )}

        {/* ── Empty state ─────────────────────────────────────────── */}
        {!isLoading && !isError && reports?.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-3 text-center">
            <BarChart3
              className="h-12 w-12 text-muted-foreground/25"
              aria-hidden="true"
            />
            <p className="font-medium text-sm">No reports yet</p>
            <p className="text-xs text-muted-foreground max-w-xs">
              Industry reports are generated automatically on the 1st of each
              month. The first report will appear here on{" "}
              <strong>{nextReportDate()}</strong>, or you can trigger one now
              using the Generate Report button above.
            </p>
          </div>
        )}

        {/* ── Report list ─────────────────────────────────────────── */}
        {!isLoading && !isError && reports?.map((report) => (
          <ReportRow key={report.period} report={report} />
        ))}

        {/* ── Plan usage report ───────────────────────────────────── */}
        <div className="border-t pt-4 mt-2">
          <PlanUsageReportSection />
        </div>

      </main>
    </div>
  );
}
