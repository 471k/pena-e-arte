import { useState } from "react";
import { useNavigate, useParams, Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { ArrowLeft, Calendar, ChevronRight, Mail, Pencil, Phone, Loader2, MapPin } from "lucide-react";
import { Avatar, AvatarFallback } from "@/shared/components/ui/avatar";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Textarea } from "@/shared/components/ui/textarea";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/components/ui/tabs";
import { cn } from "@/shared/utils/cn";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import {
  useGetClientByIdQuery,
  useGetClientProfileQuery,
  useUpsertClientProfileMutation,
  useUpdateBodyMapMutation,
} from "../clientsApi";
import { useGetAppointmentsQuery } from "@/features/appointments/appointmentsApi";
import { useGetIntakeFormsQuery } from "@/features/forms/intakeFormsApi";
import { useGetConsentFormsQuery } from "@/features/forms/consentFormsApi";
import { AppointmentStatusBadge } from "@/features/appointments/components/AppointmentStatusBadge";
import { BodyMap } from "./BodyMap";
import { TattooHistorySection } from "./TattooHistorySection";
import { useGetPortableProfileQuery } from "../clientsApi";

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

  const { data: portableProfile } = useGetPortableProfileQuery(client?.userId ?? "", {
    skip: !client?.userId,
  });

  const { data: allAppointments = [], isLoading: appsLoading } =
    useGetAppointmentsQuery({}, { skip: !id });
  const clientAppointments = allAppointments.filter((a) => a.clientId === id);

  const { data: intakeForms = [], isLoading: intakeLoading } =
    useGetIntakeFormsQuery({ clientId: id! }, { skip: !id });

  const { data: consentForms = [], isLoading: consentLoading } =
    useGetConsentFormsQuery({ clientId: id! }, { skip: !id });

  const [isEditing,    setIsEditing]    = useState(false);
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
    setIsEditing(true);
  }

  async function onSave(values: ProfileFormValues) {
    if (!id) return;
    const result = await upsertProfile({
      clientId: id,
      body: {
        dateOfBirth:  values.dateOfBirth?.trim()  || null,
        medicalNotes: values.medicalNotes?.trim() || null,
        allergies:    values.allergies?.trim()    || null,
      },
    });
    if ("error" in result) {
      toast.error("Failed to save profile.");
      return;
    }
    toast.success("Profile saved.");
    setIsEditing(false);
  }

  function startBodyMapEdit() {
    setBodyMapDraft(profile?.bodyMapLocations ?? []);
    setBodyMapMode("edit");
  }

  async function saveBodyMap() {
    if (!id) return;
    const result = await updateBodyMap({ clientId: id, locations: bodyMapDraft });
    if ("error" in result) {
      toast.error("Failed to save body map.");
      return;
    }
    toast.success("Body map saved.");
    setBodyMapMode("view");
  }

  if (clientLoading || clientUninitialized || profileLoading || profileUninitialized) {
    return (
      <div className="min-h-screen bg-background" aria-label="Loading client">
        <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
          <Skeleton className="h-8 w-24" />
        </header>
        <main className="max-w-2xl mx-auto px-4 py-8 space-y-6">
          <div className="flex items-center gap-4 p-6">
            <Skeleton className="h-14 w-14 rounded-full" />
            <div className="space-y-1.5">
              <Skeleton className="h-5 w-36" />
              <Skeleton className="h-4 w-24" />
            </div>
          </div>
          <Skeleton className="h-9 w-full" />
          <div className="space-y-3">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-12 w-full rounded-lg" />
            ))}
          </div>
        </main>
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

        {canEdit && !isEditing && (
          <Button variant="outline" size="sm" onClick={startEdit} className="gap-1.5">
            <Pencil className="h-3.5 w-3.5" />
            {profileNotFound ? "Add Profile" : "Edit Profile"}
          </Button>
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
        {/* Client identity — always read-only */}
        <div className="flex items-center gap-4">
          <Avatar className="h-14 w-14 text-base">
            <AvatarFallback>{getInitials(client.firstName, client.lastName)}</AvatarFallback>
          </Avatar>
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

        {isEditing ? (
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
              <Textarea
                id="medicalNotes"
                rows={4}
                placeholder="e.g. blood thinners, skin conditions"
                {...register("medicalNotes")}
                className={cn("resize-none", errors.medicalNotes && "border-destructive")}
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
        ) : (
          <Tabs defaultValue="profile">
            <TabsList className="w-full">
              <TabsTrigger value="profile"  className="flex-1">Profile</TabsTrigger>
              <TabsTrigger value="tattoos"  className="flex-1">Tattoo History</TabsTrigger>
              {portableProfile && (
                <TabsTrigger value="cross-studio" className="flex-1">Cross-Studio</TabsTrigger>
              )}
              <TabsTrigger value="forms"    className="flex-1">Forms</TabsTrigger>
              <TabsTrigger value="appointments" className="flex-1">Appointments</TabsTrigger>
            </TabsList>

            {/* Profile tab */}
            <TabsContent value="profile" className="mt-4 space-y-4">
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

              {!profileNotFound && (
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
            </TabsContent>

            {/* Tattoo History tab */}
            <TabsContent value="tattoos" className="mt-4">
              <TattooHistorySection clientId={id!} />
            </TabsContent>

            {/* Cross-Studio History tab */}
            {portableProfile && (
              <TabsContent value="cross-studio" className="mt-4 space-y-4">
                <p className="text-xs text-muted-foreground">
                  Tattoo history shared by this client from other studios on Pena e Artë.
                </p>
                {portableProfile.tattooHistory.length === 0 ? (
                  <p className="text-sm text-muted-foreground text-center py-4">
                    No cross-studio history available.
                  </p>
                ) : (
                  portableProfile.tattooHistory.map((record, i) => (
                    <Card key={i}>
                      <CardContent className="p-4 space-y-2">
                        <div className="flex items-center justify-between">
                          <p className="text-sm font-medium">{record.bodyLocation}</p>
                          <p className="text-xs text-muted-foreground">
                            {new Date(record.completedAt).toLocaleDateString("en-GB", {
                              day: "numeric", month: "short", year: "numeric",
                            })}
                          </p>
                        </div>
                        <p className="text-xs text-muted-foreground">
                          by {record.artistFirstName} · {record.description}
                        </p>
                        {record.photoUrls.length > 0 && (
                          <img
                            src={record.photoUrls[0]}
                            alt="Tattoo"
                            className="w-full rounded-md object-cover max-h-48"
                          />
                        )}
                      </CardContent>
                    </Card>
                  ))
                )}
              </TabsContent>
            )}

            {/* Forms tab */}
            <TabsContent value="forms" className="mt-4 space-y-4">
              {/* Intake forms */}
              <div className="space-y-2">
                <h3 className="text-sm font-medium">Intake Forms</h3>
                {intakeLoading && <Skeleton className="h-14 w-full" />}
                {!intakeLoading && intakeForms.length === 0 && (
                  <p className="text-xs text-muted-foreground py-2">No intake forms.</p>
                )}
                {!intakeLoading && intakeForms.map((form) => (
                  <Link
                    key={form.id}
                    to={`/forms/intake/${form.id}`}
                    className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
                  >
                    <Card className="hover:bg-muted/40 transition-colors">
                      <CardContent className="p-3 flex items-center justify-between gap-3">
                        <p className="text-sm">{formatDate(form.submittedAt ?? form.createdAt)}</p>
                        <ChevronRight className="h-4 w-4 text-muted-foreground" />
                      </CardContent>
                    </Card>
                  </Link>
                ))}
              </div>

              {/* Consent forms */}
              <div className="space-y-2">
                <h3 className="text-sm font-medium">Consent Forms</h3>
                {consentLoading && <Skeleton className="h-14 w-full" />}
                {!consentLoading && consentForms.length === 0 && (
                  <p className="text-xs text-muted-foreground py-2">No consent forms.</p>
                )}
                {!consentLoading && consentForms.map((form) => (
                  <Link
                    key={form.id}
                    to={`/forms/consent/${form.id}`}
                    className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
                  >
                    <Card className="hover:bg-muted/40 transition-colors">
                      <CardContent className="p-3 flex items-center justify-between gap-3">
                        <p className="text-sm">{formatDate(form.signedAt ?? form.createdAt)}</p>
                        <ChevronRight className="h-4 w-4 text-muted-foreground" />
                      </CardContent>
                    </Card>
                  </Link>
                ))}
              </div>
            </TabsContent>

            {/* Appointments tab */}
            <TabsContent value="appointments" className="mt-4 space-y-2">
              {appsLoading && (
                <div className="space-y-2">
                  {[1, 2, 3].map((i) => <Skeleton key={i} className="h-16 w-full" />)}
                </div>
              )}
              {!appsLoading && clientAppointments.length === 0 && (
                <p className="text-sm text-muted-foreground text-center py-8">
                  No appointments found.
                </p>
              )}
              {!appsLoading && clientAppointments.map((appt) => (
                <Link
                  key={appt.id}
                  to={`/appointments/${appt.id}`}
                  className="block focus:outline-none focus-visible:ring-2 focus-visible:ring-ring rounded-lg"
                >
                  <Card className="hover:bg-muted/40 transition-colors">
                    <CardContent className="p-3 flex items-center justify-between gap-3">
                      <div className="space-y-1 min-w-0">
                        <p className="text-sm font-medium">{formatDate(appt.date)}</p>
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
            </TabsContent>
          </Tabs>
        )}
      </main>
    </div>
  );
}
