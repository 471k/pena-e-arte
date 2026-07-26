namespace Pena_e_Arte.Contracts.Responses;

public record DepositRuleResponse(
    Guid Id,
    Guid StudioId,
    string Name,
    decimal? AmountFixed,
    decimal? AmountPercent,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int? CancellationWindowHours,
    int RefundPercentOnLateCancel);
