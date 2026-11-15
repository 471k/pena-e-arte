import { useEffect, useRef } from "react";
import type { MouseEvent, ReactNode } from "react";
import { Link, useLocation, useNavigationType } from "react-router-dom";
import { cn } from "@/shared/utils/cn";
import { rememberReturnTarget, takeReturnTarget } from "./recordLinkContract";
// RECORD_CONTAINER, RECORD_ACTIONS and isInsideControl live in ./recordLinkContract (fast refresh needs a
// component file to export only components).

/**
 * The one way a record (appointment, client, form, payment…) is opened from a list (S03).
 *
 * `docs/03`: "Buttons perform operations; links navigate" and "do not nest interactive controls or make the
 * whole row a pointer-only target". So the record's title is a named native link: Tab reaches it, Enter
 * opens it, a screen reader announces it as a link with the record's name, and middle-click opens a tab.
 *
 * `stretched` keeps the whole card clickable for a pointer without making the card itself the control: the
 * link's `::after` covers the nearest positioned ancestor, so put {@link RECORD_CONTAINER} on the card and
 * {@link RECORD_ACTIONS} on any sibling buttons so they sit above the overlay. Never use `stretched` inside a
 * table cell (a `<td>` is not a positioned box, so the overlay would cover something larger); a table row
 * opens through {@link isInsideControl} instead.
 */
interface RecordLinkProps {
  to:         string;
  children:   ReactNode;
  /** Cover the whole card for a pointer; see the component's doc. */
  stretched?: boolean;
  className?: string;
  /** Overrides the visible text as the accessible name, e.g. "Open appointment 14:00 with Ana". */
  "aria-label"?: string;
}

export function RecordLink({ to, children, stretched = false, className, "aria-label": ariaLabel }: RecordLinkProps) {
  const location = useLocation();
  const navigationType = useNavigationType();
  const ref = useRef<HTMLAnchorElement>(null);
  const listPath = location.pathname + location.search;

  // Coming back (browser back, a POP) to the list a record was opened from: focus that record's link, so a
  // keyboard user continues from where they were rather than from the top of the page. A forward visit to
  // the list (the sidebar, a "Back to clients" button) starts at the top as any new page does.
  useEffect(() => {
    if (navigationType !== "POP") return;
    if (takeReturnTarget(listPath, to)) ref.current?.focus();
  }, [navigationType, listPath, to]);

  function remember(event: MouseEvent<HTMLAnchorElement>) {
    if (event.defaultPrevented) return;
    rememberReturnTarget(listPath, to);
  }

  return (
    <Link
      ref={ref}
      to={to}
      onClick={remember}
      aria-label={ariaLabel}
      data-record-link=""
      className={cn(
        "rounded-sm hover:underline underline-offset-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        // Stretched: the card shows the focus ring (RECORD_CONTAINER), so the text itself doesn't.
        stretched && "after:absolute after:inset-0 after:content-[''] focus-visible:ring-0",
        className,
      )}
    >
      {children}
    </Link>
  );
}
