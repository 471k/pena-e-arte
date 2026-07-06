import { Link } from "react-router-dom";
import { BrandMark } from "@/features/public/components/PublicPageHeader";

/**
 * Minimal top bar for standalone guest auth screens (login, sign up,
 * register studio). These pages have no layout shell of their own, so
 * without this a guest who lands here has no way back to the rest of
 * the site short of the browser back button.
 */
export function GuestAuthHeader() {
  return (
    <header
      className="sticky top-0 z-20 border-b bg-background/95 backdrop-blur-sm"
      aria-label="Site header"
    >
      <div className="flex items-center justify-between px-4 py-2.5">
        <BrandMark />
        <Link
          to="/discover"
          className="text-xs text-muted-foreground hover:text-foreground
                     transition-colors px-3 py-2 rounded-md hover:bg-muted/40"
        >
          Discover
        </Link>
      </div>
    </header>
  );
}
