namespace Pena_e_Arte.Contracts.Requests;

public record CreateManualReminderRequest(
    Guid? AppointmentId,
    Guid? ClientId,
    Guid? ArtistId,          // only honored for owner/issuer callers acting on another artist's behalf
    string? RecipientName,
    string? RecipientPhone,
    string? Message,
    DateTime? ScheduledFor);
