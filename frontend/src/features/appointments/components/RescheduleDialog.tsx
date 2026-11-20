import { useEffect, useState } from "react";
import { AlertCircle, Loader2, RotateCw } from "lucide-react";
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
import { skipToken } from "@reduxjs/toolkit/query";
import { SlotAvailabilityIndicator } from "./SlotAvailabilityIndicator";
import { SlotPicker } from "./SlotPicker";
import {
  useRescheduleAppointmentMutation, useCheckSlotAvailabilityQuery, useGetReschedulePreviewQuery,
  useGetAvailableSlotsQuery,
} from "../appointmentsApi";
import type { AppointmentResponse } from "../appointment.types";
import { isSlotTaken, slotRange } from "../slotList";
import { useStudioZone } from "@/features/studios/useStudioZone";
import { StudioDateTimeNote } from "@/shared/components/StudioDateTimeNote";
import {
  resolveStudioWallTime, studioWallDate, toStudioInputValue, type StudioWallTime, type WallDate,
} from "@/shared/utils/studioDay";

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

// The input runs on the STUDIO's wall clock (S01): the value, its minimum and what it means as an
// instant all go through the studio's zone. Before, the value was read in the viewer's browser zone and
// the minimum was a UTC string (`new Date().toISOString().slice(0, 16)`) in a local-time input, which
// allowed past times west of UTC and refused valid ones east of it. What the picked time means is said by
// StudioDateTimeNote, shared with the booking forms and the reminder picker.

export function RescheduleDialog({ appointment, open, onOpenChange, description }: RescheduleDialogProps) {
  const { zone, retry: retryZone } = useStudioZone();
  const studioTz = zone.status === "ready" ? zone.timeZone : null;
  // null = untouched: the field shows the appointment's own studio time, derived once the zone is known
  // rather than copied into state before it is.
  const [editedDate, setEditedDate] = useState<string | null>(null);
  const newDate = editedDate ?? (studioTz ? toStudioInputValue(new Date(appointment.date), studioTz) : "");
  const [newDuration, setNewDuration] = useState(appointment.durationMinutes);
  const [reschedule, { isLoading }] = useRescheduleAppointmentMutation();

  // What the picked wall time means. A time skipped by the clocks going forward is refused; one that
  // happens twice when they go back uses the first occurrence, and the dialog says so.
  const wallTime: StudioWallTime | null = studioTz && newDate ? resolveStudioWallTime(newDate, studioTz) : null;
  const pickedInstant =
    wallTime?.kind === "ok" ? wallTime.instant : wallTime?.kind === "repeated" ? wallTime.first : null;
  const pickedIso = pickedInstant?.toISOString() ?? null;

  // Whether this appointment can be moved at all — the server's own status and notice-window rule,
  // the same one the save applies (E04b, A02), so a refusal is explained before a time is picked
  // rather than after submitting. Refetched on every opening: the notice window moves with the clock.
  const eligibility = useGetReschedulePreviewQuery(appointment.id, { skip: !open, refetchOnMountOrArgChange: true });
  const eligible = eligibility.data?.canReschedule === true;

  // Reset the form to the appointment's current values every time the dialog is (re)opened —
  // otherwise a cancelled edit followed by reopening shows stale draft values. Adjusted during
  // render (not in an effect) per https://react.dev/learn/you-might-not-need-an-effect.
  const [prevOpen, setPrevOpen] = useState(open);
  const [rangeStartState, setRangeStart] = useState<WallDate | null>(null);
  const [takenIso, setTakenIso] = useState<string | null>(null);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) {
      setEditedDate(null);
      setNewDuration(appointment.durationMinutes);
      setRangeStart(null);
      setTakenIso(null);
    }
  }

  // The slot list (C03) with the same choices booking has: open times for this appointment's artist (any artist
  // when it has none yet) and the chosen duration, its own slot not counting as booked. A preview, not a
  // reservation: picking a time fills the field below, and the save decides.
  const slotToday = studioTz ? studioWallDate(new Date(), studioTz) : null;
  const rangeStart = rangeStartState ?? slotToday;
  const canListSlots = open && eligible && rangeStart !== null && newDuration > 0;
  const slotsQuery = useGetAvailableSlotsQuery(
    canListSlots && rangeStart
      ? {
          artistId: appointment.artistId ?? undefined,
          ...slotRange(rangeStart),
          durationMinutes: newDuration,
          excludeAppointmentId: appointment.id,
        }
      : skipToken,
  );

  // Debounced slot-availability check for the *new* slot — same 600ms pattern as BookAppointmentForm.
  // excludeAppointmentId keeps the appointment from conflicting with its own current slot, exactly as
  // the save excludes it (before E04b the check had no exclusion, so a move overlapping the old slot
  // was reported unavailable although the save would accept it).
  const [debouncedCheck, setDebouncedCheck] = useState<{
    artistId?: string; date: string; durationMinutes: number; excludeAppointmentId: string;
  } | null>(null);
  useEffect(() => {
    if (!open || !pickedIso || !newDuration) return;
    const timer = setTimeout(() => {
      setDebouncedCheck({
        artistId: appointment.artistId ?? undefined,
        date: pickedIso,
        durationMinutes: newDuration,
        excludeAppointmentId: appointment.id,
      });
    }, 600);
    return () => clearTimeout(timer);
  }, [open, pickedIso, newDuration, appointment.artistId, appointment.id]);

  const { data: slotStatus, isFetching: checkingSlot } = useCheckSlotAvailabilityQuery(debouncedCheck!, {
    skip: debouncedCheck === null || !open || !eligible,
  });

  async function handleSubmit() {
    if (!pickedIso) return;
    const result = await reschedule({
      id:                 appointment.id,
      newDate:            pickedIso,
      newDurationMinutes: newDuration,
      notes:              appointment.notes,
    });
    if ("data" in result) {
      toast.success("Appointment rescheduled.");
      onOpenChange(false);
    } else if (isSlotTaken(result.error)) {
      // Taken between the list and the save: keep the dialog and its choices, offer the nearest open times.
      setTakenIso(pickedIso);
      if (canListSlots) void slotsQuery.refetch();
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

        <div aria-live="polite">
          {eligibility.isFetching && !eligibility.data && (
            <p className="flex items-center gap-1.5 text-xs text-muted-foreground" role="status">
              <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
              Checking whether this appointment can be moved…
            </p>
          )}
          {eligibility.isError && !eligibility.isFetching && (
            <div className="space-y-2" role="alert">
              <p className="text-sm text-destructive-text">
                Couldn&apos;t check whether this appointment can be moved. Please try again.
              </p>
              <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={() => void eligibility.refetch()}>
                <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
                Try again
              </Button>
            </div>
          )}
          {eligibility.data && !eligibility.data.canReschedule && (
            <p className="flex items-start gap-1.5 text-sm text-destructive-text" role="alert">
              <AlertCircle className="h-4 w-4 mt-0.5 shrink-0" aria-hidden="true" />
              {eligibility.data.message ?? "This appointment can't be moved."}
            </p>
          )}
          {eligible && eligibility.data?.noticeWindowApplies && (
            <p className="text-xs text-muted-foreground">
              Appointments can be moved up to {eligibility.data.noticeWindowHours} hours before they start.
            </p>
          )}
        </div>

        {eligible && (
          <div className="space-y-3 py-2">
            <SlotPicker
              id="reschedule-slots"
              zone={zone}
              today={slotToday}
              rangeStart={rangeStart}
              onRangeStart={setRangeStart}
              query={canListSlots ? slotsQuery : null}
              waitingFor="Open times need the studio's time zone, which couldn't be loaded."
              selectedIso={pickedIso}
              onPick={(iso) => { if (studioTz) setEditedDate(toStudioInputValue(new Date(iso), studioTz)); }}
              takenIso={takenIso !== null && takenIso === pickedIso ? takenIso : null}
            />

            <div className="space-y-1.5">
              <Label htmlFor="reschedule-date">
                New date &amp; time {studioTz && <span className="font-normal text-muted-foreground">(studio time, {studioTz})</span>}
              </Label>
              {zone.status === "error" ? (
                <div className="flex flex-wrap items-center gap-2" role="alert">
                  <p className="text-sm text-destructive-text">
                    Couldn&apos;t load the studio&apos;s time zone, so a new time can&apos;t be picked safely.
                  </p>
                  <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={retryZone}>
                    <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
                    Try again
                  </Button>
                </div>
              ) : (
                <Input
                  id="reschedule-date"
                  type="datetime-local"
                  disabled={!studioTz}
                  min={studioTz ? toStudioInputValue(new Date(), studioTz) : undefined}
                  value={newDate}
                  onChange={(e) => setEditedDate(e.target.value)}
                  aria-describedby="reschedule-date-note"
                />
              )}
              <StudioDateTimeNote id="reschedule-date-note" zone={zone} value={newDate} showZoneError={false} />
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
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isLoading}>
            {eligibility.data && !eligibility.data.canReschedule ? "Close" : "Cancel"}
          </Button>
          <Button
            onClick={handleSubmit}
            disabled={isLoading || !eligible || !pickedIso || slotStatus?.available === false}
          >
            {isLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : "Confirm reschedule"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
