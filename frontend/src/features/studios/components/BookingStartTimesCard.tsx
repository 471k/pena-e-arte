import { Loader2, RotateCw } from "lucide-react";
import { toast } from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { useEditBuffer } from "@/shared/hooks/useEditBuffer";
import { extractErrorMessage } from "@/shared/utils/extractErrorMessage";
import {
  useGetMyBookingSettingsQuery, useUpdateMyBookingSettingsMutation, type BookingSettingsResponse,
} from "../studiosApi";

const SESSION_LENGTH = "session-length";

function stepLabel(minutes: number): string {
  return minutes === 60 ? "Every hour" : `Every ${minutes} minutes`;
}

const toForm = (data: BookingSettingsResponse): string =>
  data.slotStepMinutes === null ? SESSION_LENGTH : String(data.slotStepMinutes);

/**
 * How far apart the start times clients are offered are (C03 follow-up, Phi 20 Nov). "Every session length" is the
 * original behaviour: a 3-hour session after a 10:00 booking is offered 12:00 and 15:00, never 11:00. A shorter step
 * offers every quarter, half or full hour the studio's and artist's hours and bookings allow.
 */
export function BookingStartTimesCard() {
  const { currentData, isLoading, isError, refetch } = useGetMyBookingSettingsQuery();
  const [save, { isLoading: saving }] = useUpdateMyBookingSettingsMutation();
  const [form, setForm, reset] = useEditBuffer(currentData, currentData ? "booking-settings" : undefined, toForm);

  async function handleSave() {
    if (form === null) return;
    try {
      const saved = await save({ slotStepMinutes: form === SESSION_LENGTH ? null : Number(form) }).unwrap();
      reset(saved);
      toast.success("Start times saved.");
    } catch (err) {
      toast.error(extractErrorMessage(err, "Start times weren't saved."));
    }
  }

  const changed = currentData !== undefined && form !== null && form !== toForm(currentData);

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Booking start times</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {isLoading ? (
          <Skeleton className="h-9 w-56" />
        ) : isError || !currentData || !Array.isArray(currentData.allowedStepMinutes) || form === null ? (
          <div role="alert" className="flex flex-wrap items-center gap-2 text-sm">
            <p className="text-destructive-text">Couldn&apos;t load this setting.</p>
            <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={() => void refetch()}>
              <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
              Try again
            </Button>
          </div>
        ) : (
          <>
            <div className="space-y-1.5">
              <Label htmlFor="booking-start-step">Offer start times</Label>
              <Select value={form} onValueChange={setForm}>
                <SelectTrigger id="booking-start-step" className="w-64">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={SESSION_LENGTH}>Every session length (default)</SelectItem>
                  {currentData.allowedStepMinutes.map((m) => (
                    <SelectItem key={m} value={String(m)}>{stepLabel(m)}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <p className="text-xs text-muted-foreground">
              How far apart the open times clients see when booking are. With &quot;Every session length&quot;, a
              3-hour session is offered 3 hours apart; every 30 minutes lets it start at 11:00 or 11:30 too, whenever
              your hours, the artist&apos;s hours and other bookings allow. Clients can still type any time.
            </p>
            <Button type="button" size="sm" disabled={!changed || saving} onClick={() => void handleSave()}>
              {saving && <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
              Save start times
            </Button>
          </>
        )}
      </CardContent>
    </Card>
  );
}
