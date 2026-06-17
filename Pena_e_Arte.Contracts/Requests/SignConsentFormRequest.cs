namespace Pena_e_Arte.Contracts.Requests;

public record SignConsentFormRequest(
    Guid   ClientId,
    Guid   AppointmentId,
    string SignatureData);
