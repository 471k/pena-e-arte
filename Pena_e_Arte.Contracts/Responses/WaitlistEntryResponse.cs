namespace Pena_e_Arte.Contracts.Responses;

public record WaitlistEntryResponse(
    Guid Id,
    Guid StudioId,
    Guid? ArtistId,
    string? ArtistName,
    Guid? ClientId,
    string? ClientName,
    string? GuestName,
    string? GuestEmail,
    string? GuestPhone,
    DateTime PreferredDateFrom,
    DateTime PreferredDateTo,
    string Status,
    DateTime? NotifiedAt,
    string? Notes,
    DateTime CreatedAt);
