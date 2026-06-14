import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Banknote, CheckCircle2, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { useAppSelector } from "@/app/hooks";
import { useCurrentUser } from "@/shared/hooks/useCurrentUser";
import { cn } from "@/shared/utils/cn";
import { Role } from "@/shared/types/roles";
import { useCreateAppointmentMutation } from "../appointmentsApi";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { useGetClientsQuery } from "@/features/clients/clientsApi";
import { PaymentMethodSelector } from "@/features/payments/components/PaymentMethodSelector";
import type { AppointmentResponse } from "../appointment.types";

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

export function BookAppointmentForm() {
  const user = useCurrentUser();
  const role = useAppSelector((s) => s.auth.role);

  const isClientRole = role === Role.Client;
  const isStaffRole  = role === Role.Artist || role === Role.Owner || role === Role.Issuer;

  const { data: artists, isLoading: loadingArtists } = useGetArtistsQuery(undefined);
  const { data: clients, isLoading: loadingClients }  = useGetClientsQuery(undefined, {
    skip: isClientRole,
  });

  const [createAppointment, { isLoading }] = useCreateAppointmentMutation();

  // Post-booking deposit step state
  const [booked,      setBooked]      = useState<AppointmentResponse | null>(null);
  const [depositDone, setDepositDone] = useState<"paid" | "cash" | "skipped" | null>(null);

  const {
    register,
    control,
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
    const result = await createAppointment({
      artistId:        values.artistId,
      clientId,
      date:            new Date(values.scheduledAt).toISOString(),
      durationMinutes: values.durationMinutes,
      notes:           values.notes ?? null,
    });
    if ("data" in result) {
      toast.success("Appointment requested.");
      setBooked(result.data);
    } else {
      toast.error("Failed to book appointment.");
    }
    resetForm({ durationMinutes: 60, clientId: isClientRole ? (user?.id ?? "") : "" });
  }

  function startOver() {
    setBooked(null);
    setDepositDone(null);
  }

  // Step 2 — deposit (clients only, when the appointment requires one)
  if (booked && isClientRole && booked.depositAmount > 0 && !depositDone) {
    return (
      <div className="space-y-4">
        <div className="text-center space-y-1">
          <p className="text-sm font-medium">Appointment requested!</p>
          <p className="text-xs text-muted-foreground">
            Secure your slot with a deposit of{" "}
            <span className="font-medium text-foreground">€{booked.depositAmount.toFixed(2)}</span>.
          </p>
        </div>

        <PaymentMethodSelector
          appointmentId={booked.id}
          amount={booked.depositAmount}
          onSuccess={(method) => setDepositDone(method === "cash" ? "cash" : "paid")}
          onError={(message) => toast.error(message)}
        />

        <button
          type="button"
          onClick={() => setDepositDone("skipped")}
          className="w-full text-xs text-muted-foreground underline underline-offset-4 hover:text-foreground"
        >
          I&apos;ll sort the deposit out later
        </button>
      </div>
    );
  }

  // Step 3 — confirmation
  if (booked) {
    return (
      <div className="text-center space-y-3 py-6">
        {depositDone === "skipped" || depositDone === null ? (
          <CheckCircle2 className="h-8 w-8 mx-auto text-green-500" />
        ) : depositDone === "cash" ? (
          <Banknote className="h-8 w-8 mx-auto text-green-500" />
        ) : (
          <CheckCircle2 className="h-8 w-8 mx-auto text-green-500" />
        )}
        <p className="text-sm font-medium">Appointment requested!</p>
        <p className="text-xs text-muted-foreground">
          {depositDone === "paid"
            ? "Your deposit is authorised — the artist will confirm soon."
            : depositDone === "cash"
            ? "Bring the deposit in cash to the studio. The artist will confirm soon."
            : depositDone === "skipped"
            ? "The studio will contact you about the deposit. The artist will confirm soon."
            : "The artist will confirm soon."}
        </p>
        <Button variant="outline" size="sm" onClick={startOver}>
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
        <Controller
          control={control}
          name="artistId"
          render={({ field }) => (
            <Select
              disabled={loadingArtists}
              value={field.value}
              onValueChange={field.onChange}
            >
              <SelectTrigger id="artistId" className={cn(errors.artistId && "border-destructive")}>
                <SelectValue placeholder={loadingArtists ? "Loading…" : "Select an artist"} />
              </SelectTrigger>
              <SelectContent>
                {artists?.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.firstName} {a.lastName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}
        />
        {errors.artistId && (
          <p className="text-xs text-destructive">{errors.artistId.message}</p>
        )}
      </div>

      {/* Client selector — visible for staff roles only */}
      {isStaffRole && (
        <div className="space-y-1.5">
          <Label htmlFor="clientId">Client</Label>
          <Controller
            control={control}
            name="clientId"
            render={({ field }) => (
              <Select
                disabled={loadingClients}
                value={field.value}
                onValueChange={field.onChange}
              >
                <SelectTrigger id="clientId" className={cn(errors.clientId && "border-destructive")}>
                  <SelectValue placeholder={loadingClients ? "Loading…" : "Select a client"} />
                </SelectTrigger>
                <SelectContent>
                  {clients?.map((c) => (
                    <SelectItem key={c.id} value={c.id}>
                      {c.firstName} {c.lastName}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          />
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
        <Textarea
          id="notes"
          rows={3}
          placeholder="Any details about the tattoo…"
          {...register("notes")}
          className="resize-none"
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
