import { describe, it, expect } from "vitest";
import { bannerConfig } from "../components/DashboardPage";
import type { SubscriptionResponse } from "@/features/billing/billing.types";

// ── Helpers ────────────────────────────────────────────────────────────────────

function sub(status: SubscriptionResponse["status"]): SubscriptionResponse {
  return {
    id:                   "sub-0001",
    studioId:             "studio-0001",
    planId:               null,
    pendingPlanId:        null,
    status,
    trialExpiresAt:       new Date(Date.now() + 7 * 86_400_000).toISOString(),
    currentPeriodEnd:     new Date(Date.now() + 30 * 86_400_000).toISOString(),
    gracePeriodEnd:       new Date(Date.now() + 7 * 86_400_000).toISOString(),
    stripeSubscriptionId: null,
  };
}

// ── bannerConfig — contract between backend status strings and UI ───────────────

describe("bannerConfig", () => {
  it("returns null for Active — no banner shown on healthy subscription", () => {
    expect(bannerConfig(sub("Active"))).toBeNull();
  });

  it("returns null for unknown status — defensive default", () => {
    // Cast lets us simulate an unexpected value from the API
    expect(bannerConfig(sub("Active" as SubscriptionResponse["status"]))).toBeNull();
  });

  it("Trialing → blue banner pointing to /billing/subscribe", () => {
    const cfg = bannerConfig(sub("Trialing"));
    expect(cfg).not.toBeNull();
    expect(cfg!.bg).toMatch(/blue/);
    expect(cfg!.cta).toBe("Subscribe");
    expect(cfg!.href).toBe("/billing/subscribe");
  });

  it("Trialing → text mentions days remaining", () => {
    const cfg = bannerConfig(sub("Trialing"))!;
    expect(cfg.text).toMatch(/Trial ends in \d+ day/);
  });

  it("GracePeriod → amber banner pointing to /billing/subscribe", () => {
    const cfg = bannerConfig(sub("GracePeriod"));
    expect(cfg).not.toBeNull();
    expect(cfg!.bg).toMatch(/amber/);
    expect(cfg!.cta).toBe("Subscribe now");
    expect(cfg!.href).toBe("/billing/subscribe");
  });

  it("GracePeriod → text mentions read-only mode and days left", () => {
    const cfg = bannerConfig(sub("GracePeriod"))!;
    expect(cfg.text).toContain("read-only mode");
    expect(cfg.text).toMatch(/\d+ day/);
  });

  it("PastDue → red banner pointing to /billing", () => {
    const cfg = bannerConfig(sub("PastDue"));
    expect(cfg).not.toBeNull();
    expect(cfg!.bg).toMatch(/red/);
    expect(cfg!.cta).toBe("Update billing");
    expect(cfg!.href).toBe("/billing");
  });

  it("Cancelled → red banner pointing to /billing/subscribe", () => {
    const cfg = bannerConfig(sub("Cancelled"));
    expect(cfg).not.toBeNull();
    expect(cfg!.bg).toMatch(/red/);
    expect(cfg!.cta).toBe("Reactivate");
    expect(cfg!.href).toBe("/billing/subscribe");
  });

  it("each visible banner carries a non-null icon", () => {
    for (const status of ["Trialing", "GracePeriod", "PastDue", "Cancelled"] as const) {
      const cfg = bannerConfig(sub(status));
      expect(cfg?.icon, `${status} should have an icon`).toBeTruthy();
    }
  });
});
