/**
 * Formats a money amount using the currency the source object itself carries — a studio's own
 * Currency for Flow A prices/payments (see architecture.md Decisions Log, "Studio currency"), or
 * PlanResponse.currency / PublicPlanResponse.currency for Flow B subscription prices — rather
 * than a currency hardcoded per component. This is the one place every money amount in the app
 * is formatted, so a currency this locale doesn't natively know (e.g. ALL, KWD) still renders
 * correctly via currencyMinorUnits below.
 *
 * Whole amounts read "€29"; fractional ones read their currency's full minor unit, e.g.
 * "€98.60" or "KWD 12.345" — never "€98.6".
 */

/** The app is English-only today (no i18n library in package.json — verified 2026-09-27).
 *  When a language switcher ships, this constant becomes the user's language; every call site
 *  already goes through here, so that is a one-line change. */
export const APP_LOCALE = "en";

export function currencyMinorUnits(currencyCode: string): number {
  return (
    new Intl.NumberFormat(APP_LOCALE, { style: "currency", currency: currencyCode }).resolvedOptions()
      .maximumFractionDigits ?? 2
  );
}

export function formatCurrency(amount: number, currencyCode: string): string {
  const digits = currencyMinorUnits(currencyCode);
  return new Intl.NumberFormat(APP_LOCALE, {
    style: "currency",
    currency: currencyCode,
    // Whole amounts stay compact ("€29", "ALL 5,000", "¥3,000"); fractional ones show the
    // currency's full minor unit ("€98.60", "KWD 12.345") — never "€98.6".
    minimumFractionDigits: Number.isInteger(amount) ? 0 : digits,
    maximumFractionDigits: digits,
  }).format(amount);
}

/** The symbol/code to put in an input label or adornment, e.g. "€", "ALL", "¥". */
export function currencyLabel(currencyCode: string): string {
  const parts = new Intl.NumberFormat(APP_LOCALE, { style: "currency", currency: currencyCode }).formatToParts(0);
  return parts.find((p) => p.type === "currency")?.value ?? currencyCode;
}
