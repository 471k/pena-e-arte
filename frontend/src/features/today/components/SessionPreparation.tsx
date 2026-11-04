import { useId, useState } from "react";
import { Link } from "react-router-dom";
import { CheckCircle2, CircleDashed, Loader2, RotateCw, Send } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import { describeDeposit } from "@/features/payments/depositOutcome";
import { useCreateConversationMutation, useSendMessageMutation } from "@/features/messaging/messagingApi";
import { useGetAppointmentPreparationQuery } from "@/features/appointments/appointmentsApi";
import { AppointmentStatus } from "@/features/appointments/appointment.types";
import type { AppointmentPreparationItem, AppointmentResponse } from "@/features/appointments/appointment.types";

// The itemised preparation for one session (A03), loaded only when the artist opens it. The facts come
// from the same server derivation as the client's checklist; the wording is about the client ("Not
// signed yet"), never the client's own instruction. Preparation informs and never blocks the session.

const MESSAGE_MAX = 2000;
const UPCOMING = new Set<AppointmentStatus>([AppointmentStatus.Pending, AppointmentStatus.Confirmed]);

function PreparationItem({ item }: { item: AppointmentPreparationItem }) {
  return (
    <li className="flex items-start gap-2">
      {item.satisfied
        ? <CheckCircle2 aria-hidden="true" className="h-4 w-4 mt-0.5 shrink-0 text-success-text" />
        : <CircleDashed aria-hidden="true" className="h-4 w-4 mt-0.5 shrink-0 text-muted-foreground" />}
      <div className="min-w-0">
        <p className="text-sm">
          <span className="font-medium">{item.label}</span>
          {": "}
          <span className={item.satisfied ? "text-success-text" : "text-warning-text"}>{item.state}</span>
        </p>
        {item.followUp && <p className="text-xs text-muted-foreground">{item.followUp}</p>}
      </div>
    </li>
  );
}

/** Message the client about this session, with the appointment attached to the thread (slice 2). */
function MessageClient({ appt }: { appt: AppointmentResponse }) {
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

export function SessionPreparation({ appt }: { appt: AppointmentResponse }) {
  const { data, isLoading, isError, refetch, isFetching } = useGetAppointmentPreparationQuery(appt.id);

  // Never shown as complete while it is still loading or couldn't be read (A03 acceptance).
  if (isLoading) {
    return (
      <p className="flex items-center gap-2 text-sm text-muted-foreground" role="status">
        <Loader2 aria-hidden="true" className="h-3.5 w-3.5 animate-spin" />
        Checking preparation…
      </p>
    );
  }
  if (isError || !data) {
    return (
      <div className="space-y-2" role="alert">
        <p className="text-sm text-destructive-text">Couldn&apos;t load the preparation details.</p>
        <Button type="button" variant="outline" size="sm" className="gap-1.5" disabled={isFetching} onClick={() => void refetch()}>
          <RotateCw aria-hidden="true" className="h-3.5 w-3.5" />
          Try again
        </Button>
      </div>
    );
  }

  const outstanding = data.items.some((i) => !i.satisfied);
  const deposit = data.deposit ? describeDeposit(data.deposit) : null;

  return (
    <div className="space-y-3">
      {data.items.length > 0 && (
        <ul aria-label="Preparation" className="space-y-2">
          {data.items.map((i) => <PreparationItem key={i.key} item={i} />)}
        </ul>
      )}
      {deposit && (
        <p className="text-sm">
          <span className="font-medium">Deposit payment: </span>
          {deposit.ownerLabel}
          {deposit.ownerDetail && <span className="text-muted-foreground"> · {deposit.ownerDetail}</span>}
        </p>
      )}
      {outstanding && UPCOMING.has(appt.status) && <MessageClient appt={appt} />}
    </div>
  );
}
