import type { JSX } from "react";
import { cn } from "@/shared/utils/cn";
import "./TattooOSLogo.css";

/** Brand master 1.0.0 (After Hours). The files under this root are byte-exact package copies. */
const ASSET_ROOT = "/brand/after-hours/1.0.0";

/**
 * The package's variant table, verbatim. `width`/`height` are each SVG's own intrinsic size
 * (they go on the <img> so the browser reserves the right box before the file loads);
 * `displayHeight` is the size the package specifies for that use.
 */
const VARIANTS = {
  micro: { folder: "marks", file: "cartridge-micro", width: 128, height: 128, displayHeight: 20 },
  compact: { folder: "marks", file: "cartridge-compact", width: 128, height: 128, displayHeight: 32 },
  contour: { folder: "marks", file: "cartridge-contour", width: 128, height: 128, displayHeight: 96 },
  small: { folder: "lockups", file: "small", width: 141, height: 24, displayHeight: 24 },
  header: { folder: "lockups", file: "header", width: 189, height: 32, displayHeight: 32 },
  signature: { folder: "lockups", file: "signature", width: 321, height: 72, displayHeight: 72 },
  wordmark: { folder: "lockups", file: "wordmark", width: 458, height: 82, displayHeight: 40 },
  // 124 px wide by default: 124 * 28 / 104 = 33.38 px tall.
  poweredBy: { folder: "lockups", file: "powered-by", width: 104, height: 28, displayHeight: (124 * 28) / 104 },
} as const;

export type TattooOSLogoVariant = keyof typeof VARIANTS;

/** The BACKGROUND the logo sits on, not the ink colour. */
export type TattooOSLogoSurface = "auto" | "light" | "dark";

export interface TattooOSLogoProps {
  variant?: TattooOSLogoVariant;
  /** "auto" follows the operating system's `prefers-color-scheme`, which is what the app's palette follows. */
  surface?: TattooOSLogoSurface;
  /** Display height in CSS px; the width follows the variant's own aspect ratio. */
  height?: number;
  /** Empty alt, for when an enclosing element already carries the accessible name. */
  decorative?: boolean;
  /** Spacing and placement only. Do not put display utilities here: TattooOSLogo.css sets `display`. */
  className?: string;
}

/** Path of one tone of a variant, e.g. `/brand/after-hours/1.0.0/lockups/small-on-dark.svg`. */
function tattooOSLogoSrc(variant: TattooOSLogoVariant, tone: "light" | "dark"): string {
  const asset = VARIANTS[variant];
  return `${ASSET_ROOT}/${asset.folder}/${asset.file}-on-${tone}.svg`;
}

/**
 * The TattooOS platform logo from the mastered brand package. Presentation only: no link, no
 * button, no handler, never in the tab order. Wrap it in a Link where a destination is wanted and
 * pass `decorative` so the brand is named once, by the wrapper.
 *
 * Tenant branding (studio logos, covers) is a different thing and never goes through this.
 */
export function TattooOSLogo({
  variant = "small",
  surface = "auto",
  height,
  decorative = false,
  className,
}: TattooOSLogoProps): JSX.Element {
  const asset = VARIANTS[variant];
  const displayHeight = height ?? asset.displayHeight;
  const displayWidth = (displayHeight * asset.width) / asset.height;
  const alt = decorative ? "" : variant === "poweredBy" ? "Powered by TattooOS" : "TattooOS";

  return (
    // The display size depends on the variant and an optional height, so it cannot be a static
    // Tailwind class; an inline size is the same pattern BillingPage uses for its usage bars.
    <picture className={cn("tattooos-logo", className)} style={{ width: displayWidth, height: displayHeight }}>
      {surface === "auto" && (
        <source media="(prefers-color-scheme: dark)" srcSet={tattooOSLogoSrc(variant, "dark")} />
      )}
      <img
        src={tattooOSLogoSrc(variant, surface === "dark" ? "dark" : "light")}
        width={asset.width}
        height={asset.height}
        alt={alt}
        draggable={false}
      />
    </picture>
  );
}
