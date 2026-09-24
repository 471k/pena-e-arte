namespace Pena_e_Arte.Contracts.Responses;

/// <summary>One row of an admin studio-detail page's recent-refunds list (A6: "visible on the
/// studio's admin page" — the "the admin can see it" half of a Failed refund's alert).</summary>
public record RecentRefundResponse(decimal Amount, string Status, DateTime CreatedAt, string? FailureReason);
