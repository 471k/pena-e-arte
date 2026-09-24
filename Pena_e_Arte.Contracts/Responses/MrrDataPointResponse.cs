namespace Pena_e_Arte.Contracts.Responses;

/// <summary>One point on the MRR trend. IsEstimated is true when the month predates the revenue
/// ledger and was reconstructed from current subscription state; false when it was read from
/// recorded ledger history.</summary>
public record MrrDataPointResponse(string Month, decimal Mrr, bool IsEstimated);
