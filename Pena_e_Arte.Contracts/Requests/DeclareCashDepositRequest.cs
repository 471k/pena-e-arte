namespace Pena_e_Arte.Contracts.Requests;

public record DeclareCashDepositRequest(Guid AppointmentId, string? Note);
