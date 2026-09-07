import { useState } from "react";
import { toast } from "sonner";
import { FileVideo, Loader2, Send } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Textarea } from "@/shared/components/ui/textarea";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import { useAppSelector } from "@/app/hooks";
import {
  useGetFeedbackMessagesQuery,
  usePostFeedbackMessageMutation,
} from "../feedbackApi";
import type { FeedbackReportResponse, FeedbackStatus } from "../feedback.types";
import { useSupportHub } from "../useSupportHub";

const STATUS_BADGE: Record<FeedbackStatus, string> = {
  Open:      "bg-blue-500/15 text-blue-600",
  Reviewing: "bg-amber-500/15 text-amber-600",
  Resolved:  "bg-green-500/15 text-green-600",
  Dismissed: "bg-muted text-muted-foreground",
};

function fmt(date: string): string {
  return new Date(date).toLocaleString("en-GB", {
    day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
  });
}

interface SupportTicketThreadProps {
  report:    FeedbackReportResponse;
  canReply?: boolean;
}

export function SupportTicketThread({ report, canReply = true }: SupportTicketThreadProps) {
  const [reply, setReply] = useState("");
  const currentUserId = useAppSelector((s) => s.auth.user?.id);
  const currentRole   = useAppSelector((s) => s.auth.role);
  const { data: messages, isLoading } = useGetFeedbackMessagesQuery(report.id);
  const [postMessage, { isLoading: isSending }] = usePostFeedbackMessageMutation();

  useSupportHub(report.id);

  // Mirrors PostFeedbackMessageHandler's own reopen condition — only a studio-side reply
  // (not admin) on an already-closed ticket can change the report row.
  const mayReopen = currentRole !== "admin" && (report.status === "Resolved" || report.status === "Dismissed");

  async function handleSend() {
    const body = reply.trim();
    if (!body) return;
    try {
      await postMessage({ feedbackReportId: report.id, body, mayReopen }).unwrap();
      setReply("");
    } catch {
      toast.error("Failed to send message.");
    }
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-sm font-medium">{report.title}</p>
        <span className={cn("text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0", STATUS_BADGE[report.status])}>
          {report.status}
        </span>
      </div>

      <div className="space-y-2 max-h-64 overflow-y-auto pr-1">
        <div className="rounded-md bg-muted/50 px-3 py-2 text-sm space-y-2">
          <p className="whitespace-pre-wrap">{report.body}</p>
          {!!report.attachmentUrls?.length && (
            <div className="flex flex-wrap gap-2">
              {report.attachmentUrls.map((url) => {
                const isVideo = /\.(mp4|webm|mov)$/i.test(url);
                return (
                  <a
                    key={url}
                    href={url}
                    target="_blank"
                    rel="noreferrer"
                    className="block h-16 w-16 rounded-md overflow-hidden border border-border/40 bg-background shrink-0"
                  >
                    {isVideo ? (
                      <div className="flex h-full w-full items-center justify-center text-muted-foreground">
                        <FileVideo className="h-6 w-6" aria-hidden="true" />
                      </div>
                    ) : (
                      <img src={url} alt="Feedback attachment" className="h-full w-full object-cover" />
                    )}
                  </a>
                );
              })}
            </div>
          )}
        </div>

        {isLoading && (
          <div className="space-y-2">
            <Skeleton className="h-10 w-3/4" />
            <Skeleton className="h-10 w-2/3 ml-auto" />
          </div>
        )}

        {!isLoading && messages?.map((m) => (
          <div
            key={m.id}
            className={cn(
              "rounded-md px-3 py-2 text-sm max-w-[85%]",
              m.authorUserId === currentUserId
                ? "bg-primary text-primary-foreground ml-auto"
                : "bg-muted",
            )}
          >
            <p className="whitespace-pre-wrap">{m.body}</p>
            <p className={cn(
              "text-[10px] mt-1",
              m.authorUserId === currentUserId ? "text-primary-foreground/70" : "text-muted-foreground",
            )}>
              {m.authorRole} · {fmt(m.createdAt)}
            </p>
          </div>
        ))}
      </div>

      {canReply && (
        <div className="flex items-end gap-2 pt-1">
          <Textarea
            value={reply}
            onChange={(e) => setReply(e.target.value)}
            placeholder="Type a reply…"
            rows={2}
            disabled={isSending}
            className="resize-none text-sm"
          />
          <Button
            type="button"
            size="icon"
            className="h-9 w-9 shrink-0"
            disabled={isSending || !reply.trim()}
            onClick={handleSend}
            aria-label="Send reply"
          >
            {isSending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
          </Button>
        </div>
      )}
    </div>
  );
}
