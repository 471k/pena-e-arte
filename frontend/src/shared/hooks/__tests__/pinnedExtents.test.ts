import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { pinnedExtents, type PinnedBox } from "../usePinnedChromeScrollPadding";

// The rules behind 2.4.11's scroll-padding. jsdom has no layout, so the boxes are written out; the real-browser
// probe (400% zoom, 320x256) checks the result on the pages themselves.
const VW = 320;
const VH = 256;
const box = (b: Partial<PinnedBox>): PinnedBox => ({ position: "sticky", top: "auto", bottom: "auto", width: VW, height: 50, ...b });

describe("pinnedExtents", () => {
  it("a sticky header pinned at the top covers its own height", () => {
    expect(pinnedExtents([box({ top: "0px", height: 56 })], VW, VH)).toEqual({ top: 56, bottom: 0 });
  });

  it("a page header pinned below the layout header reaches its offset plus its height", () => {
    const layout = box({ top: "0px", height: 56 });
    const page = box({ top: "56px", height: 49 });
    expect(pinnedExtents([layout, page], VW, VH).top).toBe(105);
  });

  it("a fixed bar at the bottom counts as bottom chrome", () => {
    expect(pinnedExtents([box({ position: "fixed", bottom: "0px", height: 69 })], VW, VH)).toEqual({ top: 0, bottom: 69 });
  });

  it("ignores anything that is not sticky or fixed", () => {
    expect(pinnedExtents([box({ position: "relative", top: "0px" }), box({ position: "static", top: "0px" })], VW, VH)).toEqual({ top: 0, bottom: 0 });
  });

  it("ignores a narrow sticky side column", () => {
    expect(pinnedExtents([box({ top: "72px", width: 120, height: 100 })], VW, VH).top).toBe(0);
  });

  it("ignores a dialog centred with top: 50%, which is not chrome", () => {
    expect(pinnedExtents([box({ position: "fixed", top: "128px", height: 100 })], VW, VH).top).toBe(0);
  });

  it("counts a tall wrapped header (over half the viewport) as chrome, within the cap", () => {
    expect(pinnedExtents([box({ top: "0px", height: 137 })], VW, VH).top).toBe(137);
  });

  it("ignores an overlay or open drawer taller than three quarters of the viewport", () => {
    expect(pinnedExtents([box({ position: "fixed", top: "0px", bottom: "0px", height: VH })], VW, VH)).toEqual({ top: 0, bottom: 0 });
  });

  it("never covers more than 60% of the viewport in total", () => {
    const { top, bottom } = pinnedExtents([box({ top: "0px", height: 120 }), box({ position: "fixed", bottom: "0px", height: 100 })], VW, VH);
    expect(top + bottom).toBeLessThanOrEqual(Math.round(VH * 0.6) + 1);
    expect(top).toBeGreaterThan(bottom);
  });
});

// The floor lives in index.css (`html:has(header[data-pinned-shell])`) and is checked in a real browser by
// e2e/pinned-chrome-floor.spec.ts; jsdom resolves neither :has() nor max(). What jsdom CAN hold is the marker the
// floor keys on: if a layout's shell header loses it, that layout silently loses its floor.
describe("the scroll-padding floor's marker", () => {
  const LAYOUTS = ["AdminLayout", "ArtistLayout", "ClientLayout", "OwnerLayout"] as const;

  it.each(LAYOUTS)("%s's pinned h-14 shell header carries data-pinned-shell", (name) => {
    const src = readFileSync(`src/layouts/${name}.tsx`, "utf8");
    const headers = src.match(/<header\b[^>]*>/g) ?? [];
    const shell = headers.filter((h) => /\bh-14\b/.test(h) && /\bsticky\b/.test(h));
    expect(shell, `${name}: exactly one pinned h-14 header`).toHaveLength(1);
    expect(shell[0]).toContain("data-pinned-shell");
  });

  it("index.css floors the top padding at the shell header's height, only where the marker is present", () => {
    const css = readFileSync("src/index.css", "utf8");
    expect(css).toMatch(/html:has\(header\[data-pinned-shell\]\)\s*\{\s*scroll-padding-top:\s*max\(var\(--pinned-top, 0px\), 3\.5rem\);/);
  });
});
