namespace Pena_e_Arte.Contracts.Responses;

/// <summary>A yearly-cancellation refund that failed, on the platform-wide list (slice 8 phase 3, D07): what failed
/// (amount, currency, rule), when, against which payment (the Stripe invoice), for which studio, and why. ReachedStripe:
/// see RecentRefundResponse; it decides what the admin does next.</summary>
public record FailedSubscriptionRefundResponse(
    Guid Id,
    Guid StudioId,
    string StudioName,
    decimal Amount,
    string Currency,
    string Rule,
    DateTime CreatedAt,
    DateTime FailedAt,
    string? FailureReason,
    string InvoiceReference,
    bool ReachedStripe);

/// <summary>The newest failed refunds and how many there are in all, so a capped list says it is capped.</summary>
public record FailedSubscriptionRefundsResponse(List<FailedSubscriptionRefundResponse> Items, int TotalCount);
