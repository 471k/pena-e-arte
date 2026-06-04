import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { ArrowLeft, Calendar, Mail, Pencil, Phone, Loader2, MapPin } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import {
  useGetClientByIdQuery,
  useGetClientProfileQuery,
  useUpsertClientProfileMutation,
  useUpdateBodyMapMutation,
} from "../clientsApi";
import { BodyMap } from "./BodyMap";
import { TattooHistorySection } from "./TattooHistorySection";

const profileSchema = z.object({
  dateOfBirth:  z.string().optional(),
  medicalNotes: z.string().max(4000, "Max 4000 characters").optional(),
  allergies:    z.string().max(1000, "Max 1000 characters").optional(),
});

type ProfileFormValues = z.infer<typeof profileSchema>;

function getInitials(firstName: string, lastName: string): string {
  return `${firstName?.[0] ?? ""}${lastName?.[0] ?? ""}`.toUpperCase();
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

export function ClientDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const canEdit = usePermission(Role.Artist);

  const {
    data: client,
    isLoading: clientLoading,
    isUninitialized: clientUninitialized,
    isError: clientError,
  } = useGetClientByIdQuery(id!);

  const {
    data: profile,
    isLoading: profileLoading,
    isUninitialized: profileUninitialized,
    error: profileError,
  } = useGetClientProfileQuery(id!, { skip: !id });

  const profileNotFound =
    profileError && "status" in profileError && profileError.status === 404;

  const [upsertProfile,  { isLoading: isSaving }]    = useUpsertClientProfileMutation();
  const [updateBodyMap, { isLoading: isSavingMap }] = useUpdateBodyMapMutation();

  const [mode,         setMode]         = useState<"view" | "edit">("view");
  const [bodyMapMode,  setBodyMapMode]  = useState<"view" | "edit">("view");
  const [bodyMapDraft, setBodyMapDraft] = useState<string[]>([]);

  const { register, handleSubmit, formState: { errors }, reset } =
    useForm<ProfileFormValues>({ resolver: zodResolver(profileSchema) });

  function startEdit() {
    reset({
      dateOfBirth:  profile?.dateOfBirth  ?? "",
      medicalNotes: profile?.medicalNotes ?? "",
      allergies:    profile?.allergies    ?? "",
    });
    setMode("edit");
  }

  async function onSave(values: ProfileFormValues) {
    if (!id) return;
    await upsertProfile({
      clientId: id,
      body: {
        dateOfBirth:  values.dateOfBirth?.trim()  || null,
        medicalNotes: values.medicalNotes?.trim() || null,
        allergies:    values.allergies?.trim()    || null,
      },
    });
    setMode("view");
  }

  function startBodyMapEdit() {
    setBodyMapDraft(profile?.bodyMapLocations ?? []);
    setBodyMapMode("edit");
  }

  async function saveBodyMap() {
    if (!id) return;
    await updateBodyMap({ clientId: id, locations: bodyMapDraft });
    setBodyMapMode("view");
  }

  if (clientLoading || clientUninitialized || profileLoading || profileUninitialized) {
    return (
      <div className="min-h-screen bg-background flex items-center justify-center gap-2 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
        <span className="text-sm">Loading…</span>
      </div>
    );
  }

  if (clientError || !client) {
    return (
      <div className="min-h-screen bg-background flex flex-col items-center justify-center gap-4">
        <p className="text-sm text-destructive">Client not found.</p>
        <Button variant="ghost" size="sm" onClick={() => navigate("/clients")}>
          <ArrowLeft className="h-4 w-4 mr-1" />
          Back to Clients
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
          onClick={() => navigate("/clients")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Clients
        </Button>

        {canEdit && mode === "view" && (
          <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
            <Pencil className="h-3.5 w-3.5" />
            {profileNotFound ? "Add Profile" : "Edit Profile"}
          </Button>
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
        {/* Client identity — always read-only */}
        <div className="flex items-center gap-4">
          <div className="flex h-14 w-14 shrink-0 items-center justify-center rounded-full bg-muted text-base font-semibold text-muted-foreground select-none">
            {getInitials(client.firstName, client.lastName)}
          </div>
          <div>
            <h1 className="text-lg font-semibold leading-tight">
              {client.firstName} {client.lastName}
            </h1>
          </div>
        </div>

        <Card>
          <CardContent className="p-4 space-y-3">
            <div className="flex items-center gap-2 text-sm">
              <Mail className="h-4 w-4 shrink-0 text-muted-foreground" />
              <span>{client.email}</span>
            </div>
            {client.phone && (
              <div className="flex items-center gap-2 text-sm">
                <Phone className="h-4 w-4 shrink-0 text-muted-foreground" />
                <span>{client.phone}</span>
              </div>
            )}
            <div className="flex items-center gap-2 text-xs text-muted-foreground pt-1 border-t">
              <Calendar className="h-3.5 w-3.5 shrink-0" />
              <span>Client since {formatDate(client.createdAt)}</span>
            </div>
          </CardContent>
        </Card>

        {/* Profile section */}
        {mode === "view" && (
          <>
            {profileNotFound ? (
              <p className="text-sm text-muted-foreground text-center py-4">
                No profile information yet.
              </p>
            ) : (
              <Card>
                <CardContent className="p-4 space-y-3">
                  <h2 className="text-sm font-medium">Health &amp; Profile</h2>
                  {profile?.dateOfBirth && (
                    <div className="space-y-0.5">
                      <p className="text-xs text-muted-foreground">Date of birth</p>
                      <p className="text-sm">{profile.dateOfBirth}</p>
                    </div>
                  )}
                  {profile?.allergies && (
                    <div className="space-y-0.5">
                      <p className="text-xs text-muted-foreground">Allergies</p>
                      <p className="text-sm whitespace-pre-wrap">{profile.allergies}</p>
                    </div>
                  )}
                  {profile?.medicalNotes && (
                    <div className="space-y-0.5">
                      <p className="text-xs text-muted-foreground">Medical notes</p>
                      <p className="text-sm whitespace-pre-wrap">{profile.medicalNotes}</p>
                    </div>
                  )}
                  {!profile?.dateOfBirth && !profile?.allergies && !profile?.medicalNotes && (
                    <p className="text-sm text-muted-foreground">No details recorded.</p>
                  )}
                </CardContent>
              </Card>
            )}
          </>
        )}

        {/* Body Map — only when profile exists and not editing profile fields */}
        {mode === "view" && !profileNotFound && (
          <Card>
            <CardContent className="p-4 space-y-3">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-1.5">
                  <MapPin className="h-4 w-4 text-muted-foreground" />
                  <h2 className="text-sm font-medium">Body Map</h2>
                </div>
                {canEdit && bodyMapMode === "view" && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={startBodyMapEdit}
                    className="h-7 gap-1 text-xs px-2"
                    data-testid="edit-body-map"
                  >
                    <Pencil className="h-3 w-3" />
                    Edit
                  </Button>
                )}
                {bodyMapMode === "edit" && (
                  <div className="flex items-center gap-1.5">
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => setBodyMapMode("view")}
                      disabled={isSavingMap}
                      className="h-7 text-xs px-2"
                    >
                      Cancel
                    </Button>
                    <Button
                      size="sm"
                      onClick={saveBodyMap}
                      disabled={isSavingMap}
                      className="h-7 text-xs px-3"
                    >
                      {isSavingMap ? (
                        <Loader2 className="h-3 w-3 animate-spin" />
                      ) : (
                        "Save"
                      )}
                    </Button>
                  </div>
                )}
              </div>

              <BodyMap
                locations={bodyMapMode === "edit" ? bodyMapDraft : (profile?.bodyMapLocations ?? [])}
                readOnly={bodyMapMode === "view"}
                onChange={bodyMapMode === "edit" ? setBodyMapDraft : undefined}
              />
            </CardContent>
          </Card>
        )}

        {/* Tattoo History — always shown, independent of profile */}
        {mode === "view" && (
          <TattooHistorySection clientId={id!} />
        )}

        {mode === "edit" && (
          <form onSubmit={handleSubmit(onSave)} className="space-y-5">
            <h2 className="text-base font-semibold">
              {profileNotFound ? "Add Profile" : "Edit Profile"}
            </h2>

            <div className="space-y-1.5">
              <Label htmlFor="dateOfBirth">Date of birth (optional)</Label>
              <Input
                id="dateOfBirth"
                type="date"
                {...register("dateOfBirth")}
                className={cn(errors.dateOfBirth && "border-destructive")}
              />
              {errors.dateOfBirth && (
                <p className="text-xs text-destructive">{errors.dateOfBirth.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="allergies">Allergies (optional)</Label>
              <Input
                id="allergies"
                placeholder="e.g. latex, nickel"
                {...register("allergies")}
                className={cn(errors.allergies && "border-destructive")}
              />
              {errors.allergies && (
                <p className="text-xs text-destructive">{errors.allergies.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="medicalNotes">Medical notes (optional)</Label>
              <textarea
                id="medicalNotes"
                rows={4}
                placeholder="e.g. blood thinners, skin conditions"
                {...register("medicalNotes")}
                className={cn(
                  "flex w-full rounded-md border border-input bg-transparent px-3 py-2 text-sm shadow-xs placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring resize-none",
                  errors.medicalNotes && "border-destructive",
                )}
              />
              {errors.medicalNotes && (
                <p className="text-xs text-destructive">{errors.medicalNotes.message}</p>
              )}
            </div>

            <Button type="submit" className="w-full" disabled={isSaving}>
              {isSaving ? (
                <>
                  <Loader2 className="h-4 w-4 animate-spin" />
                  Saving…
                </>
              ) : (
                "Save Profile"
              )}
            </Button>
          </form>
        )}
      </main>
    </div>
  );
}
