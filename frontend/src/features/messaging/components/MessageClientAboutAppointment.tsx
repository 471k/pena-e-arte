import { useId, useState } from "react";
import { Link } from "react-router-dom";
import { Loader2, Send } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import { useCreateConversationMutation, useSendMessageMutation } from "@/features/messaging/messagingApi";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";

// Moved here unchanged from E04a's SessionPreparation (E04b, A08) so the staff appointment page reuses
// the same "Message <client> about this session" path rather than a second one. The message carries
// the appointment id (slice 2's ChatMessage.AppointmentId), so the thread shows which booking it is
// about and the appointment's communication state can see it was sent.

const MESSAGE_MAX = 2000;

/** Message the client about this session, with the appointment attached to the thread (slice 2). */
export function MessageClientAboutAppointment({ appt, onSent }: { appt: AppointmentResponse; onSent?: () => void }) {
  const [body, setBody] = useState("");
  const [sentConversationId, setSentConversationId] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [createConversation, { isLoading: creating }] = useCreateConversationMutation();
  const [sendMessage, { isLoading: sending }] = useSendMessageMutation();
  const busy = creating || sending;
  const fieldId = useId();
  const name = appt.clientName ?? "the client";

  if (!appt.clientUserId) {
    return (
      <p className="text-xs text-muted-foreground">
        {name} has no TattooOS account, so they can&apos;t be messaged here.
      </p>
    );
  }
  const recipientUserId = appt.clientUserId;

  async function handleSend() {
    const text = body.trim();
    if (!text) return;
    setFailed(false);
    const conversation = await createConversation({ recipientUserId });
    if ("error" in conversation || !conversation.data) { setFailed(true); return; }
    const sent = await sendMessage({ conversationId: conversation.data.id, body: text, appointmentId: appt.id });
    if ("error" in sent) { setFailed(true); return; }
    setBody("");
    setSentConversationId(conversation.data.id);
    onSent?.();
  }

  return (
    <div className="space-y-2">
      <Label htmlFor={fieldId}>Message {name} about this session</Label>
      <Textarea
        id={fieldId}
        value={body}
        maxLength={MESSAGE_MAX}
        onChange={(e) => { setBody(e.target.value); setSentConversationId(null); }}
        placeholder="Your message"
      />
      <Button
        type="button"
        variant="outline"
        size="sm"
        className="gap-1.5"
        disabled={busy || body.trim().length === 0}
        onClick={() => void handleSend()}
      >
        {busy ? <Loader2 aria-hidden="true" className="h-3.5 w-3.5 animate-spin" /> : <Send aria-hidden="true" className="h-3.5 w-3.5" />}
        Send
      </Button>
      <div aria-live="polite">
        {sentConversationId && (
          <p className="text-sm text-success-text">
            Message sent.{" "}
            <Link className="text-link underline underline-offset-2" to={`/messages?conversation=${sentConversationId}`}>
              Open the conversation
            </Link>
          </p>
        )}
      </div>
      {failed && (
        <p role="alert" className="text-sm text-destructive-text">
          The message wasn&apos;t sent. Please try again.
        </p>
      )}
    </div>
  );
}
