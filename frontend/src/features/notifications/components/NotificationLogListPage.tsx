import { useEffect, useState } from "react";
import { Bell, CheckCircle2, XCircle } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppDispatch } from "@/app/hooks";
import { clearUnread } from "../notificationsSlice";
import { useGetNotificationsQuery } from "../notificationsApi";
import { ChannelBadge } from "./ChannelBadge";
import { formatDate, stripHtml } from "../notification.utils";
import { NotificationDetailModal } from "./NotificationDetailModal";
import type { NotificationLogResponse, NotificationsFilter } from "../notification.types";

function NotificationRow({
  log,
  onClick,
}: {
  log: NotificationLogResponse;
  onClick: () => void;
}) {
  const preview = stripHtml(log.body);
  return (
    <Card
      className="cursor-pointer hover:bg-muted/40 transition-colors"
      onClick={onClick}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") onClick(); }}
      aria-label={`Open notification: ${log.subject ?? log.channel}`}
    >
      <CardContent className="p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="space-y-1 min-w-0 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <ChannelBadge channel={log.channel} />
              {log.subject && (
                <p className="text-sm font-medium truncate">{log.subject}</p>
              )}
            </div>
            <p className="text-xs text-muted-foreground line-clamp-2">
              {preview.length > 120 ? preview.slice(0, 120) + "…" : preview}
            </p>
            <p className="text-xs text-muted-foreground">
              {log.recipientName ? (
                <>Recipient: {log.recipientName}</>
              ) : (
                <span className="font-mono">Recipient ID: {log.recipientId.slice(0, 8)}…</span>
              )}
            </p>
          </div>
          <div className="shrink-0 text-right space-y-1">
            {log.isSuccess ? (
              <span className="inline-flex items-center gap-1 text-xs text-green-600 dark:text-green-400">
                <CheckCircle2 className="h-3.5 w-3.5" />
                Delivered
              </span>
            ) : (
              <span className="inline-flex items-center gap-1 text-xs text-destructive">
                <XCircle className="h-3.5 w-3.5" />
                Failed
              </span>
            )}
            <p className="text-xs text-muted-foreground">
              {log.sentAt ? formatDate(log.sentAt) : formatDate(log.createdAt)}
            </p>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

export function NotificationLogListPage() {
  useDocumentMeta({ title: "Notifications — TattooOS", canonical: "/notifications" });

  const dispatch = useAppDispatch();
  const [channel, setChannel]       = useState<"Email" | "Sms" | "">("");
  const [from, setFrom]             = useState("");
  const [to, setTo]                 = useState("");
  const [selectedLog, setSelectedLog] = useState<NotificationLogResponse | null>(null);

  // Viewing the log marks all previously-received notifications as read.
  useEffect(() => {
    dispatch(clearUnread());
  }, [dispatch]);

  const filter: NotificationsFilter = {
    channel: channel || undefined,
    from:    from    || undefined,
    // "to" is a date-only picker — extend to the end of that day so notifications
    // sent later on the selected day aren't excluded by the SentAt <= to comparison.
    to:      to      ? `${to}T23:59:59.999` : undefined,
  };

  const { data: logs, isLoading, isError } = useGetNotificationsQuery(filter);

  const hasFilters = !!(channel || from || to);

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <Bell className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Notification Log</span>
        </div>
        {logs && (
          <span className="text-xs text-muted-foreground">
            {logs.length} {logs.length !== 1 ? "entries" : "entry"}
          </span>
        )}
      </header>

      <div className="border-b bg-muted/30 px-6 py-3">
        <div className="max-w-2xl mx-auto flex flex-wrap gap-3 items-end">
          <div className="flex flex-col gap-1">
            <label htmlFor="notification-channel-filter" className="text-xs text-muted-foreground">Channel</label>
            <select
              id="notification-channel-filter"
              value={channel}
              onChange={(e) => setChannel(e.target.value as "Email" | "Sms" | "")}
              className="h-8 rounded-md border bg-background px-2 text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-ring"
            >
              <option value="">All channels</option>
              <option value="Email">Email</option>
              <option value="Sms">SMS</option>
            </select>
          </div>

          <div className="flex flex-col gap-1">
            <label htmlFor="notification-from-filter" className="text-xs text-muted-foreground">From</label>
            <input
              id="notification-from-filter"
              type="date"
              value={from}
              onChange={(e) => setFrom(e.target.value)}
              className="h-8 rounded-md border bg-background px-2 text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-ring"
            />
          </div>

          <div className="flex flex-col gap-1">
            <label htmlFor="notification-to-filter" className="text-xs text-muted-foreground">To</label>
            <input
              id="notification-to-filter"
              type="date"
              value={to}
              onChange={(e) => setTo(e.target.value)}
              className="h-8 rounded-md border bg-background px-2 text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-ring"
            />
          </div>

          {hasFilters && (
            <button
              onClick={() => { setChannel(""); setFrom(""); setTo(""); }}
              className="h-8 px-3 rounded-md text-xs text-muted-foreground border hover:bg-muted transition-colors"
            >
              Clear filters
            </button>
          )}
        </div>
      </div>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-2">
        {isLoading && (
          <div className="space-y-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-20 w-full rounded-lg" />
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load notification log. Please try again.
          </p>
        )}

        {!isLoading && !isError && logs?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            No notifications found{hasFilters ? " for the selected filters" : ""}.
          </p>
        )}

        {!isLoading && !isError && logs && logs.length > 0 && logs.map((log) => (
          <NotificationRow key={log.id} log={log} onClick={() => setSelectedLog(log)} />
        ))}
      </main>

      <NotificationDetailModal
        log={selectedLog}
        onClose={() => setSelectedLog(null)}
      />
    </div>
  );
}
