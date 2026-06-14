import { User } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetMyClientQuery } from "../clientsApi";

function getInitials(firstName: string, lastName: string): string {
  return `${firstName?.[0] ?? ""}${lastName?.[0] ?? ""}`.toUpperCase();
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
  const { data: client, isLoading, isError } = useGetMyClientQuery();

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

            <Card>
              <CardHeader className="pb-3">
                <CardTitle className="text-sm font-medium">Contact</CardTitle>
              </CardHeader>
              <CardContent className="space-y-3">
                <ProfileField label="Email"  value={client.email} />
                <ProfileField label="Phone"  value={client.phone} />
              </CardContent>
            </Card>
          </div>
        )}
      </main>
    </div>
  );
}
