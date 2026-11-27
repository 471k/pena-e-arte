import type { JSX } from "react";
import { TattooOSLogo } from "@/shared/components/TattooOSLogo";

interface RoleHeaderBrandProps {
  /** The header's fold state from useHeaderFold: folded, the brand gives its room to the controls. */
  folded: boolean;
  /** Whose header: picks the measured width below which the micro mark replaces the small lockup
   *  (TattooOSLogo.css). The header must be a size container (`@container`). */
  fit: "owner" | "artist" | "client";
}

/**
 * The TattooOS brand in the owner, artist and client headers (brand master 1.0.0).
 *
 * - Folded (S05: 200% text on a phone, or a header too narrow for its controls): the 20px micro
 *   mark only, the same footprint the old pen icon had in that state.
 * - Unfolded: the small lockup, swapped for the micro mark below 360px by the package's
 *   `.tattooos-role-brand` rule, and also wherever this role's header is narrower than the width
 *   it was measured to need with the small lockup (TattooOSLogo.css), so the controls keep their
 *   room before the brand does.
 *
 * Exactly one accessible name ("TattooOS") whichever presentation shows; both images inside are
 * decorative. Not a link: these headers had no home link and this adds none. `shrink-0` so the
 * controls, not the brand, are what the header's overflow handling acts on.
 */
export function RoleHeaderBrand({ folded, fit }: RoleHeaderBrandProps): JSX.Element {
  if (folded) {
    return (
      <span className="tattooos-role-brand shrink-0" role="img" aria-label="TattooOS">
        <TattooOSLogo variant="micro" height={20} decorative />
      </span>
    );
  }
  return (
    <span className="tattooos-role-brand shrink-0" role="img" aria-label="TattooOS" data-fit={fit}>
      <span className="tattooos-role-brand__full">
        <TattooOSLogo variant="small" decorative />
      </span>
      <span className="tattooos-role-brand__micro">
        <TattooOSLogo variant="micro" height={20} decorative />
      </span>
    </span>
  );
}
