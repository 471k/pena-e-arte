import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Badge } from "@/shared/components/ui/badge";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMyStudioQuery, useUpdateStudioBrandingMutation } from "../studiosApi";

export function BrandingSettingsCard() {
  const { data: studio } = useGetMyStudioQuery();
  const [updateBranding, { isLoading }] = useUpdateStudioBrandingMutation();

  if (!studio) return null;

  async function handleToggle() {
    if (!studio) return;
    try {
      await updateBranding({
        id: studio.id,
        showPlatformBranding: !studio.showPlatformBranding,
      }).unwrap();
      toast.success("Branding preference saved.");
    } catch (err: unknown) {
      const message =
        err && typeof err === "object" && "data" in err && err.data && typeof err.data === "object" && "message" in err.data
          ? String((err.data as { message: string }).message)
          : "Upgrade to remove branding.";
      toast.error(message);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Platform branding</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <div className="flex items-center justify-between gap-4">
          <div className="space-y-0.5">
            <p className="text-sm font-medium">
              Show "Powered by Pena e Artë" on booking widget
            </p>
            <p className="text-xs text-muted-foreground">
              Displayed in the booking widget footer for your clients.
            </p>
          </div>
          <Badge variant={studio.showPlatformBranding ? "default" : "secondary"}>
            {studio.showPlatformBranding ? "On" : "Off"}
          </Badge>
        </div>

        <Button
          variant="outline"
          size="sm"
          onClick={handleToggle}
          disabled={isLoading}
          className="gap-2"
        >
          {isLoading && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
          {studio.showPlatformBranding ? "Disable branding" : "Enable branding"}
        </Button>
      </CardContent>
    </Card>
  );
}
