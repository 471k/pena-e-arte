namespace Pena_e_Arte.Contracts.Requests;

public record PlanPriceRequest(
    string Interval,
    decimal Price,
    string? StripePriceId = null,
    bool IsActive = true);
