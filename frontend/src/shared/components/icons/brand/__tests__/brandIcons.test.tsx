import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { InstagramIcon, TikTokIcon, FacebookIcon, XIcon, YouTubeIcon } from "@/shared/components/icons/brand";
import { SOCIAL_PLATFORM_ICON, SOCIAL_PLATFORM_FALLBACK_ICON } from "@/shared/utils/socialPlatforms";

const ICONS = { InstagramIcon, TikTokIcon, FacebookIcon, XIcon, YouTubeIcon };

describe("brand icons", () => {
  it.each(Object.entries(ICONS))("%s is a decorative, currentColor, 20px svg with real path data", (_name, Icon) => {
    const { container } = render(<Icon />);
    const svg = container.querySelector("svg");

    expect(svg).not.toBeNull();
    expect(svg).toHaveAttribute("aria-hidden", "true");
    expect(svg).toHaveAttribute("fill", "currentColor");
    expect(svg).toHaveAttribute("width", "20");
    expect(svg).toHaveAttribute("height", "20");
    expect(svg?.querySelector("path")?.getAttribute("d")?.length ?? 0).toBeGreaterThan(50);
  });

  it("honours size and className", () => {
    const { container } = render(<XIcon size={32} className="h-5 w-5" />);
    const svg = container.querySelector("svg");

    expect(svg).toHaveAttribute("width", "32");
    expect(svg).toHaveClass("h-5", "w-5");
  });

  it("maps every platform to a distinct brand icon (no Hash/Globe/Music2/Video placeholders)", () => {
    const mapped = ["Instagram", "TikTok", "Facebook", "X", "YouTube"].map((p) => SOCIAL_PLATFORM_ICON[p]);

    expect(new Set(mapped).size).toBe(5);
    expect(mapped).toEqual([InstagramIcon, TikTokIcon, FacebookIcon, XIcon, YouTubeIcon]);
    expect(SOCIAL_PLATFORM_FALLBACK_ICON).toBeDefined();
  });
});
