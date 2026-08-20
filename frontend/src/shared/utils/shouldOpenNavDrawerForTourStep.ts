import type { TourStep } from "@/shared/components/OnboardingTour";

/**
 * Should a role layout's onboarding-tour step open its NavDrawer? Only for
 * a step whose target lives in the drawer (the `-nav"]` selector suffix
 * convention shared by every tour file), and only below the `lg` breakpoint
 * — at `lg`+ the target is already visible in the persistent desktop nav,
 * so opening the drawer there would just pop a redundant modal over it.
 */
export function shouldOpenNavDrawerForTourStep(step: TourStep): boolean {
  if (!step.targetSelector.endsWith('-nav"]')) return false;
  return typeof window !== "undefined" && !window.matchMedia("(min-width: 1024px)").matches;
}
