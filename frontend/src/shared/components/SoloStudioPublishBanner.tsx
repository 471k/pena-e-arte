import { useState } from "react";
import { X, MapPin } from "lucide-react";
import { Link } from "react-router-dom";
import type { StudioResponse } from "@/features/studios/studiosApi";

const STORAGE_KEY = "solo-studio-publish-banner-dismissed";

function wasDismissedThisSession(): boolean {
  try {
    return sessionStorage.getItem(STORAGE_KEY) === "1";
  } catch {
    return false;
  }
}

type SoloStudioPublishBannerProps = {
  studio?: StudioResponse;
};

// Reappears next login until the studio actually has a real city/location saved
// (which flips IsPublished server-side) — dismissal is per-session only, via
// sessionStorage, never persisted past the current browser session.
export function SoloStudioPublishBanner({ studio }: SoloStudioPublishBannerProps) {
  const [dismissed, setDismissed] = useState(wasDismissedThisSession);

  if (!studio?.isSolo || studio.isPublished || dismissed) return null;

  function dismiss() {
    try {
      sessionStorage.setItem(STORAGE_KEY, "1");
    } catch {
      // ignore — banner will simply reappear on the next page load this session
    }
    setDismissed(true);
  }

  return (
    <div
      role="status"
      className="flex items-center gap-3 px-4 py-2.5 bg-primary/10 border-b border-primary/30 text-sm"
    >
      <MapPin className="h-4 w-4 shrink-0" aria-hidden="true" />
      <span className="flex-1">
        Finish setting up your studio — add a real city and location in{" "}
        <Link to="/studios/me" className="font-medium underline underline-offset-4">
          Studio Settings
        </Link>{" "}
        to become discoverable on the Studio Map and in Discover.
      </span>
      <button
        type="button"
        aria-label="Dismiss"
        onClick={dismiss}
        className="p-0.5 rounded hover:bg-primary/20 transition-colors"
      >
        <X className="h-4 w-4" />
      </button>
    </div>
  );
}
