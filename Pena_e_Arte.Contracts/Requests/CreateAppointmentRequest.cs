namespace Pena_e_Arte.Contracts.Requests;

public record CreateAppointmentRequest(
    Guid     ArtistId,
    Guid     ClientId,
    DateTime Date,
    int      DurationMinutes,
    decimal  DepositAmount,
    string?  Notes);
