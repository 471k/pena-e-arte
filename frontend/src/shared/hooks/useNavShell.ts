import { useCallback, useState } from "react";
import type { TourStep } from "@/shared/components/OnboardingTour";
import { navTourIdForStep, shouldOpenNavDrawerForTourStep } from "@/shared/utils/shouldOpenNavDrawerForTourStep";

/**
 * State every role layout shares for its navigation: the mobile drawer's open flag, plus the
 * onboarding-tour hook that reveals a step's nav target (opens the drawer on mobile, opens the
 * containing group / expands the icon rail on desktop) before the tour measures it.
 */
export function useNavShell() {
  const [navOpen, setNavOpen] = useState(false);
  const [revealTourId, setRevealTourId] = useState<string | null>(null);

  const onBeforeTourStep = useCallback((step: TourStep) => {
    setNavOpen(shouldOpenNavDrawerForTourStep(step));
    setRevealTourId(navTourIdForStep(step));
  }, []);

  return { navOpen, setNavOpen, revealTourId, onBeforeTourStep };
}
