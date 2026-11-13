import { useId, type ReactNode } from "react";
import { ChevronDown } from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { settingsGroupElementId, type SettingsGroupId } from "../settingsGroups";

interface SettingsGroupProps {
  id:       SettingsGroupId;
  title:    string;
  summary:  string;
  open:     boolean;
  /** Omitted for the one group that never collapses (Business details). */
  onToggle?: () => void;
  /** False renders a plain heading and always-shown contents. */
  collapsible?: boolean;
  children: ReactNode;
}

/**
 * One Studio Settings group: a heading button and the cards under it (E05c, O04). The hand-rolled `hidden`
 * disclosure (E04c's pattern), not Radix Accordion: the cards stay mounted while collapsed, so their
 * queries, unsaved edits and tour targets survive, and nothing inside a collapsed group is focusable or
 * announced.
 */
export function SettingsGroup({ id, title, summary, open, onToggle, collapsible = true, children }: SettingsGroupProps) {
  const titleId = useId();
  const summaryId = useId();
  const regionId = useId();

  if (!collapsible) {
    return (
      <section id={settingsGroupElementId(id)} aria-labelledby={titleId} data-testid={settingsGroupElementId(id)} className="scroll-mt-16 space-y-4">
        <div className="px-1">
          <h2 id={titleId} className="text-sm font-semibold text-foreground">{title}</h2>
          <p className="text-xs text-muted-foreground break-words">{summary}</p>
        </div>
        {children}
      </section>
    );
  }

  return (
    <section
      id={settingsGroupElementId(id)}
      aria-labelledby={titleId}
      data-testid={settingsGroupElementId(id)}
      // Clears the page's sticky header when a link scrolls here.
      className="scroll-mt-16"
    >
      <h2 className="m-0">
        <button
          type="button"
          aria-expanded={open}
          aria-controls={regionId}
          aria-labelledby={titleId}
          aria-describedby={summaryId}
          onClick={onToggle}
          className="w-full flex items-start justify-between gap-3 px-4 py-3 text-left rounded-xl hover:bg-muted/60 focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        >
          <span className="min-w-0">
            <span id={titleId} className="block text-sm font-semibold text-foreground">{title}</span>
            <span id={summaryId} className="block text-xs text-muted-foreground break-words">{summary}</span>
          </span>
          <ChevronDown
            className={cn("h-4 w-4 mt-0.5 shrink-0 text-muted-foreground transition-transform", open && "rotate-180")}
            aria-hidden="true"
          />
        </button>
      </h2>
      <div id={regionId} role="region" aria-labelledby={titleId} hidden={!open} className="space-y-4 px-2 sm:px-3 pb-3 pt-1">
        {children}
      </div>
    </section>
  );
}
