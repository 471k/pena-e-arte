import { X, TrendingUp } from "lucide-react";
import { Link } from "react-router-dom";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { clearPlanLimitError } from "@/features/ui/uiSlice";
import { Role } from "@/shared/types/roles";

export function PlanLimitBanner() {
  const message  = useAppSelector((s) => s.ui.planLimitError);
  const role     = useAppSelector((s) => s.auth.role);
  const dispatch = useAppDispatch();

  if (!message) return null;

  return (
    <div className="flex items-center gap-3 px-4 py-2.5 bg-violet-500/10 border-b border-violet-500/30 text-violet-700 dark:text-violet-400 text-sm">
      <TrendingUp className="h-4 w-4 shrink-0" />
      <span className="flex-1">
        {message}{" "}
        {role === Role.Owner ? (
          <Link to="/billing" className="font-medium underline underline-offset-4">
            Manage subscription
          </Link>
        ) : (
          "Ask the studio owner to upgrade the plan."
        )}
      </span>
      <button
        type="button"
        aria-label="Dismiss"
        onClick={() => dispatch(clearPlanLimitError())}
        className="p-0.5 rounded hover:bg-violet-500/20 transition-colors"
      >
        <X className="h-4 w-4" />
      </button>
    </div>
  );
}
