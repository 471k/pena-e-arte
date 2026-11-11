import { cn } from "@/shared/utils/cn";
import { formatCurrency } from "@/shared/utils/formatCurrency";

/**
 * How an artist is paid for a session (DEC10, A07a). Mirrors ArtistPayArrangement on the server.
 * Rates are percentages to the artist (60 = 60%), the unit the server stores.
 */
export const ArtistPayArrangement = {
  Commission:        "Commission",
  BoothRent:         "BoothRent",
  FlatFeePerSession: "FlatFeePerSession",
  Salaried:          "Salaried",
} as const;
export type ArtistPayArrangement = (typeof ArtistPayArrangement)[keyof typeof ArtistPayArrangement];

export const ARRANGEMENT_ORDER: readonly ArtistPayArrangement[] = [
  ArtistPayArrangement.Commission,
  ArtistPayArrangement.BoothRent,
  ArtistPayArrangement.FlatFeePerSession,
  ArtistPayArrangement.Salaried,
];

/** The arrangement's name in a select. */
export const ARRANGEMENT_LABEL: Record<ArtistPayArrangement, string> = {
  Commission:        "Commission",
  BoothRent:         "Booth rent",
  FlatFeePerSession: "Flat fee per session",
  Salaried:          "Salaried",
};

/** One sentence on what the arrangement means, shown under the select. */
export const ARRANGEMENT_HINT: Record<ArtistPayArrangement, string> = {
  Commission:        "The artist gets a percentage of what the client paid.",
  BoothRent:         "The artist pays rent and keeps what the client paid. Rent is charged separately, under Booth rent.",
  FlatFeePerSession: "The studio keeps a fixed amount per session and the artist gets the rest.",
  Salaried:          "Nothing is split per session; the artist is paid some other way.",
};

export function isArrangement(value: string): value is ArtistPayArrangement {
  return (ARRANGEMENT_ORDER as readonly string[]).includes(value);
}

/** "60%" or "62.5%": a stored percentage, without trailing zeros. */
export function formatRate(rate: number): string {
  return `${Number(rate.toFixed(2))}%`;
}

/**
 * The artist-facing description of their arrangement, used as the earnings headline's label:
 * "60% commission", "booth rent: you keep what clients pay", and so on.
 */
export function describeArtistArrangement(
  arrangement: string,
  commissionRate: number | null,
  flatFeePerSession: number | null,
  currency: string,
): string {
  switch (arrangement) {
    case ArtistPayArrangement.Commission:
      return commissionRate === null ? "commission" : `${formatRate(commissionRate)} commission`;
    case ArtistPayArrangement.BoothRent:
      return "booth rent: you keep what clients pay";
    case ArtistPayArrangement.FlatFeePerSession:
      return flatFeePerSession === null
        ? "after the studio's fee per session"
        : `after the studio's ${formatCurrency(flatFeePerSession, currency)} fee per session`;
    case ArtistPayArrangement.Salaried:
      return "salaried: nothing is earned per session";
    default:
      return arrangement;
  }
}

/** The native select styling used across forms (a native select keeps keyboard and find-in-page behaviour simple). */
export const SELECT_CLS = cn(
  "flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
  "ring-offset-background focus-visible:outline-none focus-visible:ring-2",
  "focus-visible:ring-ring focus-visible:ring-offset-2",
  "disabled:cursor-not-allowed disabled:opacity-50",
);

/** A typed rate as a number, or an error message. Empty is null (not set), never 0. */
export function parseRate(value: string): { value: number | null; error: string | null } {
  const text = value.trim();
  if (text === "") return { value: null, error: null };
  const n = Number(text);
  if (!Number.isFinite(n) || n < 0 || n > 100) return { value: null, error: "Enter a percentage from 0 to 100." };
  // Checked on the text, not with n * 100 (0.29 * 100 is 28.999… in floating point).
  if (!/^\d+(\.\d{1,2})?$/.test(text)) return { value: null, error: "Use at most 2 decimals." };
  return { value: n, error: null };
}

export function parseFee(value: string): { value: number | null; error: string | null } {
  if (value.trim() === "") return { value: null, error: null };
  const n = Number(value);
  if (!Number.isFinite(n) || n < 0) return { value: null, error: "Enter an amount of 0 or more." };
  return { value: n, error: null };
}
