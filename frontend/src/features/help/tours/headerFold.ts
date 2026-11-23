import type { TourStep } from "@/shared/components/OnboardingTour";

/** The folded header's "More" trigger (HeaderActions). */
export const HEADER_MORE_TOUR_ID = "header-more";

const HELP_BUTTON = /^\[data-tour="(owner|artist|client|admin)-help-button"\]$/;

/**
 * Every role's tour ends on the Help button. When the header is folded that button isn't rendered, and Help is
 * under "More", so the step points there and says so. Decided from the same `folded` value that renders the
 * header (#430/#438's rule), so the tour can't wait on a target that will never appear.
 */
export function foldHelpStep(steps: TourStep[], headerFolded: boolean): TourStep[] {
  if (!headerFolded) return steps;
  return steps.map((step) => HELP_BUTTON.test(step.targetSelector)
    ? {
        ...step,
        targetSelector: `[data-tour="${HEADER_MORE_TOUR_ID}"]`,
        // True for every role that folds (a client's More has no feedback item, so it isn't named).
        body: "Help is under More. Open it any time for searchable guides and FAQ — or press Shift+? from anywhere.",
      }
    : step);
}
