import { useState } from "react";
import { Loader2, Send, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from "@/shared/components/ui/dialog";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import {
  useCreateManualReminderMutation,
  useGetManualRemindersQuery,
  useCancelManualReminderMutation,
} from "../remindersApi";
import { ReminderStatusBadge } from "./ReminderStatusBadge";
import { PhoneInput } from "@/shared/components/ui/phone-input";
import { isValidE164Phone, PHONE_ERROR_MESSAGE } from "@/shared/utils/phoneValidation";

interface ReminderDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Send tied to an appointment's linked client — recipient is implicit. */
  appointmentId?: string;
  /** Send tied to an existing Client record — recipient is implicit. */
  clientId?: string;
  /** Only honored for owner/admin callers acting on another artist's behalf. */
  artistId?: string;
}

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", {
    day: "numeric", month: "short", hour: "2-digit", minute: "2-digit",
  });
}

// Formats an ISO datetime string for an <input type="datetime-local"> value.
function toDatetimeLocalValue(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

// Browsers allow a datetime-local input to be cleared even with `min` set, and `new
// Date("")` parses to an Invalid Date rather than throwing — only .toISOString() throws,
// so this must be checked up front rather than relying on a try/catch at the call site.
function isValidDateTimeLocal(value: string): boolean {
  return value.trim().length > 0 && !Number.isNaN(new Date(value).getTime());
}

export function ReminderDialog({ open, onOpenChange, appointmentId, clientId, artistId }: ReminderDialogProps) {
  const isRawContact = !appointmentId && !clientId;

  // A client with no assigned artist has no authoritative artist source, and an
  // owner/admin caller (unlike an artist, who is always resolved from their own
  // account) must supply one explicitly — mirrors CreateManualReminderCommand's
  // RequireExplicitArtistAsync fallback for exactly this case.
  const role = useAppSelector((s) => s.auth.role);
  const needsArtistPicker = !!clientId && !artistId && role !== Role.Artist;
  const { data: artists } = useGetArtistsQuery(undefined, { skip: !needsArtistPicker });

  const [recipientName, setRecipientName]   = useState("");
  const [recipientPhone, setRecipientPhone] = useState("");
  const [message, setMessage]               = useState("");
  const [pickedArtistId, setPickedArtistId] = useState("");
  const [scheduleLater, setScheduleLater]   = useState(false);
  const [scheduledFor, setScheduledFor]     = useState(() =>
    toDatetimeLocalValue(new Date(Date.now() + 60 * 60 * 1000)));

  const [createReminder, { isLoading: isSending }] = useCreateManualReminderMutation();
  const [cancelReminder] = useCancelManualReminderMutation();

  const { data: history, isLoading: historyLoading } = useGetManualRemindersQuery(
    { appointmentId, clientId },
    { skip: isRawContact },
  );

  function resetForm() {
    setRecipientName("");
    setRecipientPhone("");
    setMessage("");
    setPickedArtistId("");
    setScheduleLater(false);
    setScheduledFor(toDatetimeLocalValue(new Date(Date.now() + 60 * 60 * 1000)));
  }

  async function handleSubmit() {
    if (scheduleLater && !isValidDateTimeLocal(scheduledFor)) {
      toast.error("Pick a valid date and time to schedule for.");
      return;
    }

    const result = await createReminder({
      appointmentId,
      clientId,
      artistId: artistId ?? (needsArtistPicker ? pickedArtistId : undefined),
      recipientName:  isRawContact ? recipientName.trim() : undefined,
      recipientPhone: isRawContact ? recipientPhone.trim() : undefined,
      message: message.trim() || undefined,
      scheduledFor: scheduleLater ? new Date(scheduledFor).toISOString() : undefined,
    });

    if ("data" in result) {
      toast.success(scheduleLater ? "Reminder scheduled." : "Reminder sent.");
      resetForm();
      if (isRawContact) onOpenChange(false);
    } else {
      const errMsg =
        (result.error as { data?: { message?: string } } | undefined)?.data?.message
        ?? "Failed to send reminder.";
      toast.error(errMsg);
    }
  }

  async function handleCancel(id: string) {
    const result = await cancelReminder(id);
    if ("error" in result) {
      toast.error("Failed to cancel reminder.");
      return;
    }
    toast.success("Reminder cancelled.");
  }

  const canSubmit = (isRawContact
    ? recipientName.trim().length > 0 && isValidE164Phone(recipientPhone) && recipientPhone.trim().length > 0
    : needsArtistPicker
    ? pickedArtistId.length > 0
    : true) && (!scheduleLater || isValidDateTimeLocal(scheduledFor));

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Send Reminder</DialogTitle>
          <DialogDescription>
            {isRawContact
              ? "Send a one-off SMS reminder to a phone number — no client record is created."
              : "Send a one-off SMS reminder about this appointment."}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 py-2">
          {isRawContact && (
            <>
              <div className="space-y-1.5">
                <Label htmlFor="reminder-recipient-name">Name</Label>
                <Input
                  id="reminder-recipient-name"
                  value={recipientName}
                  onChange={(e) => setRecipientName(e.target.value)}
                  placeholder="e.g. Wendy"
                  maxLength={200}
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="reminder-recipient-phone">Phone</Label>
                <PhoneInput
                  id="reminder-recipient-phone"
                  value={recipientPhone}
                  onChange={setRecipientPhone}
                  aria-invalid={recipientPhone.length > 0 && !isValidE164Phone(recipientPhone)}
                  aria-describedby={
                    recipientPhone.length > 0 && !isValidE164Phone(recipientPhone)
                      ? "reminder-recipient-phone-error"
                      : undefined
                  }
                />
                {recipientPhone.length > 0 && !isValidE164Phone(recipientPhone) && (
                  <p id="reminder-recipient-phone-error" className="text-xs text-destructive-text">
                    {PHONE_ERROR_MESSAGE}
                  </p>
                )}
              </div>
            </>
          )}

          {needsArtistPicker && (
            <div className="space-y-1.5">
              <Label htmlFor="reminder-artist">Artist</Label>
              <Select value={pickedArtistId} onValueChange={setPickedArtistId}>
                <SelectTrigger id="reminder-artist" aria-label="Artist">
                  <SelectValue placeholder="This client has no assigned artist — pick one" />
                </SelectTrigger>
                <SelectContent>
                  {artists?.map((a) => (
                    <SelectItem key={a.id} value={a.id}>
                      {a.firstName} {a.lastName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          <div className="space-y-1.5">
            <Label htmlFor="reminder-message">Message (optional)</Label>
            <Textarea
              id="reminder-message"
              rows={3}
              value={message}
              onChange={(e) => setMessage(e.target.value)}
              placeholder="Hi, this is a reminder from your studio about your upcoming appointment."
              maxLength={320}
              className="resize-none"
            />
            <p className="text-xs text-muted-foreground text-right">{message.length}/320</p>
          </div>

          <div className="flex items-center justify-between py-1">
            <span className="text-sm font-medium">Schedule for later</span>
            <ToggleSwitch
              checked={scheduleLater}
              onChange={() => setScheduleLater((v) => !v)}
              aria-label="Schedule for later"
            />
          </div>

          {scheduleLater && (
            <div className="space-y-1.5">
              <Label htmlFor="reminder-scheduled-for">Send at</Label>
              <Input
                id="reminder-scheduled-for"
                type="datetime-local"
                min={toDatetimeLocalValue(new Date())}
                value={scheduledFor}
                onChange={(e) => setScheduledFor(e.target.value)}
              />
            </div>
          )}

          {!isRawContact && (
            <div className="pt-2 border-t space-y-2">
              <p className="text-xs font-medium text-muted-foreground">History</p>
              {historyLoading && <Skeleton className="h-10 w-full" />}
              {!historyLoading && (history ?? []).length === 0 && (
                <p className="text-xs text-muted-foreground">No reminders sent yet.</p>
              )}
              {!historyLoading && (history ?? []).map((r) => (
                <div key={r.id} className="flex items-center justify-between gap-2 text-xs py-1">
                  <div className="flex items-center gap-2 min-w-0">
                    <ReminderStatusBadge status={r.status} />
                    <span className="text-muted-foreground truncate">
                      {formatDateTime(r.scheduledFor)}
                    </span>
                  </div>
                  {r.status === "Scheduled" && (
                    <Button
                      variant="ghost"
                      size="icon"
                      className="h-6 w-6 shrink-0"
                      aria-label="Cancel reminder"
                      onClick={() => handleCancel(r.id)}
                    >
                      <X className="h-3.5 w-3.5" />
                    </Button>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isSending}>
            Close
          </Button>
          <Button onClick={handleSubmit} disabled={isSending || !canSubmit} className="gap-2">
            {isSending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
            {scheduleLater ? "Schedule reminder" : "Send now"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
