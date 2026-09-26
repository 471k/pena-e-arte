import type { PublicPlanResponse } from "./publicApi";

function formatCount(count: number): string {
  return count.toLocaleString("en");
}

function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

// The Pricing page's feature bullets, derived from what each tier really gates (its
// limits and feature flags from GET /public/plans) so they cannot drift from the
// product. A null limit means unlimited. PrioritySupport is never listed: it has no
// implementation behind it.
//
// TODO(marketing): replace with approved copy once the marketing project (to-do
// section 5) delivers it. The numbers and flags stay live; only the wording is a
// placeholder.
export function planHighlights(plan: PublicPlanResponse): string[] {
  const items: string[] = [];

  items.push(
    plan.maxArtists === null
      ? "Unlimited artists"
      : `Up to ${plural(plan.maxArtists, "artist", "artists")}`,
  );
  items.push(
    plan.maxAppointmentsPerMonth === null
      ? "Unlimited appointments"
      : `Up to ${formatCount(plan.maxAppointmentsPerMonth)} appointments per month`,
  );
  items.push(
    plan.maxNotificationsPerMonth === null
      ? "Unlimited reminders and notifications"
      : `Up to ${formatCount(plan.maxNotificationsPerMonth)} reminders and notifications per month`,
  );
  if (plan.maxStorageGb !== null) items.push(`${plan.maxStorageGb} GB of file storage`);
  if (plan.maxLocations !== null) {
    items.push(`Up to ${plural(plan.maxLocations, "location", "locations")}`);
  }
  if (plan.allowMarketingCampaigns) items.push("Email marketing campaigns");
  if (plan.allowBrandingRemoval) items.push("Remove TattooOS branding");
  if (plan.allowApiAccess) items.push("API keys and webhooks");

  return items;
}
