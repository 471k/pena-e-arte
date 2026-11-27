import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { BrandMark } from "../components/PublicPageHeader";

// Brand master 1.0.0: the public header's brand link (also GuestAuthHeader's).

describe("BrandMark", () => {
  function renderMark(): HTMLElement {
    render(
      <MemoryRouter>
        <BrandMark />
      </MemoryRouter>,
    );
    return screen.getByRole("link", { name: "TattooOS — Discover studios" });
  }

  it("keeps the same link, destination and name", () => {
    const link = renderMark();
    expect(link.getAttribute("href")).toBe("/discover");
    expect(screen.getAllByRole("link")).toHaveLength(1);
  });

  it("shows the small lockup with the micro mark for narrow screens, both decorative", () => {
    const link = renderMark();
    const srcs = Array.from(link.querySelectorAll("img")).map((i) => i.getAttribute("src"));
    expect(srcs).toEqual([
      "/brand/after-hours/1.0.0/lockups/small-on-light.svg",
      "/brand/after-hours/1.0.0/marks/cartridge-micro-on-light.svg",
    ]);
    for (const img of Array.from(link.querySelectorAll("img"))) expect(img.getAttribute("alt")).toBe("");
    // The link's own name is the only one: no image inside is exposed as an image.
    expect(screen.queryAllByRole("img")).toHaveLength(0);
  });

  it("is a 44px target and adds no tab stop of its own inside the link", () => {
    const link = renderMark();
    expect(link.className).toContain("min-h-11");
    expect(link.querySelectorAll("a, button, [tabindex]")).toHaveLength(0);
  });

  it("swaps to the micro mark at the measured width for each header (Tailwind classes on the wrappers)", () => {
    const cases = [[undefined, "359"], [390, "389"], [416, "415"]] as const;
    for (const [fullFrom, below] of cases) {
      const { container, unmount } = render(
        <MemoryRouter>
          <BrandMark fullFrom={fullFrom} />
        </MemoryRouter>,
      );
      const [full, micro] = Array.from(container.querySelectorAll("a > span"));
      expect(full.className).toContain(`max-[${below}px]:hidden`);
      expect(micro.className).toContain(`max-[${below}px]:inline-flex`);
      unmount();
    }
  });
});
