import { Bell } from "lucide-react";
import type { WaitlistClaimHandoff } from "../waitlistClaim";

/** Shown at the top of either booking form when the visit came from a waitlist claim. Never implies a hold. */
export function WaitlistClaimNotice({ handoff }: { handoff: WaitlistClaimHandoff }) {
  return (
    <div className="flex gap-2 rounded-md border bg-muted/40 p-3 text-xs text-foreground" role="note">
      <Bell className="h-4 w-4 shrink-0 text-info-text" aria-hidden="true" />
      <p>
        {handoff.slotStart
          ? "You're booking the waitlist slot you were offered. It isn't held for you: whoever books it first gets it."
          : "The slot you were offered has been taken. Pick another time below and your waitlist offer still counts as booked."}
      </p>
    </div>
  );
}
