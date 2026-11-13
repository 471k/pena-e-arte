/**
 * Studio Settings, grouped by the job being configured (E05c, O04). The cards themselves are unchanged:
 * same forms, same saves, same API rules. Only where they sit on the page changed.
 *
 * Group names borrow what booking products already call these areas (Business details, Business hours,
 * Online booking, Payments, Notifications/Communications, Integrations), so an owner who has used one
 * recognises them. Which card went where was decided by reading each card, not its component name: the
 * "Platform branding" card is the "Powered by TattooOS" line on the booking widget, so it sits with the
 * widget, and the referral card is TattooOS's studio-to-studio referral, not part of the studio's profile.
 */

export const SETTINGS_GROUPS = [
  { id: "profile",       title: "Business details",        summary: "Studio URL, name, address, map location, timezone and social accounts" },
  { id: "hours",         title: "Hours and closures",      summary: "When clients can book, and dates the whole studio is closed" },
  { id: "money",         title: "Payments and artist pay", summary: "Currency, card deposits with POK, and how artists get paid" },
  { id: "booking",       title: "Online booking",          summary: "The booking widget for your website, its “Powered by TattooOS” line, and your QR code" },
  { id: "notifications", title: "Notifications",           summary: "Which emails and texts go out when something happens in your studio" },
  { id: "integrations",  title: "Integrations",            summary: "API access and webhooks, for your own tools or a developer" },
  { id: "referrals",     title: "Referral programme",      summary: "Your link for inviting other studios, and the free months it earns" },
  { id: "activity",      title: "Recent activity",         summary: "The latest recorded actions in your studio" },
] as const;

export type SettingsGroupId = (typeof SETTINGS_GROUPS)[number]["id"];

/**
 * Business details is always shown, at the top, and never collapses. It holds the page's own form, which the
 * NIPT and address banners send the owner into, and the studio's map: Leaflet in a `hidden` (0x0) container
 * computes its fly-to from a zero size and throws "Invalid LatLng object: (NaN, NaN)", which took the whole
 * page down when a link opened Studio Settings at another group (found in the 390 px browser check).
 * The other seven groups start closed.
 */
export const ALWAYS_SHOWN_GROUP: SettingsGroupId = "profile";

/** `/studios/me?section=<id>` opens that group (setup checklist, tour steps, Help's "Go to page"). */
export const SETTINGS_SECTION_PARAM = "section";

export function isSettingsGroupId(value: string | null): value is SettingsGroupId {
  return SETTINGS_GROUPS.some((g) => g.id === value);
}

/** The link that lands on Studio Settings with this group open. */
export function settingsSectionHref(id: SettingsGroupId): string {
  return `/studios/me?${SETTINGS_SECTION_PARAM}=${id}`;
}

export function settingsGroupElementId(id: SettingsGroupId): string {
  return `settings-group-${id}`;
}
