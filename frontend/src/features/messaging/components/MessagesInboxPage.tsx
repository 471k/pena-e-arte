import { useState } from "react";
import { useSearchParams } from "react-router-dom";
import { ArrowLeft, MessagesSquare, Pencil } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import { Badge } from "@/shared/components/ui/badge";
import { cn } from "@/shared/utils/cn";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetConversationsQuery } from "../messagingApi";
import { ConversationThread } from "./ConversationThread";
import { NewConversationDialog } from "./NewConversationDialog";

function initials(name: string): string {
  return name.split(" ").filter(Boolean).slice(0, 2).map((p) => p[0]).join("").toUpperCase();
}

function fmt(date: string): string {
  return new Date(date).toLocaleDateString("en-GB", { day: "numeric", month: "short" });
}

// Distinct from any real `?conversation=` value (including null/absent) so the sync check
// below always fires on the very first render — seeding it FROM the live value instead
// would make the first render look already-in-sync and the sync would never (re-)fire when
// the param is already present at mount. See feedback_react_state_sync_pattern memory.
const PARAM_NOT_YET_SYNCED = Symbol("param-not-yet-synced");

export function MessagesInboxPage() {
  useDocumentMeta({ title: "Messages — TattooOS", canonical: "/messages" });

  const [searchParams] = useSearchParams();
  const { data: conversations, isLoading } = useGetConversationsQuery();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [newConversationOpen, setNewConversationOpen] = useState(false);

  // Adjusting state during render (react.dev-documented pattern) instead of an effect —
  // re-syncs selectedId whenever the URL's `?conversation=` param changes (e.g. a fresh
  // "Message client" navigation from AppointmentDetailPage while this page is already
  // mounted), without the extra render+flicker an effect-based sync would cause.
  const [syncedParam, setSyncedParam] = useState<string | null | typeof PARAM_NOT_YET_SYNCED>(
    PARAM_NOT_YET_SYNCED,
  );
  const currentParam = searchParams.get("conversation");
  if (currentParam !== syncedParam) {
    setSyncedParam(currentParam);
    if (currentParam) setSelectedId(currentParam);
  }

  const selected = conversations?.find((c) => c.id === selectedId) ?? null;

  return (
    <div className="min-h-screen bg-background flex flex-col">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <MessagesSquare className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Messages</span>
        <Button
          variant="outline"
          size="sm"
          className="ml-auto gap-1.5"
          onClick={() => setNewConversationOpen(true)}
        >
          <Pencil className="h-3.5 w-3.5" />
          New message
        </Button>
      </header>

      <main className="flex-1 flex min-h-0">
        <aside className={cn(
          "w-full sm:w-80 border-r overflow-y-auto shrink-0",
          selected ? "hidden sm:block" : "block",
        )}>
          {isLoading && (
            <div className="p-3 space-y-3">
              {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-14 w-full" />)}
            </div>
          )}

          {!isLoading && !conversations?.length && (
            <p className="text-center text-sm text-muted-foreground py-12 px-4">
              No conversations yet. Start one with the "New message" button above.
            </p>
          )}

          {!isLoading && conversations?.map((c) => (
            <button
              key={c.id}
              type="button"
              onClick={() => setSelectedId(c.id)}
              aria-label={`Conversation with ${c.otherDisplayName}`}
              className={cn(
                "w-full flex items-center gap-3 px-4 py-3 border-b text-left transition-colors hover:bg-muted/50",
                selectedId === c.id && "bg-muted",
              )}
            >
              <Avatar className="h-9 w-9">
                <AvatarFallback>{initials(c.otherDisplayName)}</AvatarFallback>
              </Avatar>
              <div className="flex-1 min-w-0">
                <div className="flex items-center justify-between gap-2">
                  <p className="text-sm font-medium truncate">{c.otherDisplayName}</p>
                  {c.lastMessageAt && (
                    <span className="text-[10px] text-muted-foreground shrink-0">{fmt(c.lastMessageAt)}</span>
                  )}
                </div>
                <p className="text-xs text-muted-foreground truncate">
                  {c.lastMessageFromMe && "You: "}{c.lastMessagePreview ?? "No messages yet"}
                </p>
              </div>
              {c.unreadCount > 0 && (
                <Badge className="shrink-0 h-5 min-w-5 justify-center px-1">
                  {c.unreadCount > 9 ? "9+" : c.unreadCount}
                </Badge>
              )}
            </button>
          ))}
        </aside>

        <section className={cn("flex-1 min-h-0", selected ? "flex flex-col" : "hidden sm:flex sm:flex-col")}>
          {selected ? (
            <>
              <Button
                variant="ghost"
                size="sm"
                className="sm:hidden gap-1.5 justify-start m-2 w-fit"
                onClick={() => setSelectedId(null)}
              >
                <ArrowLeft className="h-3.5 w-3.5" />
                Back
              </Button>
              <ConversationThread conversation={selected} />
            </>
          ) : (
            <div className="flex-1 flex items-center justify-center">
              <p className="text-sm text-muted-foreground">Select a conversation to view messages.</p>
            </div>
          )}
        </section>
      </main>

      <NewConversationDialog
        open={newConversationOpen}
        onOpenChange={setNewConversationOpen}
        onConversationSelected={setSelectedId}
      />
    </div>
  );
}
