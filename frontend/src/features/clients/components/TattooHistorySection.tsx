import { useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { Link } from "react-router-dom";
import { CalendarDays, ChevronRight, ImageIcon, Loader2, MapPin, Plus, Scroll, Trash2, X } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import {
  useGetTattooRecordsQuery,
  useAddTattooRecordMutation,
  useDeleteTattooRecordMutation,
} from "../clientsApi";
import type { TattooRecordResponse } from "../clientsApi";
import { ALL_BODY_ZONES, FRONT_ZONES, BACK_ZONES } from "./BodyMap";
import { FileUploadField, IMAGE_ACCEPTED_TYPES } from "@/shared/components/FileUploadField";

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
  clientId,
  artistName,
  canDelete,
  onDeleteRequest,
}: {
  record:          TattooRecordResponse;
  clientId:        string;
  artistName:      string;
  canDelete:       boolean;
  onDeleteRequest: (id: string) => void;
}) {
  return (
    <div className="relative group">
      <Link
        to={`/clients/${clientId}/tattoos/${record.id}`}
        className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
      >
        <Card className="hover:bg-muted/40 transition-colors">
          <CardContent className="p-4 flex items-start gap-3">
            <div className="flex-1 space-y-1.5 min-w-0">
              <div className="flex items-start justify-between gap-2">
                <p className="text-sm font-medium leading-snug line-clamp-2">{record.description}</p>
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
            </div>

            <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground self-center" />
          </CardContent>
        </Card>
      </Link>

      {canDelete && (
        <button
          type="button"
          onClick={(e) => { e.preventDefault(); onDeleteRequest(record.id); }}
          className="absolute top-2 right-8 opacity-0 group-hover:opacity-100 transition-opacity p-1 rounded text-muted-foreground hover:text-destructive"
          aria-label="Delete tattoo record"
        >
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      )}
    </div>
  );
}

interface TattooHistorySectionProps {
  clientId: string;
}

export function TattooHistorySection({ clientId }: TattooHistorySectionProps) {
  const canAdd    = usePermission(Role.Artist);
  const canDelete = usePermission(Role.Owner);
  const [showForm, setShowForm]  = useState(false);
  const [deleteId, setDeleteId]  = useState<string | null>(null);

  const { data: records = [], isLoading, isError } =
    useGetTattooRecordsQuery(clientId);

  const { data: artists = [] } = useGetArtistsQuery(undefined);

  const [addRecord,    { isLoading: isAdding }]   = useAddTattooRecordMutation();
  const [deleteRecord, { isLoading: isDeleting }] = useDeleteTattooRecordMutation();
  const [photoUrls, setPhotoUrls] = useState<string[]>([]);

  const {
    register,
    control,
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
        photoUrls,
        completedAt:   new Date(values.completedAt + "T00:00:00Z").toISOString(),
      },
    });
    if ("data" in result) {
      toast.success("Tattoo record added.");
      reset();
      setPhotoUrls([]);
      setShowForm(false);
    } else {
      toast.error("Failed to add tattoo record.");
    }
  }

  async function confirmDelete() {
    if (!deleteId) return;
    const result = await deleteRecord({ clientId, tattooId: deleteId });
    if ("error" in result) {
      toast.error("Failed to delete tattoo record.");
    } else {
      toast.success("Tattoo record deleted.");
    }
    setDeleteId(null);
  }

  return (
    <>
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
                onClick={() => { setShowForm(false); reset(); setPhotoUrls([]); }}
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
                <Textarea
                  id="tr-description"
                  rows={3}
                  placeholder="Describe the tattoo…"
                  {...register("description")}
                  className={cn("resize-none", errors.description && "border-destructive")}
                />
                {errors.description && (
                  <p className="text-xs text-destructive">{errors.description.message}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="tr-bodyLocation">Body location</Label>
                <Controller
                  control={control}
                  name="bodyLocation"
                  render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger
                        id="tr-bodyLocation"
                        className={cn(errors.bodyLocation && "border-destructive")}
                      >
                        <SelectValue placeholder="Select zone…" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          <SelectLabel>Front</SelectLabel>
                          {FRONT_ZONES.map((z) => (
                            <SelectItem key={z.id} value={z.id}>{z.label}</SelectItem>
                          ))}
                        </SelectGroup>
                        <SelectGroup>
                          <SelectLabel>Back</SelectLabel>
                          {BACK_ZONES.map((z) => (
                            <SelectItem key={z.id} value={z.id}>{z.label}</SelectItem>
                          ))}
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                  )}
                />
                {errors.bodyLocation && (
                  <p className="text-xs text-destructive">{errors.bodyLocation.message}</p>
                )}
              </div>

              <div className="grid grid-cols-2 gap-4">
                <div className="space-y-1.5">
                  <Label htmlFor="tr-artistId">Artist</Label>
                  <Controller
                    control={control}
                    name="artistId"
                    render={({ field }) => (
                      <Select value={field.value} onValueChange={field.onChange}>
                        <SelectTrigger
                          id="tr-artistId"
                          className={cn(errors.artistId && "border-destructive")}
                        >
                          <SelectValue placeholder="Select…" />
                        </SelectTrigger>
                        <SelectContent>
                          {artists.map((a) => (
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

              <div className="space-y-1.5">
                <FileUploadField
                  acceptedTypes={IMAGE_ACCEPTED_TYPES}
                  keyPrefix={`clients/${clientId}/photos`}
                  label="Photos (optional)"
                  disabled={isAdding}
                  onUploaded={(url) => setPhotoUrls((prev) => [...prev, url])}
                />
                {photoUrls.length > 0 && (
                  <ul className="space-y-1">
                    {photoUrls.map((url, i) => (
                      <li key={url} className="flex items-center justify-between gap-2">
                        <span className="text-xs text-muted-foreground truncate">
                          Photo {i + 1}
                        </span>
                        <button
                          type="button"
                          onClick={() => setPhotoUrls((prev) => prev.filter((_, j) => j !== i))}
                          className="shrink-0 text-muted-foreground hover:text-destructive"
                          aria-label="Remove photo"
                        >
                          <X className="h-3.5 w-3.5" />
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
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
                  clientId={clientId}
                  artistName={artistName(r.artistId)}
                  canDelete={canDelete}
                  onDeleteRequest={setDeleteId}
                />
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Delete confirmation dialog */}
      <Dialog open={deleteId !== null} onOpenChange={(open) => { if (!open) setDeleteId(null); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete tattoo record?</DialogTitle>
            <DialogDescription>
              This will permanently remove the tattoo record. This action cannot be undone.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setDeleteId(null)}
              disabled={isDeleting}
            >
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={confirmDelete}
              disabled={isDeleting}
            >
              {isDeleting ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Deleting…
                </>
              ) : (
                "Delete"
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
