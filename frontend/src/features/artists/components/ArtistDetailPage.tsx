import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  ArrowLeft,
  Calendar,
  Loader2,
  Mail,
  Pencil,
  Tag,
  Trash2,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import {
  useGetArtistByIdQuery,
  useUpdateArtistMutation,
  useDeleteArtistMutation,
} from "../artistsApi";

const editSchema = z.object({
  firstName:       z.string().min(1, "First name is required"),
  lastName:        z.string().min(1, "Last name is required"),
  email:           z.string().email("Invalid email"),
  specializations: z.string().optional(),
});

type EditFormValues = z.infer<typeof editSchema>;

function getInitials(firstName: string, lastName: string): string {
  return `${firstName[0] ?? ""}${lastName[0] ?? ""}`.toUpperCase();
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}

export function ArtistDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const canManage = usePermission(Role.Owner);

  const { data: artist, isLoading, isError } = useGetArtistByIdQuery(id!);
  const [updateArtist, { isLoading: isSaving }] = useUpdateArtistMutation();
  const [deleteArtist, { isLoading: isDeleting }] = useDeleteArtistMutation();

  const [mode, setMode] = useState<"view" | "edit" | "confirm-delete">("view");

  const {
    register,
    handleSubmit,
    formState: { errors },
    reset,
  } = useForm<EditFormValues>({
    resolver: zodResolver(editSchema),
  });

  function startEdit() {
    if (!artist) return;
    reset({
      firstName:       artist.firstName,
      lastName:        artist.lastName,
      email:           artist.email,
      specializations: artist.specializations ?? "",
    });
    setMode("edit");
  }

  async function onSave(values: EditFormValues) {
    if (!id) return;
    await updateArtist({
      id,
      body: {
        firstName:       values.firstName,
        lastName:        values.lastName,
        email:           values.email,
        specializations: values.specializations?.trim() || null,
      },
    });
    setMode("view");
  }

  async function onDelete() {
    if (!id) return;
    await deleteArtist(id);
    navigate("/artists");
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center gap-2 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span className="text-sm">Loading…</span>
      </div>
    );
  }

  if (isError || !artist) {
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-destructive">Artist not found.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate("/artists")}>
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back to Artists
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
          onClick={() => navigate("/artists")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Artists
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
      </header>

      <main className="max-w-lg mx-auto px-4 py-8 space-y-6">
        {mode === "view" && (
          <>
            <div className="flex items-center gap-4">
              <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-muted text-base font-semibold text-muted-foreground select-none">
                {getInitials(artist.firstName, artist.lastName)}
              </div>
              <div>
                <h1 className="text-lg font-semibold leading-tight">
                  {artist.firstName} {artist.lastName}
                </h1>
              </div>
            </div>

            <Card>
              <CardContent className="p-4 space-y-3">
                <div className="flex items-center gap-2 text-sm">
                  <Mail className="h-4 w-4 shrink-0 text-muted-foreground" />
                  <span>{artist.email}</span>
                </div>

                {artist.specializations && (
                  <div className="flex items-start gap-2 text-sm">
                    <Tag className="h-4 w-4 shrink-0 mt-0.5 text-muted-foreground" />
                    <span>{artist.specializations}</span>
                  </div>
                )}

                <div className="flex items-center gap-2 text-xs text-muted-foreground pt-1 border-t">
                  <Calendar className="h-3.5 w-3.5 shrink-0" />
                  <span>Joined {formatDate(artist.createdAt)}</span>
                </div>
              </CardContent>
            </Card>

            {mode === "view" && canManage && (
              <p className="text-xs text-muted-foreground text-center">
                Last updated {formatDate(artist.updatedAt)}
              </p>
            )}
          </>
        )}

        {mode === "edit" && (
          <form onSubmit={handleSubmit(onSave)} className="space-y-5">
            <h2 className="text-base font-semibold">Edit Artist</h2>

            <div className="grid grid-cols-2 gap-4">
              <div className="space-y-1.5">
                <Label htmlFor="firstName">First name</Label>
                <Input
                  id="firstName"
                  {...register("firstName")}
                  className={cn(errors.firstName && "border-destructive")}
                />
                {errors.firstName && (
                  <p className="text-xs text-destructive">{errors.firstName.message}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <Label htmlFor="lastName">Last name</Label>
                <Input
                  id="lastName"
                  {...register("lastName")}
                  className={cn(errors.lastName && "border-destructive")}
                />
                {errors.lastName && (
                  <p className="text-xs text-destructive">{errors.lastName.message}</p>
                )}
              </div>
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                {...register("email")}
                className={cn(errors.email && "border-destructive")}
              />
              {errors.email && (
                <p className="text-xs text-destructive">{errors.email.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="specializations">Specializations (optional)</Label>
              <Input
                id="specializations"
                placeholder="e.g. Traditional, Realism"
                {...register("specializations")}
              />
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

        {mode === "confirm-delete" && (
          <Card>
            <CardContent className="p-5 space-y-4">
              <p className="text-sm font-medium">
                Delete {artist.firstName} {artist.lastName}?
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
                    <>
                      <Loader2 className="h-4 w-4 animate-spin" />
                      Deleting…
                    </>
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
