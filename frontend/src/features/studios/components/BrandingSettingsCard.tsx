import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { ToggleSwitch } from "@/shared/components/ui/toggle-switch";
import { useGetMyStudioQuery, useUpdateStudioBrandingMutation } from "../studiosApi";

export function BrandingSettingsCard() {
  const { data: studio } = useGetMyStudioQuery();
  const [updateBranding, { isLoading }] = useUpdateStudioBrandingMutation();

  if (!studio) return null;

  const canToggleOff = studio.allowBrandingRemoval;
  const isDisabled   = isLoading || (!canToggleOff && studio.showPlatformBranding);
  const upgradeHint  =
    !canToggleOff && studio.showPlatformBranding
      ? "Upgrade your plan to remove platform branding."
      : undefined;

  async function handleToggle() {
    try {
      await updateBranding({
        id:                   studio!.id,
        showPlatformBranding: !studio!.showPlatformBranding,
      }).unwrap();
      toast.success("Branding preference saved.");
    } catch (err: unknown) {
      const message =
        err && typeof err === "object" && "data" in err && err.data &&
        typeof err.data === "object" && "message" in err.data
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
      <CardContent>
        <div
          className="flex items-center justify-between gap-4"
          title={upgradeHint}
        >
          <div className="space-y-0.5">
            <p className="text-sm font-medium">
              Show "Powered by TattooOS" to your clients
            </p>
            <p className="text-xs text-muted-foreground">
              A small line in the footer of the emails your clients get, at the foot of their
              consent forms and receipts, and on the booking page they use when signed in.
            </p>
            {upgradeHint && (
              <p className="text-xs text-warning-text dark:text-amber-400 mt-1">
                {upgradeHint}
              </p>
            )}
          </div>

          {isLoading
            ? <Loader2 className="h-4 w-4 animate-spin text-muted-foreground shrink-0" />
            : (
              <ToggleSwitch
                checked={studio.showPlatformBranding}
                onChange={handleToggle}
                disabled={isDisabled}
                aria-label="Show platform branding to your clients"
              />
            )
          }
        </div>
      </CardContent>
    </Card>
  );
}
