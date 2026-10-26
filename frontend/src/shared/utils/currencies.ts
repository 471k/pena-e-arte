/**
 * Currency reference data for the studio-currency picker (registration + Studio Settings). A
 * studio may only choose ALL (primary, the default) or EUR (secondary), the two POK supports. The
 * server-side validator (StudioCurrencies.IsOffered) is authoritative and mirrors this list. See
 * architecture.md Decisions Log, "Studio currency: ALL and EUR only (2026-10-26)".
 */

/** The currencies a studio may choose, in picker order: primary first. */
export const STUDIO_CURRENCIES = ["ALL", "EUR"] as const;

export interface CurrencyOption {
  code: string;
  name: string;
}

const currencyDisplayNames = new Intl.DisplayNames(["en"], { type: "currency" });

/** Full English name for a currency code (e.g. "ALL" -> "Albanian Lek"), for read-only display
 * where `currencyLabel`'s bare symbol (formatCurrency.ts — "€", or the code itself for
 * symbol-less currencies like "ALL") would be ambiguous or look like a typo. */
export function currencyDisplayName(currencyCode: string): string {
  return currencyDisplayNames.of(currencyCode) ?? currencyCode;
}

/**
 * [ALL, EUR] — what the registration, sign-up and Studio Settings pickers render. A studio created
 * before 2026-10-26 may still hold another currency: pass it as `current` and it is listed last,
 * so the picker shows the studio's real value, but it can only be changed to ALL or EUR.
 */
export function buildCurrencyOptions(current: string | null = null): CurrencyOption[] {
  const codes: string[] = [...STUDIO_CURRENCIES];
  if (current && !codes.includes(current)) codes.push(current);
  return codes.map((code) => ({ code, name: currencyDisplayName(code) }));
}
