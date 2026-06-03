namespace Pena_e_Arte.Contracts.Responses;

public record PaymentResponse(
    Guid                       Id,
    Guid                       StudioId,
    Guid                       AppointmentId,
    Guid                       ClientId,
    decimal                    Amount,
    string                     Status,
    string?                    StripePaymentIntentId,
    DateTime?                  PaidAt,
    DateTime                   CreatedAt,
    List<SessionSplitResponse> SessionSplits);
