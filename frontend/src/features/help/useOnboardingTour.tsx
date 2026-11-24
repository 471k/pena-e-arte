import { useState } from "react";
import { useGetOnboardingTourStatusQuery, useMarkOnboardingTourCompleteMutation } from "./onboardingApi";
import { useGetMyStudiosQuery } from "@/features/auth/authApi";
import { OnboardingTour, type TourStep } from "@/shared/components/OnboardingTour";
import { getClientTourSteps } from "./tours/clientTour";
import { artistTourSteps } from "./tours/artistTour";
import { getOwnerTourSteps, type OwnerTourFacts } from "./tours/ownerTour";
import { adminTourSteps } from "./tours/adminTour";
import { foldHelpStep } from "./tours/headerFold";
import { Role } from "@/shared/types/roles";

function getStepsForRole(
  role: Role, hasMultipleStudios: boolean, showPaymentMethods: boolean, ownerTourFacts: OwnerTourFacts | undefined,
): TourStep[] {
  switch (role) {
    case Role.Client: return getClientTourSteps(hasMultipleStudios, showPaymentMethods);
    case Role.Artist: return artistTourSteps;
    // Only reached once the facts are known (the tour waits for them); the fallback is never shown.
    case Role.Owner:  return ownerTourFacts ? getOwnerTourSteps(ownerTourFacts) : [];
    case Role.Admin: return adminTourSteps;
  }
}

/** `showPaymentMethods`: whether the client nav has its "Payment Methods" entry (saved cards on,
 * C-9). ClientLayout knows from its capabilities query and passes it down through HelpMenu, so the
 * tour never points at an entry that isn't there.
 * `ownerTourFacts`: whether each conditional owner step's target will render, `undefined` while any of it
 * isn't known yet. OwnerLayout passes the same values that render those targets, for the same reason. */
export function useOnboardingTour(
  role: Role | null, onBeforeStep?: (step: TourStep) => void, onTourEnd?: () => void,
  showPaymentMethods = false, ownerTourFacts?: OwnerTourFacts, headerFolded = false,
) {
  const [forceActive, setForceActive] = useState(false);
  // Hides the tour immediately on skip/complete without waiting for the
  // invalidated status query to refetch over the network.
  const [dismissed, setDismissed] = useState(false);
  const { data: studios } = useGetMyStudiosQuery(undefined, { skip: role !== Role.Client });
  const { data: status } = useGetOnboardingTourStatusQuery(
    { role: role ?? "" },
    { skip: !role },
  );
  const [markComplete] = useMarkOnboardingTourCompleteMutation();
  // Four owner steps depend on async queries (artist profile, studio, join invites). The tour waits until all
  // of it is known rather than letting OnboardingTour time steps out: a timeout can't tell "not rendered yet"
  // from "never will be". It skipped the Owner/Artist switch whenever /artists/me was slow, and cost every
  // ordinary owner two dead seconds on the banner and bell steps.
  const ownerFactsKnown = role !== Role.Owner || ownerTourFacts !== undefined;

  const shouldShow = !!role && !dismissed && ownerFactsKnown
    && (forceActive || (status !== undefined && !status.hasCompletedTour));

  function finish() {
    setDismissed(true);
    setForceActive(false);
    onTourEnd?.();
    if (role) markComplete({ role }).unwrap().catch(() => {});
  }

  function restartTour() {
    setDismissed(false);
    setForceActive(true);
  }

  // The header's fold is the same value that renders the Help button or "More", so the last step points at
  // whichever is there (headerFold.ts).
  const steps = role
    ? foldHelpStep(getStepsForRole(role, (studios?.length ?? 0) > 1, showPaymentMethods, ownerTourFacts), headerFolded)
    : [];

  const tourElement = shouldShow && role
    ? <OnboardingTour steps={steps} onComplete={finish} onSkip={finish} onBeforeStep={onBeforeStep} />
    : null;

  return { tourElement, restartTour };
}
