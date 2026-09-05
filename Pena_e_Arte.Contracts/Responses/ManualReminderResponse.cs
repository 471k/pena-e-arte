namespace Pena_e_Arte.Contracts.Responses;

public record ManualReminderResponse(
    Guid Id,
    Guid? AppointmentId,
    Guid? ClientId,
    string RecipientName,
    string RecipientPhone,
    string? Message,
    DateTime ScheduledFor,
    string Status,
    DateTime? SentAt,
    DateTime CreatedAt);
