namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// A booking form's values as a JSON object string, plus the photos it has already uploaded. The payload must not
/// contain the form's safety notes field: the server refuses it rather than store it.
/// </summary>
public record SaveBookingDraftRequest(string Payload, List<BookingDraftImageRequest>? Images);

public record BookingDraftImageRequest(string Url, string Category);
