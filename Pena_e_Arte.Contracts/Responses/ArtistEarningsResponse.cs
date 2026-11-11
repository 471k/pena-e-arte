namespace Pena_e_Arte.Contracts.Responses;

/// <param name="ClientPaidAmount">What the client paid, minus refunds.</param>
/// <param name="ArtistAmount">The artist's share under their arrangement; null when <paramref name="ShareIsKnown"/> is false.</param>
/// <param name="ShareIsKnown">False when the studio hasn't set what the arrangement needs (no rate, no fee).</param>
public record EarningsPaymentLine(
    Guid PaymentId,
    Guid AppointmentId,
    DateTime? AppointmentDate,
    string ClientName,
    decimal ClientPaidAmount,
    decimal? ArtistAmount,
    bool ShareIsKnown,
    List<SessionSplitResponse> Splits);

/// <summary>
/// One month of the artist's trend (A07a). A month with payments whose share is unknown carries a null
/// <paramref name="ArtistAmount"/> and <paramref name="ShareIsKnown"/> false: it must never read as 0.
/// A month with no payments is a known 0.
/// </summary>
/// <param name="Month">"yyyy-MM".</param>
public record EarningsMonthPoint(string Month, decimal? ArtistAmount, decimal ClientPaid, bool ShareIsKnown);

/// <summary>
/// GET /reports/my-earnings (A07a): the artist's share of each payment under their arrangement (their own,
/// else the studio's), with what clients paid alongside so they can reconcile.
/// </summary>
/// <param name="PeriodTotal">The artist's share for the period; null when the share is unknown, never 0 or the gross.</param>
/// <param name="ClientPaidTotal">What clients paid in the period, minus refunds.</param>
/// <param name="ShareIsKnown">False when the studio hasn't set what the arrangement needs.</param>
/// <param name="Arrangement">"Commission", "BoothRent", "FlatFeePerSession" or "Salaried", so the page can say why.</param>
/// <param name="CommissionRate">The artist's percentage under Commission (60.00 = 60%), else null.</param>
/// <param name="FlatFeePerSession">The studio's cut per session under FlatFeePerSession, else null.</param>
public record ArtistEarningsResponse(
    List<EarningsMonthPoint> MonthlyTrend,
    decimal? PeriodTotal,
    decimal ClientPaidTotal,
    List<EarningsPaymentLine> Payments,
    string Currency,
    int ExcludedOtherCurrencyCount,
    bool ShareIsKnown,
    string Arrangement,
    decimal? CommissionRate,
    decimal? FlatFeePerSession);
