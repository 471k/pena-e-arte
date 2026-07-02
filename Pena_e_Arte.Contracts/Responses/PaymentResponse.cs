namespace Pena_e_Arte.Contracts.Responses;

public record PaymentResponse(
    Guid      Id,
    Guid      AppointmentId,
    decimal   Amount,
    string    Status,
    string    Method,
    string?   StripePaymentIntentId,
    string?   ClientSecret,
    string?   CashNote,
    DateTime? PaidAt,
    string    ClientName,
    DateTime? AppointmentDate = null,
    IReadOnlyList<SessionSplitResponse>? Splits = null);
