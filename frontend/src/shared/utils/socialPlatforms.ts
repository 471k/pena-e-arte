import type { ComponentType } from "react";
import { AtSign } from "lucide-react";
import {
  InstagramIcon,
  TikTokIcon,
  FacebookIcon,
  XIcon,
  YouTubeIcon,
} from "@/shared/components/icons/brand";

/** Anything that renders as an icon: a brand glyph or a Lucide fallback (both take className/size). */
export type SocialIcon = ComponentType<{
  className?: string;
  size?: number;
  "aria-hidden"?: boolean | "true" | "false";
}>;

/**
 * Brand glyphs live in shared/components/icons/brand (lucide-react ships no brand icons). Logo-usage
 * terms for each platform still need a human sign-off before production — see the notes in that folder.
 */
export const SOCIAL_PLATFORM_ICON: Record<string, SocialIcon> = {
  Instagram: InstagramIcon,
  TikTok:    TikTokIcon,
  Facebook:  FacebookIcon,
  X:         XIcon,
  YouTube:   YouTubeIcon,
};

/** For an unrecognised platform string (defensive only — the backend enum is closed). */
export const SOCIAL_PLATFORM_FALLBACK_ICON: SocialIcon = AtSign;

export const SOCIAL_PLATFORM_LABEL: Record<string, string> = {
  Instagram: "Instagram",
  TikTok:    "TikTok",
  Facebook:  "Facebook",
  X:         "X",
  YouTube:   "YouTube",
};
