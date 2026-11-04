namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// GET /api/v1/appointments/{id}/reschedule-preview (E04b, A02): whether this appointment can be moved
/// at all, decided by the same function the save path (PATCH {id}/reschedule) applies, so the dialog
/// can explain a refusal before a time is picked. It says nothing about a particular new time: that is
/// GET /appointments/check-slot with excludeAppointmentId. Advisory; the save decides again.
/// </summary>
/// <param name="CanReschedule">False when the save would refuse whatever time is chosen.</param>
/// <param name="Block">Null when allowed; otherwise "AppointmentClosed" or "InsideNoticeWindow".</param>
/// <param name="Message">The exact refusal text the save would return. Null when allowed.</param>
/// <param name="NoticeWindowHours">The studio's notice window (or the platform default).</param>
/// <param name="NoticeWindowApplies">False for staff, who are never held to the notice window.</param>
public record ReschedulePreviewResponse(
    Guid AppointmentId,
    bool CanReschedule,
    string? Block,
    string? Message,
    int NoticeWindowHours,
    bool NoticeWindowApplies);
