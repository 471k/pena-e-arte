namespace Pena_e_Arte.Contracts.Requests;

public record SubmitIntakeFormRequest(
    Guid ClientId,
    Guid? AppointmentId,
    string FormData,
    string? FileUrl,
    bool ConsentAccepted);
