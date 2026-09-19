namespace Pena_e_Arte.Contracts.Requests;

public record PayWithSavedCardRequest(Guid AppointmentId, Guid SavedPaymentMethodId);
