import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
import { LineAreaChart } from "../LineAreaChart";
import { RevenueTrendChart } from "@/features/reports/components/RevenueTrendChart";

// This theme (Tailwind v4, src/index.css) only defines --color-* tokens. There is no bare
// --primary/--background/--popover/--border, so hsl(var(--primary)) is INVALID CSS: the browser
// silently drops it — an SVG `fill` falls back to black (area fills became solid black blocks)
// and a `stroke` falls back to none (lines vanished). jsdom can't see the rendering, so guard
// the source of the bug directly. Found in a real-browser pass on 2026-09-24 (MrrChart,
// RevenueTrendChart and LineAreaChart all had it; see AdminDashboardPage.test.tsx for the MRR
// charts' own guard).
describe("chart SVGs use the theme's real color tokens", () => {
  it("LineAreaChart", () => {
    const { container } = render(
      <LineAreaChart
        data={[{ v: 10 }, { v: 30 }, { v: 20 }]}
        valueOf={(d) => d.v}
        labelOf={(_d, i) => String(i)}
        ariaLabel="test trend"
      />,
    );
    const html = container.innerHTML;
    expect(html).not.toContain("hsl(var(--");
    expect(html).toContain("var(--color-primary)");
  });

  it("RevenueTrendChart", () => {
    const { container } = render(
      <RevenueTrendChart
        data={[
          { month: "2026-07", revenue: 100 },
          { month: "2026-08", revenue: 250 },
          { month: "2026-09", revenue: 180 },
        ]}
      />,
    );
    const html = container.innerHTML;
    expect(html).not.toContain("hsl(var(--");
    expect(html).toContain("var(--color-primary)");
  });
});
