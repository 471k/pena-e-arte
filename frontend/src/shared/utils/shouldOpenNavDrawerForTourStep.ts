import type { TourStep } from "@/shared/components/OnboardingTour";

/**
 * Should a role layout's onboarding-tour step open its NavDrawer? Only for
 * a step whose target lives in the drawer (the `-nav"]` selector suffix
 * convention shared by every tour file), and only below the `lg` breakpoint
 * — at `lg`+ the target is already visible in the persistent desktop sidebar,
 * so opening the drawer there would just pop a redundant modal over it.
 */
export function shouldOpenNavDrawerForTourStep(step: TourStep): boolean {
  if (!step.targetSelector.endsWith('-nav"]')) return false;
  return typeof window !== "undefined" && !window.matchMedia("(min-width: 1024px)").matches;
}

const NAV_TOUR_ID = /\[data-tour="([^"]+-nav)"\]$/;

/**
 * The `data-tour` id of a nav-link step's target, or null for any other step.
 * A nav link can sit inside a collapsed sidebar group, so the sidebar uses this
 * to open the group holding the step's target before the tour measures it.
 */
export function navTourIdForStep(step: TourStep): string | null {
  return NAV_TOUR_ID.exec(step.targetSelector)?.[1] ?? null;
}
