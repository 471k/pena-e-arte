import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Loader2, Send, CheckCheck } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Textarea } from "@/shared/components/ui/textarea";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import { cn } from "@/shared/utils/cn";
import { useAppSelector } from "@/app/hooks";
import {
  useGetMessagesQuery,
  useSendMessageMutation,
  useMarkConversationReadMutation,
} from "../messagingApi";
import type { ConversationResponse } from "../messaging.types";

const MAX_BODY_LENGTH = 2000;

function fmt(date: string): string {
  return new Date(date).toLocaleString("en-GB", {
    day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
  });
}

function initials(name: string): string {
  return name.split(" ").filter(Boolean).slice(0, 2).map((p) => p[0]).join("").toUpperCase();
}

interface ConversationThreadProps {
  conversation: ConversationResponse;
}

export function ConversationThread({ conversation }: ConversationThreadProps) {
  const [draft, setDraft] = useState("");
  const currentUserId = useAppSelector((s) => s.auth.user?.id);
  const { data: messages, isLoading } = useGetMessagesQuery({ conversationId: conversation.id });
  const [sendMessage, { isLoading: isSending }] = useSendMessageMutation();
  const [markRead] = useMarkConversationReadMutation();

  useEffect(() => {
    markRead(conversation.id);
  }, [conversation.id, markRead]);

  const trimmed = draft.trim();
  const canSend = trimmed.length > 0 && trimmed.length <= MAX_BODY_LENGTH;

  async function handleSend() {
    if (!canSend) return;
    try {
      await sendMessage({ conversationId: conversation.id, body: trimmed }).unwrap();
      setDraft("");
    } catch {
      toast.error("Failed to send message.");
    }
  }

  const lastMine = messages?.length
    ? [...messages].reverse().find((m) => m.senderUserId === currentUserId)
    : undefined;

  return (
    <div className="flex flex-col h-full">
      <div className="flex items-center gap-2 px-4 py-3 border-b">
        <Avatar className="h-8 w-8">
          <AvatarFallback>{initials(conversation.otherDisplayName)}</AvatarFallback>
        </Avatar>
        <div>
          <p className="text-sm font-medium">{conversation.otherDisplayName}</p>
          <p className="text-xs text-muted-foreground capitalize">{conversation.otherRole}</p>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-4 py-3 space-y-2">
        {isLoading && (
          <div className="space-y-2">
            <Skeleton className="h-10 w-3/4" />
            <Skeleton className="h-10 w-2/3 ml-auto" />
          </div>
        )}

        {!isLoading && !messages?.length && (
          <p className="text-center text-xs text-muted-foreground py-8">
            No messages yet — say hello.
          </p>
        )}

        {!isLoading && messages?.map((m) => {
          const isMine = m.senderUserId === currentUserId;
          return (
            <div
              key={m.id}
              className={cn(
                "rounded-md px-3 py-2 text-sm max-w-[75%]",
                isMine ? "bg-primary text-primary-foreground ml-auto" : "bg-muted",
              )}
            >
              <p className="whitespace-pre-wrap">{m.body}</p>
              <p className={cn(
                "text-[10px] mt-1 flex items-center gap-1",
                isMine ? "text-primary-foreground/70 justify-end" : "text-muted-foreground",
              )}>
                {fmt(m.createdAt)}
                {isMine && lastMine?.id === m.id && m.readAt && (
                  <CheckCheck className="h-3 w-3" aria-label="Read" />
                )}
              </p>
            </div>
          );
        })}
      </div>

      <div className="flex items-end gap-2 p-3 border-t">
        <Textarea
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          placeholder="Type a message…"
          rows={2}
          maxLength={MAX_BODY_LENGTH}
          disabled={isSending}
          className="resize-none text-sm"
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              void handleSend();
            }
          }}
        />
        <Button
          type="button"
          size="icon"
          className="h-9 w-9 shrink-0"
          disabled={!canSend || isSending}
          onClick={handleSend}
          aria-label="Send message"
        >
          {isSending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
        </Button>
      </div>
    </div>
  );
}
