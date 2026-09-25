namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Gross/net revenue retention for the last COMPLETED calendar month, as fractions
/// (0.95 = 95%). <paramref name="PeriodStart"/> is the first instant (UTC) of that month so the UI
/// can name it. Null rates when there was no MRR at the start of that month to retain.</summary>
public record RevenueRetentionResponse(
    double? GrossRevenueRetention, double? NetRevenueRetention, decimal StartMrr, DateTime PeriodStart);
