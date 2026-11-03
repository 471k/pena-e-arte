namespace Pena_e_Arte.Contracts.Requests;

public record UpdateServiceRequest(
    string Name,
    string? Description,
    int DurationMinutes,
    decimal? Price,
    decimal? DepositAmount,
    bool IsActive,
    // Preparation requirements (DEC03). Null leaves the stored value unchanged, so a caller that
    // predates them cannot switch a studio's requirements off by omission.
    bool? RequiresConsentForm = null,
    bool? RequiresIntakeForm = null,
    bool? RequiresReferenceImage = null,
    bool? RequiresDepositPaid = null);
