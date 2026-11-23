import { useLayoutEffect, useRef, useState, type RefObject } from "react";

/**
 * Whether a header is too narrow for its secondary controls, measured in rem (S05, WCAG 1.4.10 at 200% text).
 *
 * The threshold is the header's own width in root ems, so it moves with the text size: at 200% text a 390px phone
 * is a 12rem header and folds, while the same phone at 100% (24rem) keeps everything inline. A px media query
 * misses exactly this, which is what put the studio's name under the Owner/Artist switch in #415.
 *
 * Measured in script rather than written as a CSS container query because one value has to decide both what
 * renders and what the onboarding tour points at (#430 and #438: the tour reads the value that renders its
 * target, never polls for it), and script cannot read a container query's state.
 *
 * Before the first layout effect, and wherever there is no layout (jsdom: every width is 0), it reads as not
 * folded: a zero width means "not measured", never "narrow".
 */
export function useHeaderFold<T extends HTMLElement>(belowRem: number): { ref: RefObject<T | null>; folded: boolean } {
  const ref = useRef<T>(null);
  const [folded, setFolded] = useState(false);

  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;

    function measure() {
      if (!el) return;
      const rootPx = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
      const widthRem = el.getBoundingClientRect().width / rootPx;
      setFolded(widthRem > 0 && widthRem < belowRem);
    }

    measure();
    if (typeof ResizeObserver === "undefined") return;

    // The header's px width doesn't change when only the text size does, so a 1rem probe is watched too: it
    // resizes exactly when the root font size changes.
    const probe = document.createElement("div");
    probe.setAttribute("aria-hidden", "true");
    probe.style.cssText = "position:absolute;visibility:hidden;pointer-events:none;width:1rem;height:1px;top:0;left:0";
    document.body.appendChild(probe);

    const observer = new ResizeObserver(measure);
    observer.observe(el);
    observer.observe(probe);
    return () => {
      observer.disconnect();
      probe.remove();
    };
  }, [belowRem]);

  return { ref, folded };
}

/** The width in rem each layout's header needs for every control inline (measured at 390px, 100% text, 23 Nov:
 * owner and artist 383px, client 347px). Below it the secondary controls fold into "More". */
export const HEADER_FOLD_BELOW_REM = { owner: 24, artist: 24, client: 22, admin: 22.5 } as const;
