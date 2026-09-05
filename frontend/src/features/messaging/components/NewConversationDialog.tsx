import { useState } from "react";
import { toast } from "sonner";
import { Loader2 } from "lucide-react";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle,
} from "@/shared/components/ui/dialog";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import { useGetContactsQuery, useCreateConversationMutation } from "../messagingApi";

function initials(name: string): string {
  return name.split(" ").filter(Boolean).slice(0, 2).map((p) => p[0]).join("").toUpperCase();
}

interface NewConversationDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConversationSelected: (conversationId: string) => void;
}

export function NewConversationDialog({ open, onOpenChange, onConversationSelected }: NewConversationDialogProps) {
  const { data: contacts, isLoading } = useGetContactsQuery(undefined, { skip: !open });
  const [createConversation] = useCreateConversationMutation();
  const [pendingUserId, setPendingUserId] = useState<string | null>(null);

  async function handleSelect(userId: string, existingConversationId: string | null) {
    // Selecting a contact we already have a thread with navigates straight to it — no
    // point round-tripping to the server for an answer already known client-side.
    if (existingConversationId) {
      onConversationSelected(existingConversationId);
      onOpenChange(false);
      return;
    }

    setPendingUserId(userId);
    try {
      const conversation = await createConversation({ recipientUserId: userId }).unwrap();
      onConversationSelected(conversation.id);
      onOpenChange(false);
    } catch {
      toast.error("Failed to start conversation.");
    } finally {
      setPendingUserId(null);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>New conversation</DialogTitle>
        </DialogHeader>

        <div className="max-h-80 overflow-y-auto -mx-2">
          {isLoading && (
            <p className="px-2 py-6 text-center text-xs text-muted-foreground">Loading…</p>
          )}

          {!isLoading && !contacts?.length && (
            <p className="px-2 py-6 text-center text-xs text-muted-foreground">
              No one available to message yet.
            </p>
          )}

          {!isLoading && contacts?.map((c) => (
            <button
              key={c.userId}
              type="button"
              disabled={pendingUserId === c.userId}
              onClick={() => handleSelect(c.userId, c.existingConversationId)}
              className="w-full flex items-center gap-3 px-2 py-2 rounded-md text-left hover:bg-muted/50 transition-colors disabled:opacity-50"
            >
              <Avatar className="h-8 w-8">
                <AvatarFallback>{initials(c.displayName)}</AvatarFallback>
              </Avatar>
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium truncate">{c.displayName}</p>
                <p className="text-xs text-muted-foreground capitalize">{c.role}</p>
              </div>
              {pendingUserId === c.userId && <Loader2 className="h-4 w-4 animate-spin" />}
            </button>
          ))}
        </div>
      </DialogContent>
    </Dialog>
  );
}
