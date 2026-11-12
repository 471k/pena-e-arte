namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// The owner's view of card payments the reconciliation job has not settled (E05b-2, O06). Every figure
/// here is what PaymentReconciliationJob last recorded; nothing is re-derived from the local status alone.
/// Only groups with something in them are present, in the order of <see cref="UnsettledPaymentKinds.All"/>.
/// Amounts are per payment and never totalled: the rows are, by definition, money that hasn't settled.
/// </summary>
/// <param name="CheckOverdueAfterHours">A row is <c>CheckOverdue</c> when the provider hasn't answered about it
/// for longer than this (the job runs daily, so a missed run or a failing provider call shows up here).</param>
/// <param name="MaxItemsPerGroup">How many items each group carries at most; Total is always the full count.</param>
public record UnsettledPaymentsResponse(
    DateTime GeneratedAt,
    int CheckOverdueAfterHours,
    int MaxItemsPerGroup,
    IReadOnlyList<UnsettledPaymentGroupResponse> Groups);

/// <param name="Kind">One of <see cref="UnsettledPaymentKinds"/>.</param>
/// <param name="Total">The real number of payments, counted by the server, not the length of Items.</param>
/// <param name="Items">The oldest payments first, at most MaxItemsPerGroup.</param>
public record UnsettledPaymentGroupResponse(
    string Kind,
    int Total,
    IReadOnlyList<UnsettledPaymentItemResponse> Items);

/// <param name="Amount">The payment's own amount in its own currency (the deposit asked for, or the hold).</param>
/// <param name="Status">The local <c>PaymentStatus</c> name ("Pending" or "Captured", which means held).</param>
/// <param name="CreatedAt">When this payment row was created (the order was first requested).</param>
/// <param name="ReleaseDeadline">Awaiting card payment only: after this the job cancels the unpaid order.</param>
/// <param name="ProviderCheckedAt">When the job last got an answer from the provider; null = not yet asked.</param>
/// <param name="ProviderStatusAtCheck">What the provider answered then; null with a check time = no record of the order.</param>
public record UnsettledPaymentItemResponse(
    Guid PaymentId,
    Guid AppointmentId,
    DateTime AppointmentDate,
    string AppointmentStatus,
    string ClientName,
    decimal Amount,
    string Currency,
    string Status,
    DateTime CreatedAt,
    DateTime? ReleaseDeadline,
    DateTime? ProviderCheckedAt,
    string? ProviderStatusAtCheck,
    bool CheckOverdue);

public static class UnsettledPaymentKinds
{
    /// <summary>
    /// The provider's last answer doesn't match the local row, and the job left it that way on purpose
    /// (for example an unpaid row the provider calls refunded, or a held row in a state the job doesn't
    /// act on). A person has to look.
    /// </summary>
    public const string ProviderDisagrees = "ProviderDisagrees";

    /// <summary>Card deposits authorised and held, not yet charged (captured). The owner charges or releases them.</summary>
    public const string OnHold = "OnHold";

    /// <summary>Card orders the client hasn't paid yet; the job cancels each one after its ReleaseDeadline.</summary>
    public const string AwaitingCardPayment = "AwaitingCardPayment";

    /// <summary>By consequence: a disagreement needs a person, a hold can be lost if left, an unpaid order just lapses.</summary>
    public static readonly IReadOnlyList<string> All = [ProviderDisagrees, OnHold, AwaitingCardPayment];
}
