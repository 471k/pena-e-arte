import { useCallback, useState } from "react";
import type { TourStep } from "@/shared/components/OnboardingTour";
import { navTourIdForStep, shouldOpenNavDrawerForTourStep } from "@/shared/utils/shouldOpenNavDrawerForTourStep";

/**
 * State every role layout shares for its navigation: the mobile drawer's open flag, plus the
 * onboarding-tour hook that reveals a step's nav target (opens the drawer on mobile, opens the
 * containing group / expands the icon rail on desktop) before the tour measures it.
 *
 * While the tour drives the drawer it is NON-modal (`navModal` false): a modal Radix Sheet marks the
 * rest of the page aria-hidden, blocks its pointer events and traps focus — which hid the tour's own
 * popover (portalled to <body>) from screen readers and its buttons from clicks. The drawer is modal
 * again once the tour ends or the user closes the drawer.
 */
export function useNavShell() {
  const [navOpen, setNavOpenState] = useState(false);
  const [revealTourId, setRevealTourId] = useState<string | null>(null);
  const [tourDriving, setTourDriving] = useState(false);

  const onBeforeTourStep = useCallback((step: TourStep) => {
    setTourDriving(true);
    setNavOpenState(shouldOpenNavDrawerForTourStep(step));
    setRevealTourId(navTourIdForStep(step));
  }, []);

  const onTourEnd = useCallback(() => {
    setTourDriving(false);
    setRevealTourId(null);
  }, []);

  const setNavOpen = useCallback((open: boolean) => {
    setNavOpenState(open);
    if (!open) setTourDriving(false);
  }, []);

  return { navOpen, setNavOpen, revealTourId, onBeforeTourStep, onTourEnd, navModal: !tourDriving };
}
