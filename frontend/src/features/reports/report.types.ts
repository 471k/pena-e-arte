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
  monthlyTrend: MonthlyRevenuePoint[];
  perArtist:    ArtistRevenuePoint[];
}

export interface EarningsPaymentLine {
  paymentId:       string;
  appointmentId:   string;
  appointmentDate: string | null;
  clientName:      string;
  amount:          number;
  splits:          SessionSplitResponse[];
}

export interface ArtistEarningsResponse {
  monthlyTrend: MonthlyRevenuePoint[];
  periodTotal:  number;
  payments:     EarningsPaymentLine[];
}
