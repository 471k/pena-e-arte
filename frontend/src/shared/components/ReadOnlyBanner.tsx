import { X, ShieldOff } from "lucide-react";
import { Link } from "react-router-dom";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { clearReadOnlyError } from "@/features/ui/uiSlice";
import { Role } from "@/shared/types/roles";

// Shown to the people who can't fix the subscription themselves. The server's message is written for
// the owner ("Subscribe to re-enable write access"), and /billing is owner/admin only, so an artist or a
// client gets a sentence that says why changes aren't saving and who can sort it out, and no link to a
// page that would refuse them.
const ARTIST_MESSAGE =
  "Changes can't be saved right now because the studio's subscription needs attention. The studio owner can sort this out, and everything works again once they do.";
const CLIENT_MESSAGE =
  "This studio can't take changes online right now. To book or change an appointment, please contact the studio directly.";

export function ReadOnlyBanner() {
  const message  = useAppSelector((s) => s.ui.readOnlyError);
  const role     = useAppSelector((s) => s.auth.role);
  const dispatch = useAppDispatch();

  if (!message) return null;

  const canManageBilling = role === Role.Owner || role === Role.Admin;
  const text = canManageBilling ? message : role === Role.Client ? CLIENT_MESSAGE : ARTIST_MESSAGE;

  return (
    <div className="flex items-center gap-3 px-4 py-2.5 bg-amber-500/10 border-b border-amber-500/30 text-amber-800 dark:text-amber-400 text-sm">
      <ShieldOff className="h-4 w-4 shrink-0" />
      <span className="flex-1">
        {text}
        {canManageBilling && (
          <>
            {" "}
            <Link to="/billing" className="font-medium underline underline-offset-4">
              Manage subscription
            </Link>
          </>
        )}
      </span>
      <button
        type="button"
        aria-label="Dismiss"
        onClick={() => dispatch(clearReadOnlyError())}
        className="p-0.5 rounded hover:bg-amber-500/20 transition-colors"
      >
        <X className="h-4 w-4" />
      </button>
    </div>
  );
}
