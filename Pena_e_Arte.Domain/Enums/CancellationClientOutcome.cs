namespace Pena_e_Arte.Domain.Enums;

/// <summary>
/// What a cancellation means for the client's money, in the client's terms. Decided by
/// <see cref="Services.CancellationOutcome"/>; the cancellation preview shows it before the client
/// confirms. It is a projection: DELETE /api/v1/appointments/{id} decides again at commit.
/// </summary>
public enum CancellationClientOutcome
{
    /// <summary>No money was taken (no deposit, an unfinished or failed card payment, a cash
    /// deposit only promised, or one already refunded), so nothing is charged or refunded.</summary>
    NothingCollected,

    /// <summary>The whole deposit comes back (a released hold or a full card refund).</summary>
    FullyRefunded,

    /// <summary>Part of the deposit comes back; the studio keeps the rest.</summary>
    PartiallyRefunded,

    /// <summary>The studio keeps the whole deposit.</summary>
    DepositKept,

    /// <summary>A deposit paid in cash: cancelling does not move it automatically; the studio settles
    /// it with the client.</summary>
    SettledWithStudio,

    /// <summary>The platform cannot tell what will happen (a card deposit marked held or paid with no
    /// provider reference). The client is pointed at the studio instead of shown a guess.</summary>
    Unknown,
}
