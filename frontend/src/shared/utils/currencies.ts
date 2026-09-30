/**
 * Currency reference data for the studio-currency picker (registration + Studio Settings). No
 * new npm package — `Intl.supportedValuesOf("currency")` gives the browser's own active-currency
 * list, and `currencyMinorUnits` (formatCurrency.ts) gives each one's decimal places. The
 * server-side validator (CurrencyCatalog.IsSupported) is authoritative: if a browser ever offers
 * a code the backend rejects, the registration/settings form shows the 400 message rather than
 * silently accepting it. See architecture.md Decisions Log, "Studio currency".
 */

/**
 * Non-tradable ISO 4217 codes to hide from the picker — a copy of
 * Pena_e_Arte.Domain/Money/CurrencyCatalog.cs's own exclusion list (precious metals, the IMF's
 * XDR, the "no currency" code XXX, testing codes, bond-market units, the ADB unit, the Sucre, and
 * inflation-indexed/notional fund codes). Kept in sync by hand — there is no shared codegen
 * between the two layers for this static list.
 */
export const EXCLUDED_CURRENCY_CODES: ReadonlySet<string> = new Set([
  "XAU", "XAG", "XPD", "XPT", "XDR", "XXX", "XTS",
  "XBA", "XBB", "XBC", "XBD", "XUA", "XSU",
  "CLF", "UYW", "BOV", "CHE", "CHW", "COU", "MXV", "USN",
]);

/** Shown first in the picker, after the country's own default — trivially changeable (§3.3). */
export const MAJOR_CURRENCIES = ["EUR", "USD", "GBP", "CHF"] as const;

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

/** Every active currency code the browser knows about, minus the excluded fund/metal codes. */
export const ALL_CURRENCY_CODES: readonly string[] = Intl.supportedValuesOf("currency").filter(
  (code) => !EXCLUDED_CURRENCY_CODES.has(code),
);

/**
 * Ordered [countryDefault, ...majors (minus dupes), ...rest alphabetical] — the shape the
 * registration and sign-up currency pickers render. `countryDefault` is null when the server
 * couldn't resolve one (unknown country), in which case the picker just starts with the majors.
 */
export function buildCurrencyOptions(countryDefault: string | null): CurrencyOption[] {
  const toOption = (code: string): CurrencyOption => ({
    code,
    name: currencyDisplayNames.of(code) ?? code,
  });

  const seen = new Set<string>();
  const ordered: CurrencyOption[] = [];

  const addIfNew = (code: string) => {
    if (seen.has(code) || !ALL_CURRENCY_CODES.includes(code)) return;
    seen.add(code);
    ordered.push(toOption(code));
  };

  if (countryDefault) addIfNew(countryDefault);
  MAJOR_CURRENCIES.forEach(addIfNew);

  const rest = ALL_CURRENCY_CODES.filter((code) => !seen.has(code))
    .map(toOption)
    .sort((a, b) => a.name.localeCompare(b.name));

  return [...ordered, ...rest];
}
