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
