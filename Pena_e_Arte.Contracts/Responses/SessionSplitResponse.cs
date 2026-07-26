namespace Pena_e_Arte.Contracts.Responses;

public record SessionSplitResponse(
    Guid Id,
    Guid PaymentId,
    string Label,
    decimal Amount,
    DateTime? PaidAt);
