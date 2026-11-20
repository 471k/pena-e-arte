import { useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
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

const STATUS_FILTERS = ["all", ...Object.values(FEEDBACK_STATUS)] as const;

const TYPE_LABEL: Record<FeedbackType, string> = {
  BugReport:      "Bug Report",
  FeatureRequest: "Feature Request",
  General:        "General",
  SupportRequest: "Support Request",
};

const TYPE_BADGE: Record<FeedbackType, string> = {
  BugReport:      "bg-red-500/15 text-red-700 dark:text-red-400",
  FeatureRequest: "bg-purple-500/15 text-purple-700 dark:text-purple-300",
  General:        "bg-sky-500/15 text-sky-700 dark:text-sky-400",
  SupportRequest: "bg-emerald-500/15 text-emerald-800 dark:text-emerald-300",
};

const STATUS_BADGE: Record<FeedbackStatus, string> = {
  Open:      "bg-blue-500/15 text-info-text",
  Reviewing: "bg-amber-500/10 text-warning-text",
  Resolved:  "bg-green-500/10 text-success-text",
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
  const [adminNote, setAdminNote] = useState(report.adminNote ?? "");
  const [pendingStatus, setPendingStatus] = useState<FeedbackStatus | null>(null);
  const [updateStatus, { isLoading }] = useUpdateFeedbackStatusMutation();

  async function handleStatusClick(status: FeedbackStatus) {
    setPendingStatus(status);
    try {
      await updateStatus({ id: report.id, status, adminNote: adminNote.trim() || null }).unwrap();
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
              <label htmlFor={`admin-note-${report.id}`} className="text-xs font-medium text-muted-foreground">
                Admin note
              </label>
              <Textarea
                id={`admin-note-${report.id}`}
                rows={2}
                value={adminNote}
                onChange={(e) => setAdminNote(e.target.value)}
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

// D06: support work and product-feedback reading are two jobs, so they are two views. Support triage holds what
// needs an answer (support requests and bug reports), defaulting to what still needs action, oldest first like any
// support queue. Product feedback holds feature requests and general notes, newest first, to read rather than work.
// Only enum values go in the URL (?view, ?type, ?status) — never a name, note or search term.
type InboxView = "support" | "feedback";
const VIEW_TYPES: Record<InboxView, FeedbackType[]> = {
  support:  [FEEDBACK_TYPE.SupportRequest, FEEDBACK_TYPE.BugReport],
  feedback: [FEEDBACK_TYPE.FeatureRequest, FEEDBACK_TYPE.General],
};
const VIEW_LABEL: Record<InboxView, string> = { support: "Support triage", feedback: "Product feedback" };
/** Not a server status: Open or Reviewing, the triage default. */
const NEEDS_ACTION = "needs-action";
type StatusChoice = (typeof STATUS_FILTERS)[number] | typeof NEEDS_ACTION;

function parseView(value: string | null): InboxView {
  return value === "feedback" ? "feedback" : "support";
}

export function FeedbackInboxPage() {
  useDocumentMeta({ title: "Feedback Inbox — TattooOS", canonical: "/platform/feedback" });

  const [params, setParams] = useSearchParams();
  const view = parseView(params.get("view"));
  const viewTypes = VIEW_TYPES[view];
  const rawType = params.get("type");
  const typeFilter: FeedbackType | "all" =
    rawType && (viewTypes as string[]).includes(rawType) ? (rawType as FeedbackType) : "all";
  const rawStatus = params.get("status");
  const statusChoices: StatusChoice[] = view === "support" ? [NEEDS_ACTION, ...STATUS_FILTERS] : [...STATUS_FILTERS];
  const statusFilter: StatusChoice = rawStatus && (statusChoices as string[]).includes(rawStatus)
    ? (rawStatus as StatusChoice)
    : view === "support" ? NEEDS_ACTION : "all";

  function update(next: Partial<Record<"view" | "type" | "status", string | null>>) {
    setParams((current) => {
      const merged = new URLSearchParams(current);
      for (const [key, value] of Object.entries(next)) {
        if (value === null) merged.delete(key);
        else merged.set(key, value);
      }
      return merged;
    }, { replace: true });
  }

  const serverStatus = statusFilter === "all" || statusFilter === NEEDS_ACTION ? undefined : statusFilter;
  const { data, isLoading, isError, refetch } = useGetFeedbackReportsQuery({
    type:   typeFilter === "all" ? undefined : typeFilter,
    status: serverStatus,
  });

  const reports = useMemo(() => {
    if (!data) return undefined;
    const inView = data.filter((r) =>
      viewTypes.includes(r.type)
      && (statusFilter !== NEEDS_ACTION || r.status === FEEDBACK_STATUS.Open || r.status === FEEDBACK_STATUS.Reviewing));
    const byCreated = (a: FeedbackReportResponse, b: FeedbackReportResponse) =>
      new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime();
    return [...inView].sort(view === "support" ? byCreated : (a, b) => byCreated(b, a));
  }, [data, viewTypes, statusFilter, view]);

  const emptyText = view === "support"
    ? statusFilter === NEEDS_ACTION ? "Nothing needs action: no open support requests or bug reports." : "No support requests or bug reports here."
    : "No product feedback here.";

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

        <div role="tablist" aria-label="Inbox view" className="flex gap-1 border-b">
          {(["support", "feedback"] as const).map((v) => (
            <button
              key={v}
              role="tab"
              type="button"
              aria-selected={view === v}
              onClick={() => update({ view: v, type: null, status: null })}
              className={cn(
                "px-3 py-2 text-sm -mb-px border-b-2 transition-colors",
                view === v ? "border-primary font-medium" : "border-transparent text-muted-foreground hover:text-foreground",
              )}
            >
              {VIEW_LABEL[v]}
            </button>
          ))}
        </div>

        <div className="flex flex-col gap-2">
          <div className="flex gap-1 flex-wrap">
            {(["all", ...viewTypes] as const).map((t) => (
              <button
                key={t}
                type="button"
                aria-pressed={typeFilter === t}
                onClick={() => update({ type: t === "all" ? null : t })}
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
            {statusChoices.map((s) => (
              <button
                key={s}
                type="button"
                aria-pressed={statusFilter === s}
                onClick={() => update({ status: s })}
                className={cn(
                  "text-xs px-2.5 py-1 rounded-full border transition-colors",
                  statusFilter === s
                    ? "bg-primary text-primary-foreground border-primary"
                    : "hover:bg-muted border-border"
                )}
              >
                {s === "all" ? "All Statuses" : s === NEEDS_ACTION ? "Needs action" : s}
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
            <p className="text-sm text-destructive-text">Failed to load feedback.</p>
            <Button size="sm" variant="outline" onClick={() => refetch()}>Retry</Button>
          </div>
        )}

        {!isLoading && !isError && reports && reports.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 gap-3">
            <MessageSquareMore className="h-10 w-10 text-muted-foreground/30" />
            <p className="text-sm text-muted-foreground">{emptyText}</p>
          </div>
        )}

        {!isLoading && !isError && reports?.map((report) => (
          <FeedbackCard key={report.id} report={report} />
        ))}

      </main>
    </div>
  );
}
