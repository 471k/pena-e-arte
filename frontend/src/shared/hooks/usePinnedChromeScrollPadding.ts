import { useEffect } from "react";

/**
 * WCAG 2.4.11 Focus Not Obscured: a control that receives keyboard focus must not be hidden behind sticky or fixed
 * chrome (a page header, the quick-book bar, the cookie banner). The browser already scrolls a focused control
 * into view; `scroll-padding` on the scrolling element tells it how much of the viewport is covered, so it scrolls
 * far enough (W3C technique C43). The app scrolls the document, so the padding goes on <html>.
 *
 * The values are MEASURED, not written down: 79 files declare sticky chrome, from a 49 px page header to the
 * 234 px Discover header at 400% zoom, so no constant could match them. This writes `--pinned-top` and
 * `--pinned-bottom` on <html>, which index.css turns into `scroll-padding-top` / `scroll-padding-bottom`.
 */

/** What the measurement needs from one candidate element; a plain object so the rules can be unit-tested. */
export interface PinnedBox {
  /** Computed `position`. */
  position: string;
  /** Computed `top` / `bottom` ("auto" or a px length). */
  top: string;
  bottom: string;
  width: number;
  height: number;
}

export interface PinnedExtents {
  top: number;
  bottom: number;
}

const px = (value: string): number | null => (value.endsWith("px") ? Number.parseFloat(value) : null);

/**
 * How far sticky/fixed chrome reaches in from the top and bottom edges of a `vw` x `vh` viewport.
 *
 * - Only `sticky` and `fixed` boxes count, and only ones at least half the viewport wide: a sticky side column
 *   does not cover the content beside it.
 * - A box counts as top chrome when its `top` is a length within the top quarter of the viewport (bottom chrome
 *   likewise). That excludes a dialog centred with `top: 50%`, which is not chrome.
 * - A box taller than three quarters of the viewport is an overlay or an open drawer, not chrome, and is ignored.
 *   (Half was too low: a wrapped page header can be over half of a 400%-zoom viewport and still be chrome.)
 * - The total is capped at 60% of the viewport, so at least 40% stays available for the focused control.
 */
export function pinnedExtents(boxes: readonly PinnedBox[], vw: number, vh: number): PinnedExtents {
  let top = 0;
  let bottom = 0;
  for (const b of boxes) {
    if (b.position !== "sticky" && b.position !== "fixed") continue;
    if (b.height <= 0 || b.width < vw / 2 || b.height > vh * 0.75) continue;
    const t = px(b.top);
    const btm = px(b.bottom);
    if (t !== null && t >= 0 && t <= vh / 4) top = Math.max(top, t + b.height);
    else if (btm !== null && btm >= 0 && btm <= vh / 4) bottom = Math.max(bottom, btm + b.height);
  }
  const cap = vh * 0.6;
  if (top + bottom > cap) {
    const scale = cap / (top + bottom);
    top *= scale;
    bottom *= scale;
  }
  return { top: Math.round(top), bottom: Math.round(bottom) };
}

// Tailwind's own classes, including responsive variants (page-lg:sticky). Dialogs and Radix popovers position
// themselves with `fixed` too but scroll on their own, so their subtrees are excluded.
const CANDIDATES = '.sticky, .fixed, [class*=":sticky"], [class*=":fixed"]';
const EXCLUDED = '[role="dialog"], [role="alertdialog"], [data-radix-popper-content-wrapper]';

function measure(): PinnedExtents {
  const boxes: PinnedBox[] = [];
  for (const el of Array.from(document.querySelectorAll<HTMLElement>(CANDIDATES))) {
    if (el.closest(EXCLUDED)) continue;
    const cs = getComputedStyle(el);
    const r = el.getBoundingClientRect();
    boxes.push({ position: cs.position, top: cs.top, bottom: cs.bottom, width: r.width, height: r.height });
  }
  return pinnedExtents(boxes, window.innerWidth, window.innerHeight);
}

/** Mounted once, beside the router. Re-measures on resize and whenever the DOM or a class changes. */
export function PinnedChromeScrollPadding() {
  usePinnedChromeScrollPadding();
  return null;
}

export function usePinnedChromeScrollPadding() {
  useEffect(() => {
    const root = document.documentElement;
    let frame = 0;
    let last = "";
    const update = () => {
      frame = 0;
      const { top, bottom } = measure();
      const next = `${top}/${bottom}`;
      if (next === last) return;
      last = next;
      root.style.setProperty("--pinned-top", `${top}px`);
      root.style.setProperty("--pinned-bottom", `${bottom}px`);
    };
    const schedule = () => {
      if (!frame) frame = requestAnimationFrame(update);
    };

    schedule();
    window.addEventListener("resize", schedule);
    const mutations = new MutationObserver(schedule);
    mutations.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ["class", "style"] });
    // A late font load can change header heights with no DOM change; it also changes the body's size.
    const sizes = typeof ResizeObserver === "undefined" ? null : new ResizeObserver(schedule);
    sizes?.observe(document.body);

    return () => {
      if (frame) cancelAnimationFrame(frame);
      window.removeEventListener("resize", schedule);
      mutations.disconnect();
      sizes?.disconnect();
      root.style.removeProperty("--pinned-top");
      root.style.removeProperty("--pinned-bottom");
    };
  }, []);
}
