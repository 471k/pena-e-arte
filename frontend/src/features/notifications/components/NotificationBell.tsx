import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Bell } from "lucide-react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { clearUnread, toggleInbox } from "../notificationsSlice";
import { useGetNotificationsQuery } from "../notificationsApi";
import { ChannelBadge } from "./ChannelBadge";
import { formatDate, stripHtml } from "../notification.utils";
import { NotificationDetailModal } from "./NotificationDetailModal";
import type { NotificationLogResponse } from "../notification.types";

const MAX_RECENT = 5;

export function NotificationBell() {
  const dispatch     = useAppDispatch();
  const unreadCount  = useAppSelector((s) => s.notifications.unreadCount);
  const isOpen       = useAppSelector((s) => s.notifications.isInboxOpen);
  const containerRef = useRef<HTMLDivElement>(null);
  const [selectedLog, setSelectedLog] = useState<NotificationLogResponse | null>(null);

  // Only fetch once the panel is actually opened — no point polling this on every page.
  const { data: logs, isLoading, isError } = useGetNotificationsQuery({}, { skip: !isOpen });
  const recent = logs?.slice(0, MAX_RECENT) ?? [];

  // Opening the panel counts as "seen" — clears the badge.
  useEffect(() => {
    if (isOpen) dispatch(clearUnread());
  }, [isOpen, dispatch]);

  // Close on outside click.
  useEffect(() => {
    if (!isOpen) return;
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        dispatch(toggleInbox());
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [isOpen, dispatch]);

  return (
    <div className="relative" ref={containerRef}>
      <button
        type="button"
        onClick={() => dispatch(toggleInbox())}
        aria-label={unreadCount > 0 ? `View notifications, ${unreadCount} unread` : "View notifications"}
        aria-expanded={isOpen}
        className="relative h-8 w-8 flex items-center justify-center rounded-md text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
      >
        <Bell className="h-4 w-4" />
        {unreadCount > 0 && (
          <span className="absolute -top-1 -right-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-semibold leading-none text-destructive-foreground">
            {unreadCount > 9 ? "9+" : unreadCount}
          </span>
        )}
      </button>

      {isOpen && (
        <div className="absolute right-0 top-full mt-2 w-80 rounded-md border bg-background shadow-lg z-30">
          <div className="px-3 py-2 border-b">
            <span className="text-sm font-medium">Notifications</span>
          </div>

          <div className="max-h-80 overflow-y-auto">
            {isLoading && (
              <p className="px-3 py-6 text-center text-xs text-muted-foreground">Loading…</p>
            )}

            {isError && (
              <p className="px-3 py-6 text-center text-xs text-destructive">
                Failed to load notifications.
              </p>
            )}

            {!isLoading && !isError && recent.length === 0 && (
              <p className="px-3 py-6 text-center text-xs text-muted-foreground">
                No notifications yet.
              </p>
            )}

            {!isLoading && !isError && recent.map((log) => (
              <button
                key={log.id}
                type="button"
                className="w-full text-left px-3 py-2 border-b last:border-b-0 space-y-1 hover:bg-muted/50 transition-colors"
                onClick={() => setSelectedLog(log)}
                aria-label={`View notification: ${log.subject ?? log.channel}`}
              >
                <div className="flex items-center gap-2 flex-wrap">
                  <ChannelBadge channel={log.channel} />
                  {log.subject && (
                    <p className="text-xs font-medium truncate">{log.subject}</p>
                  )}
                </div>
                <p className="text-xs text-muted-foreground line-clamp-2">
                  {stripHtml(log.body)}
                </p>
                <p className="text-[10px] text-muted-foreground">
                  {log.sentAt ? formatDate(log.sentAt) : formatDate(log.createdAt)}
                </p>
              </button>
            ))}
          </div>

          <Link
            to="/notifications"
            onClick={() => dispatch(toggleInbox())}
            className="block px-3 py-2 text-center text-xs text-primary hover:bg-muted transition-colors border-t"
          >
            View all
          </Link>
        </div>
      )}
      <NotificationDetailModal
        log={selectedLog}
        onClose={() => setSelectedLog(null)}
      />
    </div>
  );
}
