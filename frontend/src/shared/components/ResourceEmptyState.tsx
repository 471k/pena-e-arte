import type { ReactNode } from "react";

interface ResourceEmptyStateProps {
  /** Icon element — rendered at ~32–40px, muted color applied by parent. */
  icon:    ReactNode;
  /** Bold heading — e.g. "No designs yet" */
  heading: string;
  /** Muted explanatory line — role-specific copy goes here. */
  body:    string;
  /** Optional CTA — fully constructed JSX. Caller decides role-gating. */
  action?: ReactNode;
}

/**
 * Canonical empty-state shell used by every resource list page.
 * Icon → heading → muted body → optional action. Single implementation,
 * no per-page variations in padding/spacing/typography.
 */
export function ResourceEmptyState({
  icon, heading, body, action,
}: ResourceEmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-4 py-16 text-center">
      <div className="text-muted-foreground/40" aria-hidden="true">
        {icon}
      </div>
      <div className="space-y-1">
        <p className="text-sm font-medium">{heading}</p>
        <p className="text-xs text-muted-foreground">{body}</p>
      </div>
      {action}
    </div>
  );
}
