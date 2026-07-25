import { useState } from "react";
import { Globe, Loader2, ShieldAlert } from "lucide-react";
import { toast } from "sonner";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { useUpdatePortableProfileOptInMutation } from "../clientsApi";

interface Props {
  currentOptIn: boolean;
}

export function PortableProfileToggle({ currentOptIn }: Props) {
  const [enabled, setEnabled] = useState(currentOptIn);
  const [update, { isLoading }] = useUpdatePortableProfileOptInMutation();

  async function handleToggle() {
    const next = !enabled;
    setEnabled(next);
    try {
      await update(next).unwrap();
      toast.success("Profile sharing updated.");
    } catch {
      setEnabled(!next);
      toast.error("Failed to update profile sharing.");
    }
  }

  return (
    <Card>
      <CardContent className="p-4 space-y-3">
        <div className="flex items-center gap-2">
          <Globe className="h-4 w-4 text-muted-foreground" />
          <h3 className="text-sm font-medium">Portable Tattoo Profile</h3>
        </div>

        <p className="text-xs text-muted-foreground">
          When enabled, any certified TattooOS artist can view your tattoo history
          before booking a session — no need to explain your existing work every time.
        </p>

        <div className="flex items-center justify-between gap-4">
          <p className="text-sm text-muted-foreground leading-snug">
            Allow any artist on TattooOS to view your tattoo history
          </p>
          <Button
            variant={enabled ? "default" : "outline"}
            size="sm"
            onClick={handleToggle}
            disabled={isLoading}
            className="shrink-0 min-w-[72px]"
          >
            {isLoading ? (
              <Loader2 className="h-4 w-4 animate-spin" />
            ) : enabled ? (
              "On"
            ) : (
              "Off"
            )}
          </Button>
        </div>

        {enabled && (
          <div className="flex items-start gap-2 rounded-md bg-amber-50 dark:bg-amber-950/30 border border-amber-200 dark:border-amber-800 p-3">
            <ShieldAlert className="h-4 w-4 text-amber-600 dark:text-amber-400 shrink-0 mt-0.5" />
            <p className="text-xs text-amber-700 dark:text-amber-300">
              Any artist on TattooOS will be able to view your tattoo history
              (body map locations, tattoo photos, and descriptions). Your contact
              information is never shared.
            </p>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
