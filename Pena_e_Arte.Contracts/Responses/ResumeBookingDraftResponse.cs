namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// C05: what following a resume link did. Outcome is "Resumed" (the draft is now the caller's and shows on the
/// booking form), "AlreadyClaimed" (it already was; Draft is still returned) or "Expired" (the link's seven days
/// are over; Draft is null). Every other failure is one 404 with one message, so the endpoint never says whether
/// the link was forged, the draft is gone, or it belongs to someone else.
/// </summary>
public record ResumeBookingDraftResponse(string Outcome, BookingDraftResponse? Draft);
