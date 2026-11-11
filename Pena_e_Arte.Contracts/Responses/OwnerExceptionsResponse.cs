namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// The owner's dashboard exceptions (E05a, O01): what needs the owner, grouped by the decision it
/// asks for. Only groups with something in them are present, always in the fixed order of
/// <see cref="OwnerExceptionKinds.All"/> (by consequence if ignored); items inside a group are soonest first.
/// </summary>
/// <param name="WindowDays">How far ahead the unpaid-deposit and preparation groups look (7 days).</param>
/// <param name="MaxItemsPerGroup">How many items each group carries at most; Total is always the full count.</param>
public record OwnerExceptionsResponse(
    DateTime GeneratedAt,
    int WindowDays,
    int MaxItemsPerGroup,
    IReadOnlyList<OwnerExceptionGroupResponse> Groups);

/// <param name="Kind">One of <see cref="OwnerExceptionKinds"/>.</param>
/// <param name="Total">The real number of items, counted by the server, not the length of Items.</param>
/// <param name="Items">The soonest items, at most MaxItemsPerGroup.</param>
public record OwnerExceptionGroupResponse(
    string Kind,
    int Total,
    IReadOnlyList<OwnerExceptionItemResponse> Items);

/// <param name="Readiness">Preparation group only: the DEC03 summary (the same one the staff list shows).</param>
/// <param name="Deposit">Cash and unpaid-deposit groups only.</param>
public record OwnerExceptionItemResponse(
    Guid AppointmentId,
    DateTime AppointmentDate,
    string ClientName,
    string? ArtistName,
    string AppointmentStatus,
    AppointmentReadinessSummaryResponse? Readiness = null,
    OwnerExceptionDepositResponse? Deposit = null);

/// <summary>
/// The deposit due on the appointment. <paramref name="Payment"/> is the deposit payment when the client
/// has started one (so the screen can describe it the way every other deposit is described), and null
/// when they haven't: no payment exists yet, which is a fact, not a status.
/// </summary>
public record OwnerExceptionDepositResponse(decimal Amount, string Currency, PaymentResponse? Payment);

public static class OwnerExceptionKinds
{
    /// <summary>Upcoming requests no artist is assigned to (the owner assigns one).</summary>
    public const string Unassigned = "Unassigned";

    /// <summary>Upcoming requests with an artist, still Pending (staff confirm or decline).</summary>
    public const string AwaitingConfirmation = "AwaitingConfirmation";

    /// <summary>Deposits the client said they'd pay in cash, any date (the owner confirms receipt).</summary>
    public const string CashToConfirm = "CashToConfirm";

    /// <summary>Deposits due in the next WindowDays that aren't secured and aren't promised in cash.</summary>
    public const string UnpaidDeposit = "UnpaidDeposit";

    /// <summary>Sessions in the next WindowDays with a preparation requirement still outstanding.</summary>
    public const string Preparation = "Preparation";

    /// <summary>
    /// The order groups are shown in, by what happens if each is ignored: no artist means no session;
    /// a client waiting on the studio loses the booking; an unpaid deposit is money at risk this week;
    /// preparation is still fixable near the date; cash to confirm is bookkeeping on money already
    /// promised in person. Dates never reorder groups: only cash can be past-dated, so ordering by the
    /// soonest date only ever promoted cash, and the staler a promise got the more firmly it led.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
        [Unassigned, AwaitingConfirmation, UnpaidDeposit, Preparation, CashToConfirm];
}
