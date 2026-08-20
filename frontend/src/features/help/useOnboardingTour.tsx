import { useState } from "react";
import { useGetOnboardingTourStatusQuery, useMarkOnboardingTourCompleteMutation } from "./onboardingApi";
import { useGetMyStudiosQuery } from "@/features/auth/authApi";
import { OnboardingTour, type TourStep } from "@/shared/components/OnboardingTour";
import { getClientTourSteps } from "./tours/clientTour";
import { artistTourSteps } from "./tours/artistTour";
import { ownerTourSteps } from "./tours/ownerTour";
import { issuerTourSteps } from "./tours/issuerTour";
import { Role } from "@/shared/types/roles";

function getStepsForRole(role: Role, hasMultipleStudios: boolean): TourStep[] {
  switch (role) {
    case Role.Client: return getClientTourSteps(hasMultipleStudios);
    case Role.Artist: return artistTourSteps;
    case Role.Owner:  return ownerTourSteps;
    case Role.Issuer: return issuerTourSteps;
  }
}

export function useOnboardingTour(role: Role | null, onBeforeStep?: (step: TourStep) => void) {
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
    if (role) markComplete({ role }).unwrap().catch(() => {});
  }

  function restartTour() {
    setDismissed(false);
    setForceActive(true);
  }

  const steps = role ? getStepsForRole(role, (studios?.length ?? 0) > 1) : [];

  const tourElement = shouldShow && role
    ? <OnboardingTour steps={steps} onComplete={finish} onSkip={finish} onBeforeStep={onBeforeStep} />
    : null;

  return { tourElement, restartTour };
}
