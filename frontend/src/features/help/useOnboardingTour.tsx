import { useState } from "react";
import { useGetOnboardingTourStatusQuery, useMarkOnboardingTourCompleteMutation } from "./onboardingApi";
import { useGetMyStudiosQuery } from "@/features/auth/authApi";
import { OnboardingTour, type TourStep } from "@/shared/components/OnboardingTour";
import { getClientTourSteps } from "./tours/clientTour";
import { artistTourSteps } from "./tours/artistTour";
import { getOwnerTourSteps } from "./tours/ownerTour";
import { adminTourSteps } from "./tours/adminTour";
import { Role } from "@/shared/types/roles";

function getStepsForRole(
  role: Role, hasMultipleStudios: boolean, showPaymentMethods: boolean, ownerHasArtistProfile: boolean,
): TourStep[] {
  switch (role) {
    case Role.Client: return getClientTourSteps(hasMultipleStudios, showPaymentMethods);
    case Role.Artist: return artistTourSteps;
    case Role.Owner:  return getOwnerTourSteps(ownerHasArtistProfile);
    case Role.Admin: return adminTourSteps;
  }
}

/** `showPaymentMethods`: whether the client nav has its "Payment Methods" entry (saved cards on,
 * C-9). ClientLayout knows from its capabilities query and passes it down through HelpMenu, so the
 * tour never points at an entry that isn't there.
 * `ownerHasArtistProfile`: whether the owner has their own artist profile, `undefined` while that isn't
 * known yet. OwnerLayout passes the same value that renders the Owner/Artist switch, for the same reason. */
export function useOnboardingTour(
  role: Role | null, onBeforeStep?: (step: TourStep) => void, onTourEnd?: () => void,
  showPaymentMethods = false, ownerHasArtistProfile?: boolean,
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
  // Two owner steps depend on whether the owner has their own artist profile. The tour waits until that is
  // known rather than starting with both steps and letting OnboardingTour time one out: a timeout can't tell
  // "not rendered yet" from "never will be", and skipped the Owner/Artist switch whenever /artists/me was slow.
  const ownerFactsKnown = role !== Role.Owner || ownerHasArtistProfile !== undefined;

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

  const steps = role
    ? getStepsForRole(role, (studios?.length ?? 0) > 1, showPaymentMethods, ownerHasArtistProfile ?? false)
    : [];

  const tourElement = shouldShow && role
    ? <OnboardingTour steps={steps} onComplete={finish} onSkip={finish} onBeforeStep={onBeforeStep} />
    : null;

  return { tourElement, restartTour };
}
