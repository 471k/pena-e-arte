namespace Pena_e_Arte.Contracts.Responses;

public record StudioResponse(
    Guid      Id,
    string    Name,
    string    Slug,
    string    City,
    double    Latitude,
    double    Longitude,
    bool      ShowPlatformBranding,
    bool      AllowBrandingRemoval,
    DateTime  TrialExpiresAt,
    DateTime  CreatedAt,
    bool      IsActive,
    DateTime? SlugLockedAt);
