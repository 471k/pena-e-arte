import { describe, it, expect } from "vitest";
import { getOwnerTourSteps, ownerTourSteps, type OwnerTourFacts } from "../tours/ownerTour";

const CTA = '[data-tour="owner-become-artist-cta"]';
const SWITCH = '[data-tour="owner-artist-mode-switch"]';
const BANNER = '[data-tour="owner-solo-publish-banner"]';
const BELL = '[data-tour="owner-join-invite-bell"]';
const CONDITIONAL = [CTA, SWITCH, BANNER, BELL];

// The ordinary owner: no artist profile of their own, not a solo studio, no invite.
const ordinary: OwnerTourFacts = { hasArtistProfile: false, showsPublishBanner: false, hasJoinInvite: false };
const selectors = (facts: Partial<OwnerTourFacts>) =>
  getOwnerTourSteps({ ...ordinary, ...facts }).map((s) => s.targetSelector);

describe("getOwnerTourSteps", () => {
  it("an owner with an artist profile gets the Owner/Artist switch and not the become-an-artist step", () => {
    expect(selectors({ hasArtistProfile: true })).toContain(SWITCH);
    expect(selectors({ hasArtistProfile: true })).not.toContain(CTA);
  });

  it("an owner without one gets the become-an-artist step and not the switch", () => {
    expect(selectors({})).toContain(CTA);
    expect(selectors({})).not.toContain(SWITCH);
  });

  // The two steps that used to cost every ordinary owner a 1 s wait each before being skipped.
  it("the ordinary owner gets neither the publish-banner step nor the join-invite step", () => {
    expect(selectors({})).not.toContain(BANNER);
    expect(selectors({})).not.toContain(BELL);
  });

  it("each of those appears exactly when its target renders", () => {
    expect(selectors({ showsPublishBanner: true })).toContain(BANNER);
    expect(selectors({ showsPublishBanner: true })).not.toContain(BELL);
    expect(selectors({ hasJoinInvite: true })).toContain(BELL);
    expect(selectors({ hasJoinInvite: true })).not.toContain(BANNER);
  });

  it("leaves every other step, in order, whatever the facts", () => {
    const others = ownerTourSteps.map((s) => s.targetSelector).filter((s) => !CONDITIONAL.includes(s));
    for (const hasArtistProfile of [true, false])
      for (const showsPublishBanner of [true, false])
        for (const hasJoinInvite of [true, false])
          expect(selectors({ hasArtistProfile, showsPublishBanner, hasJoinInvite })
            .filter((s) => !CONDITIONAL.includes(s))).toEqual(others);
  });

  it("a solo owner with everything applicable keeps the original order of the conditional steps", () => {
    const all = selectors({ showsPublishBanner: true, hasJoinInvite: true });
    expect(all.indexOf(CTA)).toBeLessThan(all.indexOf(BANNER));
    expect(all.indexOf(BANNER)).toBeLessThan(all.indexOf(BELL));
  });
});
