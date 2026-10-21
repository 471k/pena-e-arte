import { describe, it, expect } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useNavShell } from "@/shared/hooks/useNavShell";
import type { TourStep } from "@/shared/components/OnboardingTour";

const NAV_STEP: TourStep = { targetSelector: '[data-tour="owner-dashboard-nav"]', title: "Your dashboard", body: "…" };

describe("useNavShell", () => {
  it("keeps the nav drawer modal outside the tour", () => {
    const { result } = renderHook(() => useNavShell());
    expect(result.current.navModal).toBe(true);
  });

  it("makes the drawer non-modal while the tour drives it, so the tour stays visible and clickable", () => {
    const { result } = renderHook(() => useNavShell());
    act(() => result.current.onBeforeTourStep(NAV_STEP));
    expect(result.current.navModal).toBe(false);
  });

  it("makes the drawer modal again when the tour ends", () => {
    const { result } = renderHook(() => useNavShell());
    act(() => result.current.onBeforeTourStep(NAV_STEP));
    act(() => result.current.onTourEnd());
    expect(result.current.navModal).toBe(true);
    expect(result.current.revealTourId).toBeNull();
  });

  it("makes the drawer modal again when the user closes it", () => {
    const { result } = renderHook(() => useNavShell());
    act(() => result.current.onBeforeTourStep(NAV_STEP));
    act(() => result.current.setNavOpen(false));
    expect(result.current.navModal).toBe(true);
    expect(result.current.navOpen).toBe(false);
  });
});
