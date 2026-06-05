import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  ArrowLeft, CalendarDays, ImageIcon, Loader2, MapPin, Pencil, Trash2, X,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Label } from "@/shared/components/ui/label";
import { Input } from "@/shared/components/ui/input";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useGetArtistsQuery } from "@/features/artists/artistsApi";
import {
  useGetTattooRecordQuery,
  useUpdateTattooRecordMutation,
  useDeleteTattooRecordMutation,
} from "../clientsApi";
import { ALL_BODY_ZONES, FRONT_ZONES, BACK_ZONES } from "./BodyMap";
import { FileUploadField, IMAGE_ACCEPTED_TYPES } from "@/shared/components/FileUploadField";

const editSchema = z.object({
  description:  z.string().min(1, "Required").max(2000, "Max 2000 characters"),
  bodyLocation: z.string().min(1, "Select a zone"),
  completedAt:  z.string().min(1, "Required"),
});

type EditFormValues = z.infer<typeof editSchema>;

function resolveLocation(id: string): string {
  return ALL_BODY_ZONES.find((z) => z.id === id)?.label ?? id;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "long", year: "numeric",
  });
}

function toDateInput(iso: string): string {
  return iso.slice(0, 10);
}

export function TattooRecordDetailPage() {
  const { id: clientId, tattooId } = useParams<{ id: string; tattooId: string }>();
  const navigate = useNavigate();
  const canManage = usePermission(Role.Artist);

  const {
    data: record,
    isLoading,
    isUninitialized,
    isError,
  } = useGetTattooRecordQuery({ clientId: clientId!, tattooId: tattooId! });

  const { data: artists = [] } = useGetArtistsQuery(undefined);

  const [updateRecord, { isLoading: isSaving }]  = useUpdateTattooRecordMutation();
  const [deleteRecord, { isLoading: isDeleting }] = useDeleteTattooRecordMutation();

  const [mode, setMode] = useState<"view" | "edit" | "confirm-delete">("view");
  const [newPhotoUrls, setNewPhotoUrls] = useState<string[]>([]);

  const { register, handleSubmit, formState: { errors }, reset } =
    useForm<EditFormValues>({ resolver: zodResolver(editSchema) });

  function artistName(artistId: string): string {
    const a = artists.find((x) => x.id === artistId);
    return a ? `${a.firstName} ${a.lastName}` : "Unknown artist";
  }

  function startEdit() {
    if (!record) return;
    reset({
      description:  record.description,
      bodyLocation: record.bodyLocation,
      completedAt:  toDateInput(record.completedAt),
    });
    setNewPhotoUrls([]);
    setMode("edit");
  }

  async function onSave(values: EditFormValues) {
    if (!clientId || !tattooId || !record) return;
    await updateRecord({
      clientId,
      tattooId,
      body: {
        description:  values.description,
        bodyLocation: values.bodyLocation,
        photoUrls:    [...record.photoUrls, ...newPhotoUrls],
        completedAt:  new Date(values.completedAt + "T00:00:00Z").toISOString(),
      },
    });
    setMode("view");
  }

  async function onDelete() {
    if (!clientId || !tattooId) return;
    await deleteRecord({ clientId, tattooId });
    navigate(`/clients/${clientId}`);
  }

  if (isLoading || isUninitialized) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center gap-2 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span className="text-sm">Loading…</span>
      </div>
    );
  }

  if (isError || !record) {
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-destructive">Tattoo record not found.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate(-1)}>
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back
        </Button>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate(`/clients/${clientId}`)}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Client
        </Button>

        {canManage && mode === "view" && (
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setMode("confirm-delete")}
              className="gap-1.5 text-destructive hover:text-destructive"
            >
              <Trash2 className="h-3.5 w-3.5" />
              Delete
            </Button>
          </div>
        )}

        {mode === "edit" && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setMode("view")}
            disabled={isSaving}
          >
            Cancel
          </Button>
        )}

        {mode === "confirm-delete" && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setMode("view")}
            disabled={isDeleting}
          >
            Cancel
          </Button>
        )}
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">

        {/* View mode */}
        {mode === "view" && (
          <>
            <div className="space-y-1">
              <p className="text-xs text-muted-foreground">
                {formatDate(record.completedAt)}
              </p>
              <h1 className="text-lg font-semibold leading-snug">
                {record.description}
              </h1>
            </div>

            <Card>
              <CardContent className="p-4 space-y-3">
                <div className="flex items-center gap-2 text-sm">
                  <MapPin className="h-4 w-4 shrink-0 text-muted-foreground" />
                  <span>{resolveLocation(record.bodyLocation)}</span>
                </div>

                <div className="flex items-center gap-2 text-sm">
                  <CalendarDays className="h-4 w-4 shrink-0 text-muted-foreground" />
                  <span>{artistName(record.artistId)}</span>
                </div>

                {record.photoUrls.length > 0 && (
                  <div className="space-y-1 pt-1 border-t">
                    <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
                      <ImageIcon className="h-3.5 w-3.5" />
                      {record.photoUrls.length} photo{record.photoUrls.length !== 1 ? "s" : ""}
                    </p>
                    <ul className="space-y-0.5">
                      {record.photoUrls.map((url, i) => (
                        <li key={i}>
                          <a
                            href={url}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="text-xs text-primary hover:underline break-all"
                          >
                            Photo {i + 1}
                          </a>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </CardContent>
            </Card>
          </>
        )}

        {/* Edit mode */}
        {mode === "edit" && (
          <form onSubmit={handleSubmit(onSave)} className="space-y-5">
            <h2 className="text-base font-semibold">Edit Record</h2>

            <div className="space-y-1.5">
              <Label htmlFor="tr-edit-description">Description</Label>
              <textarea
                id="tr-edit-description"
                rows={4}
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
              <Label htmlFor="tr-edit-bodyLocation">Body location</Label>
              <select
                id="tr-edit-bodyLocation"
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

            <div className="space-y-1.5">
              <Label htmlFor="tr-edit-completedAt">Date completed</Label>
              <Input
                id="tr-edit-completedAt"
                type="date"
                {...register("completedAt")}
                className={cn(errors.completedAt && "border-destructive")}
              />
              {errors.completedAt && (
                <p className="text-xs text-destructive">{errors.completedAt.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              {record.photoUrls.length > 0 && (
                <p className="text-xs text-muted-foreground">
                  {record.photoUrls.length} existing photo{record.photoUrls.length !== 1 ? "s" : ""} kept.
                </p>
              )}
              <FileUploadField
                acceptedTypes={IMAGE_ACCEPTED_TYPES}
                keyPrefix={`clients/${clientId}/photos`}
                label="Add photos"
                disabled={isSaving}
                onUploaded={(url) => setNewPhotoUrls((prev) => [...prev, url])}
              />
              {newPhotoUrls.length > 0 && (
                <ul className="space-y-1">
                  {newPhotoUrls.map((url, i) => (
                    <li key={url} className="flex items-center justify-between gap-2">
                      <span className="text-xs text-muted-foreground truncate">
                        New photo {i + 1}
                      </span>
                      <button
                        type="button"
                        onClick={() => setNewPhotoUrls((prev) => prev.filter((_, j) => j !== i))}
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

            <Button type="submit" className="w-full" disabled={isSaving}>
              {isSaving ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Saving…
                </>
              ) : (
                "Save Changes"
              )}
            </Button>
          </form>
        )}

        {/* Confirm delete */}
        {mode === "confirm-delete" && (
          <Card>
            <CardContent className="p-5 space-y-4">
              <p className="text-sm font-medium">Delete this tattoo record?</p>
              <p className="text-xs text-muted-foreground">
                {record.description}
              </p>
              <p className="text-xs text-muted-foreground">
                This action cannot be undone.
              </p>
              <div className="flex gap-2">
                <Button
                  variant="destructive"
                  size="sm"
                  disabled={isDeleting}
                  onClick={onDelete}
                  className="flex-1"
                >
                  {isDeleting ? (
                    <Loader2 className="h-4 w-4 animate-spin" />
                  ) : (
                    "Delete"
                  )}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  disabled={isDeleting}
                  onClick={() => setMode("view")}
                  className="flex-1"
                >
                  Cancel
                </Button>
              </div>
            </CardContent>
          </Card>
        )}
      </main>
    </div>
  );
}
