import { useState } from "react";
import { Link } from "react-router-dom";
import { Loader2, MapPin, Pencil, User } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
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
import { ContactDetailsCard } from "./ContactDetailsCard";
import { PortableProfileToggle } from "./PortableProfileToggle";
import { DeleteAccountSection } from "./DeleteAccountSection";

function getInitials(firstName: string, lastName: string): string {
  return `${firstName?.[0] ?? ""}${lastName?.[0] ?? ""}`.toUpperCase();
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

export function MyProfilePage() {
  useDocumentMeta({ title: "My Profile — TattooOS", canonical: "/clients/me" });

  const { data: client, isLoading, isError, error, refetch } = useGetMyClientQuery();
  const { data: profile, isLoading: profileLoading, isError: profileError } = useGetMyClientProfileQuery();
  const { data: tattoos = [], isLoading: tattoosLoading } = useGetMyTattooRecordsQuery();
  const [updateMyBodyMap, { isLoading: isSavingMap }] = useUpdateMyBodyMapMutation();

  // A 404 here means this client hasn't joined a studio yet — there is no per-studio
  // Client row to have a profile against (see RegisterUserHandler: a studio-less signup
  // gets no Client row until SwitchStudioHandler creates one on first booking). Not a
  // transient failure, so retrying can never succeed. Same pattern as MyEarningsPage /
  // ConsentFormDetailPage.
  const isNoStudioYet = isError && !!error && "status" in error && error.status === 404;

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

        {isNoStudioYet && (
          <div className="flex flex-col items-center justify-center gap-3 py-16 text-center">
            <User className="h-8 w-8 text-muted-foreground/40" aria-hidden="true" />
            <div className="space-y-1">
              <p className="text-sm font-medium">You haven&apos;t joined a studio yet</p>
              <p className="text-xs text-muted-foreground max-w-xs">
                Your profile is created the first time you book at a studio. Browse studios to get started.
              </p>
            </div>
            <Button asChild size="sm" className="bg-violet-600 hover:bg-violet-700 text-white">
              <Link to="/discover">Browse studios</Link>
            </Button>
          </div>
        )}

        {isError && !isNoStudioYet && (
          <div className="flex flex-col items-center justify-center gap-3 py-16 text-center">
            <p className="text-sm text-destructive-text">Failed to load profile.</p>
            <button
              type="button"
              onClick={() => refetch()}
              className="text-xs underline text-muted-foreground hover:text-foreground"
            >
              Try again
            </button>
          </div>
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
                <ContactDetailsCard client={client} />

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

              <TabsContent value="sharing" className="mt-4 space-y-4">
                {profileLoading && <Skeleton className="h-24 w-full rounded-lg" />}
                {!profileLoading && profile && (
                  <PortableProfileToggle currentOptIn={profile.allowCrossTenantRead} />
                )}
                {!profileLoading && profileError && (
                  <p className="text-sm text-muted-foreground text-center py-4">
                    Profile sharing settings are unavailable until a profile is created.
                  </p>
                )}

                <DeleteAccountSection />
              </TabsContent>
            </Tabs>
          </div>
        )}
      </main>
    </div>
  );
}
