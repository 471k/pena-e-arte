import { useState } from "react";
import { Loader2, MapPin, Pencil, User } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/shared/components/ui/tabs";
import { ImageWithFallback } from "@/shared/components/ImageWithFallback";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import {
  useGetMyClientQuery,
  useGetMyClientProfileQuery,
  useGetMyTattooRecordsQuery,
  useUpdateMyBodyMapMutation,
} from "../clientsApi";
import { BodyMap } from "./BodyMap";
import { PortableProfileToggle } from "./PortableProfileToggle";

function getInitials(firstName: string, lastName: string): string {
  return `${firstName?.[0] ?? ""}${lastName?.[0] ?? ""}`.toUpperCase();
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

function ProfileField({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <div className="space-y-0.5">
      <p className="text-xs text-muted-foreground">{label}</p>
      <p className="text-sm font-medium">{value ?? <span className="text-muted-foreground">—</span>}</p>
    </div>
  );
}

export function MyProfilePage() {
  useDocumentMeta({ title: "My Profile — TattooOS", canonical: "/clients/me" });

  const { data: client, isLoading, isError } = useGetMyClientQuery();
  const { data: profile, isLoading: profileLoading, isError: profileError } = useGetMyClientProfileQuery();
  const { data: tattoos = [], isLoading: tattoosLoading } = useGetMyTattooRecordsQuery();
  const [updateMyBodyMap, { isLoading: isSavingMap }] = useUpdateMyBodyMapMutation();

  const [bodyMapMode,  setBodyMapMode]  = useState<"view" | "edit">("view");
  const [bodyMapDraft, setBodyMapDraft] = useState<string[]>([]);

  function startBodyMapEdit() {
    setBodyMapDraft(profile?.bodyMapLocations ?? []);
    setBodyMapMode("edit");
  }

  async function saveBodyMap() {
    const result = await updateMyBodyMap(bodyMapDraft);
    if ("error" in result) {
      toast.error("Failed to save body map.");
      return;
    }
    toast.success("Body map saved.");
    setBodyMapMode("view");
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <User className="h-5 w-5" />
        <span className="font-semibold tracking-tight">My Profile</span>
      </header>

      <main className="max-w-lg mx-auto px-4 py-6">
        {isLoading && (
          <div className="space-y-4">
            <div className="flex items-center gap-4">
              <Skeleton className="h-16 w-16 rounded-full" />
              <div className="space-y-2">
                <Skeleton className="h-5 w-40" />
                <Skeleton className="h-4 w-32" />
              </div>
            </div>
            <Skeleton className="h-32 w-full rounded-lg" />
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load profile. Please try again.
          </p>
        )}

        {!isLoading && !isError && client && (
          <div className="space-y-4">
            <div className="flex items-center gap-4">
              <div className="h-16 w-16 rounded-full bg-muted flex items-center justify-center text-xl font-semibold">
                {getInitials(client.firstName, client.lastName)}
              </div>
              <div>
                <p className="text-lg font-semibold">
                  {client.firstName} {client.lastName}
                </p>
                <p className="text-sm text-muted-foreground">{client.email}</p>
              </div>
            </div>

            <Tabs defaultValue="profile">
              <TabsList className="w-full">
                <TabsTrigger value="profile" className="flex-1">Profile</TabsTrigger>
                <TabsTrigger value="tattoos" className="flex-1">Tattoo History</TabsTrigger>
                <TabsTrigger value="sharing" className="flex-1">Sharing</TabsTrigger>
              </TabsList>

              <TabsContent value="profile" className="mt-4 space-y-4">
                <Card>
                  <CardHeader className="pb-3">
                    <CardTitle className="text-sm font-medium">Contact</CardTitle>
                  </CardHeader>
                  <CardContent className="space-y-3">
                    <ProfileField label="Email" value={client.email} />
                    <ProfileField label="Phone" value={client.phone} />
                  </CardContent>
                </Card>

                {profileLoading && <Skeleton className="h-32 w-full rounded-lg" />}

                {!profileLoading && profile && (
                  <Card>
                    <CardContent className="p-4 space-y-3">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-1.5">
                          <MapPin className="h-4 w-4 text-muted-foreground" />
                          <h2 className="text-sm font-medium">Body Map</h2>
                        </div>
                        {bodyMapMode === "view" ? (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={startBodyMapEdit}
                            className="h-7 gap-1 text-xs px-2"
                          >
                            <Pencil className="h-3 w-3" />
                            Edit
                          </Button>
                        ) : (
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
                              {isSavingMap
                                ? <Loader2 className="h-3 w-3 animate-spin" />
                                : "Save"}
                            </Button>
                          </div>
                        )}
                      </div>
                      <BodyMap
                        locations={bodyMapMode === "edit" ? bodyMapDraft : profile.bodyMapLocations}
                        readOnly={bodyMapMode === "view"}
                        onChange={bodyMapMode === "edit" ? setBodyMapDraft : undefined}
                      />
                    </CardContent>
                  </Card>
                )}

                {!profileLoading && profileError && (
                  <p className="text-sm text-muted-foreground text-center py-4">
                    No profile information yet.
                  </p>
                )}
              </TabsContent>

              <TabsContent value="tattoos" className="mt-4 space-y-3">
                {tattoosLoading && (
                  <div className="space-y-2">
                    {[1, 2, 3].map((i) => <Skeleton key={i} className="h-20 w-full" />)}
                  </div>
                )}
                {!tattoosLoading && tattoos.length === 0 && (
                  <p className="text-sm text-muted-foreground text-center py-8">
                    No tattoo history recorded yet.
                  </p>
                )}
                {!tattoosLoading && tattoos.map((record) => (
                  <Card key={record.id}>
                    <CardContent className="p-4 space-y-2">
                      <div className="flex items-center justify-between">
                        <p className="text-sm font-medium">{record.bodyLocation}</p>
                        <p className="text-xs text-muted-foreground">
                          {formatDate(record.completedAt)}
                        </p>
                      </div>
                      <p className="text-sm text-muted-foreground">{record.description}</p>
                      {record.photoUrls.length > 0 && (
                        <ImageWithFallback
                          src={record.photoUrls[0]}
                          alt="Tattoo"
                          className="w-full rounded-md object-cover max-h-48"
                        />
                      )}
                    </CardContent>
                  </Card>
                ))}
              </TabsContent>

              <TabsContent value="sharing" className="mt-4">
                {profileLoading && <Skeleton className="h-24 w-full rounded-lg" />}
                {!profileLoading && profile && (
                  <PortableProfileToggle currentOptIn={profile.allowCrossTenantRead} />
                )}
                {!profileLoading && profileError && (
                  <p className="text-sm text-muted-foreground text-center py-4">
                    Profile sharing settings are unavailable until a profile is created.
                  </p>
                )}
              </TabsContent>
            </Tabs>
          </div>
        )}
      </main>
    </div>
  );
}
