namespace Pena_e_Arte.Contracts.Responses.Public;

public record PublicServiceResponse(
    Guid Id,
    string Name,
    string? Description,
    int DurationMinutes,
    decimal? Price,
    decimal? DepositAmount);
