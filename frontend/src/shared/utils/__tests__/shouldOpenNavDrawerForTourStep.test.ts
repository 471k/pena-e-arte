import { describe, it, expect, afterEach } from "vitest";
import { shouldOpenNavDrawerForTourStep } from "../shouldOpenNavDrawerForTourStep";
import type { TourStep } from "@/shared/components/OnboardingTour";

const originalMatchMedia = window.matchMedia;
afterEach(() => { window.matchMedia = originalMatchMedia; });

function stubViewport(matchesMinWidth1024: boolean) {
  window.matchMedia = ((query: string) => ({
    matches: query.includes("min-width") ? matchesMinWidth1024 : false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as typeof window.matchMedia;
}

function stubWidth(px: number) {
  window.matchMedia = ((query: string) => ({
    matches: Number(/min-width:\s*(\d+)px/.exec(query)?.[1] ?? Infinity) <= px,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  })) as typeof window.matchMedia;
}

const navStep: TourStep = { targetSelector: '[data-tour="owner-dashboard-nav"]', title: "t", body: "b" };
const nonNavStep: TourStep = { targetSelector: '[data-tour="owner-help-button"]', title: "t", body: "b" };

describe("shouldOpenNavDrawerForTourStep", () => {
  it("returns true for a nav step below the lg breakpoint", () => {
    stubViewport(false);
    expect(shouldOpenNavDrawerForTourStep(navStep)).toBe(true);
  });

  it("returns false for a nav step at lg and above (desktop nav is already visible)", () => {
    stubViewport(true);
    expect(shouldOpenNavDrawerForTourStep(navStep)).toBe(false);
  });

  it("returns false for a non-nav step regardless of viewport", () => {
    stubViewport(false);
    expect(shouldOpenNavDrawerForTourStep(nonNavStep)).toBe(false);
  });

  // The owner's Owner/Artist switch: in the header from sm (640px), in the drawer below.
  const switchStep: TourStep = {
    targetSelector: '[data-tour="owner-artist-mode-switch"]', title: "t", body: "b", inNavDrawerBelowSm: true,
  };

  it("opens the drawer for a header-from-sm step on a phone", () => {
    stubWidth(390);
    expect(shouldOpenNavDrawerForTourStep(switchStep)).toBe(true);
  });

  it("leaves the drawer shut for a header-from-sm step between sm and lg, where the header copy shows", () => {
    stubWidth(800);
    expect(shouldOpenNavDrawerForTourStep(switchStep)).toBe(false);
  });

  it("leaves the drawer shut for a header-from-sm step at lg and above", () => {
    stubWidth(1280);
    expect(shouldOpenNavDrawerForTourStep(switchStep)).toBe(false);
  });
});
