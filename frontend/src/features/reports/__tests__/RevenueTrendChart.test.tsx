import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { RevenueTrendChart } from "@/features/reports/components/RevenueTrendChart";
import { monthLabelIndexes, MIN_LABEL_SPACING } from "@/features/reports/components/trendChartLayout";

afterEach(cleanup);

const YEAR = Array.from({ length: 12 }, (_, i) => ({
  month: `2026-${String(i + 1).padStart(2, "0")}`,
  revenue: (i + 1) * 100,
}));

// E05b-1, finding 4: the chart is drawn at its container's pixel width, so text keeps its size and the month
// labels thin out when they would collide. jsdom has no layout; e2e/reports-chart-mobile.spec.ts measures it.
describe("monthLabelIndexes", () => {
  it("labels every month when there's room (desktop: a ~630px plot, 57px apart)", () => {
    expect([...monthLabelIndexes(12, 630)].sort((a, b) => a - b)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
  });

  it("on a phone labels every other month, counted back from the newest, which is always labelled", () => {
    // 390px phone, card padding: the plot is ~254px, 23px a month.
    const shown = monthLabelIndexes(12, 254);
    expect([...shown].sort((a, b) => a - b)).toEqual([1, 3, 5, 7, 9, 11]);
  });

  it("never puts two labels closer than the minimum spacing", () => {
    for (const plot of [120, 200, 254, 320, 480, 630]) {
      const shown = [...monthLabelIndexes(12, plot)].sort((a, b) => a - b);
      const gap = plot / 11;
      for (let k = 1; k < shown.length; k++) {
        expect((shown[k] - shown[k - 1]) * gap).toBeGreaterThanOrEqual(MIN_LABEL_SPACING);
      }
      expect(shown.at(-1)).toBe(11);
    }
  });

  it("handles one point and none", () => {
    expect([...monthLabelIndexes(1, 254)]).toEqual([0]);
    expect(monthLabelIndexes(0, 254).size).toBe(0);
  });
});

describe("RevenueTrendChart", () => {
  it("draws every month's point with a title, even where its axis label is thinned out", () => {
    const { container } = render(<RevenueTrendChart data={YEAR} currency="EUR" />);
    expect(screen.getByRole("img", { name: /revenue trend/i })).toBeInTheDocument();
    const titles = Array.from(container.querySelectorAll("g > title")).map((t) => t.textContent);
    expect(titles).toHaveLength(12);
    expect(titles[0]).toMatch(/^Jan 2026: €100$/);
    expect(titles[11]).toMatch(/^Dec 2026: €1\.2k$/);
  });

  it("writes its text at a fixed 11px in a drawing that's as wide as its container", () => {
    const { container } = render(<RevenueTrendChart data={YEAR} currency="EUR" />);
    const svg = container.querySelector("svg")!;
    expect(svg.getAttribute("height")).toBe("168");
    const labels = screen.getAllByTestId("trend-month-label");
    expect(labels.length).toBeGreaterThan(0);
    for (const l of labels) expect(l.getAttribute("font-size")).toBe("11");
  });
});
