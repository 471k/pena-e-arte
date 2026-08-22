import { CheckCircle2, XCircle } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { ChannelBadge } from "./ChannelBadge";
import { formatDate } from "../notification.utils";
import type { NotificationLogResponse } from "../notification.types";

interface Props {
  log: NotificationLogResponse | null;
  onClose: () => void;
}

export function NotificationDetailModal({ log, onClose }: Props) {
  if (!log) return null;

  const date = log.sentAt ? formatDate(log.sentAt) : formatDate(log.createdAt);
  const recipient = log.recipientName
    ?? (log.recipientId ? `Recipient ID: ${log.recipientId.slice(0, 8)}…` : "External contact");

  return (
    <Dialog open={!!log} onOpenChange={(open) => { if (!open) onClose(); }}>
      <DialogContent className="max-w-2xl flex flex-col max-h-[85vh]">
        <DialogHeader>
          <div className="flex items-center gap-2 flex-wrap">
            <ChannelBadge channel={log.channel} />
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
          </div>
          <DialogTitle className="text-left mt-1 leading-snug">
            {log.subject
              ?? (log.channel === "Sms"
                  ? "SMS notification"
                  : log.channel === "Email"
                  ? "Email notification"
                  : "Notification")}
          </DialogTitle>
          <DialogDescription className="text-left">
            {recipient} · {date}
          </DialogDescription>
        </DialogHeader>

        <div className="flex-1 overflow-auto min-h-0 mt-2">
          {log.channel === "Email" ? (
            <iframe
              srcDoc={log.body}
              title="Email body"
              data-testid="email-body-iframe"
              className="w-full rounded border bg-white"
              style={{ height: "480px" }}
              sandbox="allow-same-origin"
            />
          ) : (
            <pre className="whitespace-pre-wrap text-sm text-foreground bg-muted/30 rounded p-4 min-h-[200px]">
              {log.body}
            </pre>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
