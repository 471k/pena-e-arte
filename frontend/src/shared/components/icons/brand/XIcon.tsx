import type { BrandIconProps } from "./types";

// Glyph geometry: X, from the Simple Icons set v16.32.0 (CC0-1.0, https://simpleicons.org),
// fetched raw from the package CDN at build time — NOT taken from X's own brand-guidelines
// page (those only publish downloadable asset packs). The mark itself is X's trademark; a
// human must confirm current logo-usage terms before this ships at scale (see the PR notes).
const PATH =
  "M14.234 10.162 22.977 0h-2.072l-7.591 8.824L7.251 0H.258l9.168 13.343L.258 24H2.33l8.016-9.318L16.749 24h6.993zm-2.837 3.299-.929-1.329L3.076 1.56h3.182l5.965 8.532.929 1.329 7.754 11.09h-3.182z";

export function XIcon({ className, size = 20 }: BrandIconProps) {
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
