import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from "@/shared/components/ui/dialog";
import { SlotAvailabilityIndicator } from "./SlotAvailabilityIndicator";
import { useRescheduleAppointmentMutation, useCheckSlotAvailabilityQuery } from "../appointmentsApi";
import type { AppointmentResponse } from "../appointment.types";

// Same bounds as RescheduleAppointmentValidator (30–480) and BookAppointmentForm's DURATION_OPTIONS.
const DURATION_OPTIONS: { value: number; label: string }[] = [
  { value: 30,  label: "30 min — Touch-up" },
  { value: 45,  label: "45 min" },
  { value: 60,  label: "1 hour" },
  { value: 90,  label: "1.5 hours" },
  { value: 120, label: "2 hours" },
  { value: 180, label: "3 hours" },
  { value: 240, label: "4 hours" },
  { value: 300, label: "5 hours" },
  { value: 360, label: "6 hours" },
  { value: 480, label: "Full day (8 h)" },
];

interface RescheduleDialogProps {
  appointment: AppointmentResponse;
  open:        boolean;
  onOpenChange: (open: boolean) => void;
  /** Client self-reschedule doesn't need the staff-facing "notify separately" note. */
  description?: string;
}

// Formats an ISO datetime string for an <input type="datetime-local"> value (local time, no seconds).
function toDatetimeLocalValue(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function RescheduleDialog({ appointment, open, onOpenChange, description }: RescheduleDialogProps) {
  const [newDate, setNewDate] = useState(() => toDatetimeLocalValue(appointment.date));
  const [newDuration, setNewDuration] = useState(appointment.durationMinutes);
  const [reschedule, { isLoading }] = useRescheduleAppointmentMutation();

  // Reset the form to the appointment's current values every time the dialog is (re)opened —
  // otherwise a cancelled edit followed by reopening shows stale draft values. Adjusted during
  // render (not in an effect) per https://react.dev/learn/you-might-not-need-an-effect.
  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) {
      setNewDate(toDatetimeLocalValue(appointment.date));
      setNewDuration(appointment.durationMinutes);
    }
  }

  // Debounced slot-availability check for the *new* slot — same 600ms pattern as BookAppointmentForm.
  // Excludes the appointment's own current slot from counting as a "conflict" by construction: the
  // backend's conflict check already excludes `a.Id != command.AppointmentId`, so re-submitting the
  // unchanged slot correctly reports available.
  const [debouncedCheck, setDebouncedCheck] = useState<{ artistId?: string; date: string; durationMinutes: number } | null>(null);
  useEffect(() => {
    if (!open || !newDate || !newDuration) return;
    const timer = setTimeout(() => {
      setDebouncedCheck({ artistId: appointment.artistId ?? undefined, date: new Date(newDate).toISOString(), durationMinutes: newDuration });
    }, 600);
    return () => clearTimeout(timer);
  }, [open, newDate, newDuration, appointment.artistId]);

  const { data: slotStatus, isFetching: checkingSlot } = useCheckSlotAvailabilityQuery(debouncedCheck!, {
    skip: debouncedCheck === null || !open,
  });

  async function handleSubmit() {
    const result = await reschedule({
      id:                 appointment.id,
      newDate:            new Date(newDate).toISOString(),
      newDurationMinutes: newDuration,
      notes:              appointment.notes,
    });
    if ("data" in result) {
      toast.success("Appointment rescheduled.");
      onOpenChange(false);
    } else {
      const errMsg =
        (result.error as { data?: { message?: string } } | undefined)?.data?.message
        ?? "Failed to reschedule appointment.";
      toast.error(errMsg);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Reschedule appointment</DialogTitle>
          <DialogDescription>
            {description ??
              "Pick a new date, time, and duration. The client is not automatically notified — let them know separately."}
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 py-2">
          <div className="space-y-1.5">
            <Label htmlFor="reschedule-date">New date &amp; time</Label>
            <Input
              id="reschedule-date"
              type="datetime-local"
              min={new Date().toISOString().slice(0, 16)}
              value={newDate}
              onChange={(e) => setNewDate(e.target.value)}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="reschedule-duration">Duration</Label>
            <Select value={String(newDuration)} onValueChange={(v) => setNewDuration(Number(v))}>
              <SelectTrigger id="reschedule-duration">
                <SelectValue placeholder="Select duration" />
              </SelectTrigger>
              <SelectContent>
                {DURATION_OPTIONS.map(({ value, label }) => (
                  <SelectItem key={value} value={String(value)}>{label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {debouncedCheck !== null && (
            <SlotAvailabilityIndicator checking={checkingSlot} status={slotStatus} />
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isLoading}>
            Cancel
          </Button>
          <Button
            onClick={handleSubmit}
            disabled={isLoading || slotStatus?.available === false}
          >
            {isLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : "Confirm reschedule"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
