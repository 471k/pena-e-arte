namespace Pena_e_Arte.Contracts.Requests;

public record CreatePaymentIntentRequest(
    Guid AppointmentId,
    Guid ClientId,
    decimal Amount,
    string Currency);
