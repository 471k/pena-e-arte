namespace Pena_e_Arte.Contracts.Responses;

/// <param name="Status">Derived from the latest revision: Draft, InReview, Approved or ChangesRequested.</param>
/// <param name="WaitingOn">Whose move it is (E04c, A05): "Artist" (no version yet, or changes were asked
/// for, or the review window expired), "Client" (the latest version awaits their review) or "Nobody"
/// (approved, or a catalog template). Derived, not stored.</param>
/// <param name="LatestVersionNumber">The version the status and WaitingOn are about; null with no revisions.</param>
/// <param name="LatestUploadedAt">When that version was uploaded (UTC); null with no revisions.</param>
public record DesignResponse(
    Guid Id,
    Guid StudioId,
    Guid? ClientId,
    Guid ArtistId,
    string Title,
    string? Description,
    DateTime CreatedAt,
    string Status = "Draft",
    bool IsCatalogItem = false,
    decimal? Price = null,
    string WaitingOn = "Artist",
    int? LatestVersionNumber = null,
    DateTime? LatestUploadedAt = null);
