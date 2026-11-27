import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { TattooOSLogo, type TattooOSLogoVariant } from "@/shared/components/TattooOSLogo";

// Brand master 1.0.0: the package's variant table, restated here as the expected values so a
// change to a file name or an intrinsic size fails a test rather than shipping silently.
const EXPECTED: Record<TattooOSLogoVariant, { path: string; width: number; height: number }> = {
  micro: { path: "/brand/after-hours/1.0.0/marks/cartridge-micro", width: 128, height: 128 },
  compact: { path: "/brand/after-hours/1.0.0/marks/cartridge-compact", width: 128, height: 128 },
  contour: { path: "/brand/after-hours/1.0.0/marks/cartridge-contour", width: 128, height: 128 },
  small: { path: "/brand/after-hours/1.0.0/lockups/small", width: 141, height: 24 },
  header: { path: "/brand/after-hours/1.0.0/lockups/header", width: 189, height: 32 },
  signature: { path: "/brand/after-hours/1.0.0/lockups/signature", width: 321, height: 72 },
  wordmark: { path: "/brand/after-hours/1.0.0/lockups/wordmark", width: 458, height: 82 },
  poweredBy: { path: "/brand/after-hours/1.0.0/lockups/powered-by", width: 104, height: 28 },
};

const VARIANT_NAMES = Object.keys(EXPECTED) as TattooOSLogoVariant[];

function imgOf(container: HTMLElement): HTMLImageElement {
  const img = container.querySelector("img");
  if (!img) throw new Error("no <img> rendered");
  return img;
}

describe("TattooOSLogo", () => {
  describe.each(VARIANT_NAMES)("variant %s", (variant) => {
    const expected = EXPECTED[variant];

    it("points at the package file and carries the SVG's intrinsic size", () => {
      const { container } = render(<TattooOSLogo variant={variant} />);
      const img = imgOf(container);
      expect(img.getAttribute("src")).toBe(`${expected.path}-on-light.svg`);
      expect(img.getAttribute("width")).toBe(String(expected.width));
      expect(img.getAttribute("height")).toBe(String(expected.height));
    });

    it("is named TattooOS (or Powered by TattooOS), and decorative empties the alt", () => {
      const named = render(<TattooOSLogo variant={variant} />);
      const want = variant === "poweredBy" ? "Powered by TattooOS" : "TattooOS";
      expect(imgOf(named.container).getAttribute("alt")).toBe(want);
      named.unmount();

      const decorative = render(<TattooOSLogo variant={variant} decorative />);
      expect(imgOf(decorative.container).getAttribute("alt")).toBe("");
    });

    it("follows prefers-color-scheme on surface=auto with a dark <source>", () => {
      const { container } = render(<TattooOSLogo variant={variant} surface="auto" />);
      const sources = container.querySelectorAll("source");
      expect(sources).toHaveLength(1);
      expect(sources[0].getAttribute("media")).toBe("(prefers-color-scheme: dark)");
      expect(sources[0].getAttribute("srcset")).toBe(`${expected.path}-on-dark.svg`);
    });

    it.each(["light", "dark"] as const)("renders no <source> and the on-%s file for surface=%s", (tone) => {
      const { container } = render(<TattooOSLogo variant={variant} surface={tone} />);
      expect(container.querySelectorAll("source")).toHaveLength(0);
      expect(imgOf(container).getAttribute("src")).toBe(`${expected.path}-on-${tone}.svg`);
    });

    it("is never interactive: no link, no tab stop, not draggable", () => {
      const { container } = render(<TattooOSLogo variant={variant} />);
      expect(container.querySelector("a")).toBeNull();
      expect(container.querySelector("[tabindex]")).toBeNull();
      expect(container.querySelector("button")).toBeNull();
      expect(imgOf(container).getAttribute("draggable")).toBe("false");
    });
  });

  it("sizes the box from the package's display height, keeping the aspect ratio", () => {
    const small = render(<TattooOSLogo variant="small" />);
    const smallBox = small.container.querySelector("picture") as HTMLElement;
    expect(smallBox.style.height).toBe("24px");
    expect(smallBox.style.width).toBe("141px");
    small.unmount();

    const header = render(<TattooOSLogo variant="header" />);
    const headerBox = header.container.querySelector("picture") as HTMLElement;
    expect(headerBox.style.height).toBe("32px");
    expect(headerBox.style.width).toBe("189px");
    header.unmount();

    const micro = render(<TattooOSLogo variant="micro" height={20} />);
    const microBox = micro.container.querySelector("picture") as HTMLElement;
    expect(microBox.style.height).toBe("20px");
    expect(microBox.style.width).toBe("20px");
  });

  it("defaults poweredBy to 124px wide (33.38px tall)", () => {
    const { container } = render(<TattooOSLogo variant="poweredBy" />);
    const box = container.querySelector("picture") as HTMLElement;
    expect(parseFloat(box.style.width)).toBeCloseTo(124, 5);
    expect(parseFloat(box.style.height)).toBeCloseTo(33.38, 2);
  });
});
