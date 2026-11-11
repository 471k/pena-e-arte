import type { SessionSplitResponse } from "@/features/payments";

export interface MonthlyRevenuePoint {
  month:   string;
  revenue: number;
}

export interface ArtistRevenuePoint {
  artistId:   string;
  artistName: string;
  revenue:    number;
}

export interface RevenueSummaryResponse {
  monthlyTrend:               MonthlyRevenuePoint[];
  perArtist:                  ArtistRevenuePoint[];
  currency:                   string;
  excludedOtherCurrencyCount: number;
}

export interface EarningsPaymentLine {
  paymentId:        string;
  appointmentId:    string;
  /** When the session is or was; not when it was paid. */
  appointmentDate:  string | null;
  /** When the payment came in (UTC ISO). The "last 30 days" list filters on this. */
  paidAt:           string;
  clientName:       string;
  /** What the client paid, minus refunds. */
  clientPaidAmount: number;
  /** The artist's share; null when shareIsKnown is false. */
  artistAmount:     number | null;
  shareIsKnown:     boolean;
  splits:           SessionSplitResponse[];
}

/** One month of the artist's trend (A07a). A month with payments and an unknown share is null, never 0. */
export interface EarningsMonthPoint {
  month:        string;
  artistAmount: number | null;
  clientPaid:   number;
  shareIsKnown: boolean;
}

/** GET /reports/my-earnings (A07a): the artist's share under their arrangement, with what clients paid. */
export interface ArtistEarningsResponse {
  monthlyTrend:               EarningsMonthPoint[];
  /** The artist's share for the period; null when the share is unknown. */
  periodTotal:                number | null;
  clientPaidTotal:            number;
  payments:                   EarningsPaymentLine[];
  currency:                   string;
  excludedOtherCurrencyCount: number;
  shareIsKnown:               boolean;
  arrangement:                string;
  commissionRate:             number | null;
  flatFeePerSession:          number | null;
}
