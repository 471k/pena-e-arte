import { useState } from "react";
import { AlertTriangle, X } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

// Gated on the build-time VITE_PUBLIC_URL, not a runtime check — staging's frontend image is
// built with VITE_PUBLIC_URL=https://staging.tattooos.co (k8s/overlays/staging via
// .github/workflows/cd.yml), production's with the app.tattooos.co URL. Dismiss is
// component-state only (not localStorage) so the banner reappears on the next full load
// rather than being permanently hidden — this is a safety reminder for a shared test
// environment, not a one-time notice like the cookie banner.
function isStagingBuild(): boolean {
  return (import.meta.env.VITE_PUBLIC_URL ?? "").includes("staging.");
}

export function StagingBanner() {
  const [dismissed, setDismissed] = useState(false);

  if (!isStagingBuild() || dismissed) return null;

  return (
    <div
      role="alert"
      aria-live="polite"
      className="sticky top-0 z-50 flex items-center gap-3 px-4 py-2 bg-amber-500/15 border-b border-amber-500/40 text-amber-800 dark:text-amber-300 text-sm"
    >
      <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
      <span className="flex-1 font-medium">
        STAGING — test data only, not connected to production
      </span>
      <Button
        variant="ghost"
        size="icon"
        className="h-6 w-6 shrink-0 text-amber-800 dark:text-amber-300 hover:bg-amber-500/20"
        onClick={() => setDismissed(true)}
        aria-label="Dismiss staging banner"
      >
        <X className="h-4 w-4" />
      </Button>
    </div>
  );
}
