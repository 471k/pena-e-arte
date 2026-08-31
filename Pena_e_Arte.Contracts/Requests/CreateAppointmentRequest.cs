namespace Pena_e_Arte.Contracts.Requests;

public record CreateAppointmentRequest(
    Guid? ArtistId,
    Guid ClientId,
    DateTime Date,
    int DurationMinutes,
    string? Notes,
    string TattooDescription = "",
    string? SafetyNotes = null,
    IReadOnlyList<string>? DesiredPlacementLocations = null,
    string? ReferralSource = null,          // enum name as string, nullable — "Other" requires ReferralSourceOther
    string? ReferralSourceOther = null,
    IReadOnlyList<AppointmentImageRequest>? Images = null);

/// <summary>Category: "AreaPhoto" | "Reference" (matches AppointmentAttachmentCategory).</summary>
public record AppointmentImageRequest(string Url, string Category);
