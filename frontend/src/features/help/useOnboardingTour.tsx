import { useState } from "react";
import { useGetOnboardingTourStatusQuery, useMarkOnboardingTourCompleteMutation } from "./onboardingApi";
import { useGetMyStudiosQuery } from "@/features/auth/authApi";
import { OnboardingTour, type TourStep } from "@/shared/components/OnboardingTour";
import { getClientTourSteps } from "./tours/clientTour";
import { artistTourSteps } from "./tours/artistTour";
import { ownerTourSteps } from "./tours/ownerTour";
import { adminTourSteps } from "./tours/adminTour";
import { Role } from "@/shared/types/roles";

function getStepsForRole(role: Role, hasMultipleStudios: boolean, showPaymentMethods: boolean): TourStep[] {
  switch (role) {
    case Role.Client: return getClientTourSteps(hasMultipleStudios, showPaymentMethods);
    case Role.Artist: return artistTourSteps;
    case Role.Owner:  return ownerTourSteps;
    case Role.Admin: return adminTourSteps;
  }
}

/** `showPaymentMethods`: whether the client nav has its "Payment Methods" entry (saved cards on,
 * C-9). ClientLayout knows from its capabilities query and passes it down through HelpMenu, so the
 * tour never points at an entry that isn't there. */
export function useOnboardingTour(
  role: Role | null, onBeforeStep?: (step: TourStep) => void, onTourEnd?: () => void,
  showPaymentMethods = false,
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

  const shouldShow = !!role && !dismissed && (forceActive || (status !== undefined && !status.hasCompletedTour));

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

  const steps = role
    ? getStepsForRole(role, (studios?.length ?? 0) > 1, showPaymentMethods)
    : [];

  const tourElement = shouldShow && role
    ? <OnboardingTour steps={steps} onComplete={finish} onSkip={finish} onBeforeStep={onBeforeStep} />
    : null;

  return { tourElement, restartTour };
}
