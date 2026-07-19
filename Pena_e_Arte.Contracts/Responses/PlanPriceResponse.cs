namespace Pena_e_Arte.Contracts.Responses;

public record PlanPriceResponse(
    Guid    Id,
    string  Interval,
    decimal Price,
    string? StripePriceId,
    bool    IsActive);
