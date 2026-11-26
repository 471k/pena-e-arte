import type { MouseEvent } from "react";

/** The id of each role layout's content wrapper, the skip link's target. */
export const MAIN_CONTENT_ID = "main-content";

/**
 * "Skip to content", the first Tab stop on every signed-in page (WCAG 2.4.1 Bypass Blocks). Without it a keyboard
 * user crossed the header and the whole sidebar (23 Tab stops on an owner page) before reaching the page itself.
 * Hidden until focused, then shown top-left. It moves focus to the content wrapper, so the next Tab is the page's
 * first control. It focuses the target itself rather than following the #hash, which would add a history entry.
 */
export function SkipToContent() {
  function skip(event: MouseEvent<HTMLAnchorElement>) {
    event.preventDefault();
    const target = document.getElementById(MAIN_CONTENT_ID);
    if (!target) return;
    target.focus();
    target.scrollIntoView?.({ block: "start" });
  }

  return (
    <a
      href={`#${MAIN_CONTENT_ID}`}
      onClick={skip}
      className="sr-only focus:not-sr-only focus:fixed focus:left-2 focus:top-2 focus:z-50 focus:rounded-sm focus:bg-background focus:px-3 focus:py-2 focus:text-sm focus:font-medium focus:text-foreground focus:outline-hidden focus:ring-2 focus:ring-ring"
    >
      Skip to content
    </a>
  );
}
