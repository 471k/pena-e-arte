import { X, ShieldOff } from "lucide-react";
import { Link } from "react-router-dom";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { clearReadOnlyError } from "@/features/ui/uiSlice";

export function ReadOnlyBanner() {
  const message  = useAppSelector((s) => s.ui.readOnlyError);
  const dispatch = useAppDispatch();

  if (!message) return null;

  return (
    <div className="flex items-center gap-3 px-4 py-2.5 bg-amber-500/10 border-b border-amber-500/30 text-amber-700 dark:text-amber-400 text-sm">
      <ShieldOff className="h-4 w-4 shrink-0" />
      <span className="flex-1">
        {message}{" "}
        <Link to="/billing" className="font-medium underline underline-offset-4">
          Manage subscription
        </Link>
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
