namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Gross/net revenue retention for the current calendar month so far, as fractions
/// (0.95 = 95%). Null when there was no MRR at the start of the month to retain.</summary>
public record RevenueRetentionResponse(double? GrossRevenueRetention, double? NetRevenueRetention, decimal StartMrr);
