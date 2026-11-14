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
    DateTime CreatedAt,
    // C09. The freed slot this entry was offered (null while Waiting, and for rows notified before it was
    // recorded), and, only on the signed-in client's own list for a still-open offer, the claim token their
    // "Book this slot" action opens the claim page with. Null everywhere else, staff lists included.
    DateTime? NotifiedSlotStart = null,
    string? ClaimToken = null);
