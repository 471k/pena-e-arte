/**
 * For Radix layers (e.g. the mobile NavDrawer's Sheet): true when an "outside" interaction is really on
 * the onboarding tour, which OnboardingTour portals to <body> and marks with data-onboarding-tour.
 */
export function isOnboardingTourEvent(target: EventTarget | null): boolean {
  return target instanceof Element && target.closest("[data-onboarding-tour]") !== null;
}
