namespace Pena_e_Arte.Contracts.Responses;

/// <summary>One calendar month of recorded MRR movement. Contraction and Churn are negative.
/// All zeros for months that predate the revenue ledger (no movement data is reconstructable).</summary>
public record MrrMovementsDataPointResponse(
    string Month,
    decimal New,
    decimal Expansion,
    decimal Reactivation,
    decimal Contraction,
    decimal Churn,
    decimal Net);
