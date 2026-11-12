import { LayoutDashboard, Palette } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import { cn } from "@/shared/utils/cn";
import { isInArtistContext } from "@/shared/utils/artistContext";

interface ArtistModeSwitcherProps {
  artistId: string;
  /** "fill" stretches both tabs across their container with a taller touch target (the phone nav drawer). */
  layout?: "compact" | "fill";
  /** Called after either tab navigates, e.g. so the nav drawer can close itself. */
  onSwitch?: () => void;
}

// Only rendered for an owner who has linked their own Artist profile (see
// CreateOwnArtistProfileCommand) — makes the existing "My Portfolio"/"My Earnings" pages
// reachable from a single, always-visible header control instead of being buried among 20+
// other items in the main nav list, matching how vertical-booking-SaaS competitors surface a
// dual owner/provider identity (Vagaro/Fresha/Boulevard show a persistent "acting as" affordance
// rather than requiring the person to hunt through a full settings-style nav for their own
// booking/portfolio page).
//
// OwnerLayout renders it twice: in the header from `sm` up, and at the top of the nav drawer, which is
// the only navigation a phone has. Both copies derive their state from the same isInArtistContext call,
// so they can't disagree (owner phone-navigation patch, 2026-11-12).
export function ArtistModeSwitcher({ artistId, layout = "compact", onSwitch }: ArtistModeSwitcherProps) {
  const location = useLocation();
  const navigate = useNavigate();

  const isArtistView = isInArtistContext(location.pathname, location.search, artistId);
  const fill = layout === "fill";

  function go(to: string) {
    navigate(to);
    onSwitch?.();
  }

  return (
    <div
      data-tour="owner-artist-mode-switch"
      role="tablist"
      aria-label="Switch between owner and artist view"
      className={cn(
        "flex items-center gap-0.5 rounded-md border bg-muted/40 p-0.5 text-sm",
        fill ? "w-full" : "shrink-0",
      )}
    >
      <button
        type="button"
        role="tab"
        aria-selected={!isArtistView}
        onClick={() => go("/dashboard")}
        className={cn(
          "flex items-center gap-1.5 rounded-[5px] transition-colors",
          fill ? "flex-1 justify-center px-3 py-2" : "px-2.5 py-1",
          !isArtistView
            ? "bg-primary text-primary-foreground"
            : "text-muted-foreground hover:text-foreground hover:bg-muted",
        )}
      >
        <LayoutDashboard className="h-3.5 w-3.5" />
        Owner
      </button>
      <button
        type="button"
        role="tab"
        aria-selected={isArtistView}
        onClick={() => go(`/artists/${artistId}`)}
        className={cn(
          "flex items-center gap-1.5 rounded-[5px] transition-colors",
          fill ? "flex-1 justify-center px-3 py-2" : "px-2.5 py-1",
          isArtistView
            ? "bg-primary text-primary-foreground"
            : "text-muted-foreground hover:text-foreground hover:bg-muted",
        )}
      >
        <Palette className="h-3.5 w-3.5" />
        Artist
      </button>
    </div>
  );
}
