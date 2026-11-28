import { describe, it, expect } from "vitest";

// The four role headers space their content in px, not rem: at 200% text on a 320px phone the rem padding and
// gaps doubled and, with the account menu's own spacing, pushed that menu 68px off-screen (28px at 360px).
// Measured in Chrome before and after (28 Nov 2026); this pins the classes so the rem spacing can't come back.
const layouts = import.meta.glob<string>("/src/layouts/{Owner,Artist,Client,Admin}Layout.tsx", {
  query: "?raw", import: "default", eager: true,
});

describe("role header spacing (200% text, 320 and 360px)", () => {
  it("covers all four role layouts", () => {
    expect(Object.keys(layouts).sort()).toEqual([
      "/src/layouts/AdminLayout.tsx", "/src/layouts/ArtistLayout.tsx",
      "/src/layouts/ClientLayout.tsx", "/src/layouts/OwnerLayout.tsx",
    ]);
  });

  it.each(Object.entries(layouts))("%s: the pinned header uses px padding and gap below sm", (_path, src) => {
    const header = src.split(/\r?\n/).find((line) => line.includes("data-pinned-shell"))!;
    expect(header).toContain("gap-[8px] px-[16px] sm:px-6");
    expect(header).not.toMatch(/ (gap-2|px-4) /);
  });
});
