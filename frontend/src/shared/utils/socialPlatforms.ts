import { AtSign, Music2, Globe, Hash, Video, type LucideIcon } from "lucide-react";

/**
 * Generic Lucide placeholders — lucide-react 1.17 ships no brand/logo icons at all
 * (confirmed: no Instagram/Facebook/Youtube/Twitter icon exists in this version).
 * Brand-guideline-compliant icon usage needs sign-off from whoever owns frontend/brand
 * review before shipping — flagged in the originating spec's Open Questions, not
 * resolved here.
 */
export const SOCIAL_PLATFORM_ICON: Record<string, LucideIcon> = {
  Instagram: AtSign,
  TikTok:    Music2,
  Facebook:  Globe,
  X:         Hash,
  YouTube:   Video,
};

export const SOCIAL_PLATFORM_LABEL: Record<string, string> = {
  Instagram: "Instagram",
  TikTok:    "TikTok",
  Facebook:  "Facebook",
  X:         "X",
  YouTube:   "YouTube",
};
