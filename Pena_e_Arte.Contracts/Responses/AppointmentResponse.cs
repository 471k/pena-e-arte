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
    string? Style = null,
    string? SafetyNotes = null,
    IReadOnlyList<string>? DesiredPlacementLocations = null,
    string? ReferralSource = null,
    string? ReferralSourceOther = null,
    IReadOnlyList<AppointmentAttachmentResponse>? Attachments = null,
    bool PromoCodeApplied = false,
    Guid? ServiceId = null,
    string? ServiceName = null,
    AppointmentSelfServiceResponse? SelfService = null);

public record AppointmentAttachmentResponse(string Url, string Category);

/// <summary>
/// The client self-service policy for one appointment, computed by the server
/// (ClientCancellationPolicy) when it is read: the notice window that applies and whether the client
/// still has that much notice. Filled in by GET /api/v1/appointments/mine only; null elsewhere.
/// The frontend never recomputes it (G-3/G-17). The money detail of a cancellation is in
/// GET /api/v1/appointments/{id}/cancellation-preview.
/// </summary>
public record AppointmentSelfServiceResponse(int NoticeWindowHours, bool IsWithinNoticeWindow);
