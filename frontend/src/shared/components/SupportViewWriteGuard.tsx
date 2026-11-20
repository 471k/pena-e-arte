import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Eye } from "lucide-react";
import { SUPPORT_VIEW_HOME, SUPPORT_VIEW_READ_ONLY_REASON, useSupportView } from "@/shared/hooks/useSupportView";

/**
 * Wraps a page whose whole job is to create or change something (a "new" form, an editor, booking). In a support
 * session (D05) every write is refused by the server, so the page is replaced by a plain statement of that and a
 * way back, instead of a form that can only fail on submit.
 */
export function SupportViewWriteGuard({ children }: { children: ReactNode }) {
  const supportView = useSupportView();
  if (!supportView) return <>{children}</>;
  return (
    <div className="mx-auto max-w-md space-y-3 px-4 py-16 text-center" role="status">
      <Eye className="mx-auto h-8 w-8 text-muted-foreground" aria-hidden="true" />
      <h1 className="text-base font-semibold">This page changes things, so it isn&apos;t available</h1>
      <p className="text-sm text-muted-foreground">{SUPPORT_VIEW_READ_ONLY_REASON}</p>
      <Link to={SUPPORT_VIEW_HOME} className="inline-block text-sm underline underline-offset-4">Back to the schedule</Link>
    </div>
  );
}
