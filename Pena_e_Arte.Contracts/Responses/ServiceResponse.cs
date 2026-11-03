namespace Pena_e_Arte.Contracts.Responses;

public record ServiceResponse(
    Guid Id,
    Guid StudioId,
    string Name,
    string? Description,
    int DurationMinutes,
    decimal? Price,
    decimal? DepositAmount,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool RequiresConsentForm = false,
    bool RequiresIntakeForm = false,
    bool RequiresReferenceImage = false,
    bool RequiresDepositPaid = false);
