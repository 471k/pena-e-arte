namespace Pena_e_Arte.Contracts.Requests;

public record UpdateServiceRequest(
    string Name,
    string? Description,
    int DurationMinutes,
    decimal? Price,
    decimal? DepositAmount,
    bool IsActive);
