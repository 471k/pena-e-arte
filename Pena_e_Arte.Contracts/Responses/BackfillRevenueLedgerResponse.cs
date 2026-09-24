namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Counts only — never studio names or ids, so the result is safe to log.</summary>
public record BackfillRevenueLedgerResponse(int Created, int SkippedAlreadyInLedger, int SkippedNotBilling);
