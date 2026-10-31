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

/**
 * A lek amount to show next to a euro amount, for clients of Albanian studios that price in EUR
 * (docs/specs/spec-eur-with-lek-display-2026-10-26.md). `approximate` before payment (latest Bank
 * of Albania rate); exact once frozen on the payment. `staleRateDate` ("yyyy-MM-dd") is set only
 * when the latest rate is over a week old, so the text can say which day's rate it is.
 */
export interface MoneyReference {
  amount:         number;
  currency:       string;
  approximate:    boolean;
  staleRateDate?: string | null;
}

/** Lek amounts are display-only and whole: EUR x rate, rounded once, half away from zero
 * (the backend's ReferenceAmount.ToWholeLek). Every stored lek figure comes from the backend. */
export function toWholeLek(eurAmount: number, rate: number): number {
  const lek = eurAmount * rate;
  return Math.sign(lek) * Math.round(Math.abs(lek));
}

/** True for a studio in Albania that prices in euros; every other studio sees no change. */
export function lekReferenceApplies(countryCode: string | null | undefined, currency: string | null | undefined): boolean {
  return countryCode?.toUpperCase() === "AL" && currency?.toUpperCase() === "EUR";
}

/**
 * formatCurrency plus the lek in brackets: "€50 (≈ ALL 4,537)" before payment, "€50 (ALL 4,537)"
 * once frozen, "€50 (≈ ALL 4,537 at the 23 Oct rate)" when the latest rate is stale. With no
 * reference it is exactly formatCurrency(amount, currencyCode).
 */
export function formatWithReference(amount: number, currencyCode: string, reference: MoneyReference | null): string {
  const main = formatCurrency(amount, currencyCode);
  if (!reference) return main;
  const lek = formatCurrency(reference.amount, reference.currency);
  const staleNote = reference.staleRateDate ? ` at the ${formatRateDate(reference.staleRateDate)} rate` : "";
  return `${main} (${reference.approximate ? "≈ " : ""}${lek}${staleNote})`;
}

/** "2026-10-23" -> "23 Oct", day first like the receipt's "23 Oct 2026" (a calendar date, formatted
 * in UTC so no time zone can shift the day). */
function formatRateDate(isoDate: string): string {
  const [year, month, day] = isoDate.split("-").map(Number);
  const monthName = new Intl.DateTimeFormat(APP_LOCALE, { month: "short", timeZone: "UTC" })
    .format(new Date(Date.UTC(year, month - 1, day)));
  return `${day} ${monthName}`;
}

/** The symbol/code to put in an input label or adornment, e.g. "€", "ALL", "¥". */
export function currencyLabel(currencyCode: string): string {
  const parts = new Intl.NumberFormat(APP_LOCALE, { style: "currency", currency: currencyCode }).formatToParts(0);
  return parts.find((p) => p.type === "currency")?.value ?? currencyCode;
}
