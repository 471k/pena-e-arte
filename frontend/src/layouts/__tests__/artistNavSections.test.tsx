import { describe, it, expect } from "vitest";
import { flattenNavItems, isNavGroup, withNavBadges } from "@/shared/utils/navSections";
import { buildArtistSections } from "@/layouts/artistNavSections";

const BASE = {
  scheduleHref:  "/schedule",
  designsHref:   "/designs",
  reportsHref:   "/conduct-reports",
  portfolioHref: "/artists/a1",
} as const;

describe("buildArtistSections", () => {
  it("labels the group that holds Clients/Messages/Waitlist 'People' so it no longer echoes its own first item", () => {
    const sections = buildArtistSections({ ...BASE });
    const group = sections.find((s) => s.id === "clients");

    expect(group?.label).toBe("People");
    expect(group?.entries.map((e) => (isNavGroup(e) ? e.label : e.label))).toEqual(["Clients", "Messages", "Waitlist"]);
    expect(sections.map((s) => s.label)).not.toContain("Clients");
  });

  it("renames 'Reports About Me' to 'Conduct Reports' without touching its href or tour id", () => {
    const withTour = flattenNavItems(buildArtistSections({ ...BASE, tourIds: true }));
    const withoutTour = flattenNavItems(buildArtistSections({ ...BASE, tourIds: false }));

    const item = withTour.find((i) => i.label === "Conduct Reports");
    expect(item?.href).toBe("/conduct-reports");
    expect(item?.tourId).toBe("artist-conduct-reports-nav");
    expect(withTour.some((i) => i.label === "Reports About Me")).toBe(false);

    // OwnerLayout's artist mode builds the same sections without tour ids.
    expect(withoutTour.find((i) => i.label === "Conduct Reports")?.tourId).toBeUndefined();
  });

  it("keeps the other tour ids on the items they were on", () => {
    const items = flattenNavItems(buildArtistSections({ ...BASE, tourIds: true }));

    expect(items.find((i) => i.label === "Clients")?.tourId).toBe("artist-clients-nav");
    expect(items.find((i) => i.label === "Messages")?.tourId).toBe("artist-messages-nav");
    expect(items.find((i) => i.label === "Waitlist")?.tourId).toBe("artist-waitlist-nav");
    expect(items.find((i) => i.label === "Schedule")?.tourId).toBe("artist-schedule-nav");
    expect(items.find((i) => i.label === "My Earnings")?.tourId).toBe("artist-earnings-nav");
  });

  it("carries the open-report count onto the renamed item (the badge map is keyed by label)", () => {
    const sections = withNavBadges(buildArtistSections({ ...BASE }), { "Conduct Reports": 3 });

    expect(flattenNavItems(sections).find((i) => i.label === "Conduct Reports")?.badge).toBe(3);
  });
});
