import type { ComponentType } from "react";
import { Link2 } from "lucide-react";

/** Anything that renders as an icon (a Lucide icon; takes className/size). */
export type SocialIcon = ComponentType<{
  className?: string;
  size?: number;
  "aria-hidden"?: boolean | "true" | "false";
}>;

/**
 * One neutral link icon for every platform. We deliberately do not show Instagram / TikTok / Facebook /
 * X / YouTube logos: TikTok requires prior written permission, and Meta and YouTube forbid recolouring
 * or altering their marks (see the 2026-10-01 Decisions Log entry in docs/claude/architecture.md).
 * The platform is always named in visible text next to this icon, never by the icon alone.
 */
export const SOCIAL_LINK_ICON: SocialIcon = Link2;

export const SOCIAL_PLATFORM_LABEL: Record<string, string> = {
  Instagram: "Instagram",
  TikTok:    "TikTok",
  Facebook:  "Facebook",
  X:         "X",
  YouTube:   "YouTube",
};
