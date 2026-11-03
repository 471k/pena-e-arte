namespace Pena_e_Arte.Contracts.Requests;

public record CreateServiceRequest(
    string Name,
    string? Description,
    int DurationMinutes,
    decimal? Price,
    decimal? DepositAmount,
    bool IsActive,
    // Preparation requirements (DEC03). Off unless the owner turns them on.
    bool RequiresConsentForm = false,
    bool RequiresIntakeForm = false,
    bool RequiresReferenceImage = false,
    bool RequiresDepositPaid = false);
