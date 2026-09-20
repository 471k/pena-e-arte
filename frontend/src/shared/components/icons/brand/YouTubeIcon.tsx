import type { BrandIconProps } from "./types";

// Glyph geometry: YouTube, from the Simple Icons set v16.32.0 (CC0-1.0, https://simpleicons.org),
// fetched raw from the package CDN at build time — NOT taken from YouTube's own brand-guidelines
// page (those only publish downloadable asset packs). The mark itself is YouTube's trademark; a
// human must confirm current logo-usage terms before this ships at scale (see the PR notes).
const PATH =
  "M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z";

export function YouTubeIcon({ className, size = 20 }: BrandIconProps) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="currentColor"
      aria-hidden="true"
      focusable="false"
      className={className}
    >
      <path d={PATH} />
    </svg>
  );
}
