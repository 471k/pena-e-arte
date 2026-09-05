namespace Pena_e_Arte.Contracts.Responses;

public record AppointmentResponse(
    Guid Id,
    Guid StudioId,
    Guid? ArtistId,
    Guid ClientId,
    DateTime Date,
    DateTime EndDate,
    int DurationMinutes,
    string Status,
    string DepositStatus,
    decimal DepositAmount,
    string? Notes,
    DateTime CreatedAt,
    string? CancellationReason = null,
    DateTime? AftercareSentAt = null,
    string? ClientName = null,
    IReadOnlyList<string>? ImageUrls = null,   // deprecated — use Attachments (Category-split). Kept for one release to avoid breaking frontend consumers still being migrated (Part 6); mirrors Attachments' Reference-category subset.
    string? ArtistName = null,
    Guid? ClientUserId = null,
    string? TattooDescription = null,
    string? SafetyNotes = null,
    IReadOnlyList<string>? DesiredPlacementLocations = null,
    string? ReferralSource = null,
    string? ReferralSourceOther = null,
    IReadOnlyList<AppointmentAttachmentResponse>? Attachments = null);

public record AppointmentAttachmentResponse(string Url, string Category);
