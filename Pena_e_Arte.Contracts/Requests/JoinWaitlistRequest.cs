namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// ClientId is resolved server-side from the JWT when the caller is a signed-in client — never
/// trusted from the request. Guest* fields are required only for an anonymous caller; validated
/// conditionally by JoinWaitlistValidator, mirroring CreateGuestAppointmentRequest's duality.
/// StudioSlug: the endpoint is AllowAnonymous with no ambient tenant, so an anonymous guest must
/// name the studio explicitly — required for a guest submission, ignored for a signed-in client
/// (whose own studio governs via ICurrentTenant), same resolution as
/// CreateGuestAppointmentCommand's StudioSlug.
/// </summary>
public record JoinWaitlistRequest(
    string? StudioSlug,
    Guid? ArtistId,
    DateTime PreferredDateFrom,
    DateTime PreferredDateTo,
    string? GuestName,
    string? GuestEmail,
    string? GuestPhone,
    string? Notes);
