import { getCountries, getCountryCallingCode, type CountryCode } from "libphonenumber-js/min";

// libphonenumber-js's own literal-union type (e.g. "PT" | "GB" | ...), re-exported under this
// name so call sites don't import directly from the library. A plain `string` alias here
// would silently widen every value passed into AsYouType/parsePhoneNumberFromString/
// getCountryCallingCode, which `tsc --noEmit` doesn't catch in this project's config but the
// stricter `tsc -b` (`pnpm build`) does — confirmed by a real build failure, not assumed.
export type PhoneCountryCode = CountryCode;

export interface PhoneCountryOption {
  code:        PhoneCountryCode;
  name:        string;
  callingCode: string; // e.g. "351" — no leading '+'
}

const regionNames = new Intl.DisplayNames(["en"], { type: "region" });

/**
 * Every country libphonenumber-js has calling-code metadata for, sorted by display name.
 * Built once at module load — this list is static per app version, not per render.
 */
export const PHONE_COUNTRIES: PhoneCountryOption[] = getCountries()
  .map((code) => ({
    code,
    name: regionNames.of(code) ?? code,
    callingCode: getCountryCallingCode(code),
  }))
  .sort((a, b) => a.name.localeCompare(b.name));

/**
 * Fallback country for a fresh, empty PhoneInput when the caller does not know the studio's
 * country (for example a client editing their own profile). Albania: the product's home market
 * (Albanian SMS sender, ALL default currency). It used to be Portugal only because the first
 * demo data and placeholders were Portuguese, which put +351 first for every Albanian user.
 * Screens that know the studio pass its own country through PhoneInput's `defaultCountry`.
 */
export const DEFAULT_PHONE_COUNTRY: PhoneCountryCode = "AL";

/**
 * Narrows an API country string (e.g. Studio.CountryCode, "AL") to a country the phone input
 * supports; undefined for a missing or unknown value so callers fall back to the default.
 */
export function asPhoneCountry(code: string | null | undefined): PhoneCountryCode | undefined {
  if (!code) return undefined;
  const upper: string = code.toUpperCase();
  return PHONE_COUNTRIES.find((c) => c.code === upper)?.code;
}

// Plain `string`, not `PhoneCountryCode` — this is a general case-insensitive string
// transform (confirmed by its own required lowercase-input test case), not a
// library-boundary call, so it shouldn't be constrained to the strict CountryCode union.
/** Converts an ISO 3166-1 alpha-2 code to its flag emoji via Unicode regional indicator symbols. */
export function flagEmoji(code: string): string {
  return code
    .toUpperCase()
    .replace(/./g, (char) => String.fromCodePoint(127397 + char.charCodeAt(0)));
}
