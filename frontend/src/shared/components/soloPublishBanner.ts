import type { StudioResponse } from "@/features/studios/studiosApi";

// Kept apart from SoloStudioPublishBanner.tsx so that file exports only components (React fast refresh).

export const SOLO_PUBLISH_BANNER_STORAGE_KEY = "solo-studio-publish-banner-dismissed";

export function wasDismissedThisSession(): boolean {
  try {
    return sessionStorage.getItem(SOLO_PUBLISH_BANNER_STORAGE_KEY) === "1";
  } catch {
    return false;
  }
}

/** Whether the banner renders for this studio right now. OwnerLayout passes the same answer to the owner tour,
 * so the "Get discoverable" step is in the tour exactly when its target is on the page. */
export function soloPublishBannerApplies(studio: StudioResponse | undefined): boolean {
  return !!studio?.isSolo && !studio.isPublished && !wasDismissedThisSession();
}
