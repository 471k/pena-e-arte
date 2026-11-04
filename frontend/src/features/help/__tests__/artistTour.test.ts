import { describe, it, expect } from "vitest";
import { artistTourSteps } from "../tours/artistTour";
import { buildArtistSections } from "@/layouts/artistNavSections";
import { flattenNavItems } from "@/shared/utils/navSections";
import { HELP_ARTICLES } from "../helpContent";

describe("artistTourSteps", () => {
  it("opens on Today, the artist's daily entry point, just before the schedule (E04a)", () => {
    expect(artistTourSteps[0].targetSelector).toBe('[data-tour="artist-today-nav"]');
    expect(artistTourSteps[1].targetSelector).toBe('[data-tour="artist-schedule-nav"]');
    expect(artistTourSteps[0].body).toMatch(/preparation/i);
  });

  it("targets a real nav item for every nav step", () => {
    const navTourIds = flattenNavItems(buildArtistSections({
      scheduleHref: "/schedule", designsHref: "/designs", reportsHref: "/conduct-reports",
      portfolioHref: "/artists/a1", notifications: true, tourIds: true,
    })).map((i) => i.tourId).filter(Boolean);

    for (const step of artistTourSteps.filter((s) => s.targetSelector.endsWith('-nav"]'))) {
      const id = /data-tour="([^"]+)"/.exec(step.targetSelector)?.[1];
      expect(navTourIds).toContain(id);
    }
  });
});

describe("Today in Help", () => {
  it("has an artist article that the schedule article links to", () => {
    const today = HELP_ARTICLES.find((a) => a.id === "artist-today");
    expect(today?.route).toBe("/today");
    expect(today?.tips?.join(" ")).toMatch(/never blocks/i);
    expect(HELP_ARTICLES.find((a) => a.id === "artist-schedule")?.relatedArticleIds).toContain("artist-today");
  });
});
