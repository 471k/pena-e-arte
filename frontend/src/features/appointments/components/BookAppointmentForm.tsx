import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { useAppSelector } from "@/app/hooks";
import { cn } from "@/shared/utils/cn";
import { useCreateAppointmentMutation } from "../appointmentsApi";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";

const schema = z.object({
  artistId:        z.string().min(1, "Select an artist"),
  scheduledAt:     z.string().min(1, "Select date and time").refine(
    (v) => new Date(v) > new Date(),
    "Appointment must be in the future"
  ),
  durationMinutes: z.number().int().min(30, "Minimum 30 minutes").max(480, "Maximum 8 hours"),
  notes:           z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

export function BookAppointmentForm() {
  const user = useAppSelector((s) => s.auth.user);
  const { data: artists, isLoading: loadingArtists } = useGetArtistsQuery();
  const [createAppointment, { isLoading, isSuccess, reset: resetMutation }] =
    useCreateAppointmentMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset: resetForm,
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { durationMinutes: 60 },
  });

  async function onSubmit(values: FormValues) {
    if (!user) return;
    await createAppointment({
      artistId:        values.artistId,
      clientId:        user.id,
      date:            new Date(values.scheduledAt).toISOString(),
      durationMinutes: values.durationMinutes,
      notes:           values.notes ?? null,
    });
    resetForm();
  }

  if (isSuccess) {
    return (
      <div className="text-center space-y-3 py-6">
        <p className="text-sm font-medium">Appointment requested!</p>
        <p className="text-xs text-muted-foreground">Your artist will confirm soon.</p>
        <Button variant="outline" size="sm" onClick={resetMutation}>
          Book another
        </Button>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
      <div className="space-y-1.5">
        <Label htmlFor="artistId">Artist</Label>
        <select
          id="artistId"
          disabled={loadingArtists}
          {...register("artistId")}
          className={cn(
            "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
            "ring-offset-background focus-visible:outline-none focus-visible:ring-2",
            "focus-visible:ring-ring focus-visible:ring-offset-2",
            "disabled:cursor-not-allowed disabled:opacity-50",
            errors.artistId && "border-destructive"
          )}
        >
          <option value="">
            {loadingArtists ? "Loading…" : "Select an artist"}
          </option>
          {artists?.map((a) => (
            <option key={a.id} value={a.id}>
              {a.firstName} {a.lastName}
            </option>
          ))}
        </select>
        {errors.artistId && (
          <p className="text-xs text-destructive">{errors.artistId.message}</p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="scheduledAt">Date &amp; Time</Label>
        <Input
          id="scheduledAt"
          type="datetime-local"
          min={new Date().toISOString().slice(0, 16)}
          {...register("scheduledAt")}
          className={cn(errors.scheduledAt && "border-destructive")}
        />
        {errors.scheduledAt && (
          <p className="text-xs text-destructive">{errors.scheduledAt.message}</p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="durationMinutes">Duration (min)</Label>
        <Input
          id="durationMinutes"
          type="number"
          min={30}
          max={480}
          step={30}
          {...register("durationMinutes", { valueAsNumber: true })}
          className={cn(errors.durationMinutes && "border-destructive")}
        />
        {errors.durationMinutes && (
          <p className="text-xs text-destructive">{errors.durationMinutes.message}</p>
        )}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor="notes">Notes (optional)</Label>
        <textarea
          id="notes"
          rows={3}
          placeholder="Any details about your tattoo…"
          {...register("notes")}
          className={cn(
            "flex min-h-[80px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
            "ring-offset-background placeholder:text-muted-foreground",
            "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
            "disabled:cursor-not-allowed disabled:opacity-50 resize-none"
          )}
        />
      </div>

      <Button type="submit" className="w-full" disabled={isLoading}>
        {isLoading ? (
          <>
            <Loader2 className="h-4 w-4 animate-spin" />
            Booking…
          </>
        ) : (
          "Request Appointment"
        )}
      </Button>
    </form>
  );
}
