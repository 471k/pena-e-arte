import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { CalendarDays, ImageIcon, Loader2, MapPin, Plus, Scroll } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import { useGetTattooRecordsQuery, useAddTattooRecordMutation } from "../clientsApi";
import type { TattooRecordResponse } from "../clientsApi";
import { ALL_BODY_ZONES, FRONT_ZONES, BACK_ZONES } from "./BodyMap";

const addSchema = z.object({
  description:  z.string().min(1, "Required").max(2000, "Max 2000 characters"),
  bodyLocation: z.string().min(1, "Required").max(200, "Max 200 characters"),
  artistId:     z.string().min(1, "Select an artist"),
  completedAt:  z.string().min(1, "Required"),
});

type AddFormValues = z.infer<typeof addSchema>;

function resolveLocation(id: string): string {
  return ALL_BODY_ZONES.find((z) => z.id === id)?.label ?? id;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function TattooRecordCard({
  record,
  artistName,
}: {
  record:     TattooRecordResponse;
  artistName: string;
}) {
  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-start justify-between gap-2">
          <p className="text-sm font-medium leading-snug flex-1">{record.description}</p>
          <span className="text-xs text-muted-foreground whitespace-nowrap shrink-0">
            {formatDate(record.completedAt)}
          </span>
        </div>

        <div className="flex flex-wrap gap-x-4 gap-y-1">
          <span className="flex items-center gap-1 text-xs text-muted-foreground">
            <MapPin className="h-3 w-3 shrink-0" />
            {resolveLocation(record.bodyLocation)}
          </span>
          <span className="flex items-center gap-1 text-xs text-muted-foreground">
            <CalendarDays className="h-3 w-3 shrink-0" />
            {artistName}
          </span>
          {record.photoUrls.length > 0 && (
            <span className="flex items-center gap-1 text-xs text-muted-foreground">
              <ImageIcon className="h-3 w-3 shrink-0" />
              {record.photoUrls.length} photo{record.photoUrls.length !== 1 ? "s" : ""}
            </span>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

interface TattooHistorySectionProps {
  clientId: string;
}

export function TattooHistorySection({ clientId }: TattooHistorySectionProps) {
  const canAdd = usePermission(Role.Artist);
  const [showForm, setShowForm] = useState(false);

  const { data: records = [], isLoading, isError } =
    useGetTattooRecordsQuery(clientId);

  const { data: artists = [] } = useGetArtistsQuery(undefined);

  const [addRecord, { isLoading: isAdding }] = useAddTattooRecordMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<AddFormValues>({ resolver: zodResolver(addSchema) });

  function artistName(artistId: string): string {
    const a = artists.find((x) => x.id === artistId);
    return a ? `${a.firstName} ${a.lastName}` : "Unknown artist";
  }

  async function onSubmit(values: AddFormValues) {
    const result = await addRecord({
      clientId,
      body: {
        artistId:      values.artistId,
        appointmentId: null,
        description:   values.description,
        bodyLocation:  values.bodyLocation,
        photoUrls:     [],
        completedAt:   new Date(values.completedAt + "T00:00:00Z").toISOString(),
      },
    });
    if ("data" in result) {
      reset();
      setShowForm(false);
    }
  }

  return (
    <Card>
      <CardContent className="p-4 space-y-4">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-1.5">
            <Scroll className="h-4 w-4 text-muted-foreground" />
            <h2 className="text-sm font-medium">Tattoo History</h2>
            {records.length > 0 && (
              <span className="text-xs text-muted-foreground ml-1">
                ({records.length})
              </span>
            )}
          </div>
          {canAdd && !showForm && (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setShowForm(true)}
              className="h-7 gap-1 text-xs px-2"
              data-testid="add-tattoo-record"
            >
              <Plus className="h-3 w-3" />
              Add
            </Button>
          )}
          {showForm && (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => { setShowForm(false); reset(); }}
              disabled={isAdding}
              className="h-7 text-xs px-2"
            >
              Cancel
            </Button>
          )}
        </div>

        {/* Add form */}
        {showForm && (
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-1 border-t">
            <h3 className="text-sm font-medium pt-2">New Record</h3>

            <div className="space-y-1.5">
              <Label htmlFor="tr-description">Description</Label>
              <textarea
                id="tr-description"
                rows={3}
                placeholder="Describe the tattoo…"
                {...register("description")}
                className={cn(
                  "flex w-full rounded-md border border-input bg-transparent px-3 py-2 text-sm shadow-xs placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring resize-none",
                  errors.description && "border-destructive",
                )}
              />
              {errors.description && (
                <p className="text-xs text-destructive">{errors.description.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="tr-bodyLocation">Body location</Label>
              <select
                id="tr-bodyLocation"
                {...register("bodyLocation")}
                className={cn(
                  "flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
                  errors.bodyLocation && "border-destructive",
                )}
              >
                <option value="">Select zone…</option>
                <optgroup label="Front">
                  {FRONT_ZONES.map((z) => (
                    <option key={z.id} value={z.id}>{z.label}</option>
                  ))}
                </optgroup>
                <optgroup label="Back">
                  {BACK_ZONES.map((z) => (
                    <option key={z.id} value={z.id}>{z.label}</option>
                  ))}
                </optgroup>
              </select>
              {errors.bodyLocation && (
                <p className="text-xs text-destructive">{errors.bodyLocation.message}</p>
              )}
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="tr-artistId">Artist</Label>
                <select
                  id="tr-artistId"
                  {...register("artistId")}
                  className={cn(
                    "flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
                    errors.artistId && "border-destructive",
                  )}
                >
                  <option value="">Select…</option>
                  {artists.map((a) => (
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
                <Label htmlFor="tr-completedAt">Date</Label>
                <Input
                  id="tr-completedAt"
                  type="date"
                  {...register("completedAt")}
                  className={cn(errors.completedAt && "border-destructive")}
                />
                {errors.completedAt && (
                  <p className="text-xs text-destructive">{errors.completedAt.message}</p>
                )}
              </div>
            </div>

            <Button type="submit" size="sm" className="w-full" disabled={isAdding}>
              {isAdding ? (
                <>
                  <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  Saving…
                </>
              ) : (
                "Save Record"
              )}
            </Button>
          </form>
        )}

        {/* List */}
        {isLoading && (
          <div className="flex items-center justify-center py-6 gap-2 text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" />
            <span className="text-sm">Loading…</span>
          </div>
        )}

        {isError && (
          <p className="text-sm text-destructive text-center py-4">
            Failed to load tattoo records.
          </p>
        )}

        {!isLoading && !isError && records.length === 0 && !showForm && (
          <p className="text-sm text-muted-foreground text-center py-4">
            No tattoo records yet.
          </p>
        )}

        {!isLoading && !isError && records.length > 0 && (
          <div className="space-y-2">
            {records.map((r) => (
              <TattooRecordCard
                key={r.id}
                record={r}
                artistName={artistName(r.artistId)}
              />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
