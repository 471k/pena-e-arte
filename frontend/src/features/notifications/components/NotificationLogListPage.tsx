import { useState } from "react";
import { Bell, CheckCircle2, Loader2, Mail, MessageSquare, XCircle } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { useGetNotificationsQuery } from "../notificationsApi";
import type { NotificationLogResponse, NotificationsFilter } from "../notification.types";

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

function ChannelBadge({ channel }: { channel: "Email" | "Sms" }) {
  if (channel === "Email") {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-blue-500/10 px-2 py-0.5 text-xs font-medium text-blue-600 dark:text-blue-400">
        <Mail className="h-3 w-3" />
        Email
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-purple-500/10 px-2 py-0.5 text-xs font-medium text-purple-600 dark:text-purple-400">
      <MessageSquare className="h-3 w-3" />
      SMS
    </span>
  );
}

function NotificationRow({ log }: { log: NotificationLogResponse }) {
  return (
    <Card>
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
              {log.body.length > 120 ? log.body.slice(0, 120) + "…" : log.body}
            </p>
            <p className="text-xs text-muted-foreground font-mono">
              Recipient: {log.recipientId.slice(0, 8)}…
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
  const [channel, setChannel] = useState<"Email" | "Sms" | "">("");
  const [from, setFrom]       = useState("");
  const [to, setTo]           = useState("");

  const filter: NotificationsFilter = {
    channel: channel || undefined,
    from:    from    || undefined,
    to:      to      || undefined,
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
            <label className="text-xs text-muted-foreground">Channel</label>
            <select
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
            <label className="text-xs text-muted-foreground">From</label>
            <input
              type="date"
              value={from}
              onChange={(e) => setFrom(e.target.value)}
              className="h-8 rounded-md border bg-background px-2 text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-ring"
            />
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-xs text-muted-foreground">To</label>
            <input
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
          <div className="flex items-center justify-center py-16 text-muted-foreground gap-2">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading notifications…</span>
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
          <NotificationRow key={log.id} log={log} />
        ))}
      </main>
    </div>
  );
}
