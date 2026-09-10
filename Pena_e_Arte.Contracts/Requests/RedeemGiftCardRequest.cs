namespace Pena_e_Arte.Contracts.Requests;

public record RedeemGiftCardRequest(string Code, Guid AppointmentId, decimal Amount);
