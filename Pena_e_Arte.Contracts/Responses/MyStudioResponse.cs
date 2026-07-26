namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// One entry in the list returned by GET /api/v1/auth/my-studios.
/// Represents a studio the authenticated client belongs to.
/// IsCurrentlyActive is NOT included — the frontend computes it
/// by comparing StudioId against the tenantId in the stored JWT.
/// </summary>
public record MyStudioResponse(
    Guid StudioId,
    string Name,
    string Slug,
    string City,
    string? CoverImageUrl,
    bool IsStudioActive);
