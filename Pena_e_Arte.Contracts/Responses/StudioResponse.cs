namespace Pena_e_Arte.Contracts.Responses;

public record StudioResponse(
    Guid Id,
    string Name,
    string Slug,
    string City,
    double Latitude,
    double Longitude,
    bool ShowPlatformBranding,
    bool AllowBrandingRemoval,
    bool AllowApiAccess,
    DateTime TrialExpiresAt,
    DateTime CreatedAt,
    bool IsActive,
    DateTime? SlugLockedAt,
    string? PhoneNumber = null,
    string? InstagramHandle = null,
    string? Nipt = null,
    bool IsSolo = false,
    bool IsPublished = true,
    string Timezone = "Europe/Tirane",
    string? SubscriptionStatus = null,
    DateTime? PastDueSince = null);
