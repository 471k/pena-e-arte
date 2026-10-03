// Human-readable timezone options for the studio's timezone picker. The stored value stays the IANA
// id the backend's TimeZoneInfo accepts (for example "Europe/Tirane"); only the label changes, to
// the form booking tools use: "(UTC+01:00) Tirane — Central European Time", sorted by offset.

export interface TimezoneOption {
  /** IANA id — the value that is saved. */
  id: string;
  /** What the person sees: offset, city and the generic zone name. */
  label: string;
  /** City or UTC; used for type-ahead, which matches the start of this text. */
  city: string;
  offsetMinutes: number;
}

const FALLBACK_TIMEZONE_IDS = [
  "Europe/Tirane", "Europe/London", "Europe/Lisbon", "Europe/Berlin", "Europe/Athens",
  "America/New_York", "America/Chicago", "America/Los_Angeles", "UTC",
];

/** Every IANA zone the browser knows, minus the legacy "Etc/GMT±N" and "GMT±N" aliases: their
 * signs are inverted from what people expect ("Etc/GMT+1" is UTC-1) and they read as noise. */
export function listTimezoneIds(): string[] {
  const ids: string[] =
    typeof Intl.supportedValuesOf === "function"
      ? Intl.supportedValuesOf("timeZone")
      : FALLBACK_TIMEZONE_IDS;
  const filtered = ids.filter((id) => !id.startsWith("Etc/"));
  return filtered.includes("UTC") ? filtered : [...filtered, "UTC"];
}

/** True when the browser can format dates in this zone. Intl.supportedValuesOf lists only canonical
 * zones, so aliases such as "Europe/Tirane" are missing from it even though the browser (and the
 * backend's TimeZoneInfo) accept them; membership in that list is therefore the wrong test. */
export function isTimezoneSupported(id: string): boolean {
  try {
    new Intl.DateTimeFormat("en-US", { timeZone: id });
    return true;
  } catch {
    return false;
  }
}

function offsetMinutesFor(timeZone: string, at: Date): number {
  try {
    const part = new Intl.DateTimeFormat("en-US", { timeZone, timeZoneName: "longOffset" })
      .formatToParts(at)
      .find((p) => p.type === "timeZoneName")?.value;
    // "GMT" for UTC itself, otherwise "GMT+01:00" / "GMT-03:30".
    const match = part?.match(/^GMT(?:([+-])(\d{1,2})(?::(\d{2}))?)?$/);
    if (!match || !match[1]) return 0;
    const minutes = Number(match[2]) * 60 + Number(match[3] ?? 0);
    return match[1] === "-" ? -minutes : minutes;
  } catch {
    return 0;
  }
}

function genericNameFor(timeZone: string, at: Date): string {
  try {
    return (
      new Intl.DateTimeFormat("en-US", { timeZone, timeZoneName: "longGeneric" })
        .formatToParts(at)
        .find((p) => p.type === "timeZoneName")?.value ?? ""
    );
  } catch {
    return "";
  }
}

function cityFor(id: string): string {
  if (id === "UTC") return "UTC";
  const last = id.split("/").pop() ?? id;
  return last.replace(/_/g, " ");
}

export function formatOffset(offsetMinutes: number): string {
  const sign = offsetMinutes < 0 ? "-" : "+";
  const abs = Math.abs(offsetMinutes);
  const hh = String(Math.floor(abs / 60)).padStart(2, "0");
  const mm = String(abs % 60).padStart(2, "0");
  return `UTC${sign}${hh}:${mm}`;
}

export function buildTimezoneOptions(ids: string[], at: Date = new Date()): TimezoneOption[] {
  return ids
    .map((id): TimezoneOption => {
      const offsetMinutes = offsetMinutesFor(id, at);
      const city = cityFor(id);
      const name = genericNameFor(id, at);
      const label = `(${formatOffset(offsetMinutes)}) ${city}${name && name !== city ? ` — ${name}` : ""}`;
      return { id, label, city, offsetMinutes };
    })
    .sort((a, b) => a.offsetMinutes - b.offsetMinutes || a.city.localeCompare(b.city));
}

/** The zones a country uses, most relevant first, restricted to zones the browser can format.
 * Intl.Locale's timeZones/getTimeZones is supported in current Chromium and Safari; where it is
 * missing the visitor's own IP-derived zone (when the IP is in that country) is the only hint. */
export function suggestedTimezoneIds(
  countryCode: string | null | undefined,
  ipCountry?: string | null,
  ipTimezone?: string | null,
): string[] {
  const suggestions: string[] = [];
  if (countryCode && ipCountry && ipTimezone && countryCode.toUpperCase() === ipCountry.toUpperCase()) {
    suggestions.push(ipTimezone);
  }
  if (countryCode && /^[A-Za-z]{2}$/.test(countryCode)) {
    try {
      const locale = new Intl.Locale("und", { region: countryCode.toUpperCase() }) as unknown as {
        getTimeZones?: () => string[];
        timeZones?: string[];
      };
      const zones = locale.getTimeZones?.() ?? locale.timeZones ?? [];
      suggestions.push(...zones);
    } catch {
      // unknown region: no locale-based suggestions
    }
  }
  return [...new Set(suggestions)].filter(isTimezoneSupported);
}

/** The timezone to preselect when a studio has none saved: the country's first suggested zone,
 * otherwise the browser's own zone, otherwise nothing (the person picks). */
export function defaultTimezoneId(
  suggested: string[],
  browserTimezone: string | undefined = typeof Intl !== "undefined"
    ? Intl.DateTimeFormat().resolvedOptions().timeZone
    : undefined,
): string | undefined {
  if (suggested.length > 0) return suggested[0];
  return browserTimezone && isTimezoneSupported(browserTimezone) ? browserTimezone : undefined;
}

/** The zone to send with a new registration: the selected country's best-suggested zone (the
 * visitor's own IP zone first when the IP is in that country), or undefined so the server keeps its
 * default. There is deliberately no browser-zone fallback here, unlike defaultTimezoneId: a browser's
 * zone says nothing about which country the studio is in, and a wrong zone silently shifts every
 * appointment time, where the server default is at least the product's home market. */
export function registrationTimezoneId(
  countryCode: string | null | undefined,
  ipCountry?: string | null,
  ipTimezone?: string | null,
): string | undefined {
  return suggestedTimezoneIds(countryCode, ipCountry, ipTimezone)[0];
}
