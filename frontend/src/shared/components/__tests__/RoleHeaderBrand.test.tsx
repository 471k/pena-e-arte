import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { RoleHeaderBrand } from "@/shared/components/RoleHeaderBrand";

// Brand master 1.0.0: the owner/artist/client header brand. jsdom has no layout, so the
// sub-360px swap itself is a CSS rule checked in the browser pass; this pins the markup it needs.

describe("RoleHeaderBrand", () => {
  it("unfolded: one name, the small lockup and the micro mark both present for the 360px swap", () => {
    const { container } = render(<RoleHeaderBrand folded={false} fit="owner" />);
    expect(screen.getAllByRole("img", { name: "TattooOS" })).toHaveLength(1);
    const brand = screen.getByRole("img", { name: "TattooOS" });
    expect(brand.className).toContain("tattooos-role-brand");
    expect(brand.className).toContain("shrink-0");
    // The measured-width swap (TattooOSLogo.css) keys off this attribute.
    expect(brand.getAttribute("data-fit")).toBe("owner");
    expect(container.querySelector(".tattooos-role-brand__full img")?.getAttribute("src"))
      .toBe("/brand/after-hours/1.0.0/lockups/small-on-light.svg");
    expect(container.querySelector(".tattooos-role-brand__micro img")?.getAttribute("src"))
      .toBe("/brand/after-hours/1.0.0/marks/cartridge-micro-on-light.svg");
    for (const img of Array.from(container.querySelectorAll("img"))) expect(img.getAttribute("alt")).toBe("");
  });

  it("folded: the 20px micro mark only, still named once", () => {
    const { container } = render(<RoleHeaderBrand folded fit="client" />);
    expect(screen.getAllByRole("img", { name: "TattooOS" })).toHaveLength(1);
    const imgs = container.querySelectorAll("img");
    expect(imgs).toHaveLength(1);
    expect(imgs[0].getAttribute("src")).toBe("/brand/after-hours/1.0.0/marks/cartridge-micro-on-light.svg");
    expect((container.querySelector("picture") as HTMLElement).style.height).toBe("20px");
    expect(container.querySelector(".tattooos-role-brand__full")).toBeNull();
  });

  it("is not interactive in either state", () => {
    for (const folded of [false, true]) {
      const { container, unmount } = render(<RoleHeaderBrand folded={folded} fit="artist" />);
      expect(container.querySelectorAll("a, button, [tabindex]")).toHaveLength(0);
      unmount();
    }
  });
});
