/**
 * Formats a money amount using the currency the source object itself carries
 * (PlanResponse.currency / PublicPlanResponse.currency, both sourced from the backend's
 * MrrRules.PlatformCurrency — see architecture.md Decisions Log, "Plan currency — one source
 * of truth") rather than a currency hardcoded per component. This is the one place every
 * plan/subscription price in the app is formatted: the public Pricing page, the owner's
 * Subscribe and Billing pages, and the admin Plan editor.
 *
 * Whole amounts read "€29"; fractional ones read "€98.60", never "€98.6".
 */
export function formatCurrency(amount: number, currencyCode: string): string {
  return new Intl.NumberFormat("en", {
    style: "currency",
    currency: currencyCode,
    minimumFractionDigits: Number.isInteger(amount) ? 0 : 2,
    maximumFractionDigits: 2,
  }).format(amount);
}
