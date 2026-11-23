import { describe, it, expect } from "vitest";
import { getOwnerTourSteps, ownerTourSteps } from "../tours/ownerTour";

const CTA = '[data-tour="owner-become-artist-cta"]';
const SWITCH = '[data-tour="owner-artist-mode-switch"]';
const selectors = (hasArtistProfile: boolean) => getOwnerTourSteps(hasArtistProfile).map((s) => s.targetSelector);

describe("getOwnerTourSteps", () => {
  it("an owner with an artist profile gets the Owner/Artist switch and not the become-an-artist step", () => {
    expect(selectors(true)).toContain(SWITCH);
    expect(selectors(true)).not.toContain(CTA);
  });

  it("an owner without one gets the become-an-artist step and not the switch", () => {
    expect(selectors(false)).toContain(CTA);
    expect(selectors(false)).not.toContain(SWITCH);
  });

  it("leaves every other step, in order", () => {
    const others = ownerTourSteps.map((s) => s.targetSelector).filter((s) => s !== CTA && s !== SWITCH);
    expect(selectors(true).filter((s) => s !== SWITCH)).toEqual(others);
    expect(selectors(false).filter((s) => s !== CTA)).toEqual(others);
  });
});
