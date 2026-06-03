namespace Pena_e_Arte.Contracts.Requests;

public record UpdateDepositRuleRequest(
    string   Name,
    decimal? AmountFixed,
    decimal? AmountPercent,
    bool     IsActive);
