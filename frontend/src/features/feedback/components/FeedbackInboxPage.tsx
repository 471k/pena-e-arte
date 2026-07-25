import { useState } from "react";
import { toast } from "sonner";
import { Loader2, MessageSquareMore } from "lucide-react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Textarea } from "@/shared/components/ui/textarea";
import { cn } from "@/shared/utils/cn";
import {
  useGetFeedbackReportsQuery,
  useUpdateFeedbackStatusMutation,
} from "../feedbackApi";
import { FEEDBACK_TYPE, FEEDBACK_STATUS } from "../feedback.types";
import type { FeedbackReportResponse, FeedbackType, FeedbackStatus } from "../feedback.types";
import { SupportTicketThread } from "./SupportTicketThread";

const TYPE_FILTERS = ["all", ...Object.values(FEEDBACK_TYPE)] as const;
const STATUS_FILTERS = ["all", ...Object.values(FEEDBACK_STATUS)] as const;

const TYPE_LABEL: Record<FeedbackType, string> = {
  BugReport:      "Bug Report",
  FeatureRequest: "Feature Request",
  General:        "General",
  SupportRequest: "Support Request",
};

const TYPE_BADGE: Record<FeedbackType, string> = {
  BugReport:      "bg-red-500/15 text-red-600",
  FeatureRequest: "bg-purple-500/15 text-purple-600",
  General:        "bg-sky-500/15 text-sky-600",
  SupportRequest: "bg-emerald-500/15 text-emerald-600",
};

const STATUS_BADGE: Record<FeedbackStatus, string> = {
  Open:      "bg-blue-500/15 text-blue-600",
  Reviewing: "bg-amber-500/15 text-amber-600",
  Resolved:  "bg-green-500/15 text-green-600",
  Dismissed: "bg-muted text-muted-foreground",
};

const STATUS_BUTTONS = Object.values(FEEDBACK_STATUS);

function fmt(date: string): string {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function FeedbackCardSkeleton() {
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

interface FeedbackCardProps {
  report: FeedbackReportResponse;
}

function FeedbackCard({ report }: FeedbackCardProps) {
  const [expanded, setExpanded]   = useState(false);
  const [issuerNote, setIssuerNote] = useState(report.issuerNote ?? "");
  const [pendingStatus, setPendingStatus] = useState<FeedbackStatus | null>(null);
  const [updateStatus, { isLoading }] = useUpdateFeedbackStatusMutation();

  async function handleStatusClick(status: FeedbackStatus) {
    setPendingStatus(status);
    try {
      await updateStatus({ id: report.id, status, issuerNote: issuerNote.trim() || null }).unwrap();
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
            <span className={cn("text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0", TYPE_BADGE[report.type])}>
              {TYPE_LABEL[report.type]}
            </span>
            <span className={cn("text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0", STATUS_BADGE[report.status])}>
              {report.status}
            </span>
            <p className="text-sm font-medium truncate">{report.title}</p>
          </div>
          <p className="text-xs text-muted-foreground">
            {report.studioName} · {report.submitterRole} · {fmt(report.createdAt)}
          </p>
        </button>

        {expanded && (
          <div className="pt-2 space-y-3 border-t">
            <SupportTicketThread report={report} canReply />

            <div className="space-y-1.5">
              <label htmlFor={`issuer-note-${report.id}`} className="text-xs font-medium text-muted-foreground">
                Issuer note
              </label>
              <Textarea
                id={`issuer-note-${report.id}`}
                rows={2}
                value={issuerNote}
                onChange={(e) => setIssuerNote(e.target.value)}
                placeholder="Internal note (optional)…"
                className="resize-none text-xs"
              />
            </div>

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

export function FeedbackInboxPage() {
  useDocumentMeta({ title: "Feedback Inbox — TattooOS", canonical: "/platform/feedback" });

  const [typeFilter, setTypeFilter]     = useState<(typeof TYPE_FILTERS)[number]>("all");
  const [statusFilter, setStatusFilter] = useState<(typeof STATUS_FILTERS)[number]>("all");

  const { data: reports, isLoading, isError, refetch } = useGetFeedbackReportsQuery({
    type:   typeFilter === "all" ? undefined : typeFilter,
    status: statusFilter === "all" ? undefined : statusFilter,
  });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <MessageSquareMore className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Feedback Inbox</span>
        {reports && (
          <span className="ml-1 text-xs px-1.5 py-0.5 rounded-full bg-muted text-muted-foreground font-medium">
            {reports.length}
          </span>
        )}
      </header>

      <main className="max-w-3xl mx-auto px-4 py-4 space-y-3">

        <div className="flex flex-col gap-2">
          <div className="flex gap-1 flex-wrap">
            {TYPE_FILTERS.map((t) => (
              <button
                key={t}
                onClick={() => setTypeFilter(t)}
                className={cn(
                  "text-xs px-2.5 py-1 rounded-full border transition-colors",
                  typeFilter === t
                    ? "bg-primary text-primary-foreground border-primary"
                    : "hover:bg-muted border-border"
                )}
              >
                {t === "all" ? "All Types" : TYPE_LABEL[t]}
              </button>
            ))}
          </div>
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
        </div>

        {isLoading && (
          <div className="space-y-3">
            {[1, 2, 3, 4, 5].map((i) => <FeedbackCardSkeleton key={i} />)}
          </div>
        )}

        {isError && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <p className="text-sm text-destructive">Failed to load feedback.</p>
            <Button size="sm" variant="outline" onClick={() => refetch()}>Retry</Button>
          </div>
        )}

        {!isLoading && !isError && reports && reports.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <MessageSquareMore className="h-10 w-10 text-muted-foreground/30" />
            <p className="text-sm text-muted-foreground">No feedback yet.</p>
          </div>
        )}

        {!isLoading && !isError && reports?.map((report) => (
          <FeedbackCard key={report.id} report={report} />
        ))}

      </main>
    </div>
  );
}
