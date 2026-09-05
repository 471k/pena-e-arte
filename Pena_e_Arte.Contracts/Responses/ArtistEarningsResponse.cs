namespace Pena_e_Arte.Contracts.Responses;

public record EarningsPaymentLine(
    Guid PaymentId,
    Guid AppointmentId,
    DateTime? AppointmentDate,
    string ClientName,
    decimal Amount,
    List<SessionSplitResponse> Splits);

public record ArtistEarningsResponse(
    List<MonthlyRevenuePoint> MonthlyTrend,
    decimal PeriodTotal,
    List<EarningsPaymentLine> Payments);
