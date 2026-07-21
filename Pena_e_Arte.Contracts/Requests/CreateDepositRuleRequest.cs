namespace Pena_e_Arte.Contracts.Requests;

public record CreateDepositRuleRequest(
    string   Name,
    decimal? AmountFixed,
    decimal? AmountPercent,
    bool     IsActive,
    int?     CancellationWindowHours = null,
    int      RefundPercentOnLateCancel = 0);
