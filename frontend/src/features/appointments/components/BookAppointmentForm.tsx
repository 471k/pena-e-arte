import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { useAppSelector } from "@/app/hooks";
import { cn } from "@/shared/utils/cn";
import { Role } from "@/shared/types/roles";
import { useCreateAppointmentMutation } from "../appointmentsApi";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { useGetClientsQuery } from "@/features/clients/clientsApi";

const schema = z.object({
  artistId:        z.string().min(1, "Select an artist"),
  clientId:        z.string().min(1, "Select a client"),
  scheduledAt:     z.string().min(1, "Select date and time").refine(
    (v) => new Date(v) > new Date(),
    "Appointment must be in the future"
  ),
  durationMinutes: z.number().int().min(30, "Minimum 30 minutes").max(480, "Maximum 8 hours"),
  notes:           z.string().optional(),
});

type FormValues = z.infer<typeof schema>;

const selectClass = cn(
  "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
  "ring-offset-background focus-visible:outline-none focus-visible:ring-2",
  "focus-visible:ring-ring focus-visible:ring-offset-2",
  "disabled:cursor-not-allowed disabled:opacity-50"
);

export function BookAppointmentForm() {
  const user = useAppSelector((s) => s.auth.user);
  const role = useAppSelector((s) => s.auth.role);

  const isClientRole  = role === Role.Client;
  const isStaffRole   = role === Role.Artist || role === Role.Owner || role === Role.Issuer;

  const { data: artists, isLoading: loadingArtists } = useGetArtistsQuery(undefined);
  const { data: clients, isLoading: loadingClients }  = useGetClientsQuery(undefined, {
    skip: isClientRole,
  });

  const [createAppointment, { isLoading, isSuccess, reset: resetMutation }] =
    useCreateAppointmentMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset: resetForm,
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      durationMinutes: 60,
      clientId: isClientRole ? (user?.id ?? "") : "",
    },
  });

  async function onSubmit(values: FormValues) {
    const clientId = isClientRole ? (user?.id ?? values.clientId) : values.clientId;
    await createAppointment({
      artistId:        values.artistId,
      clientId,
      date:            new Date(values.scheduledAt).toISOString(),
      durationMinutes: values.durationMinutes,
      notes:           values.notes ?? null,
    });
    resetForm({ durationMinutes: 60, clientId: isClientRole ? (user?.id ?? "") : "" });
  }

  if (isSuccess) {
    return (
      <div className="text-center space-y-3 py-6">
        <p className="text-sm font-medium">Appointment requested!</p>
        <p className="text-xs text-muted-foreground">The artist will confirm soon.</p>
        <Button variant="outline" size="sm" onClick={resetMutation}>
          Book another
        </Button>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
      {/* Artist selector */}
      <div className="space-y-1.5">
        <Label htmlFor="artistId">Artist</Label>
        <select
          id="artistId"
          disabled={loadingArtists}
          {...register("artistId")}
          className={cn(selectClass, errors.artistId && "border-destructive")}
        >
          <option value="">{loadingArtists ? "Loading…" : "Select an artist"}</option>
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

      {/* Client selector — visible for staff roles only */}
      {isStaffRole && (
        <div className="space-y-1.5">
          <Label htmlFor="clientId">Client</Label>
          <select
            id="clientId"
            disabled={loadingClients}
            {...register("clientId")}
            className={cn(selectClass, errors.clientId && "border-destructive")}
          >
            <option value="">{loadingClients ? "Loading…" : "Select a client"}</option>
            {clients?.map((c) => (
              <option key={c.id} value={c.id}>
                {c.firstName} {c.lastName}
              </option>
            ))}
          </select>
          {errors.clientId && (
            <p className="text-xs text-destructive">{errors.clientId.message}</p>
          )}
        </div>
      )}

      {/* Date & time */}
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

      {/* Duration */}
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

      {/* Notes */}
      <div className="space-y-1.5">
        <Label htmlFor="notes">Notes (optional)</Label>
        <textarea
          id="notes"
          rows={3}
          placeholder="Any details about the tattoo…"
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
