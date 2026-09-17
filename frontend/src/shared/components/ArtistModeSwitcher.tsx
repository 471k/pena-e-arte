import { LayoutDashboard, Palette } from "lucide-react";
import { useLocation, useNavigate } from "react-router-dom";
import { cn } from "@/shared/utils/cn";

interface ArtistModeSwitcherProps {
  artistId: string;
}

// Only rendered for an owner who has linked their own Artist profile (see
// CreateOwnArtistProfileCommand) — makes the existing "My Portfolio"/"My Earnings" pages
// reachable from a single, always-visible header control instead of being buried among 20+
// other items in the main nav list, matching how vertical-booking-SaaS competitors surface a
// dual owner/provider identity (Vagaro/Fresha/Boulevard show a persistent "acting as" affordance
// rather than requiring the person to hunt through a full settings-style nav for their own
// booking/portfolio page).
export function ArtistModeSwitcher({ artistId }: ArtistModeSwitcherProps) {
  const location = useLocation();
  const navigate = useNavigate();

  const isArtistView =
    location.pathname.startsWith(`/artists/${artistId}`) || location.pathname === "/earnings";

  return (
    <div
      data-tour="owner-artist-mode-switch"
      role="tablist"
      aria-label="Switch between owner and artist view"
      className="flex items-center gap-0.5 rounded-md border bg-muted/40 p-0.5 text-sm shrink-0"
    >
      <button
        type="button"
        role="tab"
        aria-selected={!isArtistView}
        onClick={() => navigate("/dashboard")}
        className={cn(
          "flex items-center gap-1.5 px-2.5 py-1 rounded-[5px] transition-colors",
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
        onClick={() => navigate(`/artists/${artistId}`)}
        className={cn(
          "flex items-center gap-1.5 px-2.5 py-1 rounded-[5px] transition-colors",
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
