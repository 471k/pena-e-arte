import { useState } from "react";
import { Cookie } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

const STORAGE_KEY = "cookie-consent";

function hasConsented(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === "accepted";
  } catch {
    return true; // localStorage unavailable — don't block rendering on a banner we can't persist
  }
}

export function CookieConsentBanner() {
  const [visible, setVisible] = useState(() => !hasConsented());

  function accept() {
    try {
      localStorage.setItem(STORAGE_KEY, "accepted");
    } catch {
      // ignore — banner will simply reappear next visit
    }
    setVisible(false);
  }

  if (!visible) return null;

  return (
    <div
      role="region"
      aria-label="Cookie consent"
      className="fixed bottom-0 inset-x-0 z-50 border-t bg-background/95 backdrop-blur px-4 py-3
                 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3"
    >
      <div className="flex items-start gap-2 max-w-2xl">
        <Cookie className="h-4 w-4 shrink-0 mt-0.5 text-muted-foreground" />
        <p className="text-xs text-muted-foreground">
          We use essential cookies to keep you signed in and remember your preferences.
          By continuing to use TattooOS, you agree to this. See our{" "}
          {/* Plain anchor (not react-router <Link>) so this banner renders correctly even
              when mounted outside a Router context, as its unit test does. */}
          <a
            href="/privacy"
            className="underline underline-offset-2 hover:text-foreground"
          >
            Privacy Policy
          </a>{" "}
          for details.
          {/* NOTE: TattooOS currently sets NO non-essential (analytics/marketing) cookies,
              so an accept-all banner is accurate today. If any analytics or marketing cookie
              is ever added, this banner MUST gain a real category-level opt-in (essential vs.
              analytics vs. marketing) at that point — do not add categories before then. */}
        </p>
      </div>
      <Button size="sm" onClick={accept} className="shrink-0 self-end sm:self-auto">
        Got it
      </Button>
    </div>
  );
}
