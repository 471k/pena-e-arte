namespace Pena_e_Arte.Contracts.Responses;

public record MonthlyRevenuePoint(string Month, decimal Revenue);

public record ArtistRevenuePoint(Guid ArtistId, string ArtistName, decimal Revenue);

public record RevenueSummaryResponse(
    List<MonthlyRevenuePoint> MonthlyTrend,
    List<ArtistRevenuePoint>  PerArtist);
