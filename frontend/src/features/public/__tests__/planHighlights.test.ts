import { describe, it, expect } from "vitest";
import { planHighlights } from "@/features/public/planHighlights";
import type { PublicPlanResponse } from "@/features/public/publicApi";

const BASE: PublicPlanResponse = {
  name: "Test",
  currency: "EUR",
  prices: [{ interval: "Monthly", price: 10 }],
  yearlyMonthsFree: null,
  allowBrandingRemoval: false,
  allowMarketingCampaigns: false,
  allowApiAccess: false,
  maxArtists: 1,
  maxAppointmentsPerMonth: 15,
  maxNotificationsPerMonth: 50,
  maxStorageGb: 1,
  maxLocations: null,
};

describe("planHighlights", () => {
  it("lists the numeric limits and only the flags a plan actually has", () => {
    expect(planHighlights(BASE)).toEqual([
      "Up to 1 artist",
      "Up to 15 appointments per month",
      "Up to 50 reminders and notifications per month",
      "1 GB of file storage",
    ]);
  });

  it("groups thousands in large limits", () => {
    const highlights = planHighlights({ ...BASE, maxAppointmentsPerMonth: 1000, maxNotificationsPerMonth: 2500 });

    expect(highlights).toContain("Up to 1,000 appointments per month");
    expect(highlights).toContain("Up to 2,500 reminders and notifications per month");
  });

  it("treats a null limit as unlimited", () => {
    const highlights = planHighlights({
      ...BASE,
      maxArtists: null,
      maxAppointmentsPerMonth: null,
      maxNotificationsPerMonth: null,
      maxStorageGb: null,
    });

    expect(highlights).toEqual([
      "Unlimited artists",
      "Unlimited appointments",
      "Unlimited reminders and notifications",
    ]);
  });

  it("adds a bullet for each enabled feature flag", () => {
    const highlights = planHighlights({
      ...BASE,
      allowMarketingCampaigns: true,
      allowBrandingRemoval: true,
      allowApiAccess: true,
    });

    expect(highlights).toContain("Email marketing campaigns");
    expect(highlights).toContain("Remove TattooOS branding");
    expect(highlights).toContain("API keys and webhooks");
  });

  it("only mentions locations when the plan has a numeric location limit", () => {
    expect(planHighlights({ ...BASE, maxLocations: 3 })).toContain("Up to 3 locations");
    expect(planHighlights(BASE).some((h) => /location/i.test(h))).toBe(false);
  });
});
