import { useState } from "react";
import { useNavigate, useParams, Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import {
  ArrowLeft,
  Calendar,
  ChevronRight,
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
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/shared/components/ui/tabs";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import {
  useGetArtistByIdQuery,
  useUpdateArtistMutation,
  useDeleteArtistMutation,
} from "../artistsApi";
import { useGetDesignsQuery } from "@/features/designs/designsApi";
import { useGetAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { AppointmentStatusBadge } from "@/features/appointments/components/AppointmentStatusBadge";

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

  const [isEditing,    setIsEditing]    = useState(false);
  const [deleteOpen,   setDeleteOpen]   = useState(false);

  const { data: designs = [], isLoading: designsLoading } =
    useGetDesignsQuery({ artistId: id! }, { skip: !id });

  const { data: allAppointments = [], isLoading: appsLoading } =
    useGetAppointmentsQuery({}, { skip: !id });

  const artistAppointments = allAppointments.filter((a) => a.artistId === id);

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
    setIsEditing(true);
  }

  async function onSave(values: EditFormValues) {
    if (!id) return;
    const result = await updateArtist({
      id,
      body: {
        firstName:       values.firstName,
        lastName:        values.lastName,
        email:           values.email,
        specializations: values.specializations?.trim() || null,
      },
    });
    if ("data" in result) {
      toast.success("Artist updated.");
      setIsEditing(false);
    } else {
      toast.error("Failed to update artist.");
    }
  }

  async function onDelete() {
    if (!id) return;
    const result = await deleteArtist(id);
    if ("error" in result) {
      toast.error("Failed to delete artist.");
      return;
    }
    navigate("/artists");
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-background">
        <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
          <Skeleton className="h-8 w-24" />
        </header>
        <main className="max-w-lg mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-14 w-14 rounded-full" />
          <Skeleton className="h-6 w-48" />
          <Skeleton className="h-24 w-full" />
        </main>
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

        {canManage && !isEditing && (
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
              <Pencil className="h-3.5 w-3.5" />
              Edit
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setDeleteOpen(true)}
              className="gap-1.5 text-destructive hover:text-destructive"
            >
              <Trash2 className="h-3.5 w-3.5" />
              Delete
            </Button>
          </div>
        )}

        {isEditing && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setIsEditing(false)}
            disabled={isSaving}
          >
            Cancel
          </Button>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-8 space-y-6">
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

        {isEditing ? (
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
        ) : (
          <Tabs defaultValue="profile">
            <TabsList className="w-full">
              <TabsTrigger value="profile" className="flex-1">Profile</TabsTrigger>
              <TabsTrigger value="schedule" className="flex-1">Schedule</TabsTrigger>
              <TabsTrigger value="designs" className="flex-1">Designs</TabsTrigger>
            </TabsList>

            {/* Profile tab */}
            <TabsContent value="profile" className="mt-4 space-y-4">
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

              {canManage && (
                <p className="text-xs text-muted-foreground text-center">
                  Last updated {formatDate(artist.updatedAt)}
                </p>
              )}
            </TabsContent>

            {/* Schedule tab */}
            <TabsContent value="schedule" className="mt-4 space-y-3">
              {appsLoading && (
                <div className="space-y-2">
                  {[1, 2, 3].map((i) => <Skeleton key={i} className="h-16 w-full" />)}
                </div>
              )}
              {!appsLoading && artistAppointments.length === 0 && (
                <p className="text-sm text-muted-foreground text-center py-8">
                  No appointments found.
                </p>
              )}
              {!appsLoading && artistAppointments.length > 0 && (
                <div className="space-y-2">
                  {artistAppointments.map((appt) => (
                    <Link
                      key={appt.id}
                      to={`/appointments/${appt.id}`}
                      className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
                    >
                      <Card className="hover:bg-muted/40 transition-colors">
                        <CardContent className="p-3 flex items-center justify-between gap-3">
                          <div className="space-y-1 min-w-0">
                            <p className="text-sm font-medium">
                              {formatDate(appt.date)}
                            </p>
                            <p className="text-xs text-muted-foreground">
                              {appt.durationMinutes} min
                            </p>
                          </div>
                          <div className="flex items-center gap-2 shrink-0">
                            <AppointmentStatusBadge status={appt.status} />
                            <ChevronRight className="h-4 w-4 text-muted-foreground" />
                          </div>
                        </CardContent>
                      </Card>
                    </Link>
                  ))}
                </div>
              )}
            </TabsContent>

            {/* Designs tab */}
            <TabsContent value="designs" className="mt-4 space-y-3">
              {designsLoading && (
                <div className="space-y-2">
                  {[1, 2, 3].map((i) => <Skeleton key={i} className="h-14 w-full" />)}
                </div>
              )}
              {!designsLoading && designs.length === 0 && (
                <p className="text-sm text-muted-foreground text-center py-8">
                  No designs found.
                </p>
              )}
              {!designsLoading && designs.length > 0 && (
                <div className="space-y-2">
                  {designs.map((design) => (
                    <Link
                      key={design.id}
                      to={`/designs/${design.id}`}
                      className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
                    >
                      <Card className="hover:bg-muted/40 transition-colors">
                        <CardContent className="p-3 flex items-center justify-between gap-3">
                          <div className="min-w-0">
                            <p className="text-sm font-medium truncate">{design.title}</p>
                            {design.description && (
                              <p className="text-xs text-muted-foreground line-clamp-1">
                                {design.description}
                              </p>
                            )}
                          </div>
                          <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
                        </CardContent>
                      </Card>
                    </Link>
                  ))}
                </div>
              )}
            </TabsContent>
          </Tabs>
        )}
      </main>

      {/* Delete confirmation dialog */}
      <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Delete {artist.firstName} {artist.lastName}?</DialogTitle>
            <DialogDescription>This action cannot be undone.</DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => setDeleteOpen(false)}
              disabled={isDeleting}
            >
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={onDelete}
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
    </div>
  );
}
