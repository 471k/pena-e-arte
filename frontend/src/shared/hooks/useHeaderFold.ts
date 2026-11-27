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
 *
 * The rem threshold is a floor, not the whole rule. From 640px up the header adds the Owner/Artist switch, the
 * studio's name and wider gaps, so the inline controls need far more room than on a phone: at 200% text a 768px
 * window overflowed by 446px with the threshold alone (slice 9's pass, 23 Nov). So when the header is unfolded and
 * its content overflows, it folds and remembers how wide the inline layout needed to be; it unfolds only once the
 * header is at least that wide. The remembered width only grows, so it can't flip back and forth.
 */
export function useHeaderFold<T extends HTMLElement>(belowRem: number): { ref: RefObject<T | null>; folded: boolean } {
  const ref = useRef<T>(null);
  const [folded, setFolded] = useState(false);
  // The width in rem the inline layout has been seen to need. Starts at the measured floor.
  const neededRem = useRef(belowRem);

  useLayoutEffect(() => {
    neededRem.current = Math.max(neededRem.current, belowRem);
    const el = ref.current;
    if (!el) return;

    function measure() {
      if (!el) return;
      const rootPx = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
      const widthRem = el.getBoundingClientRect().width / rootPx;
      if (widthRem <= 0) {
        setFolded(false);
        return;
      }
      if (!folded && el.scrollWidth > el.clientWidth + 1) {
        neededRem.current = Math.max(neededRem.current, el.scrollWidth / rootPx);
        setFolded(true);
        return;
      }
      setFolded(widthRem < neededRem.current);
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
    // Content that arrives later (the studio's name, a pending-invite bell) widens the children, not the header.
    for (const child of Array.from(el.children)) observer.observe(child);
    // A child that MOUNTS later (the owner's Owner/Artist switch, once /artists/me answers) resizes nothing that is
    // already observed when its neighbours cannot shrink, so it would never trigger a measure: watch it arrive.
    // Before brand master 1.0.0 the shrinkable wordmark absorbed the new child and its resize happened to
    // trigger the measure; the brand is shrink-0 now (owner, 1024px at 200% text overflowed 197px without this).
    const mutations = typeof MutationObserver === "undefined" ? null : new MutationObserver((records) => {
      for (const record of records) {
        for (const node of Array.from(record.addedNodes)) if (node instanceof Element) observer.observe(node);
      }
      measure();
    });
    mutations?.observe(el, { childList: true });
    return () => {
      observer.disconnect();
      mutations?.disconnect();
      probe.remove();
    };
    // Re-run when the fold changes, so the overflow check reads the layout that is actually rendered.
  }, [belowRem, folded]);

  return { ref, folded };
}

/** The width in rem each layout's header needs for every control inline (measured at 390px, 100% text, 23 Nov:
 * owner and artist 383px, client 347px). Below it the secondary controls fold into "More". */
export const HEADER_FOLD_BELOW_REM = { owner: 24, artist: 24, client: 22, admin: 22.5 } as const;
