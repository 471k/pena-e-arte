import { describe, it, expect } from "vitest";
import { foldHelpStep, HEADER_MORE_TOUR_ID } from "../tours/headerFold";
import { getOwnerTourSteps } from "../tours/ownerTour";
import { getClientTourSteps } from "../tours/clientTour";
import { artistTourSteps } from "../tours/artistTour";
import { adminTourSteps } from "../tours/adminTour";

const MORE = `[data-tour="${HEADER_MORE_TOUR_ID}"]`;
const OWNER = getOwnerTourSteps({ hasArtistProfile: true, showsPublishBanner: false, hasJoinInvite: false });
const CLIENT = getClientTourSteps(false, false);

describe("foldHelpStep (S05: the tour when the header folds)", () => {
  it.each([
    ["owner", OWNER],
    ["artist", artistTourSteps],
    ["client", CLIENT],
    ["admin", adminTourSteps],
  ] as const)("%s: the Help step points at the Help button with room, and at More when folded", (role, steps) => {
    const help = `[data-tour="${role}-help-button"]`;
    expect(steps.some((s) => s.targetSelector === help)).toBe(true);

    expect(foldHelpStep(steps, false)).toEqual(steps);

    const folded = foldHelpStep(steps, true);
    expect(folded).toHaveLength(steps.length);
    expect(folded.some((s) => s.targetSelector === help)).toBe(false);
    const moved = folded.find((s) => s.targetSelector === MORE);
    expect(moved?.body).toMatch(/^Help is under More\./);
  });

  it("moves only the Help step: every other step is the same object, in the same place", () => {
    const folded = foldHelpStep(OWNER, true);
    OWNER.forEach((step, i) => {
      if (step.targetSelector !== '[data-tour="owner-help-button"]') expect(folded[i]).toBe(step);
    });
  });
});
