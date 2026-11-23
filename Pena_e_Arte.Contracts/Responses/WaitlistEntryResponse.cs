namespace Pena_e_Arte.Contracts.Responses;

public record WaitlistEntryResponse(
    Guid Id,
    Guid StudioId,
    // S01. The studio's IANA zone, so the client's own list shows an offered slot on the studio's calendar
    // rather than the viewer's (MyWaitlistPage had no zone at all). Null only if the studio row is missing,
    // which the page shows as "Time unavailable" rather than guessing a zone.
    string? StudioTimezone,
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
